using System;
using System.Collections.Generic;
using Spectre.Console;

namespace DeployAssistant.CLI.Engine
{
    internal sealed class App
    {
        private readonly Stack<Screen> _stack = new Stack<Screen>();

        public int Run(Screen root)
        {
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

                    if (Console.WindowHeight < 10)
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
                    _stack.Pop();
                    return null;

                case ScreenAction.Push push:
                    _stack.Push(push.Next);
                    return null;

                case ScreenAction.Replace replace:
                    _stack.Pop();
                    _stack.Push(replace.Next);
                    return null;

                case ScreenAction.Exit:
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
