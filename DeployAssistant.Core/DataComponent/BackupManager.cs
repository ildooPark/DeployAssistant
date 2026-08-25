using DeployAssistant.Interfaces;
using DeployAssistant.Model;
using DeployAssistant.Utils;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;

namespace DeployAssistant.DataComponent
{
    public class BackupManager
    {
        public ProjectMetaData? ProjectMetaData { get; set; }
        public Dictionary<string, ProjectFile>? BackupFiles => ProjectMetaData?.BackupFiles;
        private LinkedList<ProjectData>? BackupProjectDataList => ProjectMetaData?.ProjectDataList;
        public ObservableCollection<ProjectData>? ProjectBackupListObservable
        {
            get
            {
                if (BackupProjectDataList == null) return null; 
                return new ObservableCollection<ProjectData>(BackupProjectDataList);
            }
        }

        //public event Action? ExportBackupEventHandler;
        public event Action<object>? ProjectRevertEventHandler;
        public event Action<object>? FetchCompleteEventHandler;
        public event Action<MetaDataState> ManagerStateEventHandler;
        /// <summary>Raised by <see cref="BuildVersionDeletePlan"/> so a preview can be shown before anything is destroyed.</summary>
        public event Action<VersionDeletePlan>? VersionDeletePlannedEventHandler;
        /// <summary>Raised on every <see cref="DeleteVersion"/> exit — Deleted, Blocked or Failed.</summary>
        public event Action<VersionDeleteResult>? VersionDeletedEventHandler;

        private FileHandlerTool _fileHandlerTool;
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        public BackupManager()
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        {
            _fileHandlerTool = new FileHandlerTool();
        }
        private void BackupProject(ProjectData projectData)
        {
            if (BackupProjectDataList == null || ProjectMetaData == null)
            {
                Trace.TraceWarning("Failed to Load ProjectMetaData: BackupProjectList is Null");
                return;
            }
            bool hasBackup = BackupProjectDataList.Contains(projectData);
            if (!hasBackup)
            {
                RegisterBackupFiles(projectData);
                ProjectMetaData.ProjectDataList.AddFirst(new ProjectData(projectData));
            }

            string projectMetaDataPath = $"{ProjectMetaData.ProjectPath}\\ProjectMetaData.bin";
            bool serializeSuccess = _fileHandlerTool.TrySerializeProjectMetaData(ProjectMetaData, projectMetaDataPath);
            if (serializeSuccess)
            {
                ProjectMetaData.SetProjectMain(projectData);
            }
            FetchCompleteEventHandler?.Invoke(ProjectBackupListObservable);
        }
        #region Callbacks
        /// <summary>
        /// By Default should Point to ProjectMain, Make backup of the ucrrent project main before applying new changes. 
        /// </summary>
        public void MetaDataManager_ProjLoadedCallback (object? projectObj)
        {
            if (projectObj is not ProjectData newMainProject) return;
            if (ProjectMetaData == null || BackupProjectDataList == null || BackupFiles == null) return;
            BackupProject(newMainProject);
        }
        public void MetaDataManager_MetaDataLoadedCallBack(object metaDataObj)
        {
            if (metaDataObj is not ProjectMetaData projectMetaData) return;
            if (projectMetaData == null) return;
            this.ProjectMetaData = projectMetaData;
        }

        #endregion
        private void RegisterBackupFiles(ProjectData projectData)
        {
            if (BackupFiles == null) return;
            string backupSrcPath = GetFileBackupSrcPath(projectData);
            int backupCount = 0; 
            if (!Directory.Exists(PathCompat.ToNetFrameworkLongPath(backupSrcPath))) Directory.CreateDirectory(PathCompat.ToNetFrameworkLongPath(backupSrcPath));
            foreach (ChangedFile changes in projectData.ChangedFiles)
            {
                if (changes.DstFile == null) continue; 
                if (changes.DstFile.DataType == ProjectDataType.Directory) continue;
                if (!BackupFiles.TryGetValue(changes.DstFile.DataHash, out ProjectFile? backupFile))
                {
                    ProjectFile newBackupFile = new ProjectFile(changes.DstFile, DataState.Backup, backupSrcPath);
                    BackupFiles.Add(newBackupFile.DataHash, newBackupFile);
                    _fileHandlerTool.HandleData(changes.DstFile.DataAbsPath, newBackupFile.DataAbsPath, ProjectDataType.File, DataState.Backup);
                    newBackupFile.DataSrcPath = backupSrcPath;
                    if (changes.SrcFile != null)
                        changes.SrcFile.DataSrcPath = backupSrcPath;
                    changes.DstFile.DeployedProjectVersion = projectData.UpdatedVersion;
                    backupCount++;
                }
                else
                {
                    if (changes.SrcFile != null)
                        changes.SrcFile.DataSrcPath = backupFile.DataSrcPath;
                    changes.DstFile.DataSrcPath = projectData.ProjectPath;
                    changes.DstFile.DeployedProjectVersion = projectData.UpdatedVersion;
                }
            }
            if (backupCount <= 0) RemoveEmptyBackupFolder(backupSrcPath);
        }

        /// <summary>
        /// Drops the folder <see cref="RegisterBackupFiles"/> created when the snapshot turned out
        /// to contribute no new blobs.  The delete is recursive, so it is gated on a re-scan of
        /// <see cref="BackupFiles"/>: a folder any live entry still points into must never be
        /// wiped, however it came to be reused.
        /// </summary>
        private void RemoveEmptyBackupFolder(string backupSrcPath)
        {
            if (!Directory.Exists(PathCompat.ToNetFrameworkLongPath(backupSrcPath))) return;
            if (BackupFiles != null)
            {
                foreach (ProjectFile blob in BackupFiles.Values)
                {
                    if (!PathsEqual(blob.DataSrcPath, backupSrcPath)) continue;
                    Trace.TraceWarning($"Backup folder kept: backup entries still reference {backupSrcPath}");
                    return;
                }
            }
            try
            {
                Directory.Delete(PathCompat.ToNetFrameworkLongPath(backupSrcPath), true);
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"Could not remove empty backup folder {backupSrcPath}: {ex.Message}");
            }
        }

        public string GetFileBackupSrcPath(ProjectData projectData)
        {
            string backupPath = $"{projectData.ProjectPath}\\Backup_{Path.GetFileName(projectData.ProjectName)}\\Backup_{projectData.UpdatedVersion}";
            return backupPath;
        }

        /// <summary>Root backup folder of the managed project (<c>Backup_&lt;ProjectName&gt;</c>).</summary>
        private string GetBackupRootPath()
        {
            if (ProjectMetaData == null) return "";
            return $"{ProjectMetaData.ProjectPath}\\Backup_{Path.GetFileName(ProjectMetaData.ProjectName)}";
        }

        /// <summary>
        /// Persists the whole <see cref="Model.ProjectMetaData"/> store to
        /// <c>ProjectMetaData.bin</c>.  Extracted from <see cref="BackupProject"/> so
        /// operations that mutate the store without changing the project main
        /// (version delete, version rename) can save without pretending to check out.
        /// </summary>
        /// <param name="takeBackup">Keep the previous file at <see cref="GetMetaDataBackupPath"/>.</param>
        public bool PersistMetaData(bool takeBackup = true)
        {
            if (ProjectMetaData == null)
            {
                Trace.TraceWarning("PersistMetaData: no ProjectMetaData loaded");
                return false;
            }
            string projectMetaDataPath = GetMetaDataFilePath();
            string? backupPath = null;
            if (takeBackup)
            {
                backupPath = GetMetaDataBackupPath();
                string? backupDir = Path.GetDirectoryName(backupPath);
                if (backupDir != null && !Directory.Exists(PathCompat.ToNetFrameworkLongPath(backupDir))) Directory.CreateDirectory(PathCompat.ToNetFrameworkLongPath(backupDir));
            }
            return _fileHandlerTool.TrySerializeProjectMetaData(ProjectMetaData, projectMetaDataPath, backupPath);
        }

        private string GetMetaDataFilePath()
        {
            return $"{ProjectMetaData?.ProjectPath}\\ProjectMetaData.bin";
        }

        /// <summary>
        /// Rollback copy of <c>ProjectMetaData.bin</c>.  Deliberately parked inside
        /// <c>Backup_&lt;ProjectName&gt;</c>: the integrity scan only ignores the exact name
        /// <c>ProjectMetaData.bin</c>, so a sibling <c>.bak</c> in the project root would be
        /// staged as a brand-new project file on the next check.
        /// </summary>
        private string GetMetaDataBackupPath()
        {
            return Path.Combine(GetBackupRootPath(), "ProjectMetaData.bin.bak");
        }

        /// <summary>Locates a snapshot in the version list by its <c>UpdatedVersion</c> tag.</summary>
        public ProjectData? FindVersion(string? versionName)
        {
            if (string.IsNullOrWhiteSpace(versionName) || BackupProjectDataList == null) return null;
            foreach (ProjectData projData in BackupProjectDataList)
            {
                if (projData.UpdatedVersion == versionName) return projData;
            }
            return null;
        }

        #region Link To View Model 
        public bool FetchBackupProjectList()
        {
            if (ProjectMetaData == null || ProjectMetaData.ProjectDataList == null) return false;
            FetchCompleteEventHandler?.Invoke(ProjectBackupListObservable);
            return true;
        }
        public string GetBackupFilePath(ProjectFile projectFile)
        {
            if (BackupFiles == null)
            {
                Trace.TraceWarning("Backupfiles is null for BackupManager");
                return "";
            }
            BackupFiles.TryGetValue(projectFile.DataHash, out ProjectFile? backupFile);
            return backupFile != null ? backupFile.DataAbsPath : "";
        }
        public void RevertProject(ProjectData revertingProjectData, List<ChangedFile>? FileDifferences)
        {
            try
            {
                bool revertSuccess = false; 
                ProjectData revertedData = new ProjectData(revertingProjectData, true);
                while (!revertSuccess)
                {
                    revertSuccess = _fileHandlerTool.TryApplyFileChanges(FileDifferences);
                    if (!revertSuccess)
                    {
                        Trace.TraceWarning("Revert Failed"); return;
                    }
                }
                ProjectRevertEventHandler?.Invoke(revertingProjectData);
            }
            catch (Exception ex)
            {
                Trace.TraceError($"BUVM RevertProject {ex.Message}");
            }
        }
        #endregion

        #region Version Deletion
        /// <summary>
        /// One relocated backup blob, kept so a failure anywhere later in
        /// <see cref="DeleteVersion"/> can be undone byte-for-byte.
        /// </summary>
        private sealed class RelocationRecord
        {
            public ProjectFile Blob = null!;
            public string PreviousSrcPath = "";
            public string CopiedFilePath = "";
            public bool FileCopied;
        }

        /// <summary>
        /// Describes what deleting <paramref name="target"/> would cost, without touching
        /// anything.  Hash sets are computed from <c>ProjectFiles</c> (the full snapshot),
        /// never from <c>ChangedFiles</c> — a delta list would under-report what a survivor
        /// still needs and we would unregister live blobs.
        /// </summary>
        public VersionDeletePlan BuildVersionDeletePlan(ProjectData target, ProjectData? currentMain)
        {
            List<string> empty = new List<string>();
            List<string> blockers = new List<string>();

            if (target == null)
            {
                // Caller-side guard; kept so the method never throws for a null target.
                Trace.TraceWarning("BuildVersionDeletePlan: target is null");
                blockers.Add("No version was selected.");
                return new VersionDeletePlan(new ProjectData(), "", empty, empty, empty, 0, false, blockers);
            }

            string backupFolderPath = GetFileBackupSrcPath(target);

            if (ProjectMetaData == null || BackupProjectDataList == null || BackupFiles == null)
            {
                blockers.Add("No project metadata is loaded.");
                return new VersionDeletePlan(target, backupFolderPath, empty, empty, empty, 0, false, blockers);
            }

            // ---- Blockers ------------------------------------------------------------
            ProjectData? listed = FindVersion(target.UpdatedVersion);
            if (listed == null)
                blockers.Add($"Version '{target.UpdatedVersion}' is not part of this project's version list.");

            if (BackupProjectDataList.Count <= 1)
                blockers.Add("The last remaining version cannot be deleted.");

            // ProjectMain is serialized separately from ProjectDataList; deleting it would
            // leave the store pointing at a version that no longer exists.
            ProjectData? main = currentMain ?? ProjectMetaData.ProjectMain;
            bool targetIsMain = main != null && main.Equals(target);
            if (targetIsMain)
                blockers.Add($"Version '{target.UpdatedVersion}' is the current project main. Check out another version first.");

            // ---- Hash sets -----------------------------------------------------------
            ProjectData source = listed ?? target;
            HashSet<string> targetHashes = CollectSnapshotHashes(source);

            HashSet<string> survivorHashes = new HashSet<string>();
            foreach (ProjectData projData in BackupProjectDataList)
            {
                if (projData.Equals(target)) continue;
                survivorHashes.UnionWith(CollectSnapshotHashes(projData));
            }
            if (main != null && !targetIsMain)
                survivorHashes.UnionWith(CollectSnapshotHashes(main));

            List<string> exclusiveHashes = new List<string>();
            List<string> sharedHashes = new List<string>();
            long reclaimableBytes = 0;
            foreach (string hash in targetHashes)
            {
                if (survivorHashes.Contains(hash))
                {
                    sharedHashes.Add(hash);
                    continue;
                }
                exclusiveHashes.Add(hash);
                if (BackupFiles.TryGetValue(hash, out ProjectFile? blob)) reclaimableBytes += blob.DataSize;
            }

            // Every surviving blob whose only physical copy sits inside the folder we are
            // about to remove has to move out first.
            List<string> relocationHashes = new List<string>();
            foreach (string hash in survivorHashes)
            {
                if (!BackupFiles.TryGetValue(hash, out ProjectFile? blob)) continue;
                if (PathsEqual(blob.DataSrcPath, backupFolderPath)) relocationHashes.Add(hash);
            }

            VersionDeletePlan plan = new VersionDeletePlan(
                source, backupFolderPath, exclusiveHashes, sharedHashes, relocationHashes,
                reclaimableBytes, Directory.Exists(PathCompat.ToNetFrameworkLongPath(backupFolderPath)), blockers);

            VersionDeletePlannedEventHandler?.Invoke(plan);
            return plan;
        }

        /// <summary>
        /// Executes <paramref name="plan"/> metadata-first: relocate shared bytes, unregister
        /// exclusive blobs, drop the list node, persist, and only then touch the disk.  A
        /// failure before the persist rolls everything back and leaves the store untouched;
        /// a failure after it is non-fatal because the metadata is already consistent.
        /// </summary>
        public VersionDeleteResult DeleteVersion(VersionDeletePlan plan)
        {
            if (plan == null)
                return CompleteDelete(new VersionDeleteResult(VersionDeleteOutcome.Blocked, "",
                    messages: new List<string> { "No delete plan supplied." }));

            if (!plan.CanDelete)
                return CompleteDelete(new VersionDeleteResult(VersionDeleteOutcome.Blocked, plan.VersionName,
                    messages: new List<string>(plan.Blockers)));

            if (ProjectMetaData == null || BackupProjectDataList == null || BackupFiles == null)
                return CompleteDelete(new VersionDeleteResult(VersionDeleteOutcome.Blocked, plan.VersionName,
                    messages: new List<string> { "No project metadata is loaded." }));

            List<string> messages = new List<string>();
            List<RelocationRecord> relocations = new List<RelocationRecord>();
            List<ProjectFile> unregistered = new List<ProjectFile>();
            LinkedListNode<ProjectData>? removedAfter = null;
            bool nodeRemoved = false;
            bool persisted = false;
            ProjectData? removedValue = null;
            VersionDeleteResult result;

            // 1. Take the manager out of Idle for the whole mutation window.
            ManagerStateEventHandler?.Invoke(MetaDataState.Deleting);
            try
            {
                // 2. Relocate every surviving blob that lives inside the doomed folder.
                if (!TryRelocateSharedBlobs(plan, relocations, messages))
                {
                    RollbackRelocations(relocations);
                    return CompleteDelete(new VersionDeleteResult(VersionDeleteOutcome.Failed, plan.VersionName,
                        messages: messages));
                }

                // 3. Unregister the exclusive blobs IN PLACE (other managers cache this dictionary).
                foreach (string hash in plan.ExclusiveHashes)
                {
                    if (!BackupFiles.TryGetValue(hash, out ProjectFile? blob)) continue;
                    unregistered.Add(blob);
                    BackupFiles.Remove(hash);
                }

                // 4. Drop the node, preserving list ordering.
                LinkedListNode<ProjectData>? node = FindVersionNode(plan.VersionName);
                if (node == null)
                {
                    messages.Add($"Version '{plan.VersionName}' disappeared from the version list before deletion.");
                    RestoreBackupEntries(unregistered);
                    RollbackRelocations(relocations);
                    return CompleteDelete(new VersionDeleteResult(VersionDeleteOutcome.Failed, plan.VersionName,
                        messages: messages));
                }
                removedAfter = node.Previous;
                removedValue = node.Value;
                BackupProjectDataList.Remove(node);
                nodeRemoved = true;

                // 5. Persist. Until this succeeds nothing on disk has been destroyed.
                if (!PersistMetaData(takeBackup: true))
                {
                    messages.Add("Could not save ProjectMetaData.bin; the version was left untouched.");
                    RestoreRemovedNode(removedAfter, removedValue);
                    nodeRemoved = false;
                    RestoreBackupEntries(unregistered);
                    RollbackRelocations(relocations);
                    _fileHandlerTool.TryRestoreProjectMetaData(GetMetaDataBackupPath(), GetMetaDataFilePath());
                    return CompleteDelete(new VersionDeleteResult(VersionDeleteOutcome.Failed, plan.VersionName,
                        messages: messages));
                }

                persisted = true;

                // 6. Metadata is consistent from here on — disk failures are reported, not fatal.
                long bytesReclaimed = DeleteUnregisteredBlobs(unregistered, messages);
                RemoveBackupFolderIfUnreferenced(plan, messages);

                // 7. Re-stamp IsProjectMain over the surviving list.
                ProjectMetaData.SetProjectMain(ProjectMetaData.ProjectMain);

                result = new VersionDeleteResult(
                    VersionDeleteOutcome.Deleted, plan.VersionName,
                    bytesReclaimed, unregistered.Count, relocations.Count, messages);
            }
            catch (Exception ex)
            {
                Trace.TraceError($"DeleteVersion failed: {ex.Message}");
                if (!persisted)
                {
                    messages.Add($"Deletion aborted: {ex.GetType().Name}: {ex.Message}");
                    if (nodeRemoved) RestoreRemovedNode(removedAfter, removedValue);
                    RestoreBackupEntries(unregistered);
                    RollbackRelocations(relocations);
                    return CompleteDelete(new VersionDeleteResult(VersionDeleteOutcome.Failed, plan.VersionName,
                        messages: messages));
                }
                // The store on disk no longer contains this version. Rolling back now would
                // re-add entries the saved file does not have AND delete the relocated blobs it
                // does point at, so the commit stands and only the cleanup failure is reported.
                messages.Add($"Version removed, but a post-delete step failed: {ex.GetType().Name}: {ex.Message}");
                result = new VersionDeleteResult(
                    VersionDeleteOutcome.Deleted, plan.VersionName,
                    0, unregistered.Count, relocations.Count, messages);
            }

            // 8. Notify exactly once, outside the mutation window. Raising these inside the
            //    try meant a throwing subscriber landed in the rollback above — after the
            //    store was already committed.
            ManagerStateEventHandler?.Invoke(MetaDataState.Idle);
            FetchCompleteEventHandler?.Invoke(ProjectBackupListObservable);
            VersionDeletedEventHandler?.Invoke(result);
            return result;
        }

        /// <summary>Common exit for the non-success paths: back to Idle, then notify.</summary>
        private VersionDeleteResult CompleteDelete(VersionDeleteResult result)
        {
            ManagerStateEventHandler?.Invoke(MetaDataState.Idle);
            VersionDeletedEventHandler?.Invoke(result);
            return result;
        }

        private bool TryRelocateSharedBlobs(VersionDeletePlan plan, List<RelocationRecord> relocations, List<string> messages)
        {
            if (BackupFiles == null) return false;
            if (plan.RelocationHashes.Count == 0) return true;

            // Backup folders are per-version, so two blobs can legitimately share a
            // DataRelPath. Once they land in one shared folder that becomes a collision,
            // so every destination slot is reserved up front against the whole store.
            HashSet<string> occupied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ProjectFile registered in BackupFiles.Values) occupied.Add(registered.DataAbsPath);

            foreach (string hash in plan.RelocationHashes)
            {
                if (!BackupFiles.TryGetValue(hash, out ProjectFile? blob)) continue;

                string srcAbsPath = blob.DataAbsPath;
                string dstFolder = ReserveSharedSlot(blob, occupied, out string dstAbsPath);

                RelocationRecord record = new RelocationRecord
                {
                    Blob = blob,
                    PreviousSrcPath = blob.DataSrcPath,
                    CopiedFilePath = dstAbsPath
                };

                if (File.Exists(PathCompat.ToNetFrameworkLongPath(srcAbsPath)))
                {
                    try
                    {
                        if (!Directory.Exists(PathCompat.ToNetFrameworkLongPath(dstFolder))) Directory.CreateDirectory(PathCompat.ToNetFrameworkLongPath(dstFolder));
                        File.Copy(PathCompat.ToNetFrameworkLongPath(srcAbsPath), PathCompat.ToNetFrameworkLongPath(dstAbsPath), overwrite: true);
                        record.FileCopied = true;
                    }
                    catch (Exception ex)
                    {
                        Trace.TraceError($"Relocation of backup blob {hash} failed: {ex.Message}");
                        messages.Add($"Could not relocate a shared backup file ({blob.DataName}): {ex.Message}");
                        return false;
                    }

                    // Verify by length rather than by re-hashing: all MD5 work in this app is
                    // funnelled through FileManager's SemaphoreSlim(12) limiter, and adding an
                    // unbounded hashing path here would sidestep it.
                    if (!File.Exists(PathCompat.ToNetFrameworkLongPath(dstAbsPath)) || new FileInfo(PathCompat.ToNetFrameworkLongPath(dstAbsPath)).Length != new FileInfo(PathCompat.ToNetFrameworkLongPath(srcAbsPath)).Length)
                    {
                        messages.Add($"Relocated copy of {blob.DataName} did not verify; deletion aborted.");
                        relocations.Add(record);
                        return false;
                    }
                }
                else
                {
                    // The blob was already missing. Repoint anyway so no surviving entry is
                    // left pointing into a folder we are about to remove.
                    messages.Add($"Backup file for {blob.DataName} was already missing; entry repointed only.");
                }

                blob.DataSrcPath = dstFolder;
                relocations.Add(record);
            }
            return true;
        }

        /// <summary>
        /// Picks a free destination for a relocated blob.  The leaf stays in
        /// <c>Backup_&lt;...&gt;</c> form and exactly one level under the backup root, because
        /// <see cref="Model.ProjectMetaData.SetBackupFilesPath"/> rebuilds every backup path as
        /// <c>Backup_&lt;ProjectName&gt;\{Path.GetFileName(DataSrcPath)}</c> when the project moves.
        /// </summary>
        private string ReserveSharedSlot(ProjectFile blob, HashSet<string> occupied, out string dstAbsPath)
        {
            string backupRoot = GetBackupRootPath();
            int suffix = 0;
            while (true)
            {
                string leaf = suffix == 0 ? "Backup_Shared" : $"Backup_Shared_{suffix}";
                string folder = Path.Combine(backupRoot, leaf);
                string candidate = Path.Combine(folder, blob.DataRelPath);
                if (!occupied.Contains(candidate) && !File.Exists(PathCompat.ToNetFrameworkLongPath(candidate)))
                {
                    occupied.Add(candidate);
                    dstAbsPath = candidate;
                    return folder;
                }
                suffix++;
            }
        }

        private void RollbackRelocations(List<RelocationRecord> relocations)
        {
            foreach (RelocationRecord record in relocations)
            {
                record.Blob.DataSrcPath = record.PreviousSrcPath;
                if (!record.FileCopied) continue;
                try { if (File.Exists(PathCompat.ToNetFrameworkLongPath(record.CopiedFilePath))) File.Delete(PathCompat.ToNetFrameworkLongPath(record.CopiedFilePath)); }
                catch (Exception ex)
                {
                    Trace.TraceWarning($"Could not clean up relocated copy {record.CopiedFilePath}: {ex.Message}");
                }
            }
            relocations.Clear();
        }

        private void RestoreBackupEntries(List<ProjectFile> unregistered)
        {
            if (BackupFiles == null) return;
            foreach (ProjectFile blob in unregistered)
            {
                if (!BackupFiles.ContainsKey(blob.DataHash)) BackupFiles.Add(blob.DataHash, blob);
            }
            unregistered.Clear();
        }

        private void RestoreRemovedNode(LinkedListNode<ProjectData>? previous, ProjectData? value)
        {
            if (BackupProjectDataList == null || value == null) return;
            if (previous != null && previous.List == BackupProjectDataList)
                BackupProjectDataList.AddAfter(previous, value);
            else
                BackupProjectDataList.AddFirst(value);
        }

        private long DeleteUnregisteredBlobs(List<ProjectFile> unregistered, List<string> messages)
        {
            long bytesReclaimed = 0;
            foreach (ProjectFile blob in unregistered)
            {
                try
                {
                    if (!File.Exists(PathCompat.ToNetFrameworkLongPath(blob.DataAbsPath))) continue;
                    bytesReclaimed += blob.DataSize;
                    File.Delete(PathCompat.ToNetFrameworkLongPath(blob.DataAbsPath));
                }
                catch (Exception ex)
                {
                    Trace.TraceWarning($"Could not delete backup blob {blob.DataAbsPath}: {ex.Message}");
                    messages.Add($"Could not delete backup file {blob.DataName}: {ex.Message}");
                }
            }
            return bytesReclaimed;
        }

        private void RemoveBackupFolderIfUnreferenced(VersionDeletePlan plan, List<string> messages)
        {
            if (BackupFiles == null) return;
            // Re-verify instead of trusting the plan: a folder holding referenced bytes must
            // never be removed, and a removed version must never leave one behind either —
            // RegisterBackupFiles would nuke it wholesale on the next load.
            foreach (ProjectFile blob in BackupFiles.Values)
            {
                if (!PathsEqual(blob.DataSrcPath, plan.BackupFolderPath)) continue;
                messages.Add("Backup folder kept: surviving backup entries still reference it.");
                return;
            }
            if (!Directory.Exists(PathCompat.ToNetFrameworkLongPath(plan.BackupFolderPath))) return;
            try
            {
                Directory.Delete(PathCompat.ToNetFrameworkLongPath(plan.BackupFolderPath), true);
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"Could not remove backup folder {plan.BackupFolderPath}: {ex.Message}");
                messages.Add($"Backup folder could not be removed: {ex.Message}");
            }
        }

        private LinkedListNode<ProjectData>? FindVersionNode(string versionName)
        {
            if (BackupProjectDataList == null) return null;
            LinkedListNode<ProjectData>? node = BackupProjectDataList.First;
            while (node != null)
            {
                if (node.Value.UpdatedVersion == versionName) return node;
                node = node.Next;
            }
            return null;
        }

        /// <summary>
        /// Hashes a snapshot actually depends on.  Computed from <c>ProjectFiles</c> — the
        /// complete file set — because <c>ChangedFiles</c> only lists that version's delta.
        /// </summary>
        private static HashSet<string> CollectSnapshotHashes(ProjectData projData)
        {
            HashSet<string> hashes = new HashSet<string>();
            if (projData?.ProjectFiles == null) return hashes;
            foreach (ProjectFile projFile in projData.ProjectFiles.Values)
            {
                if (projFile.DataType == ProjectDataType.Directory) continue;
                if (string.IsNullOrEmpty(projFile.DataHash)) continue;
                hashes.Add(projFile.DataHash);
            }
            return hashes;
        }

        private static bool PathsEqual(string? left, string? right)
        {
            if (left == null || right == null) return false;
            return string.Equals(left.TrimEnd('\\', '/'), right.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
        }
        #endregion
    }
}