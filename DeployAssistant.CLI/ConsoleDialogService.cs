using System;
using DeployAssistant.Services;

namespace DeployAssistant.CLI
{
    /// <summary>
    /// IDialogService impl for the CLI. Confirm reads stdin (auto-yes when
    /// constructed with autoYes=true). Inform writes to stderr. PickFolder
    /// returns null — CLI commands accept paths via arguments.
    /// </summary>
    public sealed class ConsoleDialogService : IDialogService
    {
        private readonly bool _autoYes;

        public ConsoleDialogService(bool autoYes = false) => _autoYes = autoYes;

        public DialogChoice Confirm(string title, string message, DialogChoice defaultChoice = DialogChoice.No)
        {
            if (_autoYes) return DialogChoice.Yes;
            Console.Error.Write($"[{title}] {message} [y/N/c]: ");
            var line = Console.ReadLine()?.Trim().ToLowerInvariant();
            return line switch
            {
                "y" or "yes" => DialogChoice.Yes,
                "c" or "cancel" => DialogChoice.Cancel,
                _ => defaultChoice
            };
        }

        public void Inform(string title, string message)
            => Console.Error.WriteLine($"[{title}] {message}");

        public string? PickFolder(string title, string? initialPath = null)
        {
            Console.Error.WriteLine($"[{title}] CLI cannot pick a folder interactively. Pass the path as a command argument.");
            return null;
        }

        public void OpenInShell(string path)
            => Console.Error.WriteLine($"(would open in shell: {path})");
    }
}
