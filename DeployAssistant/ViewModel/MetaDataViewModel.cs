using DeployAssistant.DataComponent;
using DeployAssistant.Model;
using DeployAssistant.Services;
using DeployAssistant.ViewModel.Utils;
using System;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

namespace DeployAssistant.ViewModel
{
    public class MetaDataViewModel : ViewModelBase
    {
        private string? _currentProjectPath;
        public string CurrentProjectPath
        {
            get => _currentProjectPath ?? "";
            set => _currentProjectPath = value;
        }

        private ProjectData? _projectData;
        public ProjectData? ProjectData
        {
            get => _projectData;
            set
            {
                _projectData = value;
                ProjectFiles = value?.ProjectFilesObs;
                ProjectName = value?.ProjectName ?? "Undefined";
                CurrentVersion = value?.UpdatedVersion ?? "Undefined";
            }
        }

        private ObservableCollection<ProjectFile>? _projectFiles;
        public ObservableCollection<ProjectFile>? ProjectFiles
        {
            get => _projectFiles;
            set
            {
                _projectFiles = value;
                OnPropertyChanged(nameof(ProjectFiles));
            }
        }

        private string? _updaterName;
        public string? UpdaterName
        {
            get => _updaterName ?? "";
            set
            {
                _updaterName = value;
                OnPropertyChanged(nameof(UpdaterName));
            }
        }

        private string? _updateLog;
        public string? UpdateLog
        {
            get => _updateLog ?? "";
            set
            {
                _updateLog = value;
                OnPropertyChanged(nameof(UpdateLog));
            }
        }

        private string? _currentMetaDataState;
        public string CurrentMetaDataState
        {
            get => _currentMetaDataState ??= "Idle";
            set
            {
                _currentMetaDataState = value;
                OnPropertyChanged(nameof(CurrentMetaDataState));
            }
        }

        private string? _projectName;
        public string ProjectName
        {
            get => _projectName ?? "Undefined";
            set
            {
                _projectName = value ?? "Undefined";
                OnPropertyChanged(nameof(ProjectName));
            }
        }

        private string? _currentVersion;
        public string CurrentVersion
        {
            get => _currentVersion ??= "Undefined";
            set
            {
                _currentVersion = value ?? "Undefined";
                OnPropertyChanged(nameof(CurrentVersion));
            }
        }

        private ICommand? _conductUpdate;
        public ICommand ConductUpdate => _conductUpdate ??= new RelayCommand(Update, CanUpdate);

        private ICommand? _getProject;
        public ICommand GetProject => _getProject ??= new RelayCommand(RetrieveProject, CanRetrieveProject);

        private readonly MetaDataManager _metaDataManager;
        private readonly IDialogService _dialogService;
        private readonly IUiDispatcher _uiDispatcher;
        private MetaDataState? _metaDataState = MetaDataState.Idle;

        public MetaDataViewModel(MetaDataManager metaDataManager,
                                 IDialogService dialogService,
                                 IUiDispatcher uiDispatcher)
        {
            _metaDataManager = metaDataManager;
            _dialogService = dialogService;
            _uiDispatcher = uiDispatcher;
            _metaDataManager.ProjLoadedEventHandler += MetaDataManager_ProjLoadedCallBack;
            TrackUnsubscribe(() => _metaDataManager.ProjLoadedEventHandler -= MetaDataManager_ProjLoadedCallBack);
            _metaDataManager.ManagerStateEventHandler += MetaDataStateChangeCallBack;
            TrackUnsubscribe(() => _metaDataManager.ManagerStateEventHandler -= MetaDataStateChangeCallBack);
            _metaDataManager.VersionRenameCompleteEventHandler += VersionRenameCompleteCallBack;
            TrackUnsubscribe(() => _metaDataManager.VersionRenameCompleteEventHandler -= VersionRenameCompleteCallBack);
            _metaDataManager.IntegrityProgressEventHandler += IntegrityProgressCallBack;
            TrackUnsubscribe(() => _metaDataManager.IntegrityProgressEventHandler -= IntegrityProgressCallBack);
            _metaDataManager.IntegrityFileProgressEventHandler += IntegrityFileProgressCallBack;
            TrackUnsubscribe(() => _metaDataManager.IntegrityFileProgressEventHandler -= IntegrityFileProgressCallBack);
        }

        #region Busy overlay
        // Twelve hash workers can raise thousands of per-file events per second; touching the
        // UI per event would freeze WPF, and net472's gen0 budget punishes per-event garbage.
        // Workers only enqueue; a 10 Hz UI-thread timer drains in batches into a 200-row ring.
        private const int BusyLogCapacity = 200;
        private const int BusyFlushBatchLimit = 500;
        private readonly ConcurrentQueue<KeyValuePair<string, IntegrityFileOutcome>> _busyLogQueue = new();
        private System.Windows.Threading.DispatcherTimer? _busyFlushTimer;
        private int _busyCompleted;
        private int _busyTotal;

        public sealed class BusyLogEntry
        {
            public BusyLogEntry(string text, IntegrityFileOutcome outcome, string outcomeLabel)
            {
                Text = text;
                Outcome = outcome;
                OutcomeLabel = outcomeLabel;
            }
            public string Text { get; }
            public IntegrityFileOutcome Outcome { get; }
            public string OutcomeLabel { get; }
        }

        public ObservableCollection<BusyLogEntry> BusyLog { get; } = new ObservableCollection<BusyLogEntry>();

        private bool _isBusy;
        public bool IsBusy
        {
            get => _isBusy;
            private set { _isBusy = value; OnPropertyChanged(nameof(IsBusy)); }
        }

        private string _busyTitle = "";
        public string BusyTitle
        {
            get => _busyTitle;
            private set { _busyTitle = value; OnPropertyChanged(nameof(BusyTitle)); }
        }

        private double _busyProgress;
        public double BusyProgress
        {
            get => _busyProgress;
            private set { _busyProgress = value; OnPropertyChanged(nameof(BusyProgress)); }
        }

        private bool _busyIndeterminate = true;
        public bool BusyIndeterminate
        {
            get => _busyIndeterminate;
            private set { _busyIndeterminate = value; OnPropertyChanged(nameof(BusyIndeterminate)); }
        }

        private string _busyCountText = "";
        public string BusyCountText
        {
            get => _busyCountText;
            private set { _busyCountText = value; OnPropertyChanged(nameof(BusyCountText)); }
        }

        private void IntegrityProgressCallBack(int completedCount, int totalCount)
        {
            Interlocked.Exchange(ref _busyCompleted, completedCount);
            Interlocked.Exchange(ref _busyTotal, totalCount);
        }

        private void IntegrityFileProgressCallBack(string relPath, IntegrityFileOutcome outcome)
            => _busyLogQueue.Enqueue(new KeyValuePair<string, IntegrityFileOutcome>(relPath, outcome));

        private void UpdateBusyOverlay(MetaDataState state)
        {
            bool busy = state != MetaDataState.Idle;
            if (busy && !IsBusy)
            {
                BusyLog.Clear();
                while (_busyLogQueue.TryDequeue(out _)) { }
                Interlocked.Exchange(ref _busyCompleted, 0);
                Interlocked.Exchange(ref _busyTotal, 0);
                BusyProgress = 0;
                BusyCountText = "";
                _busyFlushTimer ??= new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(100)
                };
                _busyFlushTimer.Tick -= BusyFlushTick;
                _busyFlushTimer.Tick += BusyFlushTick;
                _busyFlushTimer.Start();
            }
            else if (!busy && IsBusy)
            {
                BusyFlushTick(null, EventArgs.Empty);   // final drain
                _busyFlushTimer?.Stop();
            }
            BusyIndeterminate = state != MetaDataState.IntegrityChecking;
            BusyTitle = state switch
            {
                MetaDataState.IntegrityChecking => Loc.T("S.Ovl.Integrity", "Integrity check running…"),
                MetaDataState.Reverting or MetaDataState.CleanRestoring => Loc.T("S.Ovl.Checkout", "Checkout in progress…"),
                MetaDataState.Updating => Loc.T("S.Ovl.Deploying", "Deploying new version…"),
                MetaDataState.Deleting => Loc.T("S.Ovl.Deleting", "Deleting version…"),
                MetaDataState.Exporting => Loc.T("S.Ovl.Exporting", "Exporting…"),
                _ => Loc.T("S.Ovl.Working", "Working…"),
            };
            IsBusy = busy;
        }

        private void BusyFlushTick(object? sender, EventArgs e)
        {
            int drained = 0;
            while (drained < BusyFlushBatchLimit && _busyLogQueue.TryDequeue(out var item))
            {
                string label = item.Value switch
                {
                    IntegrityFileOutcome.Modified => Loc.T("S.Ovl.OutModified", "modified"),
                    IntegrityFileOutcome.Added => Loc.T("S.Ovl.OutAdded", "added"),
                    IntegrityFileOutcome.Deleted => Loc.T("S.Ovl.OutDeleted", "deleted"),
                    IntegrityFileOutcome.HashFailed => Loc.T("S.Ovl.OutHashFailed", "hash failed"),
                    IntegrityFileOutcome.MetadataFallback => Loc.T("S.Ovl.OutMetadata", "metadata check"),
                    _ => Loc.T("S.Ovl.OutChecked", "checked"),
                };
                BusyLog.Add(new BusyLogEntry(item.Key, item.Value, label));
                if (BusyLog.Count > BusyLogCapacity) BusyLog.RemoveAt(0);
                drained++;
            }

            int total = Interlocked.CompareExchange(ref _busyTotal, 0, 0);
            int done = Interlocked.CompareExchange(ref _busyCompleted, 0, 0);
            if (total > 0)
            {
                BusyProgress = Math.Min(100.0, done * 100.0 / total);
                BusyCountText = string.Format(Loc.T("S.Ovl.Files", "{0:N0} / {1:N0} files"), done, total);
            }
        }
        #endregion

        #region Update Version

        private bool CanUpdate(object obj)
        {
            if (ProjectFiles == null || CurrentProjectPath == "") return false;
            if (_metaDataState != MetaDataState.Idle) return false;
            return true;
        }

        private void Update(object obj)
        {
            if (UpdaterName == "" || UpdateLog == "")
            {
                _dialogService.Inform(Loc.T("S.Dlg.ValidationTitle", "Validation"),
                    Loc.T("S.Dlg.NeedVersionAndUpdater", "Must have both a commit message and an updater name."));
                return;
            }
            _metaDataManager.RequestProjectUpdate(_updaterName, UpdateLog, CurrentProjectPath);
        }

        private bool CanRetrieveProject(object parameter)
        {
            if (_metaDataState != MetaDataState.Idle) return false;
            return true;
        }

        private void RetrieveProject(object parameter)
        {
            string? projectPath = _dialogService.PickFolder(Loc.T("S.Dlg.OpenProjectFolder", "Open Managed Project"));
            if (projectPath == null || projectPath.Length == 0) return;
            OpenProjectPath(projectPath);
        }

        /// <summary>Opens a project directly by path (recents menu); same flow as RetrieveProject minus the picker.</summary>
        public void OpenProjectPath(string projectPath)
        {
            if (_metaDataState != MetaDataState.Idle) return;
            if (_projectFiles != null && _projectFiles.Count != 0) _projectFiles.Clear();
            CurrentProjectPath = projectPath;

            bool retrieveProjectResult = _metaDataManager.RequestProjectRetrieval(projectPath);
            if (!retrieveProjectResult)
            {
                var result = _dialogService.Confirm(Loc.T("S.Dlg.ImportProjectTitle", "Import Project"),
                    string.Format(Loc.T("S.Dlg.InitNewProject", "{0}\nVersionLog file not found.\nInitialize a new project?"), projectPath));
                if (result == DialogChoice.Yes)
                {
                    Task.Run(() => _metaDataManager.RequestProjectInitialization(projectPath));
                }
                else
                {
                    _dialogService.Inform(Loc.T("S.Dlg.ImportProjectTitle", "Import Project"),
                        Loc.T("S.Dlg.SelectAnotherPath", "Please select another project path."));
                    return;
                }
            }
        }

        #endregion

        #region Receiving Model Callbacks

        private void MetaDataStateChangeCallBack(MetaDataState state)
        {
            _uiDispatcher.Invoke(() =>
            {
                _metaDataState = state;
                CurrentMetaDataState = state.ToString();
                UpdateBusyOverlay(state);
            });
        }

        private void MetaDataManager_ProjLoadedCallBack(object projObj)
        {
            if (projObj is not ProjectData projectData) return;
            // Raised from a worker thread on the checkout path, and the setters push straight
            // into bound properties — marshal before touching them.
            _uiDispatcher.Invoke(() =>
            {
                ProjectData = projectData;
                ProjectName = ProjectData.ProjectName ?? "Undefined";
                CurrentVersion = ProjectData.UpdatedVersion ?? "Undefined";
                CurrentProjectPath = projectData.ProjectPath;
                UpdaterName = "";
                UpdateLog = "";
            });
        }

        /// <summary>
        /// A re-tag edits the stored <c>ProjectData</c> in place and raises no project-load
        /// event, so the header would keep showing the old tag. Re-read it from the manager.
        /// </summary>
        private void VersionRenameCompleteCallBack(VersionRenameResult result)
        {
            if (!result.Success || !result.VersionNameChanged) return;
            _uiDispatcher.Invoke(() =>
            {
                ProjectData? main = _metaDataManager.MainProjectData;
                if (main == null) return;
                CurrentVersion = main.UpdatedVersion ?? "Undefined";
            });
        }

        #endregion
    }
}
