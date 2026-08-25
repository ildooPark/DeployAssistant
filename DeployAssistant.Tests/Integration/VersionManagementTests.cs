#pragma warning disable CS0618  // V1 types used intentionally for V1 integration tests

using DeployAssistant.DataComponent;
using DeployAssistant.Model;
using DeployAssistant.Services;
using DeployAssistant.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace DeployAssistant.Tests.Integration
{
    /// <summary>
    /// Integration tests for the version-management surface added on MetaDataManager:
    /// unified checkout (<c>RequestCheckoutVersion</c>), version deletion
    /// (<c>RequestVersionDeletePreview</c> / <c>RequestDeleteVersion</c>) and version
    /// re-tagging (<c>RequestRenameVersion</c>).
    ///
    /// The harness mirrors MetaDataManagerRevertProjectTests: a real temp project is
    /// initialised, mutated on disk, then staged + updated to produce genuine snapshots
    /// with genuine Backup_&lt;Version&gt; folders — deletion semantics are only meaningful
    /// against real content-addressed backup blobs.
    /// </summary>
    public class VersionManagementTests : IDisposable
    {
        private readonly string _projectDir;

        public VersionManagementTests()
        {
            _projectDir = Path.Combine(Path.GetTempPath(), "DA_VerMgmt_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_projectDir);
            File.WriteAllText(Path.Combine(_projectDir, "app.dll"), "binary content v1");
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_projectDir))
                    Directory.Delete(_projectDir, recursive: true);
            }
            catch (IOException) { /* locked file — ignore */ }
            catch (UnauthorizedAccessException) { /* locked file — ignore */ }
        }

        // ------------------------------------------------------------------ harness

        private static MetaDataManager BuildAndAwakeManager()
        {
            var mgr = new MetaDataManager(new NullDialogService());
            mgr.Awake();
            return mgr;
        }

        private static async Task InitializeAndWaitAsync(MetaDataManager mgr, string projectDir, int timeoutMs = 20_000)
        {
            var tcs = new TaskCompletionSource<bool>();
            bool initStarted = false;

            mgr.ManagerStateEventHandler += state =>
            {
                if (state == MetaDataState.Initializing) initStarted = true;
                if (initStarted && state == MetaDataState.Idle) tcs.TrySetResult(true);
            };

            mgr.RequestProjectInitialization(projectDir);

            await Task.Delay(100);

            if (mgr.CurrentState == MetaDataState.Idle && mgr.ProjectMetaData != null)
                tcs.TrySetResult(true);

            using var cts = new System.Threading.CancellationTokenSource(timeoutMs);
            cts.Token.Register(() => tcs.TrySetCanceled());

            await tcs.Task;
        }

        private static async Task IntegrityCheckThenStageAndWaitAsync(MetaDataManager mgr, int timeoutMs = 20_000)
        {
            var integrityTcs = new TaskCompletionSource<bool>();
            mgr.IntegrityCheckCompleteEventHandler += (log, files) => integrityTcs.TrySetResult(true);

            await Task.Run(() => mgr.RequestProjectIntegrityCheck());
            await Task.Delay(100);
            integrityTcs.TrySetResult(true);

            using (var ctsi = new System.Threading.CancellationTokenSource(timeoutMs))
            {
                ctsi.Token.Register(() => integrityTcs.TrySetCanceled());
                await integrityTcs.Task;
            }

            var stageTcs = new TaskCompletionSource<bool>();
            bool stagingStarted = false;

            Action<MetaDataState> handler = null!;
            handler = state =>
            {
                if (state == MetaDataState.Processing) stagingStarted = true;
                if (stagingStarted && state == MetaDataState.Idle) stageTcs.TrySetResult(true);
            };
            mgr.ManagerStateEventHandler += handler;

            mgr.RequestStageChanges();

            await Task.Delay(150);

            if (!stagingStarted && mgr.CurrentState == MetaDataState.Idle)
                stageTcs.TrySetResult(true);

            using var cts = new System.Threading.CancellationTokenSource(timeoutMs);
            cts.Token.Register(() => stageTcs.TrySetCanceled());

            try { await stageTcs.Task; }
            finally { mgr.ManagerStateEventHandler -= handler; }
        }

        private static async Task UpdateAndWaitAsync(MetaDataManager mgr, string projectDir,
            string updaterName, string updateLog, int timeoutMs = 20_000)
        {
            var tcs = new TaskCompletionSource<bool>();
            bool updatingStarted = false;

            Action<MetaDataState> handler = null!;
            handler = state =>
            {
                if (state == MetaDataState.Updating) updatingStarted = true;
                if (updatingStarted && state == MetaDataState.Idle) tcs.TrySetResult(true);
            };
            mgr.ManagerStateEventHandler += handler;

            mgr.RequestProjectUpdate(updaterName, updateLog, projectDir);

            await Task.Delay(100);

            if (!updatingStarted && mgr.CurrentState == MetaDataState.Idle)
                tcs.TrySetResult(true);

            using var cts = new System.Threading.CancellationTokenSource(timeoutMs);
            cts.Token.Register(() => tcs.TrySetCanceled());

            try { await tcs.Task; }
            finally { mgr.ManagerStateEventHandler -= handler; }
        }

        /// <summary>Stage everything currently on disk and cut a new version.</summary>
        private async Task CutVersionAsync(MetaDataManager mgr, string log)
        {
            await IntegrityCheckThenStageAndWaitAsync(mgr);
            await UpdateAndWaitAsync(mgr, _projectDir, "tester", log);
        }

        private static List<ProjectData> Versions(MetaDataManager mgr)
            => mgr.ProjectMetaData!.ProjectDataList.ToList();

        private string BackupRoot => Path.Combine(_projectDir, $"Backup_{Path.GetFileName(_projectDir)}");

        /// <summary>
        /// Builds a three-version project:
        ///   v1: app.dll = "v1"
        ///   v2: app.dll = "v2"  + newfile.dat = "shared payload"  (blob lands in Backup_v2)
        ///   v3: app.dll = "v3"  (newfile.dat untouched, so v3 still references the v2 blob)
        /// </summary>
        private async Task<(MetaDataManager mgr, ProjectData v1, ProjectData v2, ProjectData v3)> BuildThreeVersionProjectAsync()
        {
            var mgr = BuildAndAwakeManager();
            await InitializeAndWaitAsync(mgr, _projectDir);
            string v1Name = mgr.MainProjectData!.UpdatedVersion;

            File.WriteAllText(Path.Combine(_projectDir, "app.dll"), "binary content v2");
            File.WriteAllText(Path.Combine(_projectDir, "newfile.dat"), "shared payload");
            await CutVersionAsync(mgr, "v2");
            string v2Name = mgr.MainProjectData!.UpdatedVersion;

            File.WriteAllText(Path.Combine(_projectDir, "app.dll"), "binary content v3");
            await CutVersionAsync(mgr, "v3");
            string v3Name = mgr.MainProjectData!.UpdatedVersion;

            var list = Versions(mgr);
            ProjectData v1 = list.Single(p => p.UpdatedVersion == v1Name);
            ProjectData v2 = list.Single(p => p.UpdatedVersion == v2Name);
            ProjectData v3 = list.Single(p => p.UpdatedVersion == v3Name);
            return (mgr, v1, v2, v3);
        }

        // ------------------------------------------------------------------ checkout

        [Fact]
        public async Task RequestCheckoutVersion_FastMode_SetsLastCheckedOutAndRaisesEvent()
        {
            var mgr = BuildAndAwakeManager();
            await InitializeAndWaitAsync(mgr, _projectDir);
            ProjectData v1 = mgr.MainProjectData!;

            File.WriteAllText(Path.Combine(_projectDir, "app.dll"), "binary content v2");
            await CutVersionAsync(mgr, "v2");

            CheckoutResult? captured = null;
            mgr.CheckoutCompleteEventHandler += r => captured = r;

            bool ok = mgr.RequestCheckoutVersion(v1, CheckoutMode.Fast);

            Assert.True(ok);
            Assert.Same(v1, mgr.LastCheckedOut);
            Assert.NotNull(captured);
            Assert.True(captured!.Success);
            Assert.Equal(CheckoutMode.Fast, captured.Mode);
            Assert.Equal(MetaDataState.Idle, mgr.CurrentState);
            Assert.Equal("binary content v1", File.ReadAllText(Path.Combine(_projectDir, "app.dll")));
        }

        [Fact]
        public async Task RequestCheckoutVersion_CleanRestoreMode_SetsLastCheckedOutAndRaisesEvent()
        {
            var mgr = BuildAndAwakeManager();
            await InitializeAndWaitAsync(mgr, _projectDir);
            ProjectData v1 = mgr.MainProjectData!;

            File.WriteAllText(Path.Combine(_projectDir, "app.dll"), "binary content v2");
            await CutVersionAsync(mgr, "v2");

            CheckoutResult? captured = null;
            mgr.CheckoutCompleteEventHandler += r => captured = r;

            // Clean restore is the mode that previously never set LastCheckedOut, so the
            // CLI auto-advance never fired after it.
            bool ok = mgr.RequestCheckoutVersion(v1, CheckoutMode.CleanRestore);

            Assert.True(ok);
            Assert.Same(v1, mgr.LastCheckedOut);
            Assert.NotNull(captured);
            Assert.True(captured!.Success);
            Assert.Equal(CheckoutMode.CleanRestore, captured.Mode);
            Assert.Equal(MetaDataState.Idle, mgr.CurrentState);
            Assert.Equal("binary content v1", File.ReadAllText(Path.Combine(_projectDir, "app.dll")));
        }

        [Fact]
        public void RequestCheckoutVersion_NullTarget_ReportsFailureAndReturnsToIdle()
        {
            var mgr = BuildAndAwakeManager();

            CheckoutResult? captured = null;
            mgr.CheckoutCompleteEventHandler += r => captured = r;

            bool ok = mgr.RequestCheckoutVersion((ProjectData?)null, CheckoutMode.CleanRestore);

            Assert.False(ok);
            Assert.NotNull(captured);
            Assert.False(captured!.Success);
            Assert.NotEmpty(captured.Messages);
            Assert.Equal(MetaDataState.Idle, mgr.CurrentState);
        }

        [Fact]
        public void RequestCheckoutVersion_UnknownVersionName_ReportsFailureAndReturnsToIdle()
        {
            var mgr = BuildAndAwakeManager();

            CheckoutResult? captured = null;
            mgr.CheckoutCompleteEventHandler += r => captured = r;

            bool ok = mgr.RequestCheckoutVersion("no_such_version");

            Assert.False(ok);
            Assert.NotNull(captured);
            Assert.False(captured!.Success);
            Assert.Equal(MetaDataState.Idle, mgr.CurrentState);
        }

        [Fact]
        public async Task ObsoleteRevertWrappers_StillDelegateToUnifiedCheckout()
        {
            var mgr = BuildAndAwakeManager();
            await InitializeAndWaitAsync(mgr, _projectDir);
            ProjectData v1 = mgr.MainProjectData!;

            File.WriteAllText(Path.Combine(_projectDir, "app.dll"), "binary content v2");
            await CutVersionAsync(mgr, "v2");

#pragma warning disable CS0618 // exercising the retained wrappers on purpose
            bool ok = mgr.RequestRevertProject(v1);
#pragma warning restore CS0618

            Assert.True(ok);
            Assert.Same(v1, mgr.LastCheckedOut);
            Assert.Equal("binary content v1", File.ReadAllText(Path.Combine(_projectDir, "app.dll")));
        }

        // ------------------------------------------------------------------ delete: blockers

        [Fact]
        public async Task RequestDeleteVersion_TargetIsCurrentMain_IsBlocked()
        {
            var (mgr, _, _, v3) = await BuildThreeVersionProjectAsync();

            VersionDeleteResult? captured = null;
            mgr.VersionDeleteCompleteEventHandler += r => captured = r;

            bool ok = mgr.RequestDeleteVersion(v3, confirmed: true);

            Assert.False(ok);
            Assert.NotNull(captured);
            Assert.Equal(VersionDeleteOutcome.Blocked, captured!.Outcome);
            Assert.Contains(captured.Messages, m => m.Contains("current project main"));
            Assert.Equal(3, Versions(mgr).Count);
            Assert.Equal(MetaDataState.Idle, mgr.CurrentState);
        }

        [Fact]
        public async Task RequestDeleteVersion_OnlyOneVersionRemains_IsBlocked()
        {
            var mgr = BuildAndAwakeManager();
            await InitializeAndWaitAsync(mgr, _projectDir);

            ProjectData only = Versions(mgr).Single();

            VersionDeleteResult? captured = null;
            mgr.VersionDeleteCompleteEventHandler += r => captured = r;

            bool ok = mgr.RequestDeleteVersion(only, confirmed: true);

            Assert.False(ok);
            Assert.NotNull(captured);
            Assert.Equal(VersionDeleteOutcome.Blocked, captured!.Outcome);
            Assert.Contains(captured.Messages, m => m.Contains("last remaining version"));
            Assert.Single(Versions(mgr));
            Assert.Equal(MetaDataState.Idle, mgr.CurrentState);
        }

        [Fact]
        public async Task RequestDeleteVersion_UnknownVersion_IsBlockedAndReturnsToIdle()
        {
            var (mgr, _, _, _) = await BuildThreeVersionProjectAsync();

            VersionDeleteResult? captured = null;
            mgr.VersionDeleteCompleteEventHandler += r => captured = r;

            bool ok = mgr.RequestDeleteVersion("not_a_real_version", confirmed: true);

            Assert.False(ok);
            Assert.NotNull(captured);
            Assert.Equal(VersionDeleteOutcome.Blocked, captured!.Outcome);
            Assert.Equal(3, Versions(mgr).Count);
            Assert.Equal(MetaDataState.Idle, mgr.CurrentState);
        }

        [Fact]
        public async Task RequestDeleteVersion_NullTarget_IsBlockedAndReturnsToIdle()
        {
            var (mgr, _, _, _) = await BuildThreeVersionProjectAsync();

            VersionDeleteResult? captured = null;
            mgr.VersionDeleteCompleteEventHandler += r => captured = r;

            bool ok = mgr.RequestDeleteVersion((ProjectData?)null, confirmed: true);

            Assert.False(ok);
            Assert.NotNull(captured);
            Assert.Equal(VersionDeleteOutcome.Blocked, captured!.Outcome);
            Assert.Equal(MetaDataState.Idle, mgr.CurrentState);
        }

        [Fact]
        public async Task RequestDeleteVersion_NotConfirmed_IsCancelledAndNothingChanges()
        {
            // NullDialogService.Confirm returns No, so an unconfirmed request must cancel.
            var (mgr, _, v2, _) = await BuildThreeVersionProjectAsync();

            VersionDeleteResult? captured = null;
            mgr.VersionDeleteCompleteEventHandler += r => captured = r;

            bool ok = mgr.RequestDeleteVersion(v2, confirmed: false);

            Assert.False(ok);
            Assert.NotNull(captured);
            Assert.Equal(VersionDeleteOutcome.Blocked, captured!.Outcome);
            Assert.Equal(3, Versions(mgr).Count);
            Assert.Equal(MetaDataState.Idle, mgr.CurrentState);
        }

        // ------------------------------------------------------------------ delete: happy path

        [Fact]
        public async Task RequestVersionDeletePreview_MiddleVersion_ReportsRelocationAndExclusiveBlobs()
        {
            var (mgr, _, v2, _) = await BuildThreeVersionProjectAsync();

            VersionDeletePlan? previewed = null;
            mgr.VersionDeletePreviewEventHandler += p => previewed = p;

            VersionDeletePlan? plan = mgr.RequestVersionDeletePreview(v2);

            Assert.NotNull(plan);
            Assert.Same(plan, previewed);
            Assert.True(plan!.CanDelete, "middle version should be deletable: " + string.Join("; ", plan.Blockers));
            Assert.NotEmpty(plan.ExclusiveHashes);       // app.dll @ v2 belongs to v2 alone
            Assert.NotEmpty(plan.RelocationHashes);      // newfile.dat blob lives in Backup_<v2> but v3 needs it
            Assert.True(plan.ReclaimableBytes > 0);
            Assert.True(plan.BackupFolderRemovable);
            // A preview must never mutate anything.
            Assert.Equal(3, Versions(mgr).Count);
            Assert.Equal(MetaDataState.Idle, mgr.CurrentState);
        }

        [Fact]
        public async Task RequestDeleteVersion_MiddleVersionWithSharedBytes_RelocatesAndSurvivorStillRestorable()
        {
            var (mgr, _, v2, v3) = await BuildThreeVersionProjectAsync();
            string v2Name = v2.UpdatedVersion;
            string v2BackupFolder = Path.Combine(BackupRoot, $"Backup_{v2Name}");
            Assert.True(Directory.Exists(v2BackupFolder));

            VersionDeleteResult? captured = null;
            mgr.VersionDeleteCompleteEventHandler += r => captured = r;

            bool ok = mgr.RequestDeleteVersion(v2, confirmed: true);

            Assert.True(ok, captured == null ? "no result" : string.Join("; ", captured.Messages));
            Assert.Equal(VersionDeleteOutcome.Deleted, captured!.Outcome);
            Assert.True(captured.FilesRelocated > 0, "the shared blob should have been relocated out of Backup_<v2>");
            Assert.Equal(v2Name, mgr.ConsumeLastDeletedVersion());
            Assert.Null(mgr.LastDeletedVersion);
            Assert.Equal(MetaDataState.Idle, mgr.CurrentState);

            // The version is gone and its folder with it.
            Assert.DoesNotContain(Versions(mgr), p => p.UpdatedVersion == v2Name);
            Assert.False(Directory.Exists(v2BackupFolder), "Backup_<v2> should have been removed");

            // Every surviving backup entry must resolve to a file that actually exists.
            foreach (ProjectFile blob in mgr.ProjectMetaData!.BackupFiles.Values)
                Assert.True(File.Exists(blob.DataAbsPath), $"orphaned backup entry: {blob.DataAbsPath}");

            // Proof that relocation preserved the bytes: destroy the shared file on disk and
            // clean-restore the SURVIVOR that depended on the deleted version's folder.
            File.Delete(Path.Combine(_projectDir, "newfile.dat"));
            bool restored = mgr.RequestCheckoutVersion(v3, CheckoutMode.CleanRestore);

            Assert.True(restored);
            Assert.Equal("shared payload", File.ReadAllText(Path.Combine(_projectDir, "newfile.dat")));
        }

        [Fact]
        public async Task RequestDeleteVersion_ThenReopeningProject_DoesNotWipeSurvivingBackupFolders()
        {
            var (mgr, _, v2, _) = await BuildThreeVersionProjectAsync();
            Assert.True(mgr.RequestDeleteVersion(v2, confirmed: true));

            string[] foldersBefore = Directory.GetDirectories(BackupRoot);
            string[] filesBefore = Directory.GetFiles(BackupRoot, "*", SearchOption.AllDirectories);
            Assert.NotEmpty(filesBefore);

            // RegisterBackupFiles ends with an unconditional recursive Directory.Delete when a
            // re-registered snapshot contributes zero new blobs (BackupManager.cs:105). Re-opening
            // must not reach it: the deleted version left no snapshot behind to re-register.
            var reopened = BuildAndAwakeManager();
            bool loaded = reopened.RequestProjectRetrieval(_projectDir);

            Assert.True(loaded);
            Assert.Equal(2, reopened.ProjectMetaData!.ProjectDataList.Count);
            Assert.Equal(foldersBefore.Length, Directory.GetDirectories(BackupRoot).Length);
            Assert.Equal(filesBefore.Length, Directory.GetFiles(BackupRoot, "*", SearchOption.AllDirectories).Length);
            foreach (ProjectFile blob in reopened.ProjectMetaData.BackupFiles.Values)
                Assert.True(File.Exists(blob.DataAbsPath), $"orphaned backup entry after reload: {blob.DataAbsPath}");
        }

        [Fact]
        public async Task RequestDeleteVersion_ByteDeletionFails_StillLeavesLoadableMetaData()
        {
            var (mgr, _, v2, _) = await BuildThreeVersionProjectAsync();
            string v2Name = v2.UpdatedVersion;
            string v2BackupFolder = Path.Combine(BackupRoot, $"Backup_{v2Name}");

            // Lock every blob in the doomed folder so step 6 (byte deletion + rmdir) fails
            // AFTER the metadata write has already been committed.
            var locks = Directory.GetFiles(v2BackupFolder, "*", SearchOption.AllDirectories)
                .Select(p => new FileStream(p, FileMode.Open, FileAccess.Read, FileShare.Read))
                .ToList();

            VersionDeleteResult? captured = null;
            try
            {
                mgr.VersionDeleteCompleteEventHandler += r => captured = r;
                bool ok = mgr.RequestDeleteVersion(v2, confirmed: true);

                // Metadata-first: the version is gone from the store even though disk cleanup failed.
                Assert.True(ok);
                Assert.Equal(VersionDeleteOutcome.Deleted, captured!.Outcome);
                Assert.NotEmpty(captured.Messages);
                Assert.Equal(MetaDataState.Idle, mgr.CurrentState);
            }
            finally
            {
                foreach (var fs in locks) fs.Dispose();
            }

            // The persisted store must still be loadable and must no longer list the version.
            var handler = new FileHandlerTool();
            bool read = handler.TryDeserializeProjectMetaData(
                Path.Combine(_projectDir, "ProjectMetaData.bin"), out ProjectMetaData? persisted);

            Assert.True(read);
            Assert.NotNull(persisted);
            Assert.Equal(2, persisted!.ProjectDataList.Count);
            Assert.DoesNotContain(persisted.ProjectDataList, p => p.UpdatedVersion == v2Name);
            // The rollback copy lives inside the (already ignored) backup root, never beside
            // ProjectMetaData.bin where the integrity scan would stage it as a new file.
            Assert.True(File.Exists(Path.Combine(BackupRoot, "ProjectMetaData.bin.bak")),
                "PersistMetaData(takeBackup: true) should leave a .bak behind");
            Assert.False(File.Exists(Path.Combine(_projectDir, "ProjectMetaData.bin.bak")));
        }

        // ------------------------------------------------------------------ rename

        [Fact]
        public async Task RequestRenameVersion_DuplicateName_IsRejected()
        {
            var (mgr, v1, v2, _) = await BuildThreeVersionProjectAsync();
            string originalName = v2.UpdatedVersion;

            VersionRenameResult? captured = null;
            mgr.VersionRenameCompleteEventHandler += r => captured = r;

            bool ok = mgr.RequestRenameVersion(v2, v1.UpdatedVersion, null);

            Assert.False(ok);
            Assert.NotNull(captured);
            Assert.Contains(captured!.Messages, m => m.Contains("already exists"));
            Assert.Equal(originalName, v2.UpdatedVersion);
            Assert.Equal(MetaDataState.Idle, mgr.CurrentState);
        }

        [Theory]
        [InlineData("bad\\name")]
        [InlineData("bad/name")]
        [InlineData("bad:name")]
        [InlineData("bad*name")]
        [InlineData("trailing ")]
        [InlineData("   ")]
        [InlineData("")]
        public async Task RequestRenameVersion_PathInvalidName_IsRejected(string badName)
        {
            var (mgr, _, v2, _) = await BuildThreeVersionProjectAsync();
            string originalName = v2.UpdatedVersion;

            VersionRenameResult? captured = null;
            mgr.VersionRenameCompleteEventHandler += r => captured = r;

            bool ok = mgr.RequestRenameVersion(v2, badName, null);

            Assert.False(ok);
            Assert.NotNull(captured);
            Assert.False(captured!.Success);
            Assert.Equal(originalName, v2.UpdatedVersion);
            Assert.Equal(MetaDataState.Idle, mgr.CurrentState);
        }

        [Fact]
        public async Task RequestRenameVersion_UpdateLogOnly_Succeeds()
        {
            var (mgr, _, v2, _) = await BuildThreeVersionProjectAsync();
            string originalName = v2.UpdatedVersion;

            VersionRenameResult? captured = null;
            mgr.VersionRenameCompleteEventHandler += r => captured = r;

            bool ok = mgr.RequestRenameVersion(v2, null, "re-tagged release note");

            Assert.True(ok);
            Assert.NotNull(captured);
            Assert.True(captured!.Success);
            Assert.False(captured.VersionNameChanged);
            Assert.True(captured.UpdateLogChanged);
            Assert.Equal(originalName, v2.UpdatedVersion);
            Assert.Equal("re-tagged release note", v2.UpdateLog);
            Assert.Equal(MetaDataState.Idle, mgr.CurrentState);

            // Persisted, not just in memory.
            var handler = new FileHandlerTool();
            Assert.True(handler.TryDeserializeProjectMetaData(
                Path.Combine(_projectDir, "ProjectMetaData.bin"), out ProjectMetaData? persisted));
            Assert.Equal("re-tagged release note",
                persisted!.ProjectDataList.Single(p => p.UpdatedVersion == originalName).UpdateLog);
        }

        [Fact]
        public async Task RequestRenameVersion_ValidNewName_RenamesAndKeepsBackupsIntact()
        {
            var (mgr, _, v2, _) = await BuildThreeVersionProjectAsync();
            string originalName = v2.UpdatedVersion;

            bool ok = mgr.RequestRenameVersion(v2, "Release_2024_QA", "qa candidate");

            Assert.True(ok);
            Assert.Equal("Release_2024_QA", v2.UpdatedVersion);
            Assert.Equal("qa candidate", v2.UpdateLog);
            Assert.Equal(MetaDataState.Idle, mgr.CurrentState);

            // Backup folder stays pinned to the ORIGINAL tag by design; the bytes are untouched.
            string oldFolder = Path.Combine(BackupRoot, $"Backup_{originalName}");
            Assert.True(Directory.Exists(oldFolder));
            foreach (ProjectFile blob in mgr.ProjectMetaData!.BackupFiles.Values)
                Assert.True(File.Exists(blob.DataAbsPath), $"orphaned backup entry: {blob.DataAbsPath}");

            var handler = new FileHandlerTool();
            Assert.True(handler.TryDeserializeProjectMetaData(
                Path.Combine(_projectDir, "ProjectMetaData.bin"), out ProjectMetaData? persisted));
            Assert.Contains(persisted!.ProjectDataList, p => p.UpdatedVersion == "Release_2024_QA");
        }

        [Fact]
        public void RequestRenameVersion_NullTarget_IsRejectedAndReturnsToIdle()
        {
            var mgr = BuildAndAwakeManager();

            VersionRenameResult? captured = null;
            mgr.VersionRenameCompleteEventHandler += r => captured = r;

            bool ok = mgr.RequestRenameVersion(null, "whatever", null);

            Assert.False(ok);
            Assert.NotNull(captured);
            Assert.False(captured!.Success);
            Assert.Equal(MetaDataState.Idle, mgr.CurrentState);
        }

        [Fact]
        public async Task IntegrityCheck_StreamsPerFileOutcomes()
        {
            var mgr = BuildAndAwakeManager();
            await InitializeAndWaitAsync(mgr, _projectDir);

            File.WriteAllText(Path.Combine(_projectDir, "app.dll"), "binary content v2 - drifted");
            File.WriteAllText(Path.Combine(_projectDir, "brand_new.dll"), "fresh binary");

            var outcomes = new List<KeyValuePair<string, IntegrityFileOutcome>>();
            mgr.IntegrityFileProgressEventHandler += (relPath, outcome) =>
            {
                lock (outcomes) outcomes.Add(new KeyValuePair<string, IntegrityFileOutcome>(relPath, outcome));
            };

            var done = new TaskCompletionSource<bool>();
            mgr.IntegrityCheckCompleteEventHandler += (_, _) => done.TrySetResult(true);
            mgr.RequestProjectIntegrityCheck();
            using var cts = new System.Threading.CancellationTokenSource(20_000);
            cts.Token.Register(() => done.TrySetCanceled());
            await done.Task;

            lock (outcomes)
            {
                Assert.Contains(outcomes, o => o.Key == "app.dll" && o.Value == IntegrityFileOutcome.Modified);
                Assert.Contains(outcomes, o => o.Key == "brand_new.dll" && o.Value == IntegrityFileOutcome.Added);
                Assert.DoesNotContain(outcomes, o => o.Value == IntegrityFileOutcome.Checked && o.Key == "app.dll");
            }
        }

    }
}
