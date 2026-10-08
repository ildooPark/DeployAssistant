using System;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace DeployAssistant.CLI.Engine
{
    /// <summary>
    /// In-TUI Yes/No confirmation, drawn as a modal box at the bottom of the current frame and
    /// answered with a single key. Use this instead of ConsoleDialogService.Confirm (which
    /// writes to stderr and reads a full line — disruptive in TUI mode).
    /// </summary>
    internal static class TuiPrompt
    {
        /// <summary>
        /// Set by <see cref="App"/> while the TUI runs: draws the frame with the modal and
        /// returns the answering key. Null outside the app (tests), where the prompt falls back
        /// to writing inline.
        /// </summary>
        internal static Func<IRenderable, ConsoleKeyInfo>? ModalPresenter { get; set; }

        public static bool Confirm(string title, string message)
        {
            var presenter = ModalPresenter;
            if (presenter != null)
                return Interpret(presenter(BuildModal(title, message)));
            return Confirm(title, message, () => Console.ReadKey(intercept: true));
        }

        // Test seam: pass a custom keyReader.
        internal static bool Confirm(string title, string message, Func<ConsoleKeyInfo> readKey)
        {
            if (readKey == null) throw new ArgumentNullException(nameof(readKey));

            AnsiConsole.WriteLine();
            AnsiConsole.Write(BuildModal(title, message));
            return Interpret(readKey());
        }

        internal static IRenderable BuildModal(string title, string message)
        {
            string body =
                $"{Markup.Escape(message)}\n\n" +
                $"[black on grey] y [/] yes   [black on grey] n [/] no   [black on grey] esc [/] cancel";
            return new Panel(new Markup(body))
                .Header($" [yellow bold]{Glyphs.Warn} {Markup.Escape(Ui.Fit(title, Ui.Width - 8))}[/] ")
                .Border(Glyphs.Box)
                .BorderColor(Color.Yellow)
                .Expand();
        }

        private static bool Interpret(ConsoleKeyInfo key)
        {
            if (key.Key == ConsoleKey.Y) return true;
            if (key.Key == ConsoleKey.N) return false;
            if (key.Key == ConsoleKey.Escape) return false;
            return false; // default: treat any other key as "no"
        }
    }
}
