using DeployAssistant.Model;
using DeployAssistant.ViewModel;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace DeployAssistant.View
{
    /// <summary>
    /// Interaction logic for VersionDiffWindow.xaml
    /// </summary>
    public partial class VersionDiffWindow : Window
    {
        public VersionDiffWindow(ProjectData srcProject, ProjectData dstProject, List<ChangedFile> diff, string title = "Version Diff")
        {
            InitializeComponent();
            Title = title;
            var services = ((App)Application.Current).Services!;
            var versionDiffVM = new VersionDiffViewModel(services.MetaDataManager, srcProject, dstProject, diff);
            this.DataContext = versionDiffVM;
            Closed += (_, _) => (versionDiffVM as IDisposable)?.Dispose();
        }

        private void FileFilterKeyword_TextChanged(object sender, TextChangedEventArgs e)
        {
            DiffItemsList.Items.Filter = FilterFilesMethod;
        }

        private bool FilterFilesMethod(object obj)
        {
            var file = (ChangedFile)obj;
            var name = file.SrcFile?.DataName ?? file.DstFile?.DataName ?? string.Empty;
            return name.Contains(FilterDiffInput.Text, StringComparison.OrdinalIgnoreCase);
        }
    }
}
