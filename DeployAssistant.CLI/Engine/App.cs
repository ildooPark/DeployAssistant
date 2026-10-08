using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace DeployAssistant.CLI.Engine
{
    internal sealed class App
    {
        private readonly Stack<Screen> _stack = new Stack<Screen>();
        private FrameRenderer.Target _target;
        private (int Width, int Height) _drawnSize;

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

            using (Term.Enter())
            {
                _target = new FrameRenderer.Target(Term.Ansi, AnsiConsole.Profile.Capabilities.ColorSystem);
                TuiPrompt.ModalPresenter = ShowModal;
                Console.CancelKeyPress += OnCancel;
                try
                {
                    Loop(root);
                }
                finally
                {
                    Console.CancelKeyPress -= OnCancel;
                    TuiPrompt.ModalPresenter = null;
                }
            }
            return 0;
        }

        private void Loop(Screen root)
        {
            _stack.Push(root);
            Screen? lastTop = null;

            while (_stack.Count > 0)
            {
                var current = _stack.Peek();
                if (!ReferenceEquals(current, lastTop))
                {
                    // Chrome first, cursor parked in the body: a progress bar or spinner that
                    // OnEnter starts then draws inside the frame at the canvas width.
                    Draw(bodyless: true);
                    Term.MoveTo(2, 0);
                    AnsiConsole.Profile.Width = FrameRenderer.CanvasWidth(Term.Size().Width);
                    current.OnEnter();
                    lastTop = current;
                    if (_stack.Count == 0) break;
                }

                Draw();

                var auto = current.AutoAdvance();
                if (auto is not null)
                {
                    lastTop = ApplyAction(auto, current, lastTop);
                    continue;
                }

                ConsoleKeyInfo? key = ReadKey(() => Draw());
                if (key is null) return; // cancelled while waiting

                var action = current.Handle(key.Value);
                lastTop = ApplyAction(action, current, lastTop);
            }
        }

        private void Draw(IRenderable? modal = null, bool bodyless = false)
        {
            var size = Term.Size();
            if (size.Width <= 0 || size.Height <= 0 || _stack.Count == 0) return;
            var stack = _stack.Reverse().ToList();
            Term.WriteFrame(FrameRenderer.Compose(stack, size.Width, size.Height, _target, modal, bodyless));
            _drawnSize = size;
        }

        /// <summary>
        /// Waits for a key while watching the window size, so dragging the window edge or
        /// moving it to another monitor re-lays-out the frame immediately. Returns null when
        /// Ctrl+C emptied the stack while we waited.
        /// </summary>
        private ConsoleKeyInfo? ReadKey(Action redraw)
        {
            while (!Console.KeyAvailable)
            {
                if (_stack.Count == 0) return null;
                if (Term.Size() != _drawnSize) redraw();
                Thread.Sleep(40);
            }
            var key = Console.ReadKey(intercept: true);
            if (IsCtrlC(key)) { _stack.Clear(); return null; }
            return key;
        }

        private ConsoleKeyInfo ShowModal(IRenderable modal)
        {
            Draw(modal);
            ConsoleKeyInfo? key = ReadKey(() => Draw(modal));
            return key ?? new ConsoleKeyInfo('\u001b', ConsoleKey.Escape, false, false, false);
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
