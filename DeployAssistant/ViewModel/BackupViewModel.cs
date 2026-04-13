using DeployAssistant.DataComponent;
using DeployAssistant.Model;
using DeployAssistant.Utils;
using DeployAssistant.View;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Input;
using System.Windows.Threading;
using WPF = System.Windows;

namespace DeployAssistant.ViewModel
{
    public class BackupViewModel : ViewModelBase
    {
        /// <summary>
        /// Aligns all the project in order, such that version with the highest revision number 
        /// is listed as the Newest Version. 
        /// </summary>
        private ObservableCollection<ProjectData>? _backupProjectDataList;
        public ObservableCollection<ProjectData> BackupProjectDataList
        {
            get => _backupProjectDataList ??= new ObservableCollection<ProjectData>();
            set
            {
                _backupProjectDataList = value;
                OnPropertyChanged("BackupProjectDataList");
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
                OnPropertyChanged("SelectedItem");
            }
        }

        private ICommand? fetchBackup;
        public ICommand FetchBackup => fetchBackup ??= new RelayCommand(Fetch, CanFetch);

        private ICommand? checkoutBackup;
        public ICommand CheckoutBackup => checkoutBackup ??= new RelayCommand(Revert, CanRevert);

        private ICommand? exportVersion;
        public ICommand ExportVersion => exportVersion ??= new RelayCommand(ExportBackupFiles, CanExportBackupFiles);

        

        private ICommand? cleanRestoreBackup;
        public ICommand CleanRestoreBackup => cleanRestoreBackup ??= new RelayCommand(CleanRestoreBackupFiles, CanCleanRestoreBackupFiles);
        

        private ICommand? extractVersionLog;
        public ICommand ExtractVersionLog => extractVersionLog ??= new RelayCommand(ExtractVersionMetaData);

        private ICommand? viewFullLog;
        public ICommand ViewFullLog => viewFullLog ??= new RelayCommand(OnViewFullLog, CanRevert);

        private ICommand? compareDeployedProjectWithMain;
        public ICommand? CompareDeployedProjectWithMain => compareDeployedProjectWithMain ??= new RelayCommand(CompareSrcProjWithMain, CanCompareSrcProjWithMain);
        

        private string? _updaterName;
        public string UpdaterName
        {
            get => _updaterName ??= "";
            set
            {
                _updaterName = value;
                OnPropertyChanged("UpdaterName");
            }
        }

        private string? _updateLog;
        public string UpdateLog
        {
            get => _updateLog ??= "";
            set
            {
                _updateLog = value;
                OnPropertyChanged("UpdateLog");
            }
        }

        private ObservableCollection<ProjectFile>? _diffLog;
        public ObservableCollection<ProjectFile> DiffLog
        {
            get => _diffLog ??= new ObservableCollection<ProjectFile>();
            set
            {
                _diffLog = value;
                OnPropertyChanged("DiffLog");
            }
        }

        private MetaDataManager _metaDataManager;
        private MetaDataState? _metaDataState = MetaDataState.Idle;
        public BackupViewModel()
        {
            this._metaDataManager = App.MetaDataManager;
            this._metaDataManager.FetchRequestEventHandler += FetchRequestCallBack;
            this._metaDataManager.ProjExportEventHandler += ExportRequestCallBack;
            this._metaDataManager.ManagerStateEventHandler += MetaDataStateChangeCallBack;
        }

        private bool CanFetch(object obj)
        {
            if (App.Current == null || _metaDataManager.ProjectMetaData == null) return false;
            return true;
        }
        private void Fetch(object obj)
        {
            SelectedItem = null;
            //Set up Current Project at Main 
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
            var mainWindow = obj as WPF.Window;
            IntegrityLogWindow logWindow = new IntegrityLogWindow(SelectedItem);
            logWindow.Owner = mainWindow;
            logWindow.WindowStartupLocation = WPF.WindowStartupLocation.CenterOwner;
            logWindow.Show();
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
                return;
            }
            else
            {
                return;
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
            else
            {
                return;
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

        #region CallBack From Model Events 
        private void ProjectLoadedCallBack(object? obj)
        {

        }
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
            App.Current.Dispatcher.Invoke(() =>
            {
                _metaDataState = state;
                ((MainWindow)System.Windows.Application.Current.MainWindow).UpdateLayout();
            });
        }
        #endregion

        #region Planned

        #endregion
    }
}