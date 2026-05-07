using System.Diagnostics;
using System.Windows;
using DeployAssistant.Services;
using Microsoft.Win32;

namespace DeployAssistant.Services.Wpf
{
    public sealed class WpfDialogService : IDialogService
    {
        public DialogChoice Confirm(string title, string message, DialogChoice defaultChoice = DialogChoice.No)
        {
            var defaultBtn = defaultChoice switch
            {
                DialogChoice.Yes => MessageBoxResult.Yes,
                DialogChoice.Cancel => MessageBoxResult.Cancel,
                _ => MessageBoxResult.No
            };
            var result = MessageBox.Show(message, title, MessageBoxButton.YesNoCancel, MessageBoxImage.Question, defaultBtn);
            return result switch
            {
                MessageBoxResult.Yes => DialogChoice.Yes,
                MessageBoxResult.No => DialogChoice.No,
                _ => DialogChoice.Cancel
            };
        }

        public void Inform(string title, string message)
            => MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);

        public string? PickFolder(string title, string? initialPath = null)
        {
            var dlg = new OpenFolderDialog { Title = title };
            if (!string.IsNullOrEmpty(initialPath)) dlg.InitialDirectory = initialPath;
            return dlg.ShowDialog() == true ? dlg.FolderName : null;
        }

        public void OpenInShell(string path)
        {
            try { Process.Start(new ProcessStartInfo("explorer.exe", path) { UseShellExecute = true }); }
            catch { /* shell invocation must not crash the app */ }
        }
    }
}
