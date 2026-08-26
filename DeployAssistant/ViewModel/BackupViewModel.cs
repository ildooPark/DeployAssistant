using DeployAssistant.DataComponent;
using DeployAssistant.Model;
using DeployAssistant.Services;
using DeployAssistant.View;
using DeployAssistant.ViewModel.Utils;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

namespace DeployAssistant.ViewModel
{
    public class BackupViewModel : ViewModelBase
    {
        #region Window-request events
        /// <summary>Raised when the integrity-log window should be opened for a backup version.</summary>
        public event Action<ProjectData?>? IntegrityLogWindowRequested;
        #endregion

        /// <summary>
        /// A checkout the user asked for that is waiting on the pre-checkout integrity
        /// check. Held until <see cref="ProjectIntegrityCheckCallBack"/> can show the gate.
        /// </summary>
        private sealed class PendingCheckout
        {
            internal PendingCheckout(ProjectData target, CheckoutMode mode)
            {
                Target = target;
                Mode = mode;
            }
            internal ProjectData Target { get; }
            internal CheckoutMode Mode { get; }
        }

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
                var diffRows = new ObservableCollection<DiffItem>();
                foreach (ChangedFile change in value.ChangedFiles) diffRows.Add(new DiffItem(change));
                DiffLog = diffRows;
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

        private ICommand? _deleteVersion;
        public ICommand DeleteVersion => _deleteVersion ??= new RelayCommand(DeleteSelectedVersion, CanDeleteSelectedVersion);

        private ICommand? _renameVersion;
        public ICommand RenameVersion => _renameVersion ??= new RelayCommand(RenameSelectedVersion, CanRenameSelectedVersion);

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

        private ObservableCollection<DiffItem>? _diffLog;
        public ObservableCollection<DiffItem> DiffLog
        {
            get => _diffLog ??= new ObservableCollection<DiffItem>();
            set
            {
                _diffLog = value;
                OnPropertyChanged(nameof(DiffLog));
            }
        }

        private readonly MetaDataManager _metaDataManager;
        private readonly IDialogService _dialogService;
        private readonly IUiDispatcher _uiDispatcher;
        private MetaDataState? _metaDataState = MetaDataState.Idle;

        /// <summary>Guards <see cref="_pendingCheckout"/>: it is written on the UI thread and read on a worker.</summary>
        private readonly object _gateLock = new object();
        /// <summary>Checkout waiting on its pre-checkout integrity check; null when idle.</summary>
        private PendingCheckout? _pendingCheckout;
        /// <summary>Version whose delete preview we asked for; null when the preview is not ours.</summary>
        private ProjectData? _pendingDeleteTarget;

        public BackupViewModel(MetaDataManager metaDataManager,
                               IDialogService dialogService,
                               IUiDispatcher uiDispatcher)
        {
            _metaDataManager = metaDataManager;
            _dialogService = dialogService;
            _uiDispatcher = uiDispatcher;
            _metaDataManager.FetchRequestEventHandler += FetchRequestCallBack;
            TrackUnsubscribe(() => _metaDataManager.FetchRequestEventHandler -= FetchRequestCallBack);
            _metaDataManager.ProjExportEventHandler += ExportRequestCallBack;
            TrackUnsubscribe(() => _metaDataManager.ProjExportEventHandler -= ExportRequestCallBack);
            _metaDataManager.ManagerStateEventHandler += MetaDataStateChangeCallBack;
            TrackUnsubscribe(() => _metaDataManager.ManagerStateEventHandler -= MetaDataStateChangeCallBack);
            _metaDataManager.IntegrityCheckCompleteEventHandler += ProjectIntegrityCheckCallBack;
            TrackUnsubscribe(() => _metaDataManager.IntegrityCheckCompleteEventHandler -= ProjectIntegrityCheckCallBack);
            _metaDataManager.CheckoutCompleteEventHandler += CheckoutCompleteCallBack;
            TrackUnsubscribe(() => _metaDataManager.CheckoutCompleteEventHandler -= CheckoutCompleteCallBack);
            _metaDataManager.VersionDeletePreviewEventHandler += VersionDeletePreviewCallBack;
            TrackUnsubscribe(() => _metaDataManager.VersionDeletePreviewEventHandler -= VersionDeletePreviewCallBack);
            _metaDataManager.VersionDeleteCompleteEventHandler += VersionDeleteCompleteCallBack;
            TrackUnsubscribe(() => _metaDataManager.VersionDeleteCompleteEventHandler -= VersionDeleteCompleteCallBack);
            _metaDataManager.VersionRenameCompleteEventHandler += VersionRenameCompleteCallBack;
            TrackUnsubscribe(() => _metaDataManager.VersionRenameCompleteEventHandler -= VersionRenameCompleteCallBack);
        }

        private bool CanFetch(object obj)
        {
            if (_metaDataManager.ProjectMetaData == null) return false;
            if (_metaDataState != MetaDataState.Idle) return false;
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
            if (_metaDataManager.MainProjectData == null) return false;
            return true;
        }

        private void OnViewFullLog(object obj)
        {
            if (SelectedItem == null)
            {
                _dialogService.Inform(Loc.T("S.Dlg.ViewLogTitle", "View Log"),
                    Loc.T("S.Dlg.ViewLogNull", "Couldn't get the log — the selected item is null."));
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
                _dialogService.Inform(Loc.T("S.Checkout", "Checkout"),
                    Loc.T("S.Dlg.MustSelectVersion", "Must select a version to check out."));
                return;
            }
            // The gate's integrity check already re-hashes the whole directory, so CleanRestore's
            // second scan buys nothing here; Fast after the gate reaches the same end state.
            StartGatedCheckout(_selectedItem, CheckoutMode.Fast);
        }

        private bool CanCleanRestoreBackupFiles(object obj)
        {
            if (SelectedItem == null || _metaDataManager.MainProjectData == null) return false;
            return _metaDataState == MetaDataState.Idle;
        }

        private void CleanRestoreBackupFiles(object? obj)
        {
            if (SelectedItem == null)
            {
                _dialogService.Inform(Loc.T("S.Checkout", "Checkout"),
                    Loc.T("S.Dlg.MustSelectVersion", "Must select a version to check out."));
                return;
            }
            StartGatedCheckout(SelectedItem, CheckoutMode.CleanRestore);
        }

        /// <summary>
        /// Runs the pre-checkout integrity check and parks the request. Nothing is written
        /// until <see cref="ProjectIntegrityCheckCallBack"/> has shown the gate and the user
        /// has confirmed — a checkout must never silently overwrite uncommitted local edits.
        /// </summary>
        private void StartGatedCheckout(ProjectData target, CheckoutMode mode)
        {
            lock (_gateLock)
            {
                if (_pendingCheckout != null)
                {
                    _dialogService.Inform(Loc.T("S.Checkout", "Checkout"),
                        string.Format(Loc.T("S.Dlg.CheckoutPending", "A checkout of '{0}' is still waiting on its integrity check."),
                            _pendingCheckout.Target.UpdatedVersion));
                    return;
                }
                _pendingCheckout = new PendingCheckout(target, mode);
            }
            Task.Run(() => _metaDataManager.RequestProjectIntegrityCheck(forceFullHash: true));
        }

        private bool CanDeleteSelectedVersion(object obj)
        {
            if (_metaDataState != MetaDataState.Idle) return false;
            if (SelectedItem == null) return false;
            if (SelectedItem.IsProjectMain) return false;
            if (BackupProjectDataList.Count <= 1) return false;
            return true;
        }

        private void DeleteSelectedVersion(object obj)
        {
            ProjectData? target = SelectedItem;
            if (target == null)
            {
                _dialogService.Inform(Loc.T("S.Win.DeleteVersion", "Delete Version"),
                    Loc.T("S.Dlg.MustSelectDelete", "Must select a version to delete."));
                return;
            }
            _pendingDeleteTarget = target;
            // The preview walks every backup entry and stats the blobs; keep it off the UI
            // thread. The confirmation is raised from VersionDeletePreviewCallBack.
            Task.Run(() =>
            {
                VersionDeletePlan? plan = _metaDataManager.RequestVersionDeletePreview(target);
                if (plan != null) return;
                _pendingDeleteTarget = null;
                _uiDispatcher.Invoke(() => _dialogService.Inform(Loc.T("S.Win.DeleteVersion", "Delete Version"),
                    string.Format(Loc.T("S.Dlg.DeletePreviewFailed", "Could not work out what deleting '{0}' would remove. No project metadata is loaded."),
                        target.UpdatedVersion)));
            });
        }

        private bool CanRenameSelectedVersion(object obj)
        {
            if (_metaDataState != MetaDataState.Idle) return false;
            if (SelectedItem == null) return false;
            return true;
        }

        private void RenameSelectedVersion(object obj)
        {
            ProjectData? target = SelectedItem;
            if (target == null)
            {
                _dialogService.Inform(Loc.T("S.Dlg.RenameTitle", "Rename Version"),
                    Loc.T("S.Dlg.MustSelectRename", "Must select a version to rename."));
                return;
            }
            if (!VersionRenameWindow.Prompt(target, out string versionName, out string updateLog)) return;

            Task.Run(() => _metaDataManager.RequestRenameVersion(target, versionName, updateLog));
        }

        private bool CanExportBackupFiles(object obj)
        {
            return _metaDataState == MetaDataState.Idle;
        }

        private void ExportBackupFiles(object? obj)
        {
            if (SelectedItem == null)
            {
                _dialogService.Inform(Loc.T("S.Dlg.ExportTitle", "Export"),
                    Loc.T("S.Dlg.MustSelectBackup", "Must select a backup version first."));
                return;
            }
            Task.Run(() => _metaDataManager.RequestExportProjectBackup(SelectedItem));
        }

        private void ExtractVersionMetaData(object? obj)
        {
            if (SelectedItem == null)
            {
                _dialogService.Inform(Loc.T("S.Dlg.ExtractVersionLogTitle", "Extract Version Log"),
                    Loc.T("S.Dlg.MustSelectBackup", "Must select a backup version first."));
                return;
            }
            _metaDataManager.RequestExportProjectVersionLog(SelectedItem);
        }

        /// <summary>Formats a byte count for display, e.g. <c>1536</c> becomes <c>1.5 KB</c>.</summary>
        public static string FormatBytes(long bytes)
        {
            if (bytes <= 0) return "0 B";
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double size = bytes;
            int unit = 0;
            while (size >= 1024 && unit < units.Length - 1)
            {
                size /= 1024;
                unit++;
            }
            return unit == 0 ? $"{bytes} B" : $"{size:0.##} {units[unit]}";
        }

        #region Callbacks From Model Events

        private void FetchRequestCallBack(object backupListObj)
        {
            if (backupListObj is not ObservableCollection<ProjectData> backupList) return;
            // A delete raises this from a worker thread, so marshal before touching a
            // collection the ListView is bound to.
            _uiDispatcher.Invoke(() => BackupProjectDataList = backupList);
        }

        private void ExportRequestCallBack(object exportPathObj)
        {
            if (exportPathObj is not string exportPath) return;
            try
            {
                _dialogService.OpenInShell(exportPath);
            }
            catch (Exception ex)
            {
                _dialogService.Inform(Loc.T("S.Dlg.ExportTitle", "Export"),
                    string.Format(Loc.T("S.Dlg.ExportOpenFailed", "{0} does not exist. Error: {1}"), exportPath, ex.Message));
            }
        }

        private void MetaDataStateChangeCallBack(MetaDataState state)
        {
            _uiDispatcher.Invoke(() =>
            {
                _metaDataState = state;
            });
        }

        /// <summary>
        /// Pre-checkout gate. Only reacts to the integrity check this ViewModel started —
        /// the toolbar's own integrity check leaves <see cref="_pendingCheckout"/> null and
        /// passes straight through to FileTrackViewModel.
        /// </summary>
        private void ProjectIntegrityCheckCallBack(string changeLog, ObservableCollection<ProjectFile> changedFileList)
        {
            PendingCheckout? pending;
            lock (_gateLock)
            {
                pending = _pendingCheckout;
                _pendingCheckout = null;
            }
            if (pending == null) return;

            List<ProjectFile> modifications = changedFileList == null
                ? new List<ProjectFile>()
                : new List<ProjectFile>(changedFileList);

            bool proceed = false;
            _uiDispatcher.Invoke(() => proceed = CheckoutGateWindow.Ask(pending.Target, pending.Mode, modifications));
            if (!proceed) return;

            // Fast checkout only diffs snapshot-to-snapshot, so drift in files that are
            // identical between the two snapshots would survive it. Discard the local
            // changes explicitly first, exactly like the CLI's CheckoutGateScreen does.
            // CleanRestore rescans the whole working directory itself, so it needs none of this.
            if (pending.Mode == CheckoutMode.Fast && modifications.Count > 0)
            {
                List<string> failedFiles = new List<string>();
                foreach (ProjectFile modification in modifications)
                {
                    if ((modification.DataState & DataState.IntegrityChecked) == 0) continue;
                    if (!_metaDataManager.RequestRevertChange(modification))
                        failedFiles.Add(modification.DataRelPath);
                }
                if (failedFiles.Count > 0)
                {
                    _uiDispatcher.Invoke(() => _dialogService.Inform(Loc.T("S.Checkout", "Checkout"),
                        string.Format(Loc.T("S.Dlg.CheckoutAborted", "Aborted: {0} local change(s) could not be discarded (their backups may be missing)."),
                            failedFiles.Count) +
                        Environment.NewLine + Environment.NewLine +
                        string.Join(Environment.NewLine, failedFiles)));
                    return;
                }
            }

            _metaDataManager.RequestCheckoutVersion(pending.Target, pending.Mode);
        }

        private void CheckoutCompleteCallBack(CheckoutResult result)
        {
            string versionName = result.Target?.UpdatedVersion ?? Loc.T("S.Unknown", "(unknown)");
            string modeName = result.Mode == CheckoutMode.CleanRestore
                ? Loc.T("S.SafeCheckout", "Safe checkout")
                : Loc.T("S.Checkout", "Checkout");
            string details = result.Messages.Count == 0
                ? ""
                : Environment.NewLine + Environment.NewLine + string.Join(Environment.NewLine, result.Messages);

            _uiDispatcher.Invoke(() =>
            {
                if (result.Success)
                {
                    _dialogService.Inform(modeName,
                        string.Format(Loc.T("S.Dlg.CheckoutComplete", "{0} to '{1}' complete. {2} file change(s) applied."),
                            modeName, versionName, result.FilesApplied) + details);
                }
                else
                {
                    _dialogService.Inform(string.Format(Loc.T("S.Dlg.CheckoutFailedTitle", "{0} Failed"), modeName),
                        string.Format(Loc.T("S.Dlg.CheckoutFailed", "{0} to '{1}' did not run."), modeName, versionName) + details);
                }
            });
            // No refresh needed here: a successful checkout re-points MainProjectData, which
            // BackupManager answers with its own FetchCompleteEventHandler.
        }

        private void VersionDeletePreviewCallBack(VersionDeletePlan plan)
        {
            ProjectData? target = Interlocked.Exchange(ref _pendingDeleteTarget, null);
            if (target == null) return; // a preview this ViewModel did not ask for

            bool confirmed = false;
            _uiDispatcher.Invoke(() => confirmed = VersionDeleteConfirmWindow.Confirm(plan));
            if (!confirmed) return;

            // Already confirmed here, so the manager must not raise its own prompt.
            _metaDataManager.RequestDeleteVersion(plan.Target, confirmed: true);
        }

        private void VersionDeleteCompleteCallBack(VersionDeleteResult result)
        {
            string versionName = string.IsNullOrEmpty(result.VersionName) ? Loc.T("S.Unnamed", "(unnamed)") : result.VersionName;
            string details = result.Messages.Count == 0
                ? ""
                : Environment.NewLine + Environment.NewLine + string.Join(Environment.NewLine, result.Messages);

            _uiDispatcher.Invoke(() =>
            {
                switch (result.Outcome)
                {
                    case VersionDeleteOutcome.Deleted:
                        // FetchRequestCallBack has already replaced the list; move the
                        // selection off the version that no longer exists.
                        if (BackupProjectDataList.Count > 0) SelectedItem = BackupProjectDataList[0];
                        _dialogService.Inform(Loc.T("S.Win.DeleteVersion", "Delete Version"),
                            string.Format(Loc.T("S.Dlg.DeleteComplete", "Deleted '{0}'. {1} backup entry(ies) removed, {2} file(s) relocated, {3} reclaimed."),
                                versionName, result.BackupEntriesRemoved, result.FilesRelocated, FormatBytes(result.BytesReclaimed)) + details);
                        break;
                    case VersionDeleteOutcome.Blocked:
                        _dialogService.Inform(Loc.T("S.Win.DeleteVersion", "Delete Version"),
                            string.Format(Loc.T("S.Dlg.DeleteBlocked", "'{0}' was not deleted."), versionName) + details);
                        break;
                    default:
                        _dialogService.Inform(Loc.T("S.Win.DeleteVersion", "Delete Version"),
                            string.Format(Loc.T("S.Dlg.DeleteFailed", "Deleting '{0}' failed. The version list and the backups were left unchanged."),
                                versionName) + details);
                        break;
                }
            });
        }

        private void VersionRenameCompleteCallBack(VersionRenameResult result)
        {
            string details = result.Messages.Count == 0
                ? ""
                : Environment.NewLine + Environment.NewLine + string.Join(Environment.NewLine, result.Messages);

            _uiDispatcher.Invoke(() =>
            {
                if (!result.Success)
                {
                    _dialogService.Inform(Loc.T("S.Dlg.RenameTitle", "Rename Version"),
                        string.Format(Loc.T("S.Dlg.RenameFailed", "'{0}' was not renamed."), result.PreviousVersionName) + details);
                    return;
                }
                if (!result.VersionNameChanged && !result.UpdateLogChanged)
                {
                    _dialogService.Inform(Loc.T("S.Dlg.RenameTitle", "Rename Version"),
                        string.Format(Loc.T("S.Dlg.RenameUnchanged", "'{0}' is unchanged."), result.VersionName) + details);
                    return;
                }
                string what = result.VersionNameChanged
                    ? string.Format(Loc.T("S.Dlg.RenameDone", "'{0}' is now '{1}'."), result.PreviousVersionName, result.VersionName)
                    : string.Format(Loc.T("S.Dlg.UpdateLogSaved", "Update log of '{0}' saved."), result.VersionName);
                _dialogService.Inform(Loc.T("S.Dlg.RenameTitle", "Rename Version"), $"{what}{details}");
            });

            // ProjectData does not notify, so the list has to be re-fetched to show the tag.
            if (result.Success) _metaDataManager.RequestFetchBackup();
        }

        #endregion
    }
}
