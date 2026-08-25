using DeployAssistant.Filtering;
using DeployAssistant.Interfaces;
using DeployAssistant.Model;
using DeployAssistant.Services;
using DeployAssistant.Utils;
using System;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace DeployAssistant.DataComponent
{
    public enum MetaDataState
    {
        IntegrityChecking,
        CleanRestoring,
        Exporting,
        Reverting,
        Processing,
        Retrieving,
        Updating,
        IntegrationValidating,
        Integrating,
        Initializing,
        /// <summary>
        /// Held for the whole version-delete mutation window.  Runtime-only, like the rest
        /// of this enum — it is never serialized, so extending the enum is safe.
        /// </summary>
        Deleting,
        Idle
    }
    public class MetaDataManager
    {
        public string? CurrentProjectPath {  get; set; }

        public event Action<ObservableCollection<ProjectFile>>? FileChangesEventHandler;
        public event Action<List<ChangedFile>, List<ChangedFile>>? OverlappedFileSortEventHandler;
        public event Action<string>? ProjExportEventHandler; 
        public event Action<object>? StagedChangesEventHandler;
        public event Action<object>? PreStagedChangesEventHandler;
        public event Action<object>? SrcProjectLoadedEventHandler;
        public event Action<object>? ProjLoadedEventHandler;
        public event Action<object>? MetaDataLoadedEventHandler;
        public event Action<object>? FetchRequestEventHandler;
        public event Action<string, ObservableCollection<ProjectFile>>? IntegrityCheckCompleteEventHandler;
        /// <summary>
        /// Forwarded from FileManager.IntegrityProgressEventHandler.
        /// (completed, total) — completed runs 0 → total, total = 2 × intersect file count.
        /// </summary>
        public event Action<int, int>? IntegrityProgressEventHandler;
        /// <summary>Forwarded from FileManager.IntegrityFileProgressEventHandler: (relPath, outcome) per file.</summary>
        public event Action<string, IntegrityFileOutcome>? IntegrityFileProgressEventHandler;
        public event Action<ProjectData, ProjectData, List<ChangedFile>>? ProjComparisonCompleteEventHandler;
        public event Action<MetaDataState> ManagerStateEventHandler;
        /// <summary>
        /// Fires once after both <see cref="ProjectMetaData"/> and <see cref="ProjectIgnoreData"/>
        /// are loaded (or constructed during init), carrying a composed
        /// <see cref="ProjectContext"/>.  Replaces the prior two-event
        /// MetaDataLoaded + UpdateIgnoreList coordination.
        /// </summary>
        public event Action<ProjectContext>? ProjectContextLoadedEventHandler;
        public event Action<ProjectData, List<ProjectSimilarity>>? SimilarityCheckCompleteEventHandler;
        /// <summary>
        /// Fires on every <see cref="RequestCheckoutVersion(ProjectData?, CheckoutMode)"/> exit —
        /// success and failure alike — carrying the reason in <see cref="CheckoutResult.Messages"/>.
        /// </summary>
        public event Action<CheckoutResult>? CheckoutCompleteEventHandler;
        /// <summary>Fires with the plan produced by <see cref="RequestVersionDeletePreview(ProjectData?)"/>.</summary>
        public event Action<VersionDeletePlan>? VersionDeletePreviewEventHandler;
        /// <summary>Fires on every <see cref="RequestDeleteVersion(ProjectData?, bool)"/> exit.</summary>
        public event Action<VersionDeleteResult>? VersionDeleteCompleteEventHandler;
        /// <summary>Fires on every <see cref="RequestRenameVersion"/> exit.</summary>
        public event Action<VersionRenameResult>? VersionRenameCompleteEventHandler;
        /// <summary>
        /// Fires whenever <see cref="RequestProjectRetrieval(string)"/> fails, carrying the
        /// structured reason the store could not be read (missing file, corrupt Base64,
        /// malformed JSON, schema written by a newer build, ...).  Before this existed the
        /// failure was swallowed into a <see cref="Trace"/> warning and the user saw a silent
        /// no-op; both the GUI and the CLI subscribe so the cause is shown instead.
        /// </summary>
        public event Action<MetaDataLoadResult>? MetaDataLoadFailedEventHandler;
        private MetaDataState _currentState;
        /// <summary>
        /// True while a Request* method owns the state machine end-to-end; sub-manager
        /// state notifications are dropped for its duration. See <see cref="ManagerStateCallBack"/>.
        /// </summary>
        private bool _suppressSubManagerState;
        public MetaDataState CurrentState
        {
            get => _currentState;
            set
            {
                _currentState = value;
                ManagerStateEventHandler?.Invoke(_currentState);
            }
        }

        private ProjectMetaData? _projectMetaData;
        public ProjectMetaData? ProjectMetaData
        {
            get => _projectMetaData;
            private set
            {
                if (value == null) throw new ArgumentNullException(nameof(ProjectMetaData));
                _projectMetaData = value;
                CurrentProjectPath = value.ProjectPath;
                MetaDataLoadedEventHandler?.Invoke(value);
            }
        }

        private ProjectData? _mainProjectData; 
        public ProjectData? MainProjectData 
        {
            get => _mainProjectData;
            private set
            {
                if (value == null || value is not ProjectData) throw new ArgumentNullException(nameof(_mainProjectData));
                else if (ProjectMetaData == null) throw new ArgumentNullException(nameof(ProjectMetaData));
                _mainProjectData = new ProjectData(value);
                ProjectMetaData.SetProjectMain(_mainProjectData);
                ProjLoadedEventHandler?.Invoke(_mainProjectData);
            }
        }
        private ProjectData? _srcProjectData; 
        public ProjectData? NewestProjectData
        {
            get
            {
                if (ProjectMetaData == null) return null;
                if (ProjectMetaData.ProjectDataList.First == null) return null;
                return ProjectMetaData.ProjectDataList.First.Value;
            }
        }

        private FileManager _fileManager;
        private BackupManager _backupManager;
        private UpdateManager _updateManager;
        private ExportManager _exportManager;
        private SettingManager _settingManager;
        private FileHandlerTool _fileHandlerTool;
        private HashTool _hashTool;
        private readonly IDialogService _dialogService;

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        public MetaDataManager() : this(new NullDialogService()) { }

        public MetaDataManager(IDialogService dialogService)
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        {
            _dialogService = dialogService;
            _fileHandlerTool = new FileHandlerTool();
            _hashTool = new HashTool();
        }

        public void Awake()
        {
            _backupManager = new BackupManager();
            _fileManager = new FileManager();
            _updateManager = new UpdateManager();
            _exportManager = new ExportManager();
            _settingManager = new SettingManager();

            MetaDataLoadedEventHandler += _backupManager.MetaDataManager_MetaDataLoadedCallBack;
            MetaDataLoadedEventHandler += _fileManager.MetaDataManager_MetaDataLoadedCallBack;
            MetaDataLoadedEventHandler += _updateManager.MetaDataManager_MetaDataLoadedCallBack;
            MetaDataLoadedEventHandler += _exportManager.MetaDataManager_MetaDataLoadedCallBack;
            MetaDataLoadedEventHandler += _settingManager.MetaDataManager_MetaDataLoadedCallBack;

            ProjLoadedEventHandler += _backupManager.MetaDataManager_ProjLoadedCallback;
            ProjLoadedEventHandler += _fileManager.MetaDataManager_ProjLoadedCallback;
            ProjLoadedEventHandler += _updateManager.MetaDataManager_ProjLoadedCallback;

            SrcProjectLoadedEventHandler += _updateManager.MetaDataManager_SrcProjectLoadedCallBack;
            StagedChangesEventHandler += _updateManager.MetaDataManager_StagedChangesCallBack;
            ProjectContextLoadedEventHandler += _fileManager.MetaDataManager_ProjectContextLoadedCallBack;

            _backupManager.ProjectRevertEventHandler += ProjectChangeCallBack;
            _backupManager.ManagerStateEventHandler += ManagerStateCallBack;
            _backupManager.FetchCompleteEventHandler += BackupManager_FetchCompleteCallBack;

            _updateManager.ProjectUpdateEventHandler += ProjectChangeCallBack;
            _updateManager.ManagerStateEventHandler += ManagerStateCallBack;
            _updateManager.ReportFileDifferences += UpdateManager_ReportFileDifferencesCallBack;

            _fileManager.ManagerStateEventHandler += ManagerStateCallBack;
            _fileManager.DataPreStagedEventHandler += FileManager_DataPreStagedCallBack;
            _fileManager.DataStagedEventHandler += FileManager_DataStagedCallBack;
            _fileManager.OverlappedFileFoundEventHandler += FileManager_OverlappedFileFoundCallBack;
            _fileManager.IntegrityCheckEventHandler += FileManager_IntegrityCheckCallBack;
            _fileManager.IntegrityProgressEventHandler += FileManager_IntegrityProgressCallBack;
            _fileManager.IntegrityFileProgressEventHandler += (relPath, outcome) => IntegrityFileProgressEventHandler?.Invoke(relPath, outcome);
            _fileManager.SrcProjectDataLoadedEventHandler += FileManager_SrcProjectLoadedCallBack;

            _exportManager.ManagerStateEventHandler += ManagerStateCallBack;
            _exportManager.ExportCompleteEventHandler += ExportManager_ExportCompleteCallBack;

            _settingManager.SetPrevProjectEventHandler += SettingManager_SetLastDstProjectCallBack;
            _settingManager.IgnoreDataLoadedEventHandler += SettingManager_IgnoreDataLoadedCallBack;

            _settingManager.DialogService = _dialogService;
            _settingManager.Awake();
        }

        /// <summary>
        /// Offers to reopen the last project recorded in DeployAssistant.config.
        /// Call AFTER the UI has subscribed to the manager events — the resulting
        /// project load reports back through ProjLoadedEventHandler.
        /// </summary>
        public void RequestPreviousProjectRestore() => _settingManager.PromptPreviousProjectRestore();

        public string? RequestSavedLanguage() => _settingManager.GetSavedLanguage();

        public void RequestSaveLanguage(string languageCode) => _settingManager.SaveLanguage(languageCode);

        #region View Model Request Calls
        /// <summary>
        /// Opens the project at <paramref name="projectPath"/> from its <c>ProjectMetaData.bin</c>.
        /// <para>
        /// The store is read as V1 — that is still the runtime on-disk format.  The V1→V2
        /// migration under <c>DeployAssistant.Core/Migration/</c> is <b>not</b> on this path
        /// (see <see cref="FileHandlerTool.MaxSupportedMetaDataSchemaVersion"/>); everything
        /// downstream of here is V1-shaped, so routing through <c>TryLoadProjectStore</c>
        /// would be a separate, larger change.
        /// </para>
        /// Every failure exit raises <see cref="MetaDataLoadFailedEventHandler"/> with the
        /// reason before returning <c>false</c>, so callers never have to guess.
        /// </summary>
        public bool RequestProjectRetrieval(string projectPath)
        {
            string projectMetaDataPath = $"{projectPath}\\ProjectMetaData.bin";

            try
            {
                CurrentState = MetaDataState.Retrieving;
                MetaDataLoadResult loadResult =
                    _fileHandlerTool.TryLoadProjectMetaData(projectMetaDataPath, out ProjectMetaData? retrievedData);
                if (loadResult.Success && retrievedData != null)
                {
                    if (retrievedData.ProjectPath != projectPath)
                    {
                        retrievedData.ProjectPath = projectPath;
                        retrievedData.ReconfigureProjectPath(projectPath);
                    }
                    retrievedData.ProjectPath = projectPath;
                    ProjectMetaData = retrievedData;
                    MainProjectData = retrievedData.ProjectMain;

                    _settingManager.SetRecentDstDirectory(projectPath);
                }
                else
                {
                    CurrentState = MetaDataState.Idle;
                    MetaDataLoadFailedEventHandler?.Invoke(loadResult);
                    return false;
                }
            }
            catch (Exception ex)
            {
                CurrentState = MetaDataState.Idle;
                Trace.TraceWarning($"MetaDataManager TryRetrieveProject Error {ex.Message}");
                MetaDataLoadFailedEventHandler?.Invoke(MetaDataLoadResult.Failed(
                    MetaDataLoadFailure.Unknown,
                    $"Could not open the project at '{projectPath}': {ex.Message}",
                    projectMetaDataPath));
                return false;
            }
            TryAppendProjParentDirAsProjectFile(MainProjectData, projectPath);
            CurrentState = MetaDataState.Idle;
            return true;
        }

        public async void RequestProjectInitialization(string projectPath)
        {
            try
            {
                CurrentState = MetaDataState.Initializing;
                StringBuilder changeLog = new StringBuilder();
                ProjectMetaData newProjectRepo = new ProjectMetaData(Path.GetFileName(projectPath), projectPath);
                ProjectIgnoreData newIgnoreData = new ProjectIgnoreData(projectPath);
                newIgnoreData.ConfigureDefaultIgnore(newProjectRepo.ProjectName);
                ProjectContext initCtx = ProjectContext.Create(newProjectRepo, newIgnoreData);

                Stopwatch stopwatch = new Stopwatch();
                stopwatch.Start();
                var getFilesTask = Task.Run(() => initCtx.Scanner.EnumerateFiles(projectPath, IgnoreType.Initialization).ToArray());
                var getDirsTask = Task.Run(() => initCtx.Scanner.EnumerateDirectories(projectPath, IgnoreType.Initialization).ToArray());

                string[]? newProjectFiles = await getFilesTask;
                string[]? newProjectDirs = await getDirsTask;
                stopwatch.Stop();
                Console.WriteLine(stopwatch.Elapsed.ToString());

                if (newProjectFiles == null || newProjectDirs == null)
                { 
                    Trace.TraceWarning("Couldn't Get Project Files (And Or) Directories on MetaDataManager"); 
                    return; 
                }

                ProjectData newProjectData = new ProjectData(projectPath);
                newProjectData.ProjectName = Path.GetFileName(projectPath);
                newProjectData.ConductedPC = Environment.MachineName;
                newProjectData.UpdatedVersion = GetProjectVersionName(newProjectData, true);
                changeLog.AppendLine($"Project Initialized");
                stopwatch.Reset();
                stopwatch.Start();

                var options = new ParallelOptions { MaxDegreeOfParallelism = 
                    Convert.ToInt32(Math.Ceiling((Environment.ProcessorCount * 0.75) * 1.0)) };
                ConcurrentDictionary<string, ProjectFile> tempDict = [];
                Parallel.ForEach(newProjectFiles, options, filePath =>
                {
                    ProjectFile newFile = new ProjectFile
                        (
                        ProjectDataType.File,
                        new FileInfo(PathCompat.ToNetFrameworkLongPath(filePath)).Length,
                        FileVersionInfo.GetVersionInfo(filePath).FileVersion,
                        newProjectData.UpdatedVersion,
                        DateTime.Now,
                        DataState.None,
                        Path.GetFileName(filePath),
                        projectPath,
                        PathCompat.GetRelativePath(projectPath, filePath),
                        "",
                        true
                        );
                    tempDict.TryAdd(newFile.DataRelPath, newFile); // Create ProjectFile object
                    _hashTool.GetFileMD5CheckSum(newFile);
                });
                
                newProjectData.ProjectFiles = new Dictionary<string, ProjectFile>(tempDict);
                stopwatch.Stop();
                Console.WriteLine(stopwatch.Elapsed.ToString());

                foreach (string dirPath in newProjectDirs)
                {
                    ProjectFile newFile = new ProjectFile
                        (
                        ProjectDataType.Directory,
                        0,
                        "",
                        newProjectData.UpdatedVersion,
                        DateTime.Now,
                        DataState.None,
                        Path.GetFileName(dirPath),
                        projectPath,
                        PathCompat.GetRelativePath(projectPath, dirPath),
                        "",
                        true
                        );
                    newProjectData.ProjectFiles.TryAdd(newFile.DataRelPath, newFile);
                    newProjectData.ChangedFiles.Add(new ChangedFile(new ProjectFile(newFile), DataState.Added));
                    changeLog.AppendLine($"Added {newFile.DataName}");
                }

                newProjectData.UpdatedTime = DateTime.Now;
                newProjectData.ChangeLog = changeLog.ToString();
                newProjectData.NumberOfChanges = newProjectData.ProjectFilesObs.Count;

                foreach (ProjectFile projFile in newProjectData.ProjectFiles.Values)
                {
                    newProjectData.ChangedFiles.Add(new ChangedFile(new ProjectFile(projFile), DataState.Added));
                }

                ProjectMetaData = newProjectRepo;
                MainProjectData = newProjectData;
                TryAppendProjParentDirAsProjectFile(MainProjectData, projectPath);
                TryGenerateSupplementDirectories(projectPath, newProjectData.ProjectName);
                _settingManager.SetRecentDstDirectory(projectPath);
                ProjectContextLoadedEventHandler?.Invoke(initCtx);

                CurrentState = MetaDataState.Idle;
            }
            catch (Exception ex)
            {
                CurrentState = MetaDataState.Idle;
                Trace.TraceWarning($"MetaDataManager Error InitializeProject {ex.Message}");
                return;
            }
        }

        public void RequestSrcDataRetrieval(string deployedPath)
        {
             _fileManager.RetrieveDataSrc(deployedPath);
        }

        public void RequestStagedFileListRefresh(string deployedPath)
        {

        }

        public bool RequestFetchBackup()
        {
            bool result = _backupManager.FetchBackupProjectList();
            if (!result) return false;
            return true;
        }

        /// <summary>
        /// Set when RequestRevertProject succeeds. Read-once flag for the
        /// RevisionListScreen to detect a recent checkout and pop back to MainScreen.
        /// Consumers should clear it after reading via ConsumeLastCheckedOut().
        /// </summary>
        public ProjectData? LastCheckedOut { get; private set; }

        public ProjectData? ConsumeLastCheckedOut()
        {
            var v = LastCheckedOut;
            LastCheckedOut = null;
            return v;
        }

        /// <summary>
        /// Set when RequestProjectUpdate succeeds. Read-once flag for the
        /// IntegrityResultScreen to detect a recent update and pop back to MainScreen.
        /// </summary>
        public ProjectData? LastUpdated { get; private set; }

        public ProjectData? ConsumeLastUpdated()
        {
            var v = LastUpdated;
            LastUpdated = null;
            return v;
        }

        /// <summary>
        /// Set after a successful version delete. Read-once flag mirroring
        /// <see cref="LastCheckedOut"/> / <see cref="LastUpdated"/> so a screen can detect
        /// that the list it is showing just lost an entry.
        /// </summary>
        public string? LastDeletedVersion { get; private set; }

        public string? ConsumeLastDeletedVersion()
        {
            var v = LastDeletedVersion;
            LastDeletedVersion = null;
            return v;
        }

        /// <summary>
        /// Applies <paramref name="target"/> to the working directory.
        /// <para>
        /// <see cref="CheckoutMode.Fast"/> diffs snapshot-to-snapshot;
        /// <see cref="CheckoutMode.CleanRestore"/> runs a full integrity scan of the
        /// working directory first, repairing drift that happened outside the app.
        /// </para>
        /// Both modes set <see cref="LastCheckedOut"/> and both always raise
        /// <see cref="CheckoutCompleteEventHandler"/>, so a caller waiting on the event
        /// never hangs on a silently-rejected request.
        /// </summary>
        public bool RequestCheckoutVersion(ProjectData? target, CheckoutMode mode = CheckoutMode.Fast)
        {
            List<string> messages = new List<string>();

            if (target == null)
            {
                messages.Add("No version was selected.");
                return FailCheckout(null, mode, messages, "RequestCheckoutVersion: target is null");
            }
            if (MainProjectData == null)
            {
                messages.Add("No project is loaded.");
                return FailCheckout(target, mode, messages, "RequestCheckoutVersion: MainProjectData is null");
            }

            // Sub-managers drop the state back to Idle when their own step finishes; that
            // would re-enable ViewModel commands halfway through a checkout. Own the state
            // machine for the whole operation instead.
            _suppressSubManagerState = true;
            try
            {
                CurrentState = mode == CheckoutMode.CleanRestore
                    ? MetaDataState.CleanRestoring
                    : MetaDataState.Processing;

                List<ChangedFile>? fileDifferences = mode == CheckoutMode.CleanRestore
                    ? _fileManager.ProjectIntegrityCheck(target)
                    : _fileManager.FindVersionDifferences(target, MainProjectData, true);

                if (fileDifferences == null)
                {
                    messages.Add(mode == CheckoutMode.CleanRestore
                        ? "Clean restore could not compute the file differences."
                        : "Version comparison could not compute the file differences.");
                    return FailCheckout(target, mode, messages, "RequestCheckoutVersion: file differences are null");
                }

                CurrentState = MetaDataState.Reverting;
                _backupManager.RevertProject(target, fileDifferences);

                LastCheckedOut = target;
                CurrentState = MetaDataState.Idle;
                CheckoutCompleteEventHandler?.Invoke(
                    new CheckoutResult(true, target, mode, fileDifferences.Count, messages));
                return true;
            }
            catch (Exception ex)
            {
                messages.Add($"Checkout failed: {ex.GetType().Name}: {ex.Message}");
                return FailCheckout(target, mode, messages, $"RequestCheckoutVersion failed: {ex.Message}");
            }
            finally
            {
                _suppressSubManagerState = false;
            }
        }

        /// <summary>Checkout by version tag; resolves against the stored version list.</summary>
        public bool RequestCheckoutVersion(string versionName, CheckoutMode mode = CheckoutMode.Fast)
        {
            ProjectData? target = _backupManager.FindVersion(versionName);
            if (target == null)
            {
                CurrentState = MetaDataState.Idle;
                CheckoutCompleteEventHandler?.Invoke(new CheckoutResult(false, null, mode, 0,
                    new List<string> { $"Version '{versionName}' was not found." }));
                Trace.TraceWarning($"RequestCheckoutVersion: version '{versionName}' not found");
                return false;
            }
            return RequestCheckoutVersion(target, mode);
        }

        /// <summary>Common checkout failure exit: back to Idle, then report the reason.</summary>
        private bool FailCheckout(ProjectData? target, CheckoutMode mode, List<string> messages, string traceMessage)
        {
            Trace.TraceWarning(traceMessage);
            CurrentState = MetaDataState.Idle;
            CheckoutCompleteEventHandler?.Invoke(new CheckoutResult(false, target, mode, 0, messages));
            return false;
        }

        [Obsolete("Use RequestCheckoutVersion(target, CheckoutMode.Fast).")]
        public bool RequestRevertProject(ProjectData? targetProject)
        {
            return RequestCheckoutVersion(targetProject, CheckoutMode.Fast);
        }

        [Obsolete("Use RequestCheckoutVersion(target, CheckoutMode.CleanRestore).")]
        public void RequestProjectCleanRestore(ProjectData? targetProject)
        {
            RequestCheckoutVersion(targetProject, CheckoutMode.CleanRestore);
        }

        /// <summary>
        /// Computes — without mutating anything — what deleting <paramref name="target"/>
        /// would cost and whether it is allowed. Returns <c>null</c> only when no project
        /// is loaded or no target was supplied.
        /// </summary>
        public VersionDeletePlan? RequestVersionDeletePreview(ProjectData? target)
        {
            if (target == null)
            {
                Trace.TraceWarning("RequestVersionDeletePreview: target is null");
                return null;
            }
            if (_projectMetaData == null)
            {
                Trace.TraceWarning("RequestVersionDeletePreview: no project metadata loaded");
                return null;
            }
            VersionDeletePlan plan = BuildDeletePlan(target);
            VersionDeletePreviewEventHandler?.Invoke(plan);
            return plan;
        }

        public VersionDeletePlan? RequestVersionDeletePreview(string versionName)
        {
            return RequestVersionDeletePreview(_backupManager.FindVersion(versionName));
        }

        /// <summary>
        /// Deletes one version: its exclusive backup blobs, its list entry and its backup
        /// folder.  Refuses on the current main, on the last remaining version, on an
        /// unknown version and while the manager is busy.  Always raises
        /// <see cref="VersionDeleteCompleteEventHandler"/> and always ends on
        /// <see cref="MetaDataState.Idle"/>.
        /// </summary>
        /// <param name="confirmed">Skip the interactive confirmation (CLI <c>--yes</c>, tests).</param>
        public bool RequestDeleteVersion(ProjectData? target, bool confirmed = false)
        {
            if (target == null)
            {
                Trace.TraceWarning("RequestDeleteVersion: target is null");
                RaiseDeleteBlocked("", new List<string> { "No version was selected." });
                return false;
            }
            if (_projectMetaData == null)
            {
                RaiseDeleteBlocked(target.UpdatedVersion, new List<string> { "No project is loaded." });
                return false;
            }

            VersionDeletePlan plan = BuildDeletePlan(target);
            if (!plan.CanDelete)
            {
                // Reported straight from here: routing a blocked plan through BackupManager
                // would push the state back to Idle even when the blocker IS a busy manager.
                RaiseDeleteBlocked(plan.VersionName, new List<string>(plan.Blockers));
                return false;
            }

            if (!confirmed)
            {
                bool proceed = _dialogService.Confirm("Delete Version",
                    $"Delete version '{plan.VersionName}'? " +
                    $"{plan.ExclusiveHashes.Count} backup file(s) will be removed permanently.") == DialogChoice.Yes;
                if (!proceed)
                {
                    RaiseDeleteBlocked(plan.VersionName, new List<string> { "Deletion cancelled." });
                    return false;
                }
            }

            try
            {
                CurrentState = MetaDataState.Deleting;
                VersionDeleteResult result = _backupManager.DeleteVersion(plan);
                if (result.Success) LastDeletedVersion = result.VersionName;
                CurrentState = MetaDataState.Idle;
                VersionDeleteCompleteEventHandler?.Invoke(result);
                return result.Success;
            }
            catch (Exception ex)
            {
                Trace.TraceError($"RequestDeleteVersion failed: {ex.Message}");
                CurrentState = MetaDataState.Idle;
                VersionDeleteCompleteEventHandler?.Invoke(new VersionDeleteResult(
                    VersionDeleteOutcome.Failed, plan.VersionName,
                    messages: new List<string> { $"Deletion failed: {ex.GetType().Name}: {ex.Message}" }));
                return false;
            }
        }

        public bool RequestDeleteVersion(string versionName, bool confirmed = false)
        {
            ProjectData? target = _backupManager.FindVersion(versionName);
            if (target == null)
            {
                RaiseDeleteBlocked(versionName, new List<string> { $"Version '{versionName}' was not found." });
                return false;
            }
            return RequestDeleteVersion(target, confirmed);
        }

        /// <summary>
        /// Re-tags a stored version: a new <c>UpdatedVersion</c> name, a new
        /// <c>UpdateLog</c>, or both.  Passing <c>null</c> for either leaves it as-is, so
        /// editing only the log is always allowed.
        /// <para>
        /// The version's backup folder is deliberately NOT renamed.  Backup blobs are
        /// referenced by absolute <c>DataSrcPath</c>, and moving a folder that other
        /// snapshots' <c>BackupFiles</c> entries point into means rewriting shared state
        /// with no atomic way to undo a partial move.  Pinning the folder to the original
        /// version name costs nothing at runtime — <c>GetFileBackupSrcPath</c> is only
        /// consulted when registering a snapshot that is NOT yet in the version list, and a
        /// renamed snapshot is by definition already registered.
        /// </para>
        /// </summary>
        public bool RequestRenameVersion(ProjectData? target, string? newVersionName, string? newUpdateLog)
        {
            List<string> messages = new List<string>();

            if (target == null)
            {
                messages.Add("No version was selected.");
                return FailRename(null, "", "", messages);
            }
            if (_projectMetaData == null)
            {
                messages.Add("No project is loaded.");
                return FailRename(target, target.UpdatedVersion, target.UpdatedVersion, messages);
            }
            if (CurrentState != MetaDataState.Idle)
            {
                messages.Add($"Manager is busy ({CurrentState}). Try again once the current operation finishes.");
                return FailRename(target, target.UpdatedVersion, target.UpdatedVersion, messages);
            }

            ProjectData? listed = _backupManager.FindVersion(target.UpdatedVersion);
            if (listed == null)
            {
                messages.Add($"Version '{target.UpdatedVersion}' is not part of this project's version list.");
                return FailRename(target, target.UpdatedVersion, target.UpdatedVersion, messages);
            }

            string previousName = listed.UpdatedVersion;
            bool renaming = newVersionName != null && newVersionName != previousName;

            if (renaming && !IsValidVersionName(newVersionName!, out string reason))
            {
                messages.Add(reason);
                return FailRename(listed, previousName, previousName, messages);
            }
            if (renaming && _backupManager.FindVersion(newVersionName) != null)
            {
                // ProjectData.Equals compares UpdatedVersion only — a duplicate tag would
                // silently merge two versions everywhere the list is searched.
                messages.Add($"Version '{newVersionName}' already exists.");
                return FailRename(listed, previousName, previousName, messages);
            }

            string previousLog = listed.UpdateLog;
            bool relogging = newUpdateLog != null && newUpdateLog != previousLog;
            if (!renaming && !relogging)
            {
                messages.Add("Nothing to change.");
                VersionRenameCompleteEventHandler?.Invoke(new VersionRenameResult(
                    true, listed, previousName, previousName, false, false, messages));
                return true;
            }

            ProjectData? main = MainProjectData;
            bool targetIsMain = main != null && main.UpdatedVersion == previousName;

            try
            {
                CurrentState = MetaDataState.Processing;

                if (renaming)
                {
                    listed.UpdatedVersion = newVersionName!;
                    // ProjectMain is stored separately from ProjectDataList; leaving it on the
                    // old tag would orphan the main pointer (ProjectData.Equals is tag-only).
                    if (targetIsMain && main != null) main.UpdatedVersion = newVersionName!;
                    if (targetIsMain && _projectMetaData.ProjectMain != null)
                        _projectMetaData.ProjectMain.UpdatedVersion = newVersionName!;
                }
                if (relogging)
                {
                    listed.UpdateLog = newUpdateLog!;
                    if (targetIsMain && main != null) main.UpdateLog = newUpdateLog!;
                    if (targetIsMain && _projectMetaData.ProjectMain != null)
                        _projectMetaData.ProjectMain.UpdateLog = newUpdateLog!;
                }

                if (!_backupManager.PersistMetaData(takeBackup: true))
                {
                    // Roll the in-memory edit back so the store and the file stay in sync.
                    if (renaming)
                    {
                        listed.UpdatedVersion = previousName;
                        if (targetIsMain && main != null) main.UpdatedVersion = previousName;
                        if (targetIsMain && _projectMetaData.ProjectMain != null)
                            _projectMetaData.ProjectMain.UpdatedVersion = previousName;
                    }
                    if (relogging)
                    {
                        listed.UpdateLog = previousLog;
                        if (targetIsMain && main != null) main.UpdateLog = previousLog;
                        if (targetIsMain && _projectMetaData.ProjectMain != null)
                            _projectMetaData.ProjectMain.UpdateLog = previousLog;
                    }
                    messages.Add("Could not save ProjectMetaData.bin; the version was left unchanged.");
                    return FailRename(listed, previousName, previousName, messages);
                }

                if (renaming)
                {
                    _projectMetaData.SetProjectMain(_projectMetaData.ProjectMain);
                    messages.Add($"Backup folder stays named 'Backup_{previousName}'; backup contents are unaffected.");
                }

                CurrentState = MetaDataState.Idle;
                _backupManager.FetchBackupProjectList();
                VersionRenameCompleteEventHandler?.Invoke(new VersionRenameResult(
                    true, listed, previousName, listed.UpdatedVersion, renaming, relogging, messages));
                return true;
            }
            catch (Exception ex)
            {
                Trace.TraceError($"RequestRenameVersion failed: {ex.Message}");
                messages.Add($"Rename failed: {ex.GetType().Name}: {ex.Message}");
                return FailRename(listed, previousName, listed.UpdatedVersion, messages);
            }
        }

        private bool FailRename(ProjectData? target, string previousName, string currentName, List<string> messages)
        {
            CurrentState = MetaDataState.Idle;
            VersionRenameCompleteEventHandler?.Invoke(new VersionRenameResult(
                false, target, previousName, currentName, false, false, messages));
            return false;
        }

        /// <summary>
        /// A version tag becomes a directory leaf (<c>Backup_&lt;UpdatedVersion&gt;</c>), so it
        /// has to survive as a Windows path segment.
        /// </summary>
        private static bool IsValidVersionName(string versionName, out string reason)
        {
            if (string.IsNullOrWhiteSpace(versionName))
            {
                reason = "Version name cannot be empty.";
                return false;
            }
            if (versionName != versionName.Trim())
            {
                reason = "Version name cannot start or end with whitespace.";
                return false;
            }
            if (versionName.EndsWith("."))
            {
                reason = "Version name cannot end with a period.";
                return false;
            }
            foreach (char invalid in Path.GetInvalidFileNameChars())
            {
                if (versionName.IndexOf(invalid) < 0) continue;
                reason = $"Version name contains an invalid character ({(char.IsControl(invalid) ? "control character" : invalid.ToString())}).";
                return false;
            }
            reason = "";
            return true;
        }

        private VersionDeletePlan BuildDeletePlan(ProjectData target)
        {
            VersionDeletePlan plan = _backupManager.BuildVersionDeletePlan(target, MainProjectData);
            if (CurrentState != MetaDataState.Idle)
                plan.AddBlocker($"Manager is busy ({CurrentState}). Try again once the current operation finishes.");
            return plan;
        }

        private void RaiseDeleteBlocked(string versionName, List<string> messages)
        {
            VersionDeleteCompleteEventHandler?.Invoke(
                new VersionDeleteResult(VersionDeleteOutcome.Blocked, versionName, messages: messages));
        }

        public bool RequestRevertChange(ProjectFile file)
        {
            _fileManager.RevertChange(file);
            // FileManager.RevertChange re-applies IntegrityChecked on failure paths
            // (missing backup, missing project file). Cleared flag => success.
            return (file.DataState & DataState.IntegrityChecked) == 0;
        }

        public void RequestStageChanges()
        {
            _fileManager.StageNewFilesAsync();
        }

        public void RequestClearStagedFiles()
        {
            _fileManager.ClearDeployedFileChanges();
        }

        public void RequestOverlappedFileAllocation(List<ChangedFile> overlapSorted, List<ChangedFile> newSorted)
        {
            _fileManager.RegisterAbnormalFiles(overlapSorted, newSorted);
        }

        public void RequestProjectIntegrityCheck()
        {
            _fileManager.MainProjectIntegrityCheck();
        }

        public void RequestFileRestore(ProjectFile targetFile, DataState state)
        {
            _fileManager.RegisterNewfile(targetFile, state);
        }

        public void RequestExportProjectBackup(ProjectData projectData)
        {
            _exportManager.ExportProject(projectData);
        }

        public void RequestExportProjectVersionLog(ProjectData projectData)
        {
            _exportManager.ExportProjectVersionLog(projectData);
        }

        public void RequestExportProjectVersionDiffFiles(List<ChangedFile> FileDiffs)
        {

        }

        public void RequestProjectCompatibility(ProjectData srcProjectData)
        {
            try
            {
                if (_projectMetaData == null) return;
                List<ProjectSimilarity> projectComparisons = []; 
                foreach (ProjectData projData in _projectMetaData.ProjectDataList)
                {
                    ProjectSimilarity similaraity = new ProjectSimilarity();
                    projectComparisons.Add(similaraity); 
                    //Compute the file differences 
                        try
                        {
                            int sigDiff = 0;
                            List<ChangedFile>? identifiedDiff = _fileManager.FindVersionDifferencesForIntegration(
                                srcProjectData, projData, out sigDiff, out int rawDiff);
                            similaraity.projData = projData;
                            similaraity.numDiffWithResources    = rawDiff;   // unfiltered total (incl. resource-only changes)
                            similaraity.numDiffWithoutResources = sigDiff;   // filtered (significant) count
                            similaraity.fileDifferences = identifiedDiff ?? [];
                        }
                        catch(Exception ex)
                        {
                            Trace.TraceWarning($"Error while collecting Project Compatibility {ex.Message}");
                            return;
                        }
                        finally
                        {
                        }
                    }

                SimilarityCheckCompleteEventHandler?.Invoke(srcProjectData, projectComparisons); 
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"Error while collecting Project Compatibility {ex.Message}");
                return; 
            }
            
        }

        public void RequestExportProjectFilesXLSX(ICollection<ProjectFile> projectFiles, ProjectData projData)
        {
            _exportManager.ExportProjectFilesXLSX(projData, projectFiles);
        }

        public bool RequestProjectUpdate(string? updaterName, string? updateLog, string? currentProjectPath)
        {
            if (string.IsNullOrWhiteSpace(updaterName) || string.IsNullOrWhiteSpace(currentProjectPath))
            {
                Trace.TraceWarning("RequestProjectUpdate: updaterName or projectPath is null/empty");
                return false;
            }
            try
            {
                if (_srcProjectData != null)
                {
                    bool tryIntegrate = _dialogService.Confirm("Integrate Project", "Src Project Data Found, Try Integrate?") == DialogChoice.Yes;
                    if (tryIntegrate)
                    {
                        List<ChangedFile>? fileDifferences = _fileManager.FindVersionDifferences(_srcProjectData, MainProjectData);
                        if (!_updateManager.TryIntegrateSrcProject(_srcProjectData, fileDifferences))
                        {
                            bool updateAnyway = _dialogService.Confirm("Update Project", "Integration Failed, Update Anyway?") == DialogChoice.Yes;
                            if (!updateAnyway)
                            {
                                return false;
                            }
                        }
                        else
                        {
                            _updateManager.MergeProjectMain(updaterName, updateLog, currentProjectPath);
                            return false;
                        }
                    }
                    else
                    {
                        bool updateAnyway = _dialogService.Confirm("Update Project", "Update Anyway?") == DialogChoice.Yes;
                        if (!updateAnyway)
                        {
                            return false;
                        }
                    }
                }
                bool ok = _updateManager.UpdateProjectMain(updaterName!, updateLog ?? "", currentProjectPath!);
                if (ok) LastUpdated = MainProjectData;
                return ok;
            }
            catch (Exception ex)
            {
                Trace.TraceError($"RequestProjectUpdate failed: {ex.Message}");
                return false;
            }
        }

        public void RequestProjVersionDiff(ProjectData srcData)
        {
            if (MainProjectData == null)
            {
                Trace.TraceWarning("RequestProjVersionDiff: MainProjectData is null. Load a project before comparing.");
                return;
            }
            List<ChangedFile>? fileDiff = _fileManager.FindVersionDifferences(srcData, MainProjectData);
            if (fileDiff == null)
            {
                Trace.TraceWarning("RequestProjVersionDiff: FindVersionDifferences returned null, cannot open diff window.");
                return;
            }
            ProjComparisonCompleteEventHandler?.Invoke(srcData, MainProjectData, fileDiff);
        }

        /// <summary>
        /// Deserialises a <c>.VersionLog</c> file into a <see cref="ProjectData"/> snapshot.
        /// Returns <c>null</c> when the file cannot be read or parsed.
        /// </summary>
        public ProjectData? LoadExternalMetaFile(string filePath)
        {
            _fileHandlerTool.TryDeserializeProjectData(filePath, out ProjectData? projectData);
            return projectData;
        }

        /// <summary>
        /// Computes the diff between an external <paramref name="externalData"/> metafile
        /// and the currently loaded main project.
        /// Returns <c>null</c> when no project is loaded or the diff could not be computed.
        /// </summary>
        public List<ChangedFile>? ComputeMetaFileDiff(ProjectData externalData)
        {
            if (MainProjectData == null)
            {
                Trace.TraceWarning("ComputeMetaFileDiff: MainProjectData is null. Load a project first.");
                return null;
            }
            return _fileManager.FindVersionDifferences(externalData, MainProjectData);
        }

        /// <summary>
        /// Asynchronously exports only the files in <paramref name="selectedDiff"/> as a
        /// diff-only sync package (zip + VersionLog).
        /// </summary>
        public void RequestExportDiffPackage(ProjectData currentProject, List<ChangedFile> selectedDiff)
        {
            Task.Run(() => _exportManager.ExportDiffPackage(currentProject, selectedDiff));
        }

        #endregion

        #region Version Management Tools
        private string GetProjectVersionName(ProjectData projData, bool isNewProject = false)
        {
            if (!isNewProject)
            {
                return $"{projData.ProjectName}_{Environment.MachineName}_{DateTime.Now.ToString("yyyy_MM_dd")}_v{projData.RevisionNumber + 1}";
            }
            return $"{projData.ProjectName}_{Environment.MachineName}_{DateTime.Now.ToString("yyyy_MM_dd")}_v{projData.RevisionNumber}";
        }

        #endregion

        #region Callbacks
        public void UpdateManager_ReportFileDifferencesCallBack(ProjectData srcData, ProjectData destData, List<ChangedFile> fileDifferences)
        {
            ProjComparisonCompleteEventHandler?.Invoke(srcData, MainProjectData, fileDifferences);
        }
        private void FileManager_IntegrityCheckCallBack(string changeLog, List<ProjectFile> changedFileList)
        {
            IntegrityCheckCompleteEventHandler?.Invoke(changeLog, new ObservableCollection<ProjectFile>(changedFileList));
        }

        private void FileManager_IntegrityProgressCallBack(int completed, int total)
        {
            IntegrityProgressEventHandler?.Invoke(completed, total);
        }

        private void FileManager_DataPreStagedCallBack(object preStagedFileListObj)
        {
            if (preStagedFileListObj is not List<ProjectFile> preStagedFileList) return;
            ObservableCollection<ProjectFile> preStagedChangesObs = new ObservableCollection<ProjectFile>(preStagedFileList);
            FileChangesEventHandler?.Invoke(preStagedChangesObs);
        }

        private void FileManager_DataStagedCallBack(object stagedFileListObj)
        {
            if (stagedFileListObj is not List<ChangedFile> stagedFiles)
            {
                Trace.TraceWarning("Improper stagedFile parameter value returned");
                return;
            }
            ObservableCollection<ProjectFile> stagedChangesObs = new ObservableCollection<ProjectFile>();
            foreach (ChangedFile file in stagedFiles)
            {
                if (file.DstFile != null) stagedChangesObs.Add(file.DstFile);
            }
            FileChangesEventHandler?.Invoke(stagedChangesObs);
            StagedChangesEventHandler?.Invoke(stagedFiles);
        }

        private void FileManager_OverlappedFileFoundCallBack(object overlapFileListObj, object newFileListObj)
        {
            if (overlapFileListObj is not List<ChangedFile> overlapFileList || newFileListObj is not List<ChangedFile> newFileList) return;
            OverlappedFileSortEventHandler?.Invoke(overlapFileList, newFileList);
        }

        private void ProjectChangeCallBack(object projObj)
        {
            if (projObj is not ProjectData projData) return;
            this.MainProjectData = projData;
        }

        private void FileManager_SrcProjectLoadedCallBack(object? srcProjectObj)
        {
            if (srcProjectObj is null)
            {
                _srcProjectData = null;
                SrcProjectLoadedEventHandler?.Invoke(_srcProjectData);
            }
            if (srcProjectObj is not ProjectData srcProjectData)
            {
                return;
            }
            _srcProjectData = srcProjectData;
            SrcProjectLoadedEventHandler?.Invoke(_srcProjectData);
        }

        private void BackupManager_FetchCompleteCallBack(object backupListObj)
        {
            if (backupListObj is not ObservableCollection<ProjectData> backupList) return;
            FetchRequestEventHandler?.Invoke(backupListObj);
        }

        private void ExportManager_ExportCompleteCallBack(object exportPathObj)
        {
            if (exportPathObj is not string exportPath) return;
            ProjExportEventHandler?.Invoke(exportPath);
        }

        private void ManagerStateCallBack(MetaDataState state)
        {
            // A composite operation (checkout) drives CurrentState itself; a sub-manager
            // reporting Idle at the end of its own step must not re-enable ViewModel
            // commands while the write is still in flight.
            if (_suppressSubManagerState) return;
            CurrentState = state;
        }
        private void SettingManager_SetLastDstProjectCallBack(string dstProjectPath)
        {
            if (!RequestProjectRetrieval(dstProjectPath))
            {
                Trace.TraceWarning("Project Data not found! Please Reconfigure Destination Path");
                return;
            }
        }

        private void SettingManager_IgnoreDataLoadedCallBack(ProjectMetaData metaData, ProjectIgnoreData ignoreData)
        {
            ProjectContextLoadedEventHandler?.Invoke(ProjectContext.Create(metaData, ignoreData));
        }
        #endregion

        #region Temporary
        #region Exports
        /// <summary>
        /// Input: Requested Project Data 
        /// Output: All the project files, including projectData meta file
        /// in a @.projectParentDir/Exports/ProjectVersion
        /// </summary>
        /// <param name="projectData"></param>

        public void ExportProjectRepo(ProjectMetaData projectRepository)
        {

        }
        public void GenerateProjectDataHash(object obj)
        {

        }
        private bool TryGenerateSupplementDirectories(string projPath, string projName)
        {
            try
            {
                string backupPath = $"{projPath}\\Backup_{projName}";
                string exportPath = $"{projPath}\\Export_{projName}";
                if (!Directory.Exists(PathCompat.ToNetFrameworkLongPath(backupPath))) Directory.CreateDirectory(PathCompat.ToNetFrameworkLongPath(backupPath));
                if (!Directory.Exists(PathCompat.ToNetFrameworkLongPath(exportPath))) Directory.CreateDirectory(PathCompat.ToNetFrameworkLongPath(exportPath));
                return true;
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"Project Initialization Failed due to {ex.Message}");
                return false;   
            }
        }
        //Since I did not include parent directory as part of project Directory in previous iterations of project initialization
        private bool TryAppendProjParentDirAsProjectFile(ProjectData? projData, string projectPath)
        {
            if (projData == null) return false;
            ProjectFile projParentDir = new ProjectFile
                        (
                            projectPath, 
                            "", 
                            null, 
                            DataState.None, 
                            ProjectDataType.Directory
                        );
            projData.ProjectFiles.TryAdd("", projParentDir);
            return true;
        }
        #endregion
        #endregion
    }
}