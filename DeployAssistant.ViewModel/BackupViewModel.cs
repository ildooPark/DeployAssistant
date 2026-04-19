using DeployAssistant.DataComponent;
using DeployAssistant.Model;
using DeployAssistant.ViewModel.Utils;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Input;

namespace DeployAssistant.ViewModel
{
    public class BackupViewModel : ViewModelBase
    {
        #region Window-request events
        /// <summary>Raised when the integrity-log window should be opened for a backup version.</summary>
        public event Action<ProjectData?>? IntegrityLogWindowRequested;
        /// <summary>Raised when the version-diff window should be opened.</summary>
        public event Action<ProjectData, ProjectData, List<ChangedFile>>? VersionDiffWindowRequested;
        #endregion

        private ObservableCollection<ProjectData>? _backupProjectDataList;
        public ObservableCollection<ProjectData> BackupProjectDataList
        {
            get => _backupProjectDataList ??= new ObservableCollection<ProjectData>();
            set
            {
                _backupProjectDataList = value;
                OnPropertyChanged(nameof(BackupProjectDataList));
            }
        }

        private ProjectData? _selectedItem;
        public ProjectData? SelectedItem
        {
            get { return _selectedItem; }
            set
            {
                if (value == null) return;
                _selectedItem = value;
                UpdaterName = value.UpdaterName;
                UpdateLog = value.UpdateLog;
                DiffLog = value.ChangedProjectFileObservable;
                OnPropertyChanged(nameof(SelectedItem));
            }
        }

        private ICommand? _fetchBackup;
        public ICommand FetchBackup => _fetchBackup ??= new RelayCommand(Fetch, CanFetch);

        private ICommand? _checkoutBackup;
        public ICommand CheckoutBackup => _checkoutBackup ??= new RelayCommand(Revert, CanRevert);

        private ICommand? _exportVersion;
        public ICommand ExportVersion => _exportVersion ??= new RelayCommand(ExportBackupFiles, CanExportBackupFiles);

        private ICommand? _cleanRestoreBackup;
        public ICommand CleanRestoreBackup => _cleanRestoreBackup ??= new RelayCommand(CleanRestoreBackupFiles, CanCleanRestoreBackupFiles);

        private ICommand? _extractVersionLog;
        public ICommand ExtractVersionLog => _extractVersionLog ??= new RelayCommand(ExtractVersionMetaData);

        private ICommand? _viewFullLog;
        public ICommand ViewFullLog => _viewFullLog ??= new RelayCommand(OnViewFullLog, CanRevert);

        private ICommand? _compareDeployedProjectWithMain;
        public ICommand? CompareDeployedProjectWithMain => _compareDeployedProjectWithMain ??= new RelayCommand(CompareSrcProjWithMain, CanCompareSrcProjWithMain);

        private string? _updaterName;
        public string UpdaterName
        {
            get => _updaterName ??= "";
            set
            {
                _updaterName = value;
                OnPropertyChanged(nameof(UpdaterName));
            }
        }

        private string? _updateLog;
        public string UpdateLog
        {
            get => _updateLog ??= "";
            set
            {
                _updateLog = value;
                OnPropertyChanged(nameof(UpdateLog));
            }
        }

        private ObservableCollection<ProjectFile>? _diffLog;
        public ObservableCollection<ProjectFile> DiffLog
        {
            get => _diffLog ??= new ObservableCollection<ProjectFile>();
            set
            {
                _diffLog = value;
                OnPropertyChanged(nameof(DiffLog));
            }
        }

        private readonly MetaDataManager _metaDataManager;
        private MetaDataState? _metaDataState = MetaDataState.Idle;

        public BackupViewModel(MetaDataManager metaDataManager)
        {
            _metaDataManager = metaDataManager;
            _metaDataManager.FetchRequestEventHandler += FetchRequestCallBack;
            _metaDataManager.ProjExportEventHandler += ExportRequestCallBack;
            _metaDataManager.ManagerStateEventHandler += MetaDataStateChangeCallBack;
            _metaDataManager.ProjComparisonCompleteEventHandler += ProjComparisonCompleteCallBack;
        }

        private bool CanFetch(object obj)
        {
            if (_metaDataManager.ProjectMetaData == null) return false;
            return true;
        }

        private void Fetch(object obj)
        {
            SelectedItem = null;
            if (_metaDataManager.CurrentProjectPath == null || _metaDataManager.ProjectMetaData == null) return;
            _metaDataManager.RequestFetchBackup();
        }

        private void CompareSrcProjWithMain(object obj)
        {
            _metaDataManager.RequestProjVersionDiff(SelectedItem);
        }

        private bool CanCompareSrcProjWithMain(object obj)
        {
            if (_metaDataState != MetaDataState.Idle) return false;
            if (SelectedItem == null) return false;
            return true;
        }

        private void OnViewFullLog(object obj)
        {
            if (SelectedItem == null)
            {
                MessageBox.Show("Couldn't Get the Log, Selected Item is Null");
                return;
            }
            IntegrityLogWindowRequested?.Invoke(SelectedItem);
        }

        private bool CanRevert(object obj)
        {
            if (SelectedItem == null || _metaDataManager.MainProjectData == null) return false;
            if (_metaDataState != MetaDataState.Idle) return false;
            return true;
        }

        private void Revert(object obj)
        {
            if (_selectedItem == null)
            {
                MessageBox.Show("BUVM 164: Selected BackupVersion is null");
                return;
            }
            var response = MessageBox.Show($"Do you want to Revert to {_selectedItem.UpdatedVersion}", "Confirm Updates",
                MessageBoxButtons.YesNo);
            if (response == DialogResult.Yes)
            {
                _metaDataManager.RequestRevertProject(_selectedItem);
            }
        }

        private bool CanCleanRestoreBackupFiles(object obj)
        {
            return _metaDataState == MetaDataState.Idle;
        }

        private void CleanRestoreBackupFiles(object? obj)
        {
            if (SelectedItem == null)
            {
                MessageBox.Show("Must Select Certain Backup For Clean Backup Restoration");
                return;
            }
            var response = MessageBox.Show($"Would You like to Restore back to Version: {SelectedItem.UpdatedVersion}\n " +
                $"This may take longer than regular version Checkout", "Clean Restore", MessageBoxButtons.YesNo);

            if (response == DialogResult.Yes)
            {
                Task.Run(() => _metaDataManager.RequestProjectCleanRestore(SelectedItem));
            }
        }

        private bool CanExportBackupFiles(object obj)
        {
            return _metaDataState == MetaDataState.Idle;
        }

        private void ExportBackupFiles(object? obj)
        {
            if (SelectedItem == null)
            {
                MessageBox.Show("Must Select Certain Backup For Clean Backup Restoration");
                return;
            }
            Task.Run(() => _metaDataManager.RequestExportProjectBackup(SelectedItem));
        }

        private void ExtractVersionMetaData(object? obj)
        {
            if (SelectedItem == null)
            {
                MessageBox.Show("Must Select Certain Backup For Clean Backup Restoration");
                return;
            }
            _metaDataManager.RequestExportProjectVersionLog(SelectedItem);
        }

        #region Callbacks From Model Events

        private void FetchRequestCallBack(object backupListObj)
        {
            if (backupListObj is not ObservableCollection<ProjectData> backupList) return;
            BackupProjectDataList = backupList;
        }

        private void ExportRequestCallBack(object exportPathObj)
        {
            if (exportPathObj is not string exportPath) return;
            try
            {
                Process.Start("explorer.exe", exportPath);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"{exportPath} does not Exists! : ERROR: {ex.Message}");
            }
        }

        private void MetaDataStateChangeCallBack(MetaDataState state)
        {
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                _metaDataState = state;
                System.Windows.Application.Current?.MainWindow?.UpdateLayout();
            });
        }

        private void ProjComparisonCompleteCallBack(ProjectData srcProject, ProjectData dstProject, List<ChangedFile> diff)
        {
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                VersionDiffWindowRequested?.Invoke(srcProject, dstProject, diff);
            });
        }

        #endregion
    }
}
