using System.Collections.Generic;
using DeployAssistant.Services;

namespace DeployAssistant.Tests.Fakes
{
    /// <summary>
    /// Programmable IDialogService for headless tests.
    /// Queue Confirm answers via <see cref="EnqueueConfirm"/> / <see cref="EnqueueFolder"/>.
    /// Inspect <see cref="ShownInfos"/> / <see cref="OpenedShellPaths"/> for assertions.
    /// </summary>
    public sealed class FakeDialogService : IDialogService
    {
        private readonly Queue<DialogChoice> _confirmAnswers = new();
        private readonly Queue<string?> _folderAnswers = new();

        public List<(string Title, string Message)> ShownInfos { get; } = new();
        public List<string> OpenedShellPaths { get; } = new();

        public void EnqueueConfirm(DialogChoice answer) => _confirmAnswers.Enqueue(answer);
        public void EnqueueFolder(string? answer) => _folderAnswers.Enqueue(answer);

        public DialogChoice Confirm(string title, string message, DialogChoice defaultChoice = DialogChoice.No)
            => _confirmAnswers.Count > 0 ? _confirmAnswers.Dequeue() : defaultChoice;

        public void Inform(string title, string message) => ShownInfos.Add((title, message));

        public string? PickFolder(string title, string? initialPath = null)
            => _folderAnswers.Count > 0 ? _folderAnswers.Dequeue() : null;

        public void OpenInShell(string path) => OpenedShellPaths.Add(path);
    }
}
