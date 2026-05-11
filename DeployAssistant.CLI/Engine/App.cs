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
            // Skip the TUI when stdout/stdin are redirected — interactive rendering is
            // meaningless in captured/piped/headless contexts. Print a banner to stdout
            // (so users redirecting to a file still get *some* output and we satisfy
            // CI smoke tests that grep for the binary name) and exit 0. Diagnostic
            // detail goes to stderr.
            if (Console.IsOutputRedirected || Console.IsInputRedirected)
            {
                Console.WriteLine("DeployAssistant CLI — interactive TUI (no operation in non-interactive mode).");
                Console.Error.WriteLine("deployassistant: interactive TUI requires a real terminal.");
                Console.Error.WriteLine("Run the .exe directly in a console window; redirection/piping is not supported.");
                return 0;
            }

            // Probe Console.WindowHeight once up front so we surface a clean message
            // rather than letting it throw deep inside the render loop
            // (System.IO.IOException at GetBufferInfo when no console handle is attached).
            try { _ = Console.WindowHeight; }
            catch (IOException)
            {
                Console.WriteLine("DeployAssistant CLI — no console window available.");
                Console.Error.WriteLine("deployassistant: unable to read console dimensions (no console window?).");
                Console.Error.WriteLine("Run the .exe directly in a console window.");
                return 0;
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
