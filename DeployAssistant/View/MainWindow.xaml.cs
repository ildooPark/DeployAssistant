using DeployAssistant.Model;
using DeployAssistant.ViewModel;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;

namespace DeployAssistant.View
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private static readonly string OrphanedLayoutFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "DeployAssistant.layout");

        public MainWindow()
        {
            InitializeComponent();

            ShowVersionInTitle();
            RemoveOrphanedLayoutFile();

            var services = ((App)Application.Current).Services!;
            var mainVM = new MainViewModel(services);
            SubscribeToViewModelEvents(mainVM);
            this.DataContext = mainVM;

            LanguageSelect.SelectedIndex = ((App)Application.Current).CurrentLanguage == "en-US" ? 1 : 0;
            _languageSelectReady = true;

            mainVM.MetaDataVM.BusyLog.CollectionChanged += (_, _) => BusyLogScroll.ScrollToEnd();

            Loaded += (_, _) => services.MetaDataManager.RequestPreviousProjectRestore();
        }

        private bool _languageSelectReady;

        private void ProjectMenu_SubmenuOpened(object sender, RoutedEventArgs e)
        {
            var services = ((App)Application.Current).Services;
            if (services == null) return;
            RecentProjectsMenu.Items.Clear();
            var recents = services.MetaDataManager.RequestRecentProjects();
            if (recents.Count == 0)
            {
                RecentProjectsMenu.Items.Add(new System.Windows.Controls.MenuItem
                {
                    Header = TryFindResource("S.Menu.NoRecent") as string ?? "(none)",
                    IsEnabled = false
                });
                return;
            }
            foreach (string path in recents)
            {
                var item = new System.Windows.Controls.MenuItem { Header = path, ToolTip = path };
                string captured = path;
                item.Click += (_, _) => (DataContext as MainViewModel)?.MetaDataVM.OpenProjectPath(captured);
                RecentProjectsMenu.Items.Add(item);
            }
        }

        private void SettingsMenu_SubmenuOpened(object sender, RoutedEventArgs e)
        {
            var services = ((App)Application.Current).Services;
            if (services == null) return;
            int current = services.MetaDataManager.RequestFastIntegritySamplePercent();
            foreach (var item in new[] { Sample100, Sample50, Sample25, Sample10 })
                item.IsChecked = item.Tag is string tag && int.TryParse(tag, out int v) && v == current;
        }

        private void SamplePercent_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.MenuItem item || item.Tag is not string tag) return;
            if (!int.TryParse(tag, out int percent)) return;
            ((App)Application.Current).Services?.MetaDataManager.RequestSaveFastIntegritySamplePercent(percent);
        }

        private void MenuExit_Click(object sender, RoutedEventArgs e) => Close();

        private void LanguageSelect_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (!_languageSelectReady) return;
            if (LanguageSelect.SelectedItem is not System.Windows.Controls.ComboBoxItem item || item.Tag is not string code) return;
            var app = (App)Application.Current;
            app.ApplyLanguage(code);
            app.Services?.MetaDataManager.RequestSaveLanguage(code);
        }

        private void SubscribeToViewModelEvents(MainViewModel mainVM)
        {
            mainVM.FileTrackVM.OverlapWindowRequested += OpenOverlapFileWindow;
            mainVM.FileTrackVM.IntegrityLogWindowRequested += OpenIntegrityLogWindowFromFileTrack;
            mainVM.FileTrackVM.VersionDiffWindowRequested += OpenVersionDiffWindow;
            mainVM.FileTrackVM.VersionComparisonWindowRequested += OpenVersionComparisonWindow;
            mainVM.FileTrackVM.SrcProjectInfoWindowRequested += OpenSrcProjectInfoWindow;

            mainVM.BackupVM.IntegrityLogWindowRequested += OpenIntegrityLogWindowFromBackup;
        }

        // ------------------------------------------------------------------ //
        //  Window lifecycle                                                   //
        // ------------------------------------------------------------------ //

        /// <summary>Appends the assembly version to the title so the running build is visible.</summary>
        private void ShowVersionInTitle()
        {
            Version? version = Assembly.GetExecutingAssembly().GetName().Version;
            if (version != null) Title = $"Deploy Assistant  v{version.ToString(3)}";
        }

        /// <summary>
        /// The docking layout was dropped; delete the layout file older builds left in Documents.
        /// </summary>
        private static void RemoveOrphanedLayoutFile()
        {
            try
            {
                if (File.Exists(OrphanedLayoutFilePath)) File.Delete(OrphanedLayoutFilePath);
            }
            catch (Exception ex)
            {
                // Best-effort cleanup; a leftover file is harmless and must not block startup.
                Debug.WriteLine($"[DeployAssistant] Could not remove orphaned layout file: {ex.Message}");
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
            var outcomes = (DataContext as MainViewModel)?.FileTrackVM.IntegrityOutcomesSnapshot;
            var window = new IntegrityLogWindow(projData, changeLog, fileList, outcomes);
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

        private const string VersionLogExtension = ".VersionLog";

        // ------------------------------------------------------------------ //
        //  Metafile Compare drag-drop                                       //
        // ------------------------------------------------------------------ //

        private void MetaFileDropZone_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (files?.Any(f => f.EndsWith(VersionLogExtension, StringComparison.OrdinalIgnoreCase)) == true)
                {
                    e.Effects = DragDropEffects.Copy;
                    e.Handled = true;
                    return;
                }
            }
            e.Effects = DragDropEffects.None;
            e.Handled = true;
        }

        private void MetaFileDropZone_Drop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files == null || files.Length == 0) return;

            string? versionLogFile = files.FirstOrDefault(
                f => f.EndsWith(VersionLogExtension, StringComparison.OrdinalIgnoreCase));
            if (versionLogFile == null) return;

            (DataContext as MainViewModel)?.MetaFileDiffVM.LoadMetaFileFromPath(versionLogFile);
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
