using DeployAssistant.Model;
using DeployAssistant.ViewModel;
using System.Collections.Generic;
using System.Windows;

namespace DeployAssistant.View
{
    /// <summary>
    /// Pre-checkout gate — the WPF counterpart of the CLI's CheckoutGateScreen.
    /// <para>
    /// A checkout overwrites the working directory, so the user is never asked a plain
    /// "Yes/No". When the pre-checkout integrity check found local modifications the
    /// window lists them and the confirm button spells out that they will be discarded;
    /// Cancel is the default button on both paths.
    /// </para>
    /// </summary>
    public partial class CheckoutGateWindow : Window
    {
        private CheckoutGateWindow(ProjectData target, CheckoutMode mode, IReadOnlyList<ProjectFile> modifications)
        {
            InitializeComponent();

            string versionName = string.IsNullOrEmpty(target.UpdatedVersion) ? Loc.T("S.Unnamed", "(unnamed)") : target.UpdatedVersion;
            string modeName = mode == CheckoutMode.CleanRestore
                ? Loc.T("S.SafeCheckout", "Safe checkout")
                : Loc.T("S.Checkout", "Checkout");
            string updatedBy = string.Format(Loc.T("S.Gate.UpdatedBy", "Updated {0:yyyy-MM-dd HH:mm} by {1}."),
                                             target.UpdatedTime, target.UpdaterName);

            if (modifications.Count == 0)
            {
                HeadlineText.Text = string.Format(Loc.T("S.Gate.HeadlineClean", "{0} to '{1}'?"), modeName, versionName);
                DetailText.Text = updatedBy + "\n" +
                                  Loc.T("S.Gate.CleanDetail", "The working directory matches the current version — nothing will be lost.");
                CleanText.Visibility = Visibility.Visible;
                ModificationList.Visibility = Visibility.Collapsed;
                ProceedButton.Content = modeName;
            }
            else
            {
                HeadlineText.Text = string.Format(Loc.T("S.Gate.HeadlineDirty", "Local modifications detected — {0} to '{1}'?"), modeName, versionName);
                DetailText.Text = updatedBy + "\n" +
                                  string.Format(Loc.T("S.Gate.DirtyDetail",
                                      "{0} uncommitted change(s) in the working directory will be DISCARDED and cannot be recovered. Stage and deploy them first if you want to keep them."),
                                      modifications.Count);
                ModificationList.ItemsSource = modifications;
                ProceedButton.Content = Loc.T("S.Gate.DiscardAndCheckout", "Discard Local Changes and Check Out");
            }
        }

        /// <summary>
        /// Shows the gate modally and returns <c>true</c> only when the user explicitly
        /// confirmed. Returns <c>false</c> when there is no WPF application (headless),
        /// so a caller can never be blocked into checking out unattended.
        /// </summary>
        internal static bool Ask(ProjectData target, CheckoutMode mode, IReadOnlyList<ProjectFile> modifications)
        {
            if (Application.Current == null) return false;
            var window = new CheckoutGateWindow(target, mode, modifications)
            {
                Owner = Application.Current.MainWindow
            };
            return window.ShowDialog() == true;
        }

        private void Proceed_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }
    }
}
