using DeployAssistant.Model;
using DeployAssistant.ViewModel;
using System.Windows;

namespace DeployAssistant.View
{
    /// <summary>
    /// Explicit confirmation for the one irreversible action in the version list.
    /// <para>
    /// Shows the blast radius <c>BackupManager.BuildVersionDeletePlan</c> computed —
    /// reclaimable bytes, how many backup blobs disappear, how many have to be relocated
    /// first — and names the version in the headline. When the plan carries blockers the
    /// delete button is disabled and the reasons are listed instead. Cancel is the
    /// default button; the delete button never is.
    /// </para>
    /// </summary>
    public partial class VersionDeleteConfirmWindow : Window
    {
        private VersionDeleteConfirmWindow(VersionDeletePlan plan)
        {
            InitializeComponent();

            string versionName = string.IsNullOrEmpty(plan.VersionName) ? Loc.T("S.Unnamed", "(unnamed)") : plan.VersionName;
            HeadlineText.Text = string.Format(Loc.T("S.Del.Headline", "Delete version '{0}'?"), versionName);
            WarningText.Text = Loc.T("S.Del.Warning",
                "This permanently removes the version from the history and deletes the backup files only it references. It cannot be undone.");

            string fileCountFormat = Loc.T("S.Del.FileCount", "{0} file(s)");
            ReclaimableText.Text = BackupViewModel.FormatBytes(plan.ReclaimableBytes);
            ExclusiveText.Text = string.Format(fileCountFormat, plan.ExclusiveHashes.Count);
            RelocationText.Text = string.Format(Loc.T("S.Del.RelocationCount", "{0} file(s) shared with a surviving version"), plan.RelocationHashes.Count);
            SharedText.Text = string.Format(fileCountFormat, plan.SharedHashes.Count);
            BackupFolderText.Text = plan.BackupFolderRemovable
                ? plan.BackupFolderPath
                : string.Format(Loc.T("S.Del.FolderKept", "{0} (kept)"), plan.BackupFolderPath);

            DeleteButton.Content = Loc.T("S.Del.DeleteButton", "Delete Version");
            if (!plan.CanDelete)
            {
                DeleteButton.IsEnabled = false;
                BlockerList.ItemsSource = plan.Blockers;
                BlockerPanel.Visibility = Visibility.Visible;
            }
        }

        /// <summary>
        /// Shows the confirmation modally. Returns <c>true</c> only on an explicit
        /// confirm; <c>false</c> when cancelled, when the plan is blocked, or when there
        /// is no WPF application (headless).
        /// </summary>
        internal static bool Confirm(VersionDeletePlan plan)
        {
            if (Application.Current == null) return false;
            var window = new VersionDeleteConfirmWindow(plan)
            {
                Owner = Application.Current.MainWindow
            };
            return window.ShowDialog() == true;
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }
    }
}
