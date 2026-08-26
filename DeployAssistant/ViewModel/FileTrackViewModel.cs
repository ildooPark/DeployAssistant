using DeployAssistant.DataComponent;
using DeployAssistant.Model;
using DeployAssistant.Services;
using DeployAssistant.ViewModel.Utils;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
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

        private ICommand? _revertAllChanges;
        public ICommand RevertAllChanges => _revertAllChanges ??= new RelayCommand(RevertAllIntegrityCheckFiles, CanRevertAllChanges);

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
        private readonly IDialogService _dialogService;
        private readonly IUiDispatcher _uiDispatcher;
        private MetaDataState _metaDataState = MetaDataState.Idle;
        private ProjectData? _srcProjectData;
        private ProjectData? _dstProjData;
        private string? _deploySrcPath;

        public FileTrackViewModel(MetaDataManager metaDataManager,
                                  IDialogService dialogService,
                                  IUiDispatcher uiDispatcher)
        {
            _metaDataManager = metaDataManager;
            _dialogService = dialogService;
            _uiDispatcher = uiDispatcher;
            _metaDataManager.OverlappedFileSortEventHandler += OverlapFileSortCallBack;
            TrackUnsubscribe(() => _metaDataManager.OverlappedFileSortEventHandler -= OverlapFileSortCallBack);
            _metaDataManager.SrcProjectLoadedEventHandler += SrcProjectDataCallBack;
            TrackUnsubscribe(() => _metaDataManager.SrcProjectLoadedEventHandler -= SrcProjectDataCallBack);
            _metaDataManager.PreStagedChangesEventHandler += PreStagedChangesCallBack;
            TrackUnsubscribe(() => _metaDataManager.PreStagedChangesEventHandler -= PreStagedChangesCallBack);
            _metaDataManager.IntegrityCheckCompleteEventHandler += ProjectIntegrityCheckCallBack;
            TrackUnsubscribe(() => _metaDataManager.IntegrityCheckCompleteEventHandler -= ProjectIntegrityCheckCallBack);
            _metaDataManager.FileChangesEventHandler += MetaDataManager_FileChangeCallBack;
            TrackUnsubscribe(() => _metaDataManager.FileChangesEventHandler -= MetaDataManager_FileChangeCallBack);
            _metaDataManager.ManagerStateEventHandler += MetaDataManager_IssueEventCallBack;
            TrackUnsubscribe(() => _metaDataManager.ManagerStateEventHandler -= MetaDataManager_IssueEventCallBack);
            _metaDataManager.ProjLoadedEventHandler += MetaDataManager_ProjLoadedCallBack;
            TrackUnsubscribe(() => _metaDataManager.ProjLoadedEventHandler -= MetaDataManager_ProjLoadedCallBack);
            _metaDataManager.ProjComparisonCompleteEventHandler += MetaDataManager_ProjComparisonCompleteCallBack;
            TrackUnsubscribe(() => _metaDataManager.ProjComparisonCompleteEventHandler -= MetaDataManager_ProjComparisonCompleteCallBack);
            _metaDataManager.SimilarityCheckCompleteEventHandler += MetaDataManager_SimilarityCheckCompleteCallBack;
            TrackUnsubscribe(() => _metaDataManager.SimilarityCheckCompleteEventHandler -= MetaDataManager_SimilarityCheckCompleteCallBack);
            _metaDataManager.IntegrityFileProgressEventHandler += IntegrityFileOutcomeCallBack;
            TrackUnsubscribe(() => _metaDataManager.IntegrityFileProgressEventHandler -= IntegrityFileOutcomeCallBack);
        }

        // Per-file verdicts of the LAST integrity run; the results window reads a snapshot.
        private readonly ConcurrentDictionary<string, IntegrityFileOutcome> _lastIntegrityOutcomes = new();

        public IReadOnlyDictionary<string, IntegrityFileOutcome> IntegrityOutcomesSnapshot
            => new Dictionary<string, IntegrityFileOutcome>(_lastIntegrityOutcomes);

        private void IntegrityFileOutcomeCallBack(string relPath, IntegrityFileOutcome outcome)
        {
            // HashFailed arrives before the same file's final verdict; keep the failure
            // visible in tallies by never overwriting it with a milder outcome.
            if (_lastIntegrityOutcomes.TryGetValue(relPath, out var existing)
                && existing == IntegrityFileOutcome.HashFailed
                && outcome == IntegrityFileOutcome.MetadataFallback)
                return;
            _lastIntegrityOutcomes[relPath] = outcome;
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
                string? srcPath = _dialogService.PickFolder(Loc.T("S.Dlg.PickSrcFolder", "Set Deploy Source Directory"));
                if (srcPath != null)
                {
                    _srcProjectData = null;
                    _deploySrcPath = srcPath;
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
                _dialogService.Inform(Loc.T("S.Dlg.ErrorTitle", "Error"), ex.Message);
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
                (projFile.DataState & DataState.Added) != 0 ||
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
                _dialogService.Inform(Loc.T("S.Refresh", "Refresh"),
                    Loc.T("S.Dlg.SetSrcPath", "Please set the source deploy path first."));
            }
            _metaDataManager.RequestClearStagedFiles();
            _metaDataManager.RequestSrcDataRetrieval(_deploySrcPath);
        }

        private bool CanRevertAllChanges(object? obj)
        {
            if (_metaDataState != MetaDataState.Idle) return false;
            foreach (ProjectFile file in ChangedFileList)
                if ((file.DataState & DataState.IntegrityChecked) != 0) return true;
            return false;
        }

        private void RevertAllIntegrityCheckFiles(object? obj)
        {
            var revertTargets = new List<ProjectFile>();
            foreach (ProjectFile file in ChangedFileList)
                if ((file.DataState & DataState.IntegrityChecked) != 0) revertTargets.Add(file);
            if (revertTargets.Count == 0) return;

            var choice = _dialogService.Confirm(Loc.T("S.RevertAll", "Revert All"),
                string.Format(Loc.T("S.Dlg.RevertAllConfirm", "Revert all {0} integrity-detected change(s) back to the current version?"),
                    revertTargets.Count));
            if (choice != DialogChoice.Yes) return;

            foreach (ProjectFile file in revertTargets)
                _metaDataManager.RequestRevertChange(file);
        }

        private void RevertIntegrityCheckFile(object? obj)
        {
            if (_selectedItem is ProjectFile file)
            {
                if ((file.DataState & DataState.IntegrityChecked) == 0)
                {
                    _dialogService.Inform(Loc.T("S.Dlg.RevertChangeTitle", "Revert Change"),
                        Loc.T("S.Dlg.RevertOnlyIntegrity", "Only applicable to files flagged by the integrity check."));
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
            _uiDispatcher.Invoke(() =>
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
                _dialogService.Inform(Loc.T("S.IntegrityCheck", "Integrity Check"),
                    Loc.T("S.Dlg.ChangedListEmpty", "Model binding issue: the changed list is empty."));
                return;
            }

            _uiDispatcher.Invoke(() =>
            {
                IntegrityLogWindowRequested?.Invoke(_dstProjData, changeLog, changedFileList);
            });
        }

        private void MetaDataManager_ProjComparisonCompleteCallBack(ProjectData srcProject, ProjectData dstProject, List<ChangedFile> diff)
        {
            _uiDispatcher.Invoke(() =>
            {
                VersionDiffWindowRequested?.Invoke(srcProject, dstProject, diff);
            });
        }

        private void MetaDataManager_IssueEventCallBack(MetaDataState state)
        {
            if (state == MetaDataState.IntegrityChecking) _lastIntegrityOutcomes.Clear();
            _uiDispatcher.Invoke(() =>
            {
                _metaDataState = state;
            });
        }

        private void MetaDataManager_SimilarityCheckCompleteCallBack(ProjectData data, List<ProjectSimilarity> diffList)
        {
            _uiDispatcher.Invoke(() =>
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
