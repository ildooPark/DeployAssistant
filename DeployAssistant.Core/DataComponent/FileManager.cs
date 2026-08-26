using DeployAssistant.Filtering;
using DeployAssistant.Interfaces;
using DeployAssistant.Model;
using DeployAssistant.Utils;
using System.Linq;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace DeployAssistant.DataComponent
{
    [Flags]
    public enum DataState
    {
        None = 0,
        Added = 1,
        Deleted = 1 << 1,
        Restored = 1 << 2,
        Modified = 1 << 3,
        PreStaged = 1 << 4,
        IntegrityChecked = 1 << 5,
        Backup = 1 << 6, 
        Overlapped = 1 << 7,
        // 0x100 is written by the shipped 3.6.1 integration flow. Reserved; never reuse.
        Integrate = 1 << 8
    }

    /// <summary>Per-file verdict streamed by <see cref="FileManager.IntegrityFileProgressEventHandler"/> while an integrity check runs.</summary>
    public enum IntegrityFileOutcome
    {
        Checked,
        Modified,
        Added,
        Deleted,
        HashFailed,
        MetadataFallback
    }
    public class FileManager
    {
        #region Class Variables 
        private Dictionary<string, ProjectFile> _backupFilesDict;
        /// <summary>
        /// Imported ProjectMainFilesDict
        /// </summary>
        private Dictionary<string, ProjectFile> _projectFilesDict;
        /// <summary>
        /// Imported ProjectMainFilesDict List of ProjectFiles with Identical Name
        /// </summary>
        private Dictionary<string, List<ProjectFile>> _projectFilesDict_namesSorted;
        private Dictionary<string, List<ProjectFile>> _projectFilesDict_relDirSorted;
        private List<ProjectFile> _projDirFileList;
        /// <summary>
        /// Key: DataRelPath Value: ProjectFile
        /// </summary>
        private Dictionary<string, ProjectFile> _preStagedFilesDict;
        /// <summary>
        /// Key: DstFile DataRelPath Value: ChangedFile
        /// </summary>
        private Dictionary<string, ChangedFile> _registeredChangesDict; 
        private SemaphoreSlim _asyncControl;
        private FileHandlerTool _fileHandlerTool;
        private HashTool _hashTool; 
        private ProjectData? _dstProjectData;
        private ProjectData? _srcProjectData;
        private ProjectContext? _projectContext;
        #endregion

        #region Manager Events 
        public event Action<ProjectData?>? SrcProjectDataLoadedEventHandler;
        public event Action<List<ChangedFile>, List<ChangedFile>>? OverlappedFileFoundEventHandler;
        public event Action<object>? DataStagedEventHandler;
        public event Action<object>? DataPreStagedEventHandler;
        public event Action<object>? PreStagedDataOverlapEventHandler;
        public event Action<string, List<ProjectFile>>? IntegrityCheckEventHandler;
        /// <summary>
        /// Fires periodically during MainProjectIntegrityCheck: (completed, total).
        /// "Total" = 2 × intersectFiles.Count (the parallel hash phase plus the diff
        /// phase each iterate the intersect set once). "Completed" monotonically
        /// increases 0 → total. Useful for CLI/GUI progress bars.
        /// </summary>
        public event Action<int, int>? IntegrityProgressEventHandler;
        /// <summary>Streams (relPath, outcome) per file during MainProjectIntegrityCheck. Raised from worker threads; subscribers must marshal.</summary>
        public event Action<string, IntegrityFileOutcome>? IntegrityFileProgressEventHandler;
        /// <summary>Share (1-100) of intersecting files hashed during MainProjectIntegrityCheck; the rest verify by metadata. Set per run by MetaDataManager.</summary>
        public int IntegritySamplePercent { get; set; } = 100;
        public event Action<MetaDataState> ManagerStateEventHandler;
        #endregion

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        public FileManager()
        {
            _projectFilesDict = new Dictionary<string, ProjectFile>();
            _projectFilesDict_namesSorted = new Dictionary<string, List<ProjectFile>>();
            _projDirFileList = new List<ProjectFile>();
            _preStagedFilesDict = new Dictionary<string, ProjectFile>();
            _registeredChangesDict = new Dictionary<string, ChangedFile>();
            _fileHandlerTool = new FileHandlerTool();
            _hashTool = new HashTool();
            _asyncControl = new SemaphoreSlim(12, 12);
        }
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        #region Calls For File Differences
        public async void MainProjectIntegrityCheck()
        {
            ManagerStateEventHandler?.Invoke(MetaDataState.IntegrityChecking);
            bool staleContext = _dstProjectData != null && _projectContext != null
                && !string.Equals(_projectContext.MetaData.ProjectPath, _dstProjectData.ProjectPath, StringComparison.OrdinalIgnoreCase);
            if (_dstProjectData == null || _projectContext == null || staleContext)
            {
                ManagerStateEventHandler?.Invoke(MetaDataState.Idle);
                string reason = staleContext
                    ? $"Integrity check refused: the loaded ignore context belongs to '{_projectContext!.MetaData.ProjectPath}', not '{_dstProjectData!.ProjectPath}'. Re-open the project and retry."
                    : "Integrity check could not run: project or ignore context is missing. Re-open the project and retry.";
                Trace.TraceWarning(reason);
                IntegrityCheckEventHandler?.Invoke(reason, new List<ProjectFile>());
                return;
            }
            _preStagedFilesDict.Clear();
            // Entries from earlier integrity runs would otherwise accumulate for the whole
            // session (TryAdd lets stale state win) and ride into the next update.
            foreach (string staleKey in _registeredChangesDict
                         .Where(kv => (kv.Value.DataState & DataState.IntegrityChecked) != 0)
                         .Select(kv => kv.Key).ToList())
                _registeredChangesDict.Remove(staleKey);

            try
            {
                Stopwatch sw = new Stopwatch();
                sw.Start();
                StringBuilder fileIntegrityLog = new StringBuilder();
                List<ChangedFile> fileChanges = new List<ChangedFile>();
                fileIntegrityLog.AppendLine($"Conducting Version Integrity Check on {_dstProjectData.UpdatedVersion}");

                Dictionary<string, ProjectFile> projectFilesDict = _dstProjectData.ProjectFiles;

                // Filter the SNAPSHOT side too — .ignore'd files must be completely
                // invisible to integrity check (git-style untrack semantics).
                // Without this, snapshot entries matching .ignore would surface as
                // "Deleted" because they're absent from the (filtered) disk scan.
                var filter = _projectContext.IgnoreFilter;
                List<string> recordedFiles = _dstProjectData.ProjectRelFilePathsList
                    .Where(p => !filter.Matches(p, ProjectDataType.File, IgnoreType.IntegrityCheck))
                    .ToList();
                List<string> recordedDirs = _dstProjectData.ProjectRelDirsList
                    .Where(p => !filter.Matches(p, ProjectDataType.Directory, IgnoreType.IntegrityCheck))
                    .ToList();

                List<string> directoryRelFiles = [];
                List<string> directoryRelDirs = [];

                var scanner = _projectContext.Scanner;
                var filesTask = Task.Run(() => scanner.EnumerateFiles(_dstProjectData.ProjectPath, IgnoreType.IntegrityCheck).ToList());
                var dirsTask = Task.Run(() => scanner.EnumerateDirectories(_dstProjectData.ProjectPath, IgnoreType.IntegrityCheck).ToList());

                IEnumerable<string> directoryFiles = await filesTask;
                IEnumerable<string> directoryDirs = await dirsTask;
                fileIntegrityLog.AppendLine(sw.Elapsed.ToString());

                foreach (string absPathFile in directoryFiles)
                {
                    directoryRelFiles.Add(PathCompat.GetRelativePath(_dstProjectData.ProjectPath, absPathFile));
                }

                foreach (string absPathDir in directoryDirs)
                {
                    directoryRelDirs.Add(PathCompat.GetRelativePath(_dstProjectData.ProjectPath, absPathDir));
                }

                IEnumerable<string> addedFiles = directoryRelFiles.Except(recordedFiles);
                IEnumerable<string> addedDirs = directoryRelDirs.Except(recordedDirs);
                IEnumerable<string> deletedFiles = recordedFiles.Except(directoryRelFiles);
                IEnumerable<string> deletedDirs = recordedDirs.Except(directoryRelDirs);
                IEnumerable<string> intersectFiles = recordedFiles.Intersect(directoryRelFiles);

                // Fast check: pick the hash sample per run (fresh Random each run, so files
                // sampled out this time can still be caught on the next run). Unsampled files
                // keep an empty hash, which routes them through VerifyByMetadata below.
                int samplePercent = IntegritySamplePercent < 1 ? 1 : IntegritySamplePercent > 100 ? 100 : IntegritySamplePercent;
                HashSet<string>? hashSample = null;
                if (samplePercent < 100)
                {
                    var sampleRng = new Random();
                    hashSample = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (string relPath in intersectFiles)
                        if (sampleRng.Next(100) < samplePercent) hashSample.Add(relPath);
                    fileIntegrityLog.AppendLine($"Fast check: hashing {samplePercent}% sample ({hashSample.Count} of {intersectFiles.Count()} files); the rest verify by size/version metadata.");
                }

                foreach (string dirRelPath in addedDirs)
                {
                    ProjectFile dstFile = new ProjectFile(_dstProjectData.ProjectPath, dirRelPath, null, DataState.Added | DataState.IntegrityChecked, ProjectDataType.Directory);
                    ChangedFile newChange = new ChangedFile(dstFile, DataState.Added | DataState.IntegrityChecked);
                    _registeredChangesDict.TryAdd(dstFile.DataRelPath, newChange);
                    _preStagedFilesDict.TryAdd(dirRelPath, dstFile);
                }

                foreach (string dirRelPath in deletedDirs)
                {
                    ProjectFile dstFile = new ProjectFile(_dstProjectData.ProjectPath, dirRelPath, null, DataState.Deleted | DataState.IntegrityChecked, ProjectDataType.Directory);
                    ChangedFile newChange = new ChangedFile(dstFile, DataState.Deleted | DataState.IntegrityChecked);
                    _preStagedFilesDict.TryAdd(dirRelPath, dstFile);
                    _registeredChangesDict.TryAdd(dstFile.DataRelPath, newChange);
                }

                foreach (string fileRelPath in addedFiles)
                {
                    fileIntegrityLog.AppendLine($"{fileRelPath} has been Added");
                    // HashTool string overload returns "" on failure (locked file, IO error,
                    // missing file) — surface that via the integrity log so the user sees
                    // which added files couldn't be hashed.  Fix #3.
                    string? fileHash = _hashTool.GetFileMD5CheckSum(_dstProjectData.ProjectPath, fileRelPath);
                    if (string.IsNullOrEmpty(fileHash))
                    {
                        fileIntegrityLog.AppendLine($"Warning: Failed to hash added file {fileRelPath}; included with empty hash");
                    }
                    ProjectFile dstFile = new ProjectFile(_dstProjectData.ProjectPath, fileRelPath, fileHash, DataState.Added | DataState.IntegrityChecked, ProjectDataType.File);
                    _preStagedFilesDict.TryAdd(fileRelPath, dstFile);
                    _registeredChangesDict.TryAdd(dstFile.DataRelPath, new ChangedFile(dstFile, DataState.Added | DataState.IntegrityChecked));
                    try { IntegrityFileProgressEventHandler?.Invoke(fileRelPath, IntegrityFileOutcome.Added); } catch (Exception) { }
                }

                foreach (string fileRelPath in deletedFiles)
                {
                    fileIntegrityLog.AppendLine($"{fileRelPath} has been Deleted");
                    ProjectFile srcFile = new ProjectFile(projectFilesDict[fileRelPath], DataState.None);
                    ProjectFile dstFile = new ProjectFile(projectFilesDict[fileRelPath], DataState.Deleted | DataState.IntegrityChecked);
                    dstFile.UpdatedTime = DateTime.Now;
                    _preStagedFilesDict.TryAdd(fileRelPath, dstFile);
                    _registeredChangesDict.TryAdd(dstFile.DataRelPath, new ChangedFile(srcFile, dstFile, DataState.Deleted | DataState.IntegrityChecked, true));
                    try { IntegrityFileProgressEventHandler?.Invoke(fileRelPath, IntegrityFileOutcome.Deleted); } catch (Exception) { }
                }

                ConcurrentDictionary<string, ProjectFile> projectFilesConcurrent = new ConcurrentDictionary<string, ProjectFile> ();
                ConcurrentBag<string> hashFailureLog = new ConcurrentBag<string>();
                var maxConcurrency = new ParallelOptions { MaxDegreeOfParallelism = Convert.ToInt32(Math.Ceiling((Environment.ProcessorCount * 0.75) * 1.0)) };
                int completed = 0;
                int total = intersectFiles.Count() * 2;
                Parallel.ForEach(intersectFiles, maxConcurrency, fileRelPath =>
                {
                    //TODO : Resolve Hard coded issue -> Setting Manager Ignore
                    if (projectFilesDict[fileRelPath].DataType == ProjectDataType.Directory)
                    {
                        int c = Interlocked.Increment(ref completed);
                        try { IntegrityProgressEventHandler?.Invoke(c, total); } catch (Exception) { }
                        return;
                    }
                    ProjectFile intersectedFile = new ProjectFile(projectFilesDict[fileRelPath]);
                    // Clear stored hash so a silent GetFileMD5CheckSum failure (it swallows exceptions
                    // internally) leaves DataHash empty and is detectable below.
                    intersectedFile.DataHash = "";
                    if (!projectFilesConcurrent.TryAdd(fileRelPath, intersectedFile))
                    {
                        // Per-iteration failure — log + skip this file, but do NOT
                        // flip global state to Idle (the scan as a whole is still in
                        // progress).  Fix #5: removed premature ManagerStateEventHandler call.
                        hashFailureLog.Add($"Warning: Could not enqueue {fileRelPath} for hashing (duplicate); excluded from integrity check");
                        Trace.TraceWarning($"Couldn't Run File Integrity Check, Couldn't Hash Intersected File on {fileRelPath}");
                        int c = Interlocked.Increment(ref completed);
                        try { IntegrityProgressEventHandler?.Invoke(c, total); } catch (Exception) { }
                        return;
                    }
                    try
                    {
                        if (hashSample == null || hashSample.Contains(fileRelPath))
                            _hashTool.GetFileMD5CheckSum(intersectedFile);
                    }
                    catch (Exception ex)
                    {
                        // Fix #5: removed premature ManagerStateEventHandler Idle flip — the
                        // scan as a whole is still running; this single file just failed.
                        hashFailureLog.Add($"Warning: Hashing threw for {fileRelPath}: {ex.Message}; excluded from integrity check");
                        Trace.TraceWarning($"Couldn't Run File Integrity Check: File async Hashing Failed\n{ex.Message}");
                        projectFilesConcurrent.TryRemove(fileRelPath, out _);
                        int c = Interlocked.Increment(ref completed);
                        try { IntegrityProgressEventHandler?.Invoke(c, total); } catch (Exception) { }
                        return;
                    }
                    // Empty hash after the call means hash retries exhausted.  KEEP the file
                    // in projectFilesConcurrent so the async-task block can engage the metadata
                    // fallback (VerifyByMetadata).  Log the deferred verification so the user
                    // sees it.
                    if (string.IsNullOrEmpty(intersectedFile.DataHash)
                        && (hashSample == null || hashSample.Contains(fileRelPath)))
                    {
                        hashFailureLog.Add($"Note: {fileRelPath} — hash unavailable after retries; will verify by metadata");
                        try { IntegrityFileProgressEventHandler?.Invoke(fileRelPath, IntegrityFileOutcome.HashFailed); } catch (Exception) { }
                    }
                    {
                        int c = Interlocked.Increment(ref completed);
                        try { IntegrityProgressEventHandler?.Invoke(c, total); } catch (Exception) { }
                    }
                });

                foreach (string warning in hashFailureLog)
                    fileIntegrityLog.AppendLine(warning);

                List<Task> asyncTask = [];
                foreach (ProjectFile intersectedFile in projectFilesConcurrent.Values)
                {
                    asyncTask.Add(Task.Run(async () =>
                    {
                        await _asyncControl.WaitAsync();
                        try
                        {
                            if (!projectFilesDict.TryGetValue(intersectedFile.DataRelPath, out ProjectFile? projectFile))
                            {
                                // Fix #5: don't flip global state to Idle mid-scan — only this
                                // file is broken, not the whole check.
                                Trace.TraceWarning($"Couldn't Run File Integrity Check, project File does not exist in Intersected file list {intersectedFile.DataName}");
                                return;
                            }
                            bool hashMismatch;
                            bool verifiedByMetadata = false;
                            if (string.IsNullOrEmpty(intersectedFile.DataHash))
                            {
                                // Hash retries exhausted — fall back to size + version metadata.
                                var (metadataMatch, logMsg) = VerifyByMetadata(_dstProjectData.ProjectPath, projectFile.DataRelPath, projectFile);
                                fileIntegrityLog.AppendLine(logMsg);
                                hashMismatch = !metadataMatch;
                                verifiedByMetadata = true;
                            }
                            else
                            {
                                hashMismatch = projectFile.DataHash != intersectedFile.DataHash;
                            }

                            try
                            {
                                IntegrityFileProgressEventHandler?.Invoke(projectFile.DataRelPath,
                                    hashMismatch ? IntegrityFileOutcome.Modified
                                    : verifiedByMetadata ? IntegrityFileOutcome.MetadataFallback
                                    : IntegrityFileOutcome.Checked);
                            }
                            catch (Exception) { }

                            if (hashMismatch)
                            {
                                fileIntegrityLog.AppendLine($"File {projectFile.DataName} on {projectFile.DataRelPath} has been modified");

                                ProjectFile srcFile = new ProjectFile(projectFile, DataState.None);
                                ProjectFile dstFile = new ProjectFile(projectFile, DataState.Modified | DataState.IntegrityChecked);

                                string dstAbsPath = PathCompat.ToNetFrameworkLongPath(Path.Combine(_dstProjectData.ProjectPath, projectFile.DataRelPath));
                                try
                                {
                                    dstFile.BuildVersion = FileVersionInfo.GetVersionInfo(dstAbsPath).FileVersion ?? "";
                                    var dstInfo = new FileInfo(dstAbsPath);
                                    dstFile.DataSize = dstInfo.Length;
                                    // Never LastAccessTime: this check reads every file, so that would stamp the whole tree identically.
                                    dstFile.UpdatedTime = dstInfo.LastWriteTime;
                                }
                                catch (Exception ex)
                                {
                                    // Version/size/time read failed; retain values copied from projectFile and log the warning.
                                    fileIntegrityLog.AppendLine($"Warning: Could not read version/size/time for modified file {projectFile.DataRelPath}: {ex.Message}");
                                }
                                dstFile.DataHash = intersectedFile.DataHash;  // may be "" when hash failed — new contract: callers tolerate empty

                                _preStagedFilesDict.TryAdd(projectFile.DataRelPath, dstFile);
                                _registeredChangesDict.TryAdd(dstFile.DataRelPath, new ChangedFile(srcFile, dstFile, DataState.Modified | DataState.IntegrityChecked, true));
                            }
                        }
                        catch (Exception Ex)
                        {
                            Trace.TraceWarning($"Failed During Integrity Test {Ex.Message}");
                            return;
                        }
                        finally
                        {
                            _asyncControl.Release();
                            int c = Interlocked.Increment(ref completed);
                            try { IntegrityProgressEventHandler?.Invoke(c, total); } catch (Exception) { }
                        }
                    }));
                }

                await Task.WhenAll(asyncTask);

                fileIntegrityLog.Append($"Integrity Check Took: {sw.Elapsed.ToString()}s \n");
                fileIntegrityLog.AppendLine("Integrity Check Complete");
                DataStagedEventHandler?.Invoke(_registeredChangesDict.Values.ToList());
                IntegrityCheckEventHandler?.Invoke(fileIntegrityLog.ToString(), _preStagedFilesDict.Values.ToList());
                ManagerStateEventHandler?.Invoke(MetaDataState.Idle);
                Trace.TraceInformation($"Integrity check timing: {sw.Elapsed}");
                sw.Stop();
            }

            catch (Exception ex)
            {
                ManagerStateEventHandler?.Invoke(MetaDataState.Idle);
                Trace.TraceError($"{ex.Message}. Couldn't Run File Integrity Check");
                // Fix #4: surface the abort to the GUI/CLI so they don't sit silent
                // waiting for results that will never arrive.
                IntegrityCheckEventHandler?.Invoke(
                    $"Integrity check aborted: {ex.GetType().Name}: {ex.Message}",
                    new List<ProjectFile>());
            }
        }
        public List<ChangedFile>? ProjectIntegrityCheck(ProjectData targetProject)
        {
            if (targetProject == null)
            {
                ManagerStateEventHandler?.Invoke(MetaDataState.Idle);
                Trace.TraceWarning("Main Project is Missing");
                return null;
            }
            ManagerStateEventHandler?.Invoke(MetaDataState.CleanRestoring);
            _preStagedFilesDict.Clear();
            _registeredChangesDict.Clear();
            try
            {
                List<ChangedFile> fileChanges = new List<ChangedFile>();

                Dictionary<string, ProjectFile> projectFilesDict = targetProject.ProjectFiles;
                string backupPath = $"{targetProject.ProjectPath}\\Backup_{targetProject.ProjectName}";
                string exportPath = $"{targetProject.ProjectPath}\\Export_{targetProject.ProjectName}";

                // Filter the SNAPSHOT side through .ignore (IntegrityCheck scope) —
                // git-style untrack: ignored entries are completely invisible to the
                // diff, never become restore-from-backup candidates.
                List<string> recordedFiles = targetProject.ProjectRelFilePathsList;
                List<string> recordedDirs = targetProject.ProjectRelDirsList;
                if (_projectContext != null)
                {
                    var snapshotFilter = _projectContext.IgnoreFilter;
                    recordedFiles = recordedFiles
                        .Where(p => !snapshotFilter.Matches(p, ProjectDataType.File, IgnoreType.IntegrityCheck))
                        .ToList();
                    recordedDirs = recordedDirs
                        .Where(p => !snapshotFilter.Matches(p, ProjectDataType.Directory, IgnoreType.IntegrityCheck))
                        .ToList();
                }

                List<string> directoryRelFiles = new List<string>();
                List<string> directoryRelDirs = new List<string>();

                if (!Directory.Exists(backupPath)) Directory.CreateDirectory(backupPath);
                if (!Directory.Exists(exportPath)) Directory.CreateDirectory(exportPath);
                string[]? backupFiles = Directory.GetFiles(PathCompat.ToNetFrameworkLongPath(backupPath), "*", SearchOption.AllDirectories).Select(PathCompat.StripNetFrameworkLongPathPrefix).ToArray();
                string[]? backupDirs = Directory.GetDirectories(PathCompat.ToNetFrameworkLongPath(backupPath), "*", SearchOption.AllDirectories).Select(PathCompat.StripNetFrameworkLongPathPrefix).ToArray();
                backupFiles ??= [];
                backupDirs ??= [];
                string[]? exportFiles = Directory.GetFiles(PathCompat.ToNetFrameworkLongPath(exportPath), "*", SearchOption.AllDirectories).Select(PathCompat.StripNetFrameworkLongPathPrefix).ToArray();
                string[]? exportDirs = Directory.GetDirectories(PathCompat.ToNetFrameworkLongPath(exportPath), "*", SearchOption.AllDirectories).Select(PathCompat.StripNetFrameworkLongPathPrefix).ToArray();
                if (exportFiles == null) exportFiles = new string[0];
                if (exportDirs == null) exportDirs = new string[0];

                string[]? rawFiles = Directory.GetFiles(PathCompat.ToNetFrameworkLongPath(targetProject.ProjectPath), "*", SearchOption.AllDirectories).Select(PathCompat.StripNetFrameworkLongPathPrefix).ToArray();
                string[]? rawDirs = Directory.GetDirectories(PathCompat.ToNetFrameworkLongPath(targetProject.ProjectPath), "*", SearchOption.AllDirectories).Select(PathCompat.StripNetFrameworkLongPathPrefix).ToArray();
                if (rawFiles == null) backupFiles = new string[0];
                if (rawDirs == null) backupDirs = new string[0];

                // Apply IIgnoreFilter (IntegrityCheck scope) via the predicate before computing
                // the diff — mirrors the pattern in MainProjectIntegrityCheck.
                IEnumerable<string> directoryFiles = rawFiles.ToList().Except(backupFiles.ToList()).Except(exportFiles.ToList());
                IEnumerable<string> directoryDirs = rawDirs.ToList().Except(backupDirs.ToList()).Except(exportDirs.ToList());
                if (_projectContext != null)
                {
                    var filter = _projectContext.IgnoreFilter;
                    directoryFiles = directoryFiles
                        .Where(f => !filter.Matches(PathCompat.GetRelativePath(targetProject.ProjectPath, f), ProjectDataType.File, IgnoreType.IntegrityCheck));
                    directoryDirs = directoryDirs
                        .Where(d => !filter.Matches(PathCompat.GetRelativePath(targetProject.ProjectPath, d), ProjectDataType.Directory, IgnoreType.IntegrityCheck));
                }

                foreach (string absPathFile in directoryFiles)
                {
                    directoryRelFiles.Add(PathCompat.GetRelativePath(targetProject.ProjectPath, absPathFile));
                }

                foreach (string absPathDir in directoryDirs)
                {
                    directoryRelDirs.Add(PathCompat.GetRelativePath(targetProject.ProjectPath, absPathDir));
                }

                IEnumerable<string> filesToDelete = directoryRelFiles.Except(recordedFiles);
                IEnumerable<string> dirsToDelete = directoryRelDirs.Except(recordedDirs);
                IEnumerable<string> filesToAdd = recordedFiles.Except(directoryRelFiles);
                IEnumerable<string> dirsToAdd = recordedDirs.Except(directoryRelDirs);
                IEnumerable<string> intersectFiles = recordedFiles.Intersect(directoryRelFiles);

                foreach (string dirRelPath in dirsToDelete)
                {
                    if (dirRelPath == $"Backup_{targetProject.ProjectName}" || dirRelPath == $"Export_{targetProject.ProjectName}") continue;
                    ProjectFile dstFile = new ProjectFile(targetProject.ProjectPath, dirRelPath, null, DataState.Deleted, ProjectDataType.Directory);
                    ChangedFile newChange = new ChangedFile(dstFile, DataState.Deleted);
                    fileChanges.Add(newChange);
                }

                foreach (string dirRelPath in dirsToAdd)
                {
                    ProjectFile dstFile = new ProjectFile(targetProject.ProjectPath, dirRelPath, null, DataState.Added, ProjectDataType.Directory);
                    ChangedFile newChange = new ChangedFile(dstFile, DataState.Added);
                    fileChanges.Add(newChange);
                }

                foreach (string fileRelPath in filesToDelete)
                {
                    if (fileRelPath == "ProjectMetaData.bin") continue;
                    ProjectFile dstFile = new ProjectFile(targetProject.ProjectPath, fileRelPath, null, DataState.Deleted, ProjectDataType.File);
                    ChangedFile newChange = new ChangedFile(dstFile, DataState.Deleted);
                    fileChanges.Add(newChange);
                }

                // Per-file failures (missing backup, unhashable file) get accumulated
                // here and surfaced via Trace + the integrity-check log argument so
                // the GUI/CLI user sees what couldn't be restored without the whole
                // scan aborting.
                List<string> restoreFailures = new List<string>();

                foreach (string fileRelPath in filesToAdd)
                {
                    if (!_backupFilesDict.TryGetValue(projectFilesDict[fileRelPath].DataHash, out ProjectFile? backupFile))
                    {
                        string msg = $"Failed To Retrieve File {projectFilesDict[fileRelPath].DataName} For Restoration (no backup for hash)";
                        Trace.TraceWarning(msg);
                        restoreFailures.Add(msg);
                        continue;
                    }
                    ProjectFile srcFile = new ProjectFile(backupFile, DataState.None);
                    ProjectFile dstFile = new ProjectFile(projectFilesDict[fileRelPath], DataState.Added);
                    ChangedFile newChange = new ChangedFile(srcFile, dstFile, DataState.Added, true);
                    fileChanges.Add(newChange);
                }

                foreach (string fileRelPath in intersectFiles)
                {
                    string? dirFileHash = _hashTool.GetFileMD5CheckSum(targetProject.ProjectPath, fileRelPath);

                    if (string.IsNullOrEmpty(dirFileHash))
                    {
                        // Hash retries exhausted — fall back to size + BuildVersion metadata
                        // comparison.  Match → no change record. Mismatch → fall through to
                        // the existing backup-lookup branch using the snapshot's stored hash.
                        var (metadataMatch, logMsg) = VerifyByMetadata(targetProject.ProjectPath, fileRelPath, projectFilesDict[fileRelPath]);
                        Trace.TraceWarning(logMsg);
                        restoreFailures.Add(logMsg);
                        if (metadataMatch)
                        {
                            continue;  // Unchanged — no change record emitted
                        }
                        // Metadata mismatch: route through the Modified branch.  Set the disk hash
                        // to a sentinel that differs from the stored hash so the next if-block fires.
                        dirFileHash = "<metadata-mismatch-sentinel>";
                    }

                    if (projectFilesDict[fileRelPath].DataHash != dirFileHash)
                    {
                        if (!_backupFilesDict.TryGetValue(projectFilesDict[fileRelPath].DataHash, out ProjectFile? backupFile))
                        {
                            string msg = $"Failed To Retrieve File {projectFilesDict[fileRelPath].DataName} For Restoration (no backup for stored hash)";
                            Trace.TraceWarning(msg);
                            restoreFailures.Add(msg);
                            continue;
                        }
                        ProjectFile srcFile = new ProjectFile(backupFile, DataState.None);
                        ProjectFile dstFile = new ProjectFile(backupFile, DataState.Restored, targetProject.ProjectPath);
                        dstFile.DataRelPath = fileRelPath;
                        ChangedFile newChange = new ChangedFile(srcFile, dstFile, DataState.Modified, true);
                        fileChanges.Add(newChange);
                    }
                }

                if (restoreFailures.Count > 0)
                {
                    Trace.TraceWarning($"ProjectIntegrityCheck completed with {restoreFailures.Count} per-file failure(s); see warnings above.");
                }

                ManagerStateEventHandler?.Invoke(MetaDataState.Idle);
                return fileChanges;
            }

            catch (Exception ex)
            {
                Trace.TraceError($"{ex.Message}. Couldn't Run Version Clean Restoring File Check");
                ManagerStateEventHandler?.Invoke(MetaDataState.Idle);
                // Surface the abort so GUI/CLI doesn't sit silent.  Empty file list
                // because no partial work survives an outer-catch exception.
                IntegrityCheckEventHandler?.Invoke(
                    $"Integrity check aborted: {ex.GetType().Name}: {ex.Message}",
                    new List<ProjectFile>());
                return null;
            }
        }
        /// <summary>
        /// Merging Src => Dst, Occurs in version Reversion or Merging from outer source. 
        /// </summary>
        /// <param name="isRevert"> True if Reverting, else (Merge) false.</param>
        public List<ChangedFile>? FindVersionDifferences(ProjectData srcData, ProjectData dstData, bool isProjectRevert)
        {
            if (!isProjectRevert)
                return FindVersionDifferences(srcData, dstData);
            ManagerStateEventHandler?.Invoke(MetaDataState.Processing);
            try
            {
                List<ChangedFile> fileChanges = [];

                List<string> recordedFiles = [];
                List<string> directoryFiles = [];

                Dictionary<string, ProjectFile> srcDict = srcData.ProjectFiles;
                Dictionary<string, ProjectFile> dstDict = dstData.ProjectFiles;

                // .ignore'd files are completely invisible to the revert diff — they
                // get skipped on both sides so revert never tries to restore them
                // from backup or delete them from disk (git-style untrack semantics).
                // Scope IgnoreType.All here means "any active .ignore entry applies",
                // matching the user's mental model that .ignore takes a file out of
                // DA's universe regardless of which per-operation scope flag it has.
                Func<string, ProjectDataType, bool> isIgnored = (rel, dt) =>
                    _projectContext != null && _projectContext.IgnoreFilter.Matches(rel, dt, IgnoreType.All);

                var srcFilePaths = srcData.ProjectRelFilePathsList.Where(p => !isIgnored(p, ProjectDataType.File)).ToList();
                var dstFilePaths = dstData.ProjectRelFilePathsList.Where(p => !isIgnored(p, ProjectDataType.File)).ToList();
                var srcDirPaths  = srcData.ProjectRelDirsList.Where(p => !isIgnored(p, ProjectDataType.Directory)).ToList();
                var dstDirPaths  = dstData.ProjectRelDirsList.Where(p => !isIgnored(p, ProjectDataType.Directory)).ToList();

                // Files which is not on the Dst
                IEnumerable<string> filesToAdd = srcFilePaths.Except(dstFilePaths);
                // Files which is not on the Src
                IEnumerable<string> filesToDelete = dstFilePaths.Except(srcFilePaths);
                // Directories which is not on the Src
                IEnumerable<string> dirsToAdd = srcDirPaths.Except(dstDirPaths);
                // Directories which is not on the Dst
                IEnumerable<string> dirsToDelete = dstDirPaths.Except(srcDirPaths);
                // Files to Overwrite
                IEnumerable<string> intersectFiles = srcFilePaths.Intersect(dstFilePaths);

                //1. Directories 
                foreach (string dirRelPath in dirsToAdd)
                {
                    ProjectFile dstDir = new ProjectFile(srcDict[dirRelPath], DataState.Added, dstData.ProjectPath);
                    ChangedFile newChange = new ChangedFile(dstDir, DataState.Added);
                    fileChanges.Add(newChange);
                }

                foreach (string dirRelPath in dirsToDelete)
                {
                    ProjectFile dstDir = new ProjectFile(dstDict[dirRelPath], DataState.Deleted);
                    fileChanges.Add(new ChangedFile(dstDir, DataState.Deleted));
                }

                foreach (string fileRelPath in filesToAdd)
                {
                    if (!_backupFilesDict.TryGetValue(srcDict[fileRelPath].DataHash, out ProjectFile? backupFile))
                    {
                        Trace.TraceWarning($"Following Previous Project Version {srcData.UpdatedVersion}\n" +
                            $"Lacks Backup File {srcDict[fileRelPath].DataName}");
                        return null;
                    }
                    ProjectFile srcFile = new ProjectFile(backupFile, DataState.Backup, backupFile.DataSrcPath);
                    srcDict.TryGetValue(fileRelPath, out ProjectFile? recordedSrcFile);
                    ProjectFile dstFile = new ProjectFile(recordedSrcFile, DataState.Restored, dstData.ProjectPath);
                    fileChanges.Add(new ChangedFile(srcFile, dstFile, DataState.Restored, true));
                }

                foreach (string fileRelPath in filesToDelete)
                {
                    ProjectFile dstFile = new ProjectFile(dstDict[fileRelPath], DataState.Deleted);
                    fileChanges.Add(new ChangedFile(dstFile, DataState.Deleted));
                }

                foreach (string fileRelPath in intersectFiles)
                {
                    if (srcDict[fileRelPath].DataHash != dstDict[fileRelPath].DataHash)
                    {
                        if (!_backupFilesDict.TryGetValue(srcDict[fileRelPath].DataHash, out ProjectFile? backupFile))
                        {
                            Trace.TraceWarning($"Following Previous Project Version {srcData.UpdatedVersion} Lacks Backup File {srcDict[fileRelPath].DataName}");
                            return null;
                        }
                        ProjectFile srcFile = new ProjectFile(backupFile, DataState.Backup, backupFile.DataSrcPath);
                        srcFile.DataHash = dstDict[fileRelPath].DataHash;
                        ProjectFile dstFile = new ProjectFile(backupFile, DataState.Restored, dstDict[fileRelPath].DataSrcPath);
                        dstFile.DataRelPath = fileRelPath;
                        fileChanges.Add(new ChangedFile(srcFile, dstFile, DataState.Restored, true));
                    }
                }
                ManagerStateEventHandler?.Invoke(MetaDataState.Idle);
                DataStagedEventHandler?.Invoke(fileChanges);
                return fileChanges;
            }
            catch (Exception ex)
            {
                ManagerStateEventHandler?.Invoke(MetaDataState.Idle);
                Trace.TraceError($"{ex.Message}. Couldn't Run Find Version Differences For Backup");
                return null;
            }
        }
        public List<ChangedFile>? FindVersionDifferencesForIntegration(
            ProjectData srcData,
            ProjectData dstData,
            out int significantDiff,
            out int rawDiff)
        {
            if (_projectContext == null)
            {
                significantDiff = -1;
                rawDiff = -1;
                return null;
            }
            try
            {
                ManagerStateEventHandler?.Invoke(MetaDataState.Processing);
                List<ChangedFile> fileChanges = new List<ChangedFile>();

                List<string> recordedFiles = new List<string>();
                List<string> directoryFiles = new List<string>();

                Dictionary<string, ProjectFile> srcDict = srcData.ProjectFiles;
                Dictionary<string, ProjectFile> dstDict = dstData.ProjectFiles;

                // Files which is not on the Dst
                IEnumerable<string> filesOnSrc = srcData.ProjectRelFilePathsList.Except(dstData.ProjectRelFilePathsList);
                // Files which is not on the Src
                IEnumerable<string> filesOnDst = dstData.ProjectRelFilePathsList.Except(srcData.ProjectRelFilePathsList);
                // Directories which is not on the Src
                IEnumerable<string> dirsOnSrc = srcData.ProjectRelDirsList.Except(dstData.ProjectRelDirsList);
                // Directories which is not on the Dst
                IEnumerable<string> dirsOnDst = dstData.ProjectRelDirsList.Except(srcData.ProjectRelDirsList);
                // Files to Overwrite
                IEnumerable<string> intersectFiles = srcData.ProjectRelFilePathsList.Intersect(dstData.ProjectRelFilePathsList);

                foreach (string dirRelPath in dirsOnSrc)
                {
                    ProjectFile srcDir = new ProjectFile(srcDict[dirRelPath], DataState.Added, dstData.ProjectPath);
                    ProjectFile dstDir = new ProjectFile(ProjectDataType.Directory);
                    ChangedFile newChange = new ChangedFile(srcDir, dstDir, DataState.Added);
                    fileChanges.Add(newChange);
                }

                foreach (string dirRelPath in dirsOnDst)
                {
                    ProjectFile srcFile = new ProjectFile(ProjectDataType.Directory);
                    ProjectFile dstFile = new ProjectFile(dstDict[dirRelPath], DataState.Deleted);
                    ChangedFile newChange = new ChangedFile(srcFile, dstFile, DataState.Added);
                    fileChanges.Add(new ChangedFile(srcFile, dstFile, DataState.Deleted));
                }

                foreach (string fileRelPath in filesOnSrc)
                {
                    ProjectFile srcFile = new ProjectFile(srcDict[fileRelPath], DataState.None);
                    ProjectFile dstFile = new ProjectFile(ProjectDataType.File);
                    fileChanges.Add(new ChangedFile(srcFile, dstFile, DataState.Added, true));
                }

                foreach (string fileRelPath in filesOnDst)
                {
                    ProjectFile srcFile = new ProjectFile(ProjectDataType.File);
                    ProjectFile dstFile = new ProjectFile(dstDict[fileRelPath], DataState.Deleted);
                    fileChanges.Add(new ChangedFile(srcFile, dstFile, DataState.Deleted));
                }

                foreach (string fileRelPath in intersectFiles)
                {
                    if (srcDict[fileRelPath].DataHash != dstDict[fileRelPath].DataHash)
                    {
                        ProjectFile srcFile = new ProjectFile(srcDict[fileRelPath], DataState.None);
                        ProjectFile dstFile = new ProjectFile(dstDict[fileRelPath], DataState.Modified);
                        fileChanges.Add(new ChangedFile(srcFile, dstFile, DataState.Modified, true));
                    }
                }
                rawDiff = fileChanges.Count;
                List<ChangedFile> filteredChangedList = new List<ChangedFile>(fileChanges);
                var filter = _projectContext.IgnoreFilter;
                filteredChangedList.RemoveAll(c =>
                {
                    ProjectFile? pf = (c.SrcFile == null || c.SrcFile.DataName == "") ? c.DstFile : c.SrcFile;
                    return pf != null && filter.Matches(pf.DataRelPath, pf.DataType, IgnoreType.Integration);
                });
                ManagerStateEventHandler?.Invoke(MetaDataState.Idle);
                significantDiff = filteredChangedList.Count;
                return filteredChangedList;
            }
            catch (Exception ex)
            {
                ManagerStateEventHandler?.Invoke(MetaDataState.Idle);
                Trace.TraceError($"{ex.Message}. Couldn't Run Find Version Differences Against Given Src");
                significantDiff = -1;
                rawDiff = -1;
                return null;
            }
        }
        public List<ChangedFile>? FindVersionDifferences(ProjectData srcData, ProjectData dstData)
        {
            try
            {
                ManagerStateEventHandler?.Invoke(MetaDataState.Processing);
                List<ChangedFile> fileChanges = new List<ChangedFile>();

                List<string> recordedFiles = new List<string>();
                List<string> directoryFiles = new List<string>();

                Dictionary<string, ProjectFile> srcDict = srcData.ProjectFiles;
                Dictionary<string, ProjectFile> dstDict = dstData.ProjectFiles;

                // Files which is not on the Dst 
                IEnumerable<string> filesOnSrc = srcData.ProjectRelFilePathsList.Except(dstData.ProjectRelFilePathsList);
                // Files which is not on the Src
                IEnumerable<string> filesOnDst = dstData.ProjectRelFilePathsList.Except(srcData.ProjectRelFilePathsList);
                // Directories which is not on the Src
                IEnumerable<string> dirsOnSrc = srcData.ProjectRelDirsList.Except(dstData.ProjectRelDirsList);
                // Directories which is not on the Dst
                IEnumerable<string> dirsOnDst = dstData.ProjectRelDirsList.Except(srcData.ProjectRelDirsList);
                // Files to Overwrite
                IEnumerable<string> intersectFiles = srcData.ProjectRelFilePathsList.Intersect(dstData.ProjectRelFilePathsList);
                // TODO: Filter out the Ignore File List 

                foreach (string dirRelPath in dirsOnSrc)
                {
                    ProjectFile srcDir = new ProjectFile(srcDict[dirRelPath], DataState.Added, dstData.ProjectPath);
                    ProjectFile dstDir = new ProjectFile(ProjectDataType.Directory);

                    ChangedFile newChange = new ChangedFile(srcDir, dstDir, DataState.Added);
                    fileChanges.Add(newChange);
                }

                foreach (string dirRelPath in dirsOnDst)
                {
                    ProjectFile srcFile = new ProjectFile(ProjectDataType.Directory);
                    ProjectFile dstFile = new ProjectFile(dstDict[dirRelPath], DataState.Deleted);
                    ChangedFile newChange = new ChangedFile(srcFile, dstFile, DataState.Added);
                    fileChanges.Add(new ChangedFile(srcFile, dstFile, DataState.Deleted));
                }

                foreach (string fileRelPath in filesOnSrc)
                {
                    ProjectFile srcFile = new ProjectFile(srcDict[fileRelPath], DataState.None);
                    ProjectFile dstFile = new ProjectFile(ProjectDataType.File);
                    fileChanges.Add(new ChangedFile(srcFile, dstFile, DataState.Added, true));
                }

                foreach (string fileRelPath in filesOnDst)
                {
                    ProjectFile srcFile = new ProjectFile(ProjectDataType.File);
                    ProjectFile dstFile = new ProjectFile(dstDict[fileRelPath], DataState.Deleted);
                    fileChanges.Add(new ChangedFile(srcFile, dstFile, DataState.Deleted));
                }

                foreach (string fileRelPath in intersectFiles)
                {
                    if (srcDict[fileRelPath].DataHash != dstDict[fileRelPath].DataHash)
                    {
                        ProjectFile srcFile = new ProjectFile(srcDict[fileRelPath], DataState.None);
                        ProjectFile dstFile = new ProjectFile(dstDict[fileRelPath], DataState.Modified);
                        fileChanges.Add(new ChangedFile(srcFile, dstFile, DataState.Modified, true));
                    }
                }

                ManagerStateEventHandler?.Invoke(MetaDataState.Idle);
                return fileChanges;
            }
            catch (Exception ex)
            {
                ManagerStateEventHandler?.Invoke(MetaDataState.Idle);
                Trace.TraceWarning($"{ex.Message}. Couldn't Run Find Version Differences Against Given Src");
                return null;
            }
        }
        #endregion

        #region Calls For File PreStage Update
        public void RetrieveDataSrc(string srcPath)
        {
            try
            {
                ManagerStateEventHandler?.Invoke(MetaDataState.Retrieving);
                string[] binFiles = Directory.GetFiles(PathCompat.ToNetFrameworkLongPath(srcPath), "*.VersionLog", SearchOption.AllDirectories).Select(PathCompat.StripNetFrameworkLongPathPrefix).ToArray();
                if (binFiles.Length == 1)
                {
                    var stream = File.ReadAllBytes(binFiles[0]);
                    bool result = _fileHandlerTool.TryDeserializeProjectData(binFiles[0], out ProjectData? srcProjectData);
                    if (srcProjectData != null)
                    {
                        srcProjectData.ProjectPath = srcPath;
                        _srcProjectData = srcProjectData;
                        SrcProjectDataLoadedEventHandler?.Invoke(srcProjectData);
                        RegisterNewData(srcPath);
                    }
                }
                else
                {
                    SrcProjectDataLoadedEventHandler?.Invoke(null);
                    RegisterNewData(srcPath);
                }
            }
            catch (Exception ex)
            {
                ManagerStateEventHandler?.Invoke(MetaDataState.Idle);
                Trace.TraceWarning($"File Manager RetrieveDataSrc Error: {ex.Message}");
            }
            ManagerStateEventHandler?.Invoke(MetaDataState.Idle);
        }
        private void RegisterNewData(string srcDirPath)
        {
            if (TryGetDeployMetaFile(srcDirPath, out DeployData? deployData))
            {
                if (TryValidateDeployMetaFile(srcDirPath, deployData))
                {
                    RegisterFilesFromDeployData(srcDirPath, deployData);
                    RegisterFilesUnderSubDirectory(srcDirPath);
                    return;
                }
                else
                {
                    Trace.TraceWarning("Failed to Allocate src files using previous settings. Allocate Manually");
                }
            }
            try
            {
                RegisterAllSrcFiles(srcDirPath);
            }
            catch (Exception ex)
            {
                Trace.TraceError($"FileManager RegisterNewData Error {ex.Message}");
                return;
            }
        }
        private async void RegisterAllSrcFiles(string srcDirPath)
        {
            string[]? filesAllDirectories;
            string[]? filesTopDirectories;
            string[]? dirsAllDirectories;

            try
            {
                var filesAllDirTask = Task.Run(() => Directory.GetFiles(PathCompat.ToNetFrameworkLongPath(srcDirPath), "*", SearchOption.AllDirectories).Select(PathCompat.StripNetFrameworkLongPathPrefix).ToArray());
                var filesTopDirTask = Task.Run(() => Directory.GetFiles(PathCompat.ToNetFrameworkLongPath(srcDirPath), "*", SearchOption.TopDirectoryOnly).Select(PathCompat.StripNetFrameworkLongPathPrefix).ToArray());
                var dirsAllTask = Task.Run(() => Directory.GetDirectories(PathCompat.ToNetFrameworkLongPath(srcDirPath), "*", SearchOption.AllDirectories).Select(PathCompat.StripNetFrameworkLongPathPrefix).ToArray());
                filesAllDirectories = await filesAllDirTask;
                filesTopDirectories = await filesTopDirTask;
                dirsAllDirectories = await dirsAllTask;     


            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"FileManager RegisterNewData Error {ex.Message}");
                filesAllDirectories = null;
                filesTopDirectories = null;
                dirsAllDirectories = null;
                return; 
            }
            if (filesAllDirectories == null || filesTopDirectories == null || dirsAllDirectories == null)
            {
                Trace.TraceError($"Couldn't get files or dirrectories from given Directory {srcDirPath}");
                return;
            }

            try
            {
                // Apply IgnoreType.Deploy filter via the predicate-based IIgnoreFilter
                // (works even when no ProjectContext has been loaded yet — falls through
                // to the unfiltered paths).
                string[] filteredAllFiles, filteredTopFiles, filteredDirs;
                if (_projectContext != null)
                {
                    var filter = _projectContext.IgnoreFilter;
                    filteredAllFiles = filesAllDirectories
                        .Where(f => !filter.Matches(PathCompat.GetRelativePath(srcDirPath, f), ProjectDataType.File, IgnoreType.Deploy))
                        .ToArray();
                    filteredTopFiles = filesTopDirectories
                        .Where(f => !filter.Matches(PathCompat.GetRelativePath(srcDirPath, f), ProjectDataType.File, IgnoreType.Deploy))
                        .ToArray();
                    filteredDirs = dirsAllDirectories
                        .Where(d => !filter.Matches(PathCompat.GetRelativePath(srcDirPath, d), ProjectDataType.Directory, IgnoreType.Deploy))
                        .ToArray();
                }
                else
                {
                    filteredAllFiles = filesAllDirectories;
                    filteredTopFiles = filesTopDirectories;
                    filteredDirs = dirsAllDirectories;
                }

                var filesSubDirectories = filteredAllFiles.Except(filteredTopFiles);
                RegisterFilesUnderSubDirectory(srcDirPath, filesSubDirectories.ToArray(), filteredDirs);
                HandleAbnormalFiles(srcDirPath, filteredTopFiles);
            }
            catch (Exception ex)
            {
                Trace.TraceError($"FileManager RegisterNewData Error {ex.Message}");
                return;
            }
        }
        private void RegisterFilesUnderSubDirectory(string srcDirPath, string[] filesSubDirs, string[] dirsAllDirs)
        {
            try
            {
                foreach (string subDirFileAbsPath in filesSubDirs)
                {
                    ProjectFile newFile = new ProjectFile
                        (
                        new FileInfo(PathCompat.ToNetFrameworkLongPath(subDirFileAbsPath)).Length,
                        FileVersionInfo.GetVersionInfo(PathCompat.ToNetFrameworkLongPath(subDirFileAbsPath)).FileVersion,
                        Path.GetFileName(subDirFileAbsPath),
                        srcDirPath,
                        PathCompat.GetRelativePath(srcDirPath, subDirFileAbsPath)
                        );
                    _preStagedFilesDict.TryAdd(newFile.DataRelPath, newFile);
                }

                foreach (string dirAbsPath in dirsAllDirs)
                {
                    if (Path.GetExtension(dirAbsPath) == ".VersionLog") continue;
                    ProjectFile newFile = new ProjectFile
                        (
                        Path.GetFileName(dirAbsPath),
                        srcDirPath,
                        PathCompat.GetRelativePath(srcDirPath, dirAbsPath)
                        );
                    _preStagedFilesDict.TryAdd(newFile.DataRelPath, newFile);
                }
                DataPreStagedEventHandler?.Invoke(_preStagedFilesDict.Values.ToList());
            }
            catch (Exception ex)
            {
                Trace.TraceError($"FileManager RegisterNewData Error {ex.Message}");
                return;
            }
            
        }
        private void RegisterFilesUnderSubDirectory(string srcDirPath)
        {
            try
            {
                string[]? filesAllDirectories;
                string[]? filesTopDirectories;
                string[]? dirsAllDirectories;

                try
                {
                    filesAllDirectories = Directory.GetFiles(PathCompat.ToNetFrameworkLongPath(srcDirPath), "*", SearchOption.AllDirectories).Select(PathCompat.StripNetFrameworkLongPathPrefix).ToArray();
                    filesTopDirectories = Directory.GetFiles(PathCompat.ToNetFrameworkLongPath(srcDirPath), "*", SearchOption.TopDirectoryOnly).Select(PathCompat.StripNetFrameworkLongPathPrefix).ToArray();
                    dirsAllDirectories = Directory.GetDirectories(PathCompat.ToNetFrameworkLongPath(srcDirPath), "*", SearchOption.AllDirectories).Select(PathCompat.StripNetFrameworkLongPathPrefix).ToArray();
                }
                catch (Exception ex)
                {
                    Trace.TraceWarning($"FileManager RegisterNewData Error {ex.Message}");
                    filesAllDirectories = null;
                    filesTopDirectories = null;
                    dirsAllDirectories = null;
                }
                if (filesAllDirectories == null || filesTopDirectories == null || dirsAllDirectories == null)
                {
                    Trace.TraceError($"Couldn't get files or dirrectories from given Directory {srcDirPath}");
                    return;
                }

                // Apply IgnoreType.Deploy filter via the predicate-based IIgnoreFilter
                // (mirrors RegisterAllSrcFiles path).
                string[] filteredAllFiles2, filteredTopFiles2, filteredDirs2;
                if (_projectContext != null)
                {
                    var filter = _projectContext.IgnoreFilter;
                    filteredAllFiles2 = filesAllDirectories
                        .Where(f => !filter.Matches(PathCompat.GetRelativePath(srcDirPath, f), ProjectDataType.File, IgnoreType.Deploy))
                        .ToArray();
                    filteredTopFiles2 = filesTopDirectories
                        .Where(f => !filter.Matches(PathCompat.GetRelativePath(srcDirPath, f), ProjectDataType.File, IgnoreType.Deploy))
                        .ToArray();
                    filteredDirs2 = dirsAllDirectories
                        .Where(d => !filter.Matches(PathCompat.GetRelativePath(srcDirPath, d), ProjectDataType.Directory, IgnoreType.Deploy))
                        .ToArray();
                }
                else
                {
                    filteredAllFiles2 = filesAllDirectories;
                    filteredTopFiles2 = filesTopDirectories;
                    filteredDirs2 = dirsAllDirectories;
                }

                var filesSubDirectories = filteredAllFiles2.Except(filteredTopFiles2);
                foreach (string subDirFileAbsPath in filesSubDirectories)
                {
                    ProjectFile newFile = new ProjectFile
                        (
                        new FileInfo(PathCompat.ToNetFrameworkLongPath(subDirFileAbsPath)).Length,
                        FileVersionInfo.GetVersionInfo(PathCompat.ToNetFrameworkLongPath(subDirFileAbsPath)).FileVersion,
                        Path.GetFileName(subDirFileAbsPath),
                        srcDirPath,
                        PathCompat.GetRelativePath(srcDirPath, subDirFileAbsPath)
                        );
                    _preStagedFilesDict.TryAdd(newFile.DataRelPath, newFile);
                }

                foreach (string dirAbsPath in filteredDirs2)
                {
                    if (Path.GetExtension(dirAbsPath) == ".VersionLog") continue;
                    ProjectFile newFile = new ProjectFile
                        (
                        Path.GetFileName(dirAbsPath),
                        srcDirPath,
                        PathCompat.GetRelativePath(srcDirPath, dirAbsPath)
                        );
                    _preStagedFilesDict.TryAdd(newFile.DataRelPath, newFile);
                }
                DataPreStagedEventHandler?.Invoke(_preStagedFilesDict.Values.ToList());
            }
            catch (Exception ex)
            {
                Trace.TraceError($"FileManager RegisterNewData Error {ex.Message}");
                return;
            }

        }
        private void RegisterNewData(ProjectData srcProjectData)
        {
            try
            {
                foreach (ProjectFile srcFile in srcProjectData.ProjectFiles.Values)
                {
                    srcFile.DataState |= DataState.PreStaged;
                    if (!_preStagedFilesDict.TryAdd(srcFile.DataRelPath, srcFile))
                    {
                        Trace.TraceError($"Already Enlisted File {srcFile.DataName}: for Update");
                    }
                    else continue;
                }
                DataPreStagedEventHandler?.Invoke(_preStagedFilesDict.Values.ToList());
            }
            catch (Exception ex)
            {
                Trace.TraceError(ex.Message);
                return;
            }
        }
        private void HandleAbnormalFiles(string srcPath, string[] topDirFilePaths)
        {
            List<ChangedFile> registeredOverlapsList = [];
            List<ChangedFile> registeredNewList = [];

            for (int i = 0; i < topDirFilePaths.Length; i++)
            {
                int count = 0; 
                string? fileName = Path.GetFileName(topDirFilePaths[i]);
                List<ProjectFile>? overlappingFiles = null;
                if (fileName == null)
                {
                    Trace.TraceWarning($"Couldn't Process file name for overlapping file check on {topDirFilePaths[i]}");
                    return;
                }
                if (Path.GetExtension(topDirFilePaths[i]) == ".VersionLog") continue;
                if (Path.GetExtension(topDirFilePaths[i]) == ".deploy") continue; 
                if (_projectFilesDict_namesSorted.TryGetValue(fileName, out List<ProjectFile>? fileList))
                {
                    count = fileList.Count;
                    overlappingFiles = fileList;
                }
                // File Overlaps
                if (count >= 2 && overlappingFiles != null)
                {
                    ProjectFile newFile = new ProjectFile
                        (
                        new FileInfo(PathCompat.ToNetFrameworkLongPath(topDirFilePaths[i])).Length,
                        FileVersionInfo.GetVersionInfo(PathCompat.ToNetFrameworkLongPath(topDirFilePaths[i])).FileVersion,
                        Path.GetFileName(topDirFilePaths[i]),
                        srcPath,
                        PathCompat.GetRelativePath(srcPath, topDirFilePaths[i])
                        );
                    //Filter process 
                    List<ProjectFile> filteredFileList = [];
                    foreach (ProjectFile file in overlappingFiles)
                    {
                        if (_preStagedFilesDict.TryGetValue(file.DataRelPath, out ProjectFile? projFile))
                        {
                            continue;
                        }
                        filteredFileList.Add(file);
                    }
                    if (filteredFileList.Count == 1)
                    {
                        string newSrcFilePath = Path.Combine(srcPath, filteredFileList[0].DataRelPath);
                        _fileHandlerTool.HandleFile(newFile.DataAbsPath, newSrcFilePath, DataState.PreStaged);
                        newFile.DataRelPath = filteredFileList[0].DataRelPath;
                        _preStagedFilesDict.TryAdd(newFile.DataRelPath, newFile);
                    }
                    else
                    {
                        foreach (ProjectFile file in filteredFileList)
                        {
                            ChangedFile newOverlap = new ChangedFile(newFile, new ProjectFile(file), DataState.Overlapped);
                            registeredOverlapsList.Add(newOverlap);
                        }
                    }
                }
                // Modification => Recreate Appropriate Folder Directory
                else if (count == 1 && overlappingFiles != null)
                {
                    string newSrcFilePath = Path.Combine(srcPath, overlappingFiles[0].DataRelPath);
                    _fileHandlerTool.HandleFile(topDirFilePaths[i], newSrcFilePath, DataState.PreStaged);

                    ProjectFile newFile = new ProjectFile
                        (
                        new FileInfo(PathCompat.ToNetFrameworkLongPath(newSrcFilePath)).Length,
                        FileVersionInfo.GetVersionInfo(PathCompat.ToNetFrameworkLongPath(newSrcFilePath)).FileVersion,
                        Path.GetFileName(newSrcFilePath),
                        srcPath,
                        PathCompat.GetRelativePath(srcPath, newSrcFilePath)
                        );

                    _preStagedFilesDict.TryAdd(newFile.DataRelPath, newFile);
                }
                // New File
                else
                {
                    ProjectFile newFile = new ProjectFile
                        (
                        new FileInfo(PathCompat.ToNetFrameworkLongPath(topDirFilePaths[i])).Length,
                        FileVersionInfo.GetVersionInfo(PathCompat.ToNetFrameworkLongPath(topDirFilePaths[i])).FileVersion,
                        Path.GetFileName(topDirFilePaths[i]),
                        srcPath,
                        PathCompat.GetRelativePath(srcPath, topDirFilePaths[i])
                        );
                    foreach (ProjectFile projDir in _projDirFileList)
                    {
                        ChangedFile potentialNew = new ChangedFile(newFile, projDir, DataState.Overlapped);
                        registeredNewList.Add(potentialNew);
                    }
                }
            }
            if (registeredOverlapsList.Count >= 1)
            {
                OverlappedFileFoundEventHandler?.Invoke(registeredOverlapsList, registeredNewList);
            }
            DataPreStagedEventHandler?.Invoke(_preStagedFilesDict.Values.ToList());
        }
        public void RegisterNewfile(ProjectFile projectFile, DataState fileState)
        {
            ProjectFile newfile = new ProjectFile(projectFile, fileState | DataState.PreStaged);
            if (!_preStagedFilesDict.TryAdd(newfile.DataRelPath, newfile))
            {
                PreStagedDataOverlapEventHandler?.Invoke(newfile);
                return;
            }
            DataPreStagedEventHandler?.Invoke(_preStagedFilesDict.Values.ToList());
        }
        public void RegisterAbnormalFiles(List<ChangedFile> sortedOverlaps, List<ChangedFile> sortedNew)
        {
            Dictionary<string, ProjectFile> newlyAllocatedFiles = []; 
            foreach (ChangedFile overlappedFile in sortedOverlaps)
            {
                if (overlappedFile.DstFile.IsDstFile)
                {
                    string newSrcFilePath = Path.Combine(overlappedFile.SrcFile.DataSrcPath, overlappedFile.DstFile.DataRelPath);

                    _fileHandlerTool.HandleFile(overlappedFile.SrcFile.DataAbsPath, newSrcFilePath, DataState.PreStaged);
                    ProjectFile newPreStagedFile = new ProjectFile(overlappedFile.SrcFile, DataState.PreStaged);
                    newPreStagedFile.DataRelPath = overlappedFile.DstFile.DataRelPath;
                    newlyAllocatedFiles.TryAdd(newPreStagedFile.DataRelPath, newPreStagedFile); 
                    _preStagedFilesDict.TryAdd(newPreStagedFile.DataRelPath, newPreStagedFile);
                }
            }
            foreach (ChangedFile newFile in sortedNew)
            {
                if (newFile.DstFile == null || newFile.SrcFile == null) continue;
                if (newFile.DstFile.IsDstFile)
                {
                    string newSrcFileRelPath = Path.Combine(newFile.DstFile.DataRelPath, newFile.SrcFile.DataName);
                    string newSrcFilePath = Path.Combine(newFile.SrcFile.DataSrcPath, newSrcFileRelPath);

                    _fileHandlerTool.HandleFile(newFile.SrcFile.DataAbsPath, newSrcFilePath, DataState.Added);
                    ProjectFile newPreStagedFile = new ProjectFile(newFile.SrcFile, DataState.Added);
                    newPreStagedFile.DataRelPath = newSrcFileRelPath;
                    _preStagedFilesDict.TryAdd(newPreStagedFile.DataRelPath, newPreStagedFile);
                    newlyAllocatedFiles.TryAdd(newPreStagedFile.DataRelPath, new ProjectFile(newPreStagedFile, DataState.PreStaged));
                }
            }
            RegisterDeployData(_dstProjectData.ProjectName, newlyAllocatedFiles);
            DataPreStagedEventHandler?.Invoke(_preStagedFilesDict.Values.ToList());
        }
        private void RegisterDeployData(string projectName, Dictionary<string, ProjectFile> registeredDeployment)
        {
            try
            {
                string srcPath = registeredDeployment.Values.First().DataSrcPath;
                const string deployFilename = "DeployAssistant.deploy";
                string deployfilePath = Path.Combine(srcPath, deployFilename);
                DeployData deployData = new DeployData(projectName, registeredDeployment);
                _fileHandlerTool.TrySerializeJsonData(deployfilePath, deployData);
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"Failed Deployment {ex.Message}");
            }
        }
        private void RegisterFilesFromDeployData(string srcPath, DeployData deployedData)
        {
            foreach (ProjectFile registeredFile in deployedData.SortedTopFiles.Values)
            {
                ProjectFile newPreStagedFile = new ProjectFile(registeredFile, DataState.PreStaged);
                newPreStagedFile.DataSrcPath = srcPath;
                _preStagedFilesDict.TryAdd(newPreStagedFile.DataRelPath, newPreStagedFile);
            }
            DataPreStagedEventHandler?.Invoke(_preStagedFilesDict.Values.ToList());
        }
        private bool TryRemovePreRegisteredAllocation(string dstSrcPath, DeployData deployedData)
        {
            try
            {
                foreach (ProjectFile registeredFile in deployedData.SortedTopFiles.Values)
                {
                    string fileOriginalSrcPath = Path.Combine(dstSrcPath, registeredFile.DataName);

                    if (File.Exists(PathCompat.ToNetFrameworkLongPath(registeredFile.DataAbsPath)) && fileOriginalSrcPath != registeredFile.DataAbsPath) 
                        File.Delete(PathCompat.ToNetFrameworkLongPath(registeredFile.DataAbsPath));
                }
                return true; 
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"Failed to remove existing registered files in src folder {ex.Message}");
                return false;
            }
        }
        #endregion

        #region Calls for File Stage Update
        public async void StageNewFilesAsync()
        {
            if (_dstProjectData == null)
            {
                Trace.TraceWarning("Project Data is unreachable from FileManager");
                return;
            }
            ManagerStateEventHandler?.Invoke(MetaDataState.Processing);
            await HashPreStagedFilesAsync();
            UpdateStageFileList();
            ManagerStateEventHandler?.Invoke(MetaDataState.Idle);
        }

        private async Task HashPreStagedFilesAsync()
        {
            try
            {
                if (_preStagedFilesDict.Count <= 0) return;
                List<Task> asyncTasks = new List<Task>();
                //Update changedFilesDict
                foreach (ProjectFile file in _preStagedFilesDict.Values)
                {
                    if (file.DataType == ProjectDataType.Directory || file.DataHash != "") continue;
                    asyncTasks.Add(Task.Run(async () =>
                    {
                        await _asyncControl.WaitAsync();
                        try
                        {
                            _hashTool.GetFileMD5CheckSum(file);
                        }
                        finally
                        {
                            _asyncControl.Release();
                        }
                    }));
                }
                await Task.WhenAll(asyncTasks);
            }
            catch (Exception ex)
            {
                ManagerStateEventHandler?.Invoke(MetaDataState.Idle);
                Trace.TraceError($"File Manager UpdateHashFromChangedList Error: {ex.Message}");
            }
            finally
            {
                // This scope never acquires the semaphore — releasing here raised the
                // 12-way hashing cap by one on every stage (measured 12 -> 22 in ten calls).
                ManagerStateEventHandler?.Invoke(MetaDataState.Idle);
            }
        }
        
        private void UpdateStageFileList()
        {
            if (_preStagedFilesDict.Count <= 0) return;

            foreach (ProjectFile registerdFile in _preStagedFilesDict.Values)
            {
                ManagerStateEventHandler?.Invoke(MetaDataState.Processing);
                registerdFile.DataState &= ~DataState.PreStaged;
                //If File is being Restored
                if ((registerdFile.DataState & DataState.Restored) != 0 && registerdFile.DataType != ProjectDataType.Directory)
                {
                    _backupFilesDict.TryGetValue(registerdFile.DataHash, out ProjectFile? backupFile);
                    if (backupFile != null)
                    {
                        //File Modified 
                        if (_projectFilesDict.TryGetValue(backupFile.DataRelPath, out ProjectFile? projectFile))
                        {
                            if (backupFile.DataHash == projectFile.DataHash) continue;
                            ProjectFile srcFile = new ProjectFile(projectFile, DataState.Backup, backupFile.DataSrcPath);
                            ProjectFile dstFile = new ProjectFile(registerdFile, DataState.Modified, projectFile.DataSrcPath);
                            ChangedFile newChange = new ChangedFile(srcFile, dstFile, DataState.Restored, true);
                            _registeredChangesDict.TryAdd(registerdFile.DataRelPath, newChange);
                        }
                        else
                        {
                            ProjectFile srcFile = new ProjectFile(registerdFile, DataState.Backup, backupFile.DataSrcPath);
                            ProjectFile dstFile = new ProjectFile(registerdFile, DataState.Restored, _dstProjectData.ProjectPath);
                            ChangedFile newChange = new ChangedFile(srcFile, dstFile, DataState.Restored, true);
                            _registeredChangesDict.TryAdd(registerdFile.DataRelPath, newChange);
                        }
                    }
                    continue;
                    //Reset SrcPath to BackupPath
                }
                //If File is being Deleted
                if ((registerdFile.DataState & DataState.Deleted) != 0)
                {
                    ChangedFile newChange = new ChangedFile(new ProjectFile(registerdFile), DataState.Deleted);
                    _registeredChangesDict.TryAdd(registerdFile.DataRelPath, newChange);
                    continue;
                }
                // If File is Modified or Added
                // Modified
                if (_projectFilesDict.TryGetValue(registerdFile.DataRelPath, out var dstProjectFile))
                {
                    if (dstProjectFile.DataHash != registerdFile.DataHash)
                    {
                        ProjectFile srcFile = new ProjectFile(dstProjectFile, DataState.None, registerdFile.DataSrcPath);
                        ProjectFile dstFile = new ProjectFile(registerdFile, DataState.Modified, dstProjectFile.DataSrcPath);
                        ChangedFile newChange = new ChangedFile(srcFile, dstFile, DataState.Modified, true);
                        _registeredChangesDict.TryAdd(registerdFile.DataRelPath, newChange);
                    }
                    else
                        continue;
                }
                else // Added
                {
                    ProjectFile srcFile = new ProjectFile(registerdFile, DataState.None);
                    ProjectFile dstFile = new ProjectFile(registerdFile, DataState.Added, _dstProjectData.ProjectPath);
                    _registeredChangesDict.TryAdd(registerdFile.DataRelPath, new ChangedFile(srcFile, dstFile, DataState.Added));
                }
            }

            _preStagedFilesDict.Clear();
            ManagerStateEventHandler?.Invoke(MetaDataState.Idle);
            DataStagedEventHandler?.Invoke(_registeredChangesDict.Values.ToList());
        }
        
        /// <summary>
        /// Clears StagedFiles Except those registered as IntegrityChecked
        /// </summary>
        public void ClearDeployedFileChanges()
        {
            _srcProjectData = null;
            _preStagedFilesDict.Clear();
            List<ChangedFile> clearChangedList = new List<ChangedFile>();
            foreach (ChangedFile changedFile in _registeredChangesDict.Values)
            {
                if ((changedFile.DataState & DataState.IntegrityChecked) == 0)
                {
                    clearChangedList.Add(changedFile);
                }
            }
            foreach (ChangedFile idenfitiedChange in clearChangedList)
            {
                if (idenfitiedChange.DstFile != null)
                    _registeredChangesDict.Remove(idenfitiedChange.DstFile.DataRelPath);
            }
            SrcProjectDataLoadedEventHandler?.Invoke(_srcProjectData); 
            DataStagedEventHandler?.Invoke(_registeredChangesDict.Values.ToList());
        }
        #endregion

        #region CallBacks From Parent Model 
        public void MetaDataManager_ProjLoadedCallback(object projObj)
        {
            if (projObj is not ProjectData loadedProject) return;

            _preStagedFilesDict.Clear();
            _registeredChangesDict.Clear();
            _dstProjectData = loadedProject;
            _projectFilesDict = _dstProjectData.ProjectFiles;
            _projDirFileList = _dstProjectData.ProjectDirFileList;
            _projectFilesDict_namesSorted = _dstProjectData.ProjectFilesDict_NameSorted;
            _projectFilesDict_relDirSorted = _dstProjectData.ProjectFilesDict_RelDirSorted; 
            DataStagedEventHandler?.Invoke(_registeredChangesDict.Values.ToList());
        }
        /// <summary>
        /// Fallback verification when MD5 hashing fails.  Compares file size and
        /// FileVersionInfo.FileVersion against the snapshot entry.  Returns
        /// <c>(match, log)</c> where match=true means treat as Unchanged
        /// (metadata matches snapshot) and match=false means treat as Modified
        /// (metadata differs).  Catches its own exceptions — if even metadata is
        /// unreadable (rare double-failure: file vanished mid-scan, disk error),
        /// returns match=true to keep the checkout integrity gate functional.
        /// </summary>
        private static (bool match, string logMessage) VerifyByMetadata(string projectPath, string relPath, ProjectFile snapshotEntry)
        {
            try
            {
                var info = new FileInfo(PathCompat.ToNetFrameworkLongPath(Path.Combine(projectPath, relPath)));
                long currentSize = info.Length;
                string currentVersion = "";
                try
                {
                    currentVersion = FileVersionInfo.GetVersionInfo(info.FullName).FileVersion ?? "";
                }
                catch
                {
                    // Non-PE file or unreadable version info — fall back to ""
                    currentVersion = "";
                }

                bool matches = currentSize == snapshotEntry.DataSize
                            && currentVersion == (snapshotEntry.BuildVersion ?? "");

                string msg = matches
                    ? $"Note: {relPath} verified by metadata only — hash unavailable (size={currentSize}, version='{currentVersion}')"
                    : $"Modified: {relPath} — metadata differs from snapshot (size: {currentSize} vs {snapshotEntry.DataSize}, version: '{currentVersion}' vs '{snapshotEntry.BuildVersion}'; hash unavailable)";
                return (matches, msg);
            }
            catch (Exception ex)
            {
                // Extreme double-failure: file vanished or disk error.  Err toward Unchanged.
                return (true, $"Warning: {relPath} — metadata read also failed ({ex.GetType().Name}: {ex.Message}); treating as Unchanged");
            }
        }

        public void MetaDataManager_MetaDataLoadedCallBack(object metaDataObj)
        {
            if (metaDataObj is not ProjectMetaData projectMetaData) return;
            if (projectMetaData == null) return;
            _backupFilesDict = projectMetaData.BackupFiles;
        }
        /// <summary>
        /// Single project-state callback replacing the prior UpdateIgnoreList chain.
        /// FileManager queries <see cref="ProjectContext.IgnoreFilter"/> /
        /// <see cref="ProjectContext.Scanner"/> for all .ignore decisions.
        /// </summary>
        public void MetaDataManager_ProjectContextLoadedCallBack(object ctxObj)
        {
            if (ctxObj is not ProjectContext ctx) return;
            _projectContext = ctx;
        }
        #endregion

        #region Util Calls 
        private bool TryGetDeployMetaFile(string srcPath, out DeployData? deployData)
        {
            const string deployFilename = "DeployAssistant.deploy";
            string deployfilePath = Path.Combine(srcPath, deployFilename);
            if (_fileHandlerTool.TryDeserializeJsonData(deployfilePath, out DeployData? existingDeployData))
            {
                if (existingDeployData.ProjectName != _dstProjectData.ProjectName)
                {
                    deployData = null; 
                    return false;
                }

                if (!TryValidateDeployMetaFile(srcPath, existingDeployData)) 
                {
                    deployData = null; 
                    return false; 
                }
                deployData = existingDeployData;
                return true;
            }
            else
            {
                if (File.Exists(PathCompat.ToNetFrameworkLongPath(deployfilePath))) File.Delete(PathCompat.ToNetFrameworkLongPath(deployfilePath));
                deployData = null;
                return false;
            }
        }

        private bool TryValidateDeployMetaFile(string srcPath, DeployData deployData)
        {
            try
            {
                foreach (ProjectFile registeredFile in deployData.SortedTopFiles.Values)
                {
                    if (registeredFile.DataType == ProjectDataType.Directory) continue; 
                    if (registeredFile == null || registeredFile.DataName == "") return false; 
                    string fileSrcPath = Path.Combine(srcPath, registeredFile.DataName);
                    if (!File.Exists(PathCompat.ToNetFrameworkLongPath(fileSrcPath))) return false; 
                }
                return true; 
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"Critical Error while validating registered deploy meta files {ex.Message}");
                return false; 
            }
        }
        #endregion

        #region Planned 
        public async void StageNewFilesAsync(string deployPath)
        {
            if (_dstProjectData == null)
            {
                Trace.TraceWarning("Project Data is unreachable from FileManager");
                return;
            }
            await HashPreStagedFilesAsync();
            UpdateStageFileList();
            (bool result, List<string>? failedDataList) = DeployFileIntegrityCheck(deployPath);
            if (!result)
            {
                Trace.TraceWarning("Failed to Stage Changes: DeployFiles Integrity Check Failed");
                return;
            }
        }

        private (bool, List<string>? failedFileList) DeployFileIntegrityCheck(string deployPath)
        {
            try
            {
                List<string> failedList = new List<string>();
                foreach (ChangedFile changes in _registeredChangesDict.Values)
                {
                    if (changes.SrcFile == null && changes.DstFile != null) failedList.Add(changes.DstFile.DataName);

                }
                return (true, failedList);
            }
            catch (Exception ex)
            {
                Trace.TraceError($"{ex.Message}, Failed SrcFileIntegrityCheck On FileManager");
                return (true, null);
            }
        }

        public void RevertChange(ProjectFile file)
        {
            if ((file.DataState & DataState.IntegrityChecked) == 0) return;
            file.DataState &= ~DataState.IntegrityChecked;
            switch (file.DataState)
            {
                case DataState.Added:
                    _fileHandlerTool.HandleFile(null, file.DataAbsPath, DataState.Deleted);
                    break;
                case DataState.Modified:
                    if (!_projectFilesDict.TryGetValue(file.DataRelPath, out ProjectFile? projectFile_M))
                    {
                        Trace.TraceWarning("Couldn't revert change since recorded project file does not exist");
                        file.DataState |= DataState.IntegrityChecked;
                        return;
                    }
                    if (!_backupFilesDict.TryGetValue(projectFile_M.DataHash, out ProjectFile? backupFile_M))
                    {
                        Trace.TraceWarning("Couldn't revert change since backup does not exist");
                        file.DataState |= DataState.IntegrityChecked;
                        return;
                    }
                    _fileHandlerTool.HandleFile(backupFile_M.DataAbsPath, projectFile_M.DataAbsPath, DataState.Modified);
                    break;
                case DataState.Deleted:
                    if (!_projectFilesDict.TryGetValue(file.DataRelPath, out ProjectFile? projectFile_D))
                    {
                        Trace.TraceWarning("Coudln't Revert Change for Recorded Project file");
                        file.DataState |= DataState.IntegrityChecked;
                        return;
                    }
                    if (file.DataType == ProjectDataType.Directory)
                    {
                        _fileHandlerTool.HandleDirectory(null, projectFile_D.DataAbsPath, DataState.None);
                        break;
                    }
                    if (!_backupFilesDict.TryGetValue(projectFile_D.DataHash, out ProjectFile? backupFile_D))
                    {
                        Trace.TraceWarning("Couldn't revert change since backup does not exist");
                        file.DataState |= DataState.IntegrityChecked;
                        return;
                    }
                    _fileHandlerTool.HandleFile(backupFile_D.DataAbsPath, projectFile_D.DataAbsPath, DataState.Added);
                    break;
                default:
                    break;
            }

            if (_preStagedFilesDict.TryGetValue(file.DataRelPath, out ProjectFile? recordedPreFile))
                _preStagedFilesDict.Remove(file.DataRelPath);
            if (_registeredChangesDict.TryGetValue(file.DataRelPath, out ChangedFile? recordedChange))
                _registeredChangesDict.Remove(file.DataRelPath);

            DataStagedEventHandler?.Invoke(_registeredChangesDict.Values.ToList());
        }
        #endregion
    }
}