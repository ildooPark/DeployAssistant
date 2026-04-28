using DeployAssistant.DataComponent;
using DeployAssistant.Model;
using DeployAssistant.ViewModel.Utils;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;

namespace DeployAssistant.ViewModel
{
    public enum VMState
    {
        Idle,
        Calculating
    }

    public class FileTrackViewModel : ViewModelBase
    {
        #region Window-request events raised by this ViewModel
        /// <summary>Raised when the overlap-file resolution window should be opened.</summary>
        public event Action<List<ChangedFile>, List<ChangedFile>>? OverlapWindowRequested;
        /// <summary>Raised when the integrity-log window should be opened.</summary>
        public event Action<ProjectData?, string, ObservableCollection<ProjectFile>>? IntegrityLogWindowRequested;
        /// <summary>Raised when the version-diff window should be opened.</summary>
        public event Action<ProjectData, ProjectData, List<ChangedFile>>? VersionDiffWindowRequested;
        /// <summary>Raised when the version-comparison (similarity) window should be opened.</summary>
        public event Action<ProjectData, List<ProjectSimilarity>>? VersionComparisonWindowRequested;
        /// <summary>Raised when the src-project info window should be opened.</summary>
        public event Action<ProjectData?>? SrcProjectInfoWindowRequested;
        #endregion

        private ObservableCollection<ProjectFile>? _changedFileList;
        public ObservableCollection<ProjectFile> ChangedFileList
        {
            get => _changedFileList ??= new ObservableCollection<ProjectFile>();
            set
            {
                _changedFileList = value;
                OnPropertyChanged(nameof(ChangedFileList));
            }
        }

        private ProjectFile? _srcProjectFile;
        public ProjectFile? SrcProjectFile
        {
            get => _srcProjectFile;
            set
            {
                _srcProjectFile = value;
                OnPropertyChanged(nameof(SelectedItem));
            }
        }

        private ProjectFile? _selectedItem;
        public ProjectFile? SelectedItem
        {
            get => _selectedItem;
            set
            {
                _selectedItem = value;
                OnPropertyChanged(nameof(SelectedItem));
            }
        }

        private ICommand? _clearNewfiles;
        public ICommand ClearNewfiles => _clearNewfiles ??= new RelayCommand(ClearFiles, CanClearFiles);

        private ICommand? _refreshDeployFileList;
        public ICommand RefreshDeployFileList => _refreshDeployFileList ??= new RelayCommand(RefreshFilesList);

        private ICommand? _revertChange;
        public ICommand RevertChange => _revertChange ??= new RelayCommand(RevertIntegrityCheckFile);

        private ICommand? _checkProjectIntegrity;
        public ICommand CheckProjectIntegrity => _checkProjectIntegrity ??= new RelayCommand(MainProjectIntegrityTest, CanRunIntegrityTest);

        private ICommand? _stageChanges;
        public ICommand StageChanges => _stageChanges ??= new RelayCommand(StageNewChanges, CanStageChanges);

        private ICommand? _addForRestore;
        public ICommand AddForRestore => _addForRestore ??= new RelayCommand(RestoreFile, CanRestoreFile);

        private ICommand? _getDeployedProjectInfo;
        public ICommand? GetDeployedProjectInfo => _getDeployedProjectInfo ??= new RelayCommand(OpenDeployedProjectInfo, CanOpenDeployedProjectInfo);

        private ICommand? _compareDeployedProjectWithMain;
        public ICommand? CompareDeployedProjectWithMain => _compareDeployedProjectWithMain ??= new RelayCommand(CompareSrcProjWithMain, CanCompareSrcProjWithMain);

        private ICommand? _srcSimilarityWithBackups;
        public ICommand? SrcSimilarityWithBackups => _srcSimilarityWithBackups ??= new RelayCommand(GetSimilaritiesWithLocal, CanCompareSrcProjWithMain);

        private ICommand? _getDeploySrcDir;
        public ICommand GetDeploySrcDir => _getDeploySrcDir ??= new RelayCommand(SetDeploySrcDirectory, CanSetDeployDir);

        private readonly MetaDataManager _metaDataManager;
        private MetaDataState _metaDataState = MetaDataState.Idle;
        private ProjectData? _srcProjectData;
        private ProjectData? _dstProjData;
        private string? _deploySrcPath;

        public FileTrackViewModel(MetaDataManager metaDataManager)
        {
            _metaDataManager = metaDataManager;
            _metaDataManager.OverlappedFileSortEventHandler += OverlapFileSortCallBack;
            _metaDataManager.SrcProjectLoadedEventHandler += SrcProjectDataCallBack;
            _metaDataManager.PreStagedChangesEventHandler += PreStagedChangesCallBack;
            _metaDataManager.IntegrityCheckCompleteEventHandler += ProjectIntegrityCheckCallBack;
            _metaDataManager.FileChangesEventHandler += MetaDataManager_FileChangeCallBack;
            _metaDataManager.ManagerStateEventHandler += MetaDataManager_IssueEventCallBack;
            _metaDataManager.ProjLoadedEventHandler += MetaDataManager_ProjLoadedCallBack;
            _metaDataManager.ProjComparisonCompleteEventHandler += MetaDataManager_ProjComparisonCompleteCallBack;
            _metaDataManager.SimilarityCheckCompleteEventHandler += MetaDataManager_SimilarityCheckCompleteCallBack;
        }

        private bool CanSetDeployDir(object obj)
        {
            if (_metaDataState != MetaDataState.Idle) return false;
            if (_dstProjData == null) return false;
            return true;
        }

        private void SetDeploySrcDirectory(object obj)
        {
            try
            {
                var openUpdateDir = new OpenFolderDialog();
                if (openUpdateDir.ShowDialog() == true)
                {
                    _srcProjectData = null;
                    _deploySrcPath = openUpdateDir.FolderName;
                    _metaDataManager.RequestSrcDataRetrieval(_deploySrcPath);
                }
                else
                {
                    _deploySrcPath = null;
                    return;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        private bool CanStageChanges(object obj)
        {
            if (_metaDataState != MetaDataState.Idle) return false;
            return ChangedFileList.Count != 0;
        }

        private void StageNewChanges(object obj)
        {
            _metaDataManager.RequestStageChanges();
        }

        private bool CanOpenDeployedProjectInfo(object obj)
        {
            return _srcProjectData != null;
        }

        public void OpenDeployedProjectInfo(object obj)
        {
            SrcProjectInfoWindowRequested?.Invoke(_srcProjectData);
        }

        private bool CanClearFiles(object obj) { return ChangedFileList.Count != 0; }

        private void ClearFiles(object obj)
        {
            _metaDataManager.RequestClearStagedFiles();
        }

        private bool CanRestoreFile(object? obj)
        {
            if (_metaDataState != MetaDataState.Idle) return false;
            if (obj is ProjectFile projFile &&
                (projFile.DataState == DataState.Deleted ||
                !projFile.IsDstFile)) return true;
            else return false;
        }

        private void RestoreFile(object? obj)
        {
            if (obj is ProjectFile file)
            {
                _metaDataManager.RequestFileRestore(file, DataState.Restored);
            }
        }

        private void RefreshFilesList(object? obj)
        {
            if (_deploySrcPath == null)
            {
                MessageBox.Show("Please Set Src Deploy Path");
            }
            _metaDataManager.RequestClearStagedFiles();
            _metaDataManager.RequestSrcDataRetrieval(_deploySrcPath);
        }

        private void RevertIntegrityCheckFile(object? obj)
        {
            if (_selectedItem is ProjectFile file)
            {
                if ((file.DataState & DataState.IntegrityChecked) == 0)
                {
                    MessageBox.Show("Only Applicable for Integrity Check Failed Files");
                    return;
                }
                _metaDataManager.RequestRevertChange(file);
            }
        }

        private bool CanRunIntegrityTest(object sender)
        {
            return _metaDataState == MetaDataState.Idle;
        }

        private void MainProjectIntegrityTest(object sender)
        {
            Task.Run(() => _metaDataManager.RequestProjectIntegrityCheck());
        }

        private void GetSimilaritiesWithLocal(object obj)
        {
            _metaDataManager.RequestProjectCompatibility(_srcProjectData);
        }

        private bool CanCompareSrcProjWithMain(object obj)
        {
            if (_metaDataState != MetaDataState.Idle) return false;
            if (_srcProjectData == null) return false;
            if (_metaDataManager.MainProjectData == null) return false;
            return true;
        }

        private void CompareSrcProjWithMain(object obj)
        {
            _metaDataManager.RequestProjVersionDiff(_srcProjectData);
        }

        #region Receive Callbacks From Model

        private void MetaDataManager_ProjLoadedCallBack(object projObj)
        {
            if (projObj is not ProjectData projectData) return;
            _dstProjData = projectData;
        }

        private void OverlapFileSortCallBack(List<ChangedFile> overlappedFileObj, List<ChangedFile> newFileObj)
        {
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                OverlapWindowRequested?.Invoke(overlappedFileObj, newFileObj);
            });
        }

        private void PreStagedChangesCallBack(object changedFileList)
        {
            if (changedFileList is ObservableCollection<ProjectFile> projectFileList)
            {
                ChangedFileList = projectFileList;
            }
        }

        private void MetaDataManager_FileChangeCallBack(ObservableCollection<ProjectFile> changedFileList)
        {
            ChangedFileList = changedFileList;
        }

        private void ProjectIntegrityCheckCallBack(string changeLog, ObservableCollection<ProjectFile> changedFileList)
        {
            if (changedFileList == null)
            {
                MessageBox.Show("Model Binding Issue: ChangedList is Empty");
                return;
            }

            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                IntegrityLogWindowRequested?.Invoke(_dstProjData, changeLog, changedFileList);
            });
        }

        private void MetaDataManager_ProjComparisonCompleteCallBack(ProjectData srcProject, ProjectData dstProject, List<ChangedFile> diff)
        {
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                VersionDiffWindowRequested?.Invoke(srcProject, dstProject, diff);
            });
        }

        private void MetaDataManager_IssueEventCallBack(MetaDataState state)
        {
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                _metaDataState = state;
                System.Windows.Application.Current?.MainWindow?.UpdateLayout();
            });
        }

        private void MetaDataManager_SimilarityCheckCompleteCallBack(ProjectData data, List<ProjectSimilarity> diffList)
        {
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                VersionComparisonWindowRequested?.Invoke(data, diffList);
            });
        }

        private void SrcProjectDataCallBack(object? srcProjectDataObj)
        {
            if (srcProjectDataObj is not ProjectData srcProjectData)
            {
                _srcProjectData = null;
                return;
            }
            _srcProjectData = srcProjectData;
        }

        #endregion
    }
}
