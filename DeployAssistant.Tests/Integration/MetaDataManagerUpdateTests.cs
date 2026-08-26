#pragma warning disable CS0618  // V1 types used intentionally for V1 integration tests

using DeployAssistant.DataComponent;
using DeployAssistant.Model;
using DeployAssistant.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace DeployAssistant.Tests.Integration
{
    /// <summary>
    /// Integration tests for MetaDataManager.RequestProjectUpdate (bool return)
    /// and the LastUpdated / ConsumeLastUpdated read-once flag.
    /// </summary>
    public class MetaDataManagerUpdateTests : IDisposable
    {
        private readonly string _projectDir;

        public MetaDataManagerUpdateTests()
        {
            _projectDir = Path.Combine(Path.GetTempPath(), "DA_UpdateTest_" + Guid.NewGuid().ToString("N"));
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
        }

        private static MetaDataManager BuildAndAwakeManager()
        {
            // NullDialogService auto-returns DialogChoice.No for all Confirm() calls,
            // which means the src-project integration dialog always declines.
            var mgr = new MetaDataManager(new NullDialogService());
            mgr.Awake();
            return mgr;
        }

        private static async Task InitializeAndWaitAsync(MetaDataManager mgr, string projectDir, int timeoutMs = 10_000)
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

        private static async Task IntegrityCheckThenStageAndWaitAsync(MetaDataManager mgr, int timeoutMs = 10_000)
        {
            // Step 1: integrity check
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

            // Step 2: stage changes
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

            await Task.Delay(100);

            if (!stagingStarted && mgr.CurrentState == MetaDataState.Idle)
                stageTcs.TrySetResult(true);

            using var cts = new System.Threading.CancellationTokenSource(timeoutMs);
            cts.Token.Register(() => stageTcs.TrySetCanceled());

            try { await stageTcs.Task; }
            finally { mgr.ManagerStateEventHandler -= handler; }
        }

        // ------------------------------------------------------------------ tests

        [Theory]
        [InlineData(null, "log", "path")]
        [InlineData("", "log", "path")]
        [InlineData("  ", "log", "path")]
        [InlineData("updater", "log", null)]
        [InlineData("updater", "log", "")]
        [InlineData("updater", "log", "   ")]
        public void RequestProjectUpdate_NullOrWhitespaceArgs_ReturnsFalse(
            string? updaterName, string? updateLog, string? projectPath)
        {
            var mgr = BuildAndAwakeManager();

            bool result = mgr.RequestProjectUpdate(updaterName, updateLog, projectPath);

            Assert.False(result, "Should return false when updaterName or currentProjectPath is null/empty/whitespace");
        }

        [Fact]
        public async Task RequestProjectUpdate_NoStagedChanges_ReturnsFalse()
        {
            var mgr = BuildAndAwakeManager();
            await InitializeAndWaitAsync(mgr, _projectDir);

            // Do NOT modify any file — there are no staged changes
            bool result = mgr.RequestProjectUpdate("tester", "no changes", _projectDir);

            Assert.False(result, "Should return false when there are no staged changes");
        }

        [Fact]
        public async Task RequestProjectUpdate_HappyPath_ReturnsTrueAndSetsLastUpdated()
        {
            var mgr = BuildAndAwakeManager();
            await InitializeAndWaitAsync(mgr, _projectDir);

            // Modify a file, run integrity check, and stage the changes
            File.WriteAllText(Path.Combine(_projectDir, "app.dll"), "MODIFIED content v2");
            await IntegrityCheckThenStageAndWaitAsync(mgr);

            // Capture the new MainProjectData reference before calling update
            bool result = mgr.RequestProjectUpdate("tester", "v2 update", _projectDir);

            Assert.True(result, "Should return true after a successful update");
            Assert.NotNull(mgr.LastUpdated);
            Assert.Equal(mgr.MainProjectData?.UpdatedVersion, mgr.LastUpdated?.UpdatedVersion);
        }

        [Fact]
        public async Task ConsumeLastUpdated_ReturnsValueAndClears()
        {
            var mgr = BuildAndAwakeManager();
            await InitializeAndWaitAsync(mgr, _projectDir);

            // Set up a successful update
            File.WriteAllText(Path.Combine(_projectDir, "app.dll"), "MODIFIED content v2");
            await IntegrityCheckThenStageAndWaitAsync(mgr);

            bool ok = mgr.RequestProjectUpdate("tester", "v2 update", _projectDir);
            Assert.True(ok);
            Assert.NotNull(mgr.LastUpdated);

            // First consume returns the value
            ProjectData? consumed = mgr.ConsumeLastUpdated();
            Assert.NotNull(consumed);

            // Second read returns null — read-once semantics
            Assert.Null(mgr.LastUpdated);
            Assert.Null(mgr.ConsumeLastUpdated());
        }

        [Fact]
        public async Task RequestStageChanges_QueuedFileRestore_StaysInStagedList()
        {
            var mgr = BuildAndAwakeManager();
            await InitializeAndWaitAsync(mgr, _projectDir);

            // Commit v2 so the newest version's diff log holds a v1 SrcFile to restore.
            File.WriteAllText(Path.Combine(_projectDir, "app.dll"), "MODIFIED content v2");
            await IntegrityCheckThenStageAndWaitAsync(mgr);
            Assert.True(mgr.RequestProjectUpdate("tester", "v2 update", _projectDir));

            ChangedFile? change = mgr.MainProjectData?.ChangedFiles
                .FirstOrDefault(c => c.SrcFile != null && c.SrcFile.DataName == "app.dll");
            Assert.NotNull(change);

            object? stagedPayload = null;
            mgr.StagedChangesEventHandler += payload => stagedPayload = payload;

            // The Diff Log restore button path (FileTrackViewModel.RestoreFile),
            // followed directly by the Stage button (no integrity check between).
            mgr.RequestFileRestore(change!.SrcFile!, DataState.Restored);

            var stageTcs = new TaskCompletionSource<bool>();
            bool stagingStarted = false;
            mgr.ManagerStateEventHandler += state =>
            {
                if (state == MetaDataState.Processing) stagingStarted = true;
                if (stagingStarted && state == MetaDataState.Idle) stageTcs.TrySetResult(true);
            };
            mgr.RequestStageChanges();
            await Task.Delay(100);
            if (!stagingStarted && mgr.CurrentState == MetaDataState.Idle) stageTcs.TrySetResult(true);
            using (var cts = new System.Threading.CancellationTokenSource(10_000))
            {
                cts.Token.Register(() => stageTcs.TrySetCanceled());
                await stageTcs.Task;
            }

            var staged = Assert.IsAssignableFrom<List<ChangedFile>>(stagedPayload);
            Assert.Contains(staged, c =>
                (c.DstFile?.DataName ?? c.SrcFile?.DataName) == "app.dll" &&
                (c.DataState & DataState.Restored) != 0);
        }

        [Fact]
        public async Task RequestDroppedFiles_SingleNameMatch_PreStagesAtMatchedRelPath()
        {
            string subDir = Path.Combine(_projectDir, "sub");
            Directory.CreateDirectory(subDir);
            File.WriteAllText(Path.Combine(subDir, "engine.dll"), "engine v1");
            var mgr = BuildAndAwakeManager();
            await InitializeAndWaitAsync(mgr, _projectDir);

            string dropSrc = Path.Combine(Path.GetTempPath(), "DA_DropSrc_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dropSrc);
            string droppedFile = Path.Combine(dropSrc, "engine.dll");
            File.WriteAllText(droppedFile, "engine v2");
            try
            {
                object? preStagedPayload = null;
                mgr.FileChangesEventHandler += p => preStagedPayload = p;

                mgr.RequestDroppedFiles(new[] { droppedFile });

                var preStaged = Assert.IsAssignableFrom<System.Collections.ObjectModel.ObservableCollection<ProjectFile>>(preStagedPayload);
                Assert.Contains(preStaged, f => f.DataRelPath == Path.Combine("sub", "engine.dll"));
            }
            finally
            {
                Directory.Delete(dropSrc, recursive: true);
            }
        }

        [Fact]
        public async Task RequestDroppedFiles_NewFileOnly_RaisesDestinationPicker()
        {
            var mgr = BuildAndAwakeManager();
            await InitializeAndWaitAsync(mgr, _projectDir);

            string dropSrc = Path.Combine(Path.GetTempPath(), "DA_DropSrc_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dropSrc);
            string droppedFile = Path.Combine(dropSrc, "brandnew.dll");
            File.WriteAllText(droppedFile, "new content");
            try
            {
                List<ChangedFile>? newCandidates = null;
                mgr.OverlappedFileSortEventHandler += (overlaps, news) => newCandidates = news;

                mgr.RequestDroppedFiles(new[] { droppedFile });

                Assert.NotNull(newCandidates);
                Assert.Contains(newCandidates!, c => c.SrcFile?.DataName == "brandnew.dll");
            }
            finally
            {
                Directory.Delete(dropSrc, recursive: true);
            }
        }

        [Fact]
        public async Task RetrieveSrc_DeployFilePresent_IsIgnoredNotRestored()
        {
            var mgr = BuildAndAwakeManager();
            await InitializeAndWaitAsync(mgr, _projectDir);

            string srcDir = Path.Combine(Path.GetTempPath(), "DA_DeploySrc_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(srcDir);
            File.WriteAllText(Path.Combine(srcDir, "newfile.txt"), "content");
            // A stale sidecar from an older build claiming a bogus allocation.
            File.WriteAllText(Path.Combine(srcDir, "DeployAssistant.deploy"),
                "{\"ProjectName\":\"" + mgr.ProjectMetaData!.ProjectName + "\",\"SortedTopFiles\":{\"ghost\\\\newfile.txt\":{\"DataType\":0,\"DataSize\":7,\"BuildVersion\":\"\",\"DeployedProjectVersion\":\"\",\"UpdatedTime\":\"2024-01-01T00:00:00\",\"DataState\":16,\"dataName\":\"newfile.txt\",\"dataSrcPath\":\"" + srcDir.Replace("\\", "\\\\") + "\",\"dataRelPath\":\"ghost\\\\newfile.txt\",\"dataHash\":\"\",\"IsDstFile\":false}}}");
            try
            {
                object? preStagedPayload = null;
                mgr.FileChangesEventHandler += p => preStagedPayload = p;

                mgr.RequestSrcDataRetrieval(srcDir);
                await Task.Delay(300);

                var preStaged = Assert.IsAssignableFrom<System.Collections.ObjectModel.ObservableCollection<ProjectFile>>(preStagedPayload);
                // The stale allocation must not be applied, and the sidecar itself must not stage.
                Assert.DoesNotContain(preStaged, f => f.DataRelPath.StartsWith("ghost"));
                Assert.DoesNotContain(preStaged, f => f.DataName == "DeployAssistant.deploy");
            }
            finally
            {
                Directory.Delete(srcDir, recursive: true);
            }
        }

        [Fact]
        public async Task OverlapAllocation_NoLongerWritesDeploySidecar()
        {
            var mgr = BuildAndAwakeManager();
            await InitializeAndWaitAsync(mgr, _projectDir);

            string srcDir = Path.Combine(Path.GetTempPath(), "DA_DeployWrite_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(srcDir);
            string newFilePath = Path.Combine(srcDir, "alloc.dll");
            File.WriteAllText(newFilePath, "alloc content");
            try
            {
                var srcFile = new ProjectFile(13, "", "alloc.dll", srcDir, "alloc.dll");
                var dstDir = new ProjectFile("sub", _projectDir, "sub") { IsDstFile = true };
                var newAlloc = new ChangedFile(srcFile, dstDir, DataState.Overlapped);

                mgr.RequestOverlappedFileAllocation(new List<ChangedFile>(), new List<ChangedFile> { newAlloc });

                Assert.False(File.Exists(Path.Combine(srcDir, "DeployAssistant.deploy")),
                    "The deprecated .deploy sidecar must no longer be written.");
            }
            finally
            {
                Directory.Delete(srcDir, recursive: true);
            }
        }

        [Fact]
        public async Task RequestStageChanges_IntegrityCheckBetweenRestoreAndStage_KeepsQueuedRestore()
        {
            var mgr = BuildAndAwakeManager();
            await InitializeAndWaitAsync(mgr, _projectDir);

            File.WriteAllText(Path.Combine(_projectDir, "app.dll"), "MODIFIED content v2");
            await IntegrityCheckThenStageAndWaitAsync(mgr);
            Assert.True(mgr.RequestProjectUpdate("tester", "v2 update", _projectDir));

            ChangedFile? change = mgr.MainProjectData?.ChangedFiles
                .FirstOrDefault(c => c.SrcFile != null && c.SrcFile.DataName == "app.dll");
            Assert.NotNull(change);

            object? stagedPayload = null;
            mgr.StagedChangesEventHandler += payload => stagedPayload = payload;

            // Queue the restore, then run an integrity check (checkout gates and the
            // toolbar button both do this) before pressing Stage. The queued restore
            // must survive the check.
            mgr.RequestFileRestore(change!.SrcFile!, DataState.Restored);
            await IntegrityCheckThenStageAndWaitAsync(mgr);

            var staged = Assert.IsAssignableFrom<List<ChangedFile>>(stagedPayload);
            Assert.Contains(staged, c =>
                (c.DstFile?.DataName ?? c.SrcFile?.DataName) == "app.dll" &&
                (c.DataState & DataState.Restored) != 0);
        }
    }
}
