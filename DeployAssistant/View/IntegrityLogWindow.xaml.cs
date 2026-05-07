using DeployAssistant.Model;
using DeployAssistant.ViewModel;
using System.Collections.ObjectModel;
using System.Windows;

namespace DeployAssistant.View
{
    /// <summary>
    /// Interaction logic for IntegrityLogWindow.xaml
    /// </summary>
    public partial class IntegrityLogWindow : Window
    {
        public IntegrityLogWindow(ProjectData projData, string versionLog, ObservableCollection<ProjectFile> fileList)
        {
            InitializeComponent();
            var services = ((App)Application.Current).Services!;
            var versionCheckViewModel = new VersionCheckViewModel(services.MetaDataManager, projData, versionLog, fileList);
            this.DataContext = versionCheckViewModel;
            Closed += (_, _) => (versionCheckViewModel as IDisposable)?.Dispose();
        }

        public IntegrityLogWindow(ProjectData projectData)
        {
            InitializeComponent();
            var services = ((App)Application.Current).Services!;
            var versionCheckViewModel = new VersionCheckViewModel(services.MetaDataManager, projectData);
            this.DataContext = versionCheckViewModel;
            Closed += (_, _) => (versionCheckViewModel as IDisposable)?.Dispose();
        }

        private void FileFilterKeyword_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            BackupFileList.Items.Filter = FilterFilesMethod;
        }

        private bool FilterFilesMethod(object obj)
        {
            var file = (ProjectFile)obj;
            return file.DataName.Contains(FileFilterKeyword.Text, StringComparison.OrdinalIgnoreCase);
        }
    }
}
