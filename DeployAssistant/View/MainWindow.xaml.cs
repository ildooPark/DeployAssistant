using AvalonDock.Layout.Serialization;
using DeployAssistant.Model;
using DeployAssistant.ViewModel;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;

namespace DeployAssistant.View
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private static readonly string LayoutFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "DeployAssistant.layout");

        public MainWindow()
        {
            InitializeComponent();

            // Boot the core service layer before constructing ViewModels.
            App.AwakeModel();

            var mainVM = new MainViewModel(App.MetaDataManager);
            SubscribeToViewModelEvents(mainVM);
            this.DataContext = mainVM;
        }

        private void SubscribeToViewModelEvents(MainViewModel mainVM)
        {
            mainVM.FileTrackVM.OverlapWindowRequested += OpenOverlapFileWindow;
            mainVM.FileTrackVM.IntegrityLogWindowRequested += OpenIntegrityLogWindowFromFileTrack;
            mainVM.FileTrackVM.VersionDiffWindowRequested += OpenVersionDiffWindow;
            mainVM.FileTrackVM.VersionComparisonWindowRequested += OpenVersionComparisonWindow;
            mainVM.FileTrackVM.SrcProjectInfoWindowRequested += OpenSrcProjectInfoWindow;

            mainVM.BackupVM.IntegrityLogWindowRequested += OpenIntegrityLogWindowFromBackup;
            mainVM.BackupVM.VersionDiffWindowRequested += OpenVersionDiffWindow;
        }

        // ------------------------------------------------------------------ //
        //  Window lifecycle                                                   //
        // ------------------------------------------------------------------ //

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            if (File.Exists(LayoutFilePath))
            {
                try
                {
                    var serializer = new XmlLayoutSerializer(DockManager);
                    serializer.Deserialize(LayoutFilePath);
                }
                catch
                {
                    // Layout file is invalid or from a different version; fall back to default.
                }
            }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            try
            {
                var serializer = new XmlLayoutSerializer(DockManager);
                serializer.Serialize(LayoutFilePath);
            }
            catch
            {
                // Best-effort; ignore save failures.
            }
        }

        // ------------------------------------------------------------------ //
        //  Secondary window openers                                           //
        // ------------------------------------------------------------------ //

        private void OpenOverlapFileWindow(List<ChangedFile> overlapped, List<ChangedFile> newFiles)
        {
            var window = new OverlapFileWindow(overlapped, newFiles);
            window.Owner = this;
            window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            window.Show();
        }

        private void OpenIntegrityLogWindowFromFileTrack(ProjectData? projData, string changeLog, ObservableCollection<ProjectFile> fileList)
        {
            if (projData == null) return;
            var window = new IntegrityLogWindow(projData, changeLog, fileList);
            window.Owner = this;
            window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            window.Show();
        }

        private void OpenIntegrityLogWindowFromBackup(ProjectData? projData)
        {
            if (projData == null) return;
            var window = new IntegrityLogWindow(projData);
            window.Owner = this;
            window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            window.Show();
        }

        private void OpenVersionDiffWindow(ProjectData srcProject, ProjectData dstProject, List<ChangedFile> diff)
        {
            var window = new VersionDiffWindow(srcProject, dstProject, diff);
            window.Owner = this;
            window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            window.Show();
        }

        private void OpenVersionComparisonWindow(ProjectData srcData, List<ProjectSimilarity> similarities)
        {
            var window = new VersionComparisonWindow(srcData, similarities);
            window.Owner = this;
            window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            window.Show();
        }

        private void OpenSrcProjectInfoWindow(ProjectData? projData)
        {
            if (projData == null) return;
            var window = new IntegrityLogWindow(projData);
            window.Owner = this;
            window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            window.Show();
        }

        // ------------------------------------------------------------------ //
        //  Filter handler for the Project Files panel                        //
        // ------------------------------------------------------------------ //

        private void FileFilterKeyword_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            ProjectMainFileList.Items.Filter = FilterFilesMethod;
        }

        private bool FilterFilesMethod(object obj)
        {
            var file = (ProjectFile)obj;
            return file.DataName.Contains(FileFilterKeyword.Text, StringComparison.OrdinalIgnoreCase);
        }
    }
}
