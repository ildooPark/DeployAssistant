using System;
using System.Collections.Generic;
using System.IO;
using Spectre.Console;

namespace DeployAssistant.CLI.Engine
{
    internal sealed class App
    {
        private readonly Stack<Screen> _stack = new Stack<Screen>();

        public int Run(Screen root)
        {
            // Fail fast if there is no real console attached — the TUI is meaningless
            // when stdout/stdin are redirected (e.g. captured output, piped, headless run).
            if (Console.IsOutputRedirected || Console.IsInputRedirected)
            {
                Console.Error.WriteLine("deployassistant: interactive TUI requires a real terminal.");
                Console.Error.WriteLine("Run the .exe directly in a console window; redirection/piping is not supported.");
                return 1;
            }

            // Probe Console.WindowHeight once up front so we surface a clean error rather than
            // letting it throw deep inside the render loop (System.IO.IOException at GetBufferInfo
            // when launched without an attached console handle).
            try { _ = Console.WindowHeight; }
            catch (IOException)
            {
                Console.Error.WriteLine("deployassistant: unable to read console dimensions (no console window?).");
                Console.Error.WriteLine("Run the .exe directly in a console window.");
                return 1;
            }

            _stack.Push(root);
            Screen? lastTop = null;

            Console.CancelKeyPress += OnCancel;

            try
            {
                while (_stack.Count > 0)
                {
                    var current = _stack.Peek();
                    if (!ReferenceEquals(current, lastTop))
                    {
                        current.OnEnter();
                        lastTop = current;
                    }

                    AnsiConsole.Clear();

                    int windowHeight;
                    try { windowHeight = Console.WindowHeight; }
                    catch (IOException) { windowHeight = 0; }
                    if (windowHeight < 10)
                    {
                        AnsiConsole.MarkupLine(TextStyle.Dim("Terminal too small — please resize."));
                        var key = Console.ReadKey(intercept: true);
                        if (IsCtrlC(key)) return 0;
                        continue;
                    }

                    current.Render();

                    var auto = current.AutoAdvance();
                    if (auto is not null)
                    {
                        lastTop = ApplyAction(auto, current, lastTop);
                        continue;
                    }

                    {
                        var key = Console.ReadKey(intercept: true);
                        if (IsCtrlC(key)) return 0;

                        var action = current.Handle(key);
                        lastTop = ApplyAction(action, current, lastTop);
                    }
                }
            }
            finally
            {
                Console.CancelKeyPress -= OnCancel;
            }

            return 0;
        }

        private Screen? ApplyAction(ScreenAction action, Screen current, Screen? lastTop)
        {
            switch (action)
            {
                case ScreenAction.Stay:
                    return lastTop;

                case ScreenAction.Pop:
                    current.OnExit();
                    _stack.Pop();
                    return null;

                case ScreenAction.Push push:
                    _stack.Push(push.Next);
                    return null;

                case ScreenAction.Replace replace:
                    current.OnExit();
                    _stack.Pop();
                    _stack.Push(replace.Next);
                    return null;

                case ScreenAction.Exit:
                    current.OnExit();
                    _stack.Clear();
                    return lastTop;

                default:
                    return lastTop;
            }
        }

        private static bool IsCtrlC(ConsoleKeyInfo key) =>
            key.Key == ConsoleKey.C && (key.Modifiers & ConsoleModifiers.Control) != 0;

        private void OnCancel(object? sender, ConsoleCancelEventArgs e)
        {
            e.Cancel = true;
            _stack.Clear();
        }
    }
}
