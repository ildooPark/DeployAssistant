using System;
using Spectre.Console;

namespace DeployAssistant.CLI.Engine
{
    /// <summary>
    /// In-TUI Yes/No confirmation. Renders inline (preserves the Spectre buffer)
    /// and reads a single key. Use this instead of ConsoleDialogService.Confirm
    /// (which writes to stderr and reads a full line — disruptive in TUI mode).
    /// </summary>
    internal static class TuiPrompt
    {
        public static bool Confirm(string title, string message)
            => Confirm(title, message, () => Console.ReadKey(intercept: true));

        // Test seam: pass a custom keyReader.
        internal static bool Confirm(string title, string message, Func<ConsoleKeyInfo> readKey)
        {
            if (readKey == null) throw new ArgumentNullException(nameof(readKey));

            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"  [yellow]![/] [bold]{Markup.Escape(title)}[/]  {Markup.Escape(message)}");
            AnsiConsole.MarkupLine(TextStyle.Dim("  [[y]] yes  ·  [[n]] no  ·  esc cancel"));

            var key = readKey();
            if (key.Key == ConsoleKey.Y) return true;
            if (key.Key == ConsoleKey.N) return false;
            if (key.Key == ConsoleKey.Escape) return false;
            return false; // default: treat any other key as "no"
        }
    }
}
