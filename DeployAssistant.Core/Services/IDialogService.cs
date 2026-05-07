namespace DeployAssistant.Services
{
    /// <summary>
    /// Abstracts user-facing dialog interactions so ViewModels are not coupled to
    /// System.Windows. WPF GUI provides WpfDialogService; CLI provides
    /// ConsoleDialogService; tests provide FakeDialogService.
    /// </summary>
    public interface IDialogService
    {
        /// <summary>Yes/No/Cancel confirmation dialog.</summary>
        DialogChoice Confirm(string title, string message, DialogChoice defaultChoice = DialogChoice.No);

        /// <summary>Informational dialog with OK button (or stderr write in CLI).</summary>
        void Inform(string title, string message);

        /// <summary>Folder picker. Returns null if the user cancels or the surface does not support it.</summary>
        string? PickFolder(string title, string? initialPath = null);

        /// <summary>Open a path in the platform shell (Explorer on Windows). No-op in headless contexts.</summary>
        void OpenInShell(string path);
    }
}
