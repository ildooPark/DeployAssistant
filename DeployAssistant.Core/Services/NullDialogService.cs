namespace DeployAssistant.Services
{
    /// <summary>
    /// Default IDialogService when no UI surface is available. Confirm always
    /// returns the supplied default; Inform / PickFolder / OpenInShell are no-ops.
    /// This is the public API for fallback wiring (scaffold ctors in ViewModels
    /// use it until Task 4 replaces them with proper AppServices wiring).
    /// </summary>
    public sealed class NullDialogService : IDialogService
    {
        public DialogChoice Confirm(string title, string message, DialogChoice defaultChoice = DialogChoice.No) => defaultChoice;
        public void Inform(string title, string message) { }
        public string? PickFolder(string title, string? initialPath = null) => null;
        public void OpenInShell(string path) { }
    }
}
