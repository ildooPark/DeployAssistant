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
            var versionCheckViewModel = new VersionCheckViewModel(App.MetaDataManager, projData, versionLog, fileList);
            this.DataContext = versionCheckViewModel;
        }

        public IntegrityLogWindow(ProjectData projectData)
        {
            InitializeComponent();
            var versionCheckViewModel = new VersionCheckViewModel(App.MetaDataManager, projectData);
            this.DataContext = versionCheckViewModel;
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
