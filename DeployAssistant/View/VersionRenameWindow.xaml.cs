using DeployAssistant.Model;
using DeployAssistant.ViewModel;
using System.Windows;

namespace DeployAssistant.View
{
    /// <summary>
    /// Small editor for a stored version's tag and update log, pre-filled with the
    /// current values. Validation is deliberately left to
    /// <c>MetaDataManager.RequestRenameVersion</c> (duplicate tags, invalid path
    /// characters, trailing whitespace) so the GUI and the CLI reject exactly the same
    /// names; the rejection reason is reported back through
    /// <c>VersionRenameCompleteEventHandler</c>.
    /// </summary>
    public partial class VersionRenameWindow : Window
    {
        private VersionRenameWindow(ProjectData target)
        {
            InitializeComponent();

            string versionName = string.IsNullOrEmpty(target.UpdatedVersion) ? Loc.T("S.Unnamed", "(unnamed)") : target.UpdatedVersion;
            HeadlineText.Text = string.Format(Loc.T("S.Ren.Headline", "Rename / re-tag '{0}'"), versionName);
            VersionNameBox.Text = target.UpdatedVersion;
            UpdateLogBox.Text = target.UpdateLog;

            Loaded += (_, _) =>
            {
                VersionNameBox.Focus();
                VersionNameBox.SelectAll();
            };
        }

        /// <summary>
        /// Shows the editor modally. Returns <c>true</c> when the user applied a change,
        /// with the edited values in the out parameters. Returns <c>false</c> when
        /// cancelled or when there is no WPF application (headless).
        /// </summary>
        internal static bool Prompt(ProjectData target, out string versionName, out string updateLog)
        {
            versionName = target.UpdatedVersion;
            updateLog = target.UpdateLog;
            if (Application.Current == null) return false;

            var window = new VersionRenameWindow(target)
            {
                Owner = Application.Current.MainWindow
            };
            if (window.ShowDialog() != true) return false;

            versionName = window.VersionNameBox.Text;
            updateLog = window.UpdateLogBox.Text;
            return true;
        }

        private void Apply_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }
    }
}
