#pragma warning disable CS0618  // V1 types used intentionally for V1 integration tests

using DeployAssistant.DataComponent;
using DeployAssistant.Interfaces;
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
        public async Task RequestDroppedFilesAsync_SingleNameMatch_PreStagesAndReturnsToIdle()
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
                var states = new List<MetaDataState>();
                mgr.FileChangesEventHandler += p => preStagedPayload = p;
                mgr.ManagerStateEventHandler += s => states.Add(s);

                await mgr.RequestDroppedFilesAsync(new[] { droppedFile });

                var preStaged = Assert.IsAssignableFrom<System.Collections.ObjectModel.ObservableCollection<ProjectFile>>(preStagedPayload);
                Assert.Contains(preStaged, f => f.DataRelPath == Path.Combine("sub", "engine.dll"));
                Assert.Equal(new[] { MetaDataState.Processing, MetaDataState.Idle }, states);
                Assert.Equal(MetaDataState.Idle, mgr.CurrentState);
            }
            finally
            {
                Directory.Delete(dropSrc, recursive: true);
            }
        }

        [Fact]
        public async Task RequestClearStagedFiles_DeletesDropFolderOnlyOnceUnreferenced()
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
                await mgr.RequestDroppedFilesAsync(new[] { droppedFile });

                var preStaged = Assert.IsAssignableFrom<System.Collections.ObjectModel.ObservableCollection<ProjectFile>>(preStagedPayload);
                string dropRoot = Assert.Single(preStaged).DataSrcPath;
                Assert.StartsWith(FileManager.DropStagingRoot, dropRoot, StringComparison.OrdinalIgnoreCase);
                Assert.True(Directory.Exists(dropRoot));

                mgr.RequestClearStagedFiles(keepDroppedFiles: true);
                Assert.True(Directory.Exists(dropRoot));

                mgr.RequestClearStagedFiles();
                Assert.False(Directory.Exists(dropRoot));
            }
            finally
            {
                Directory.Delete(dropSrc, recursive: true);
            }
        }

        [Fact]
        public void PurgeStaleDropFolders_DeletesOnlyFoldersOlderThanMaxAge()
        {
            string stale = Path.Combine(FileManager.DropStagingRoot, "Drop_test_stale_" + Guid.NewGuid().ToString("N"));
            string fresh = Path.Combine(FileManager.DropStagingRoot, "Drop_test_fresh_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stale);
            Directory.CreateDirectory(fresh);
            File.WriteAllText(Path.Combine(stale, "a.dll"), "x");
            Directory.SetCreationTimeUtc(stale, DateTime.UtcNow.AddDays(-2));
            try
            {
                FileManager.PurgeStaleDropFolders(TimeSpan.FromHours(24));

                Assert.False(Directory.Exists(stale));
                Assert.True(Directory.Exists(fresh));
            }
            finally
            {
                if (Directory.Exists(stale)) Directory.Delete(stale, recursive: true);
                if (Directory.Exists(fresh)) Directory.Delete(fresh, recursive: true);
            }
        }

        [Fact]
        public async Task RequestDroppedFiles_TwoSequentialDrops_ViewListKeepsBothFiles()
        {
            Directory.CreateDirectory(Path.Combine(_projectDir, "sub"));
            File.WriteAllText(Path.Combine(_projectDir, "sub", "engine.dll"), "engine v1");
            Directory.CreateDirectory(Path.Combine(_projectDir, "sub2"));
            File.WriteAllText(Path.Combine(_projectDir, "sub2", "vision.dll"), "vision v1");
            var mgr = BuildAndAwakeManager();
            await InitializeAndWaitAsync(mgr, _projectDir);

            string dropSrc = Path.Combine(Path.GetTempPath(), "DA_DropSrc_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dropSrc);
            File.WriteAllText(Path.Combine(dropSrc, "engine.dll"), "engine v2");
            File.WriteAllText(Path.Combine(dropSrc, "vision.dll"), "vision v2");
            try
            {
                object? lastViewPayload = null;
                mgr.FileChangesEventHandler += p => lastViewPayload = p;

                mgr.RequestDroppedFiles(new[] { Path.Combine(dropSrc, "engine.dll") });
                mgr.RequestDroppedFiles(new[] { Path.Combine(dropSrc, "vision.dll") });

                var view = Assert.IsAssignableFrom<System.Collections.ObjectModel.ObservableCollection<ProjectFile>>(lastViewPayload);
                Assert.Contains(view, f => f.DataRelPath == Path.Combine("sub", "engine.dll"));
                Assert.Contains(view, f => f.DataRelPath == Path.Combine("sub2", "vision.dll"));
            }
            finally
            {
                Directory.Delete(dropSrc, recursive: true);
            }
        }

        [Fact]
        public async Task RequestDroppedFiles_DropAfterStaging_ViewKeepsStagedAndPreStaged()
        {
            Directory.CreateDirectory(Path.Combine(_projectDir, "sub"));
            File.WriteAllText(Path.Combine(_projectDir, "sub", "engine.dll"), "engine v1");
            Directory.CreateDirectory(Path.Combine(_projectDir, "sub2"));
            File.WriteAllText(Path.Combine(_projectDir, "sub2", "vision.dll"), "vision v1");
            var mgr = BuildAndAwakeManager();
            await InitializeAndWaitAsync(mgr, _projectDir);

            string dropSrc = Path.Combine(Path.GetTempPath(), "DA_DropSrc_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dropSrc);
            File.WriteAllText(Path.Combine(dropSrc, "engine.dll"), "engine v2");
            File.WriteAllText(Path.Combine(dropSrc, "vision.dll"), "vision v2");
            try
            {
                object? lastViewPayload = null;
                mgr.FileChangesEventHandler += p => lastViewPayload = p;

                // Drop A and stage it, then drop B — the combined view must keep
                // showing the staged A entry alongside the newly queued B.
                mgr.RequestDroppedFiles(new[] { Path.Combine(dropSrc, "engine.dll") });

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

                mgr.RequestDroppedFiles(new[] { Path.Combine(dropSrc, "vision.dll") });

                var view = Assert.IsAssignableFrom<System.Collections.ObjectModel.ObservableCollection<ProjectFile>>(lastViewPayload);
                Assert.Contains(view, f => f.DataRelPath == Path.Combine("sub", "engine.dll"));
                Assert.Contains(view, f => f.DataRelPath == Path.Combine("sub2", "vision.dll"));
            }
            finally
            {
                Directory.Delete(dropSrc, recursive: true);
            }
        }

        [Fact]
        public async Task RequestClearStagedFiles_KeepDropped_PreservesDropsClearsFolderScan()
        {
            Directory.CreateDirectory(Path.Combine(_projectDir, "sub"));
            File.WriteAllText(Path.Combine(_projectDir, "sub", "engine.dll"), "engine v1");
            var mgr = BuildAndAwakeManager();
            await InitializeAndWaitAsync(mgr, _projectDir);

            string dropSrc = Path.Combine(Path.GetTempPath(), "DA_DropSrc_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dropSrc);
            File.WriteAllText(Path.Combine(dropSrc, "engine.dll"), "engine v2");
            string scanSrc = Path.Combine(Path.GetTempPath(), "DA_ScanSrc_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(scanSrc, "sub"));
            File.WriteAllText(Path.Combine(scanSrc, "sub", "scanned.dll"), "scanned");
            try
            {
                object? lastViewPayload = null;
                mgr.FileChangesEventHandler += p => lastViewPayload = p;

                mgr.RequestDroppedFiles(new[] { Path.Combine(dropSrc, "engine.dll") });
                mgr.RequestSrcDataRetrieval(scanSrc);
                await Task.Delay(300);

                mgr.RequestClearStagedFiles(keepDroppedFiles: true);

                var view = Assert.IsAssignableFrom<System.Collections.ObjectModel.ObservableCollection<ProjectFile>>(lastViewPayload);
                Assert.Contains(view, f => f.DataRelPath == Path.Combine("sub", "engine.dll"));
                Assert.DoesNotContain(view, f => f.DataName == "scanned.dll");

                mgr.RequestClearStagedFiles();
                view = Assert.IsAssignableFrom<System.Collections.ObjectModel.ObservableCollection<ProjectFile>>(lastViewPayload);
                Assert.Empty(view);
            }
            finally
            {
                Directory.Delete(dropSrc, recursive: true);
                Directory.Delete(scanSrc, recursive: true);
            }
        }

        [Fact]
        public async Task RequestIgnoreEntries_PublishesCurrentList()
        {
            var mgr = BuildAndAwakeManager();
            await InitializeAndWaitAsync(mgr, _projectDir);

            List<RecordedFile>? published = null;
            mgr.IgnoreEntriesEventHandler += list => published = list;

            mgr.RequestIgnoreEntries();

            Assert.NotNull(published);
            Assert.Contains(published!, e => e.DataName == "ProjectMetaData.bin");
            Assert.Contains(published!, e => e.DataName == "*.ignore");
            Assert.Contains(published!, e => e.DataName == "Backup_" + mgr.ProjectMetaData!.ProjectName);
        }

        [Fact]
        public async Task RequestAddIgnoreEntry_AppendsPersistsAndIsIdempotent()
        {
            var mgr = BuildAndAwakeManager();
            await InitializeAndWaitAsync(mgr, _projectDir);

            List<RecordedFile>? published = null;
            mgr.IgnoreEntriesEventHandler += list => published = list;

            Assert.True(mgr.RequestAddIgnoreEntry("*.tmp", ProjectDataType.File));
            Assert.NotNull(published);
            Assert.Contains(published!, e => e.DataName == "*.tmp" && e.DataType == ProjectDataType.File);
            Assert.Contains("*.tmp", File.ReadAllText(Path.Combine(_projectDir, "DeployAssistant.ignore")));

            // Adding the same entry again succeeds without creating a duplicate row.
            Assert.True(mgr.RequestAddIgnoreEntry("*.tmp", ProjectDataType.File));
            Assert.Equal(1, published!.Count(e => e.DataName == "*.tmp"));
        }

        [Fact]
        public async Task CountTrackedFilesMatching_CountsByPatternAndFolder()
        {
            Directory.CreateDirectory(Path.Combine(_projectDir, "sub"));
            File.WriteAllText(Path.Combine(_projectDir, "sub", "engine.dll"), "engine");
            var mgr = BuildAndAwakeManager();
            await InitializeAndWaitAsync(mgr, _projectDir);

            // Project tracks app.dll (root) and sub\engine.dll.
            Assert.Equal(2, mgr.CountTrackedFilesMatching(new RecordedFile("*.dll", ProjectDataType.File, IgnoreType.All)));
            Assert.Equal(1, mgr.CountTrackedFilesMatching(new RecordedFile("engine.dll", ProjectDataType.File, IgnoreType.All)));
            Assert.Equal(1, mgr.CountTrackedFilesMatching(new RecordedFile("sub", ProjectDataType.Directory, IgnoreType.All)));
            Assert.Equal(0, mgr.CountTrackedFilesMatching(new RecordedFile("*.xyz", ProjectDataType.File, IgnoreType.All)));
        }

        [Fact]
        public async Task RequestSaveIgnoreEntries_PersistsAndFiltersNextScan()
        {
            var mgr = BuildAndAwakeManager();
            await InitializeAndWaitAsync(mgr, _projectDir);

            List<RecordedFile>? published = null;
            mgr.IgnoreEntriesEventHandler += list => published = list;
            mgr.RequestIgnoreEntries();
            Assert.NotNull(published);

            var edited = new List<RecordedFile>(published!)
            {
                new RecordedFile("*.log", ProjectDataType.File, IgnoreType.All),
                new RecordedFile("TempDir", ProjectDataType.Directory, IgnoreType.All),
            };

            Assert.True(mgr.RequestSaveIgnoreEntries(edited));

            string ignoreJson = File.ReadAllText(Path.Combine(_projectDir, "DeployAssistant.ignore"));
            Assert.Contains("*.log", ignoreJson);
            Assert.Contains("TempDir", ignoreJson);

            // The rebuilt filter must apply to the very next source scan.
            string scanSrc = Path.Combine(Path.GetTempPath(), "DA_IgnScan_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(scanSrc, "sub"));
            Directory.CreateDirectory(Path.Combine(scanSrc, "TempDir"));
            File.WriteAllText(Path.Combine(scanSrc, "sub", "keep.txt"), "keep");
            File.WriteAllText(Path.Combine(scanSrc, "sub", "skip.log"), "skip");
            File.WriteAllText(Path.Combine(scanSrc, "TempDir", "inside.txt"), "skip");
            try
            {
                object? lastView = null;
                mgr.FileChangesEventHandler += p => lastView = p;
                mgr.RequestSrcDataRetrieval(scanSrc);
                await Task.Delay(300);

                var view = Assert.IsAssignableFrom<System.Collections.ObjectModel.ObservableCollection<ProjectFile>>(lastView);
                Assert.Contains(view, f => f.DataName == "keep.txt");
                Assert.DoesNotContain(view, f => f.DataName == "skip.log");
                Assert.DoesNotContain(view, f => f.DataName == "inside.txt");
            }
            finally
            {
                Directory.Delete(scanSrc, recursive: true);
            }
        }

        [Fact]
        public async Task RequestDroppedFiles_NewFileOnly_RaisesDestinationPicker()
        {
            Directory.CreateDirectory(Path.Combine(_projectDir, "sub"));
            File.WriteAllText(Path.Combine(_projectDir, "sub", "other.dll"), "x");
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
                // Root ("") is offered and listed first, then sub-directories.
                Assert.Equal("", newCandidates![0].DstFile?.DataRelPath);
                Assert.Contains(newCandidates!, c => c.DstFile?.DataRelPath == "sub");
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

        [Fact]
        public async Task RequestProjectIntegrityCheck_FolderIgnoredAfterFirstRun_ResultOmitsIgnoredFiles()
        {
            Directory.CreateDirectory(Path.Combine(_projectDir, "logs"));
            File.WriteAllText(Path.Combine(_projectDir, "logs", "run.log"), "log v1");
            var mgr = BuildAndAwakeManager();
            await InitializeAndWaitAsync(mgr, _projectDir);

            File.WriteAllText(Path.Combine(_projectDir, "logs", "run.log"), "log v2 — drifted");
            List<ProjectFile> first = await IntegrityCheckAndCaptureAsync(mgr);
            Assert.Contains(first, f => f.DataRelPath == Path.Combine("logs", "run.log"));

            // The user notices the noise, ignores the folder, and re-runs the check.
            // Both the result popup and the staged list must drop the folder.
            Assert.True(mgr.RequestAddIgnoreEntry("logs", ProjectDataType.Directory));
            List<ProjectFile> second = await IntegrityCheckAndCaptureAsync(mgr);

            Assert.DoesNotContain(second, f => f.DataRelPath.StartsWith("logs"));
        }

        [Fact]
        public async Task RequestAddIgnoreEntry_AfterIntegrityCheck_IgnoredChangesNotCommitted()
        {
            Directory.CreateDirectory(Path.Combine(_projectDir, "logs"));
            File.WriteAllText(Path.Combine(_projectDir, "logs", "run.log"), "log v1");
            var mgr = BuildAndAwakeManager();
            await InitializeAndWaitAsync(mgr, _projectDir);

            File.WriteAllText(Path.Combine(_projectDir, "logs", "run.log"), "log v2 — drifted");
            File.WriteAllText(Path.Combine(_projectDir, "app.dll"), "binary content v2");
            await IntegrityCheckAndCaptureAsync(mgr);

            // Ignore straight from the result list, then Stage + Update without re-checking.
            object? view = null;
            mgr.FileChangesEventHandler += p => view = p;
            Assert.True(mgr.RequestAddIgnoreEntry("logs", ProjectDataType.Directory));
            var shown = Assert.IsAssignableFrom<System.Collections.ObjectModel.ObservableCollection<ProjectFile>>(view);
            Assert.DoesNotContain(shown, f => f.DataRelPath.StartsWith("logs"));

            await StageAndWaitAsync(mgr);
            Assert.True(mgr.RequestProjectUpdate("tester", "v2 update", _projectDir));

            Assert.Contains(mgr.MainProjectData!.ChangedFiles, c => c.DstFile?.DataRelPath == "app.dll");
            Assert.DoesNotContain(mgr.MainProjectData!.ChangedFiles, c => (c.DstFile?.DataRelPath ?? "").StartsWith("logs"));
        }

        [Fact]
        public async Task RequestProjVersionDiff_IgnoredFolder_OmittedFromComparison()
        {
            Directory.CreateDirectory(Path.Combine(_projectDir, "logs"));
            File.WriteAllText(Path.Combine(_projectDir, "logs", "run.log"), "log v1");
            var mgr = BuildAndAwakeManager();
            await InitializeAndWaitAsync(mgr, _projectDir);
            ProjectData v1 = mgr.MainProjectData!;

            File.WriteAllText(Path.Combine(_projectDir, "logs", "run.log"), "log v2");
            File.WriteAllText(Path.Combine(_projectDir, "app.dll"), "binary content v2");
            await IntegrityCheckThenStageAndWaitAsync(mgr);
            Assert.True(mgr.RequestProjectUpdate("tester", "v2 update", _projectDir));
            Assert.True(mgr.RequestAddIgnoreEntry("logs", ProjectDataType.Directory));

            // Fast checkout to v1 skips ignored paths, so the comparison must not list them either.
            List<ChangedFile>? diff = null;
            mgr.ProjComparisonCompleteEventHandler += (src, dst, d) => diff = d;
            mgr.RequestProjVersionDiff(v1);

            Assert.NotNull(diff);
            Assert.Contains(diff!, c => (c.DstFile?.DataRelPath ?? c.SrcFile?.DataRelPath) == "app.dll");
            Assert.DoesNotContain(diff!, c =>
                (c.DstFile?.DataRelPath ?? "").StartsWith("logs") || (c.SrcFile?.DataRelPath ?? "").StartsWith("logs"));
        }

        [Fact]
        public async Task RequestDroppedFiles_PathHeldByIntegrityFinding_DeployedFileWins()
        {
            string subDir = Path.Combine(_projectDir, "sub");
            Directory.CreateDirectory(subDir);
            File.WriteAllText(Path.Combine(subDir, "engine.dll"), "engine v1");
            var mgr = BuildAndAwakeManager();
            await InitializeAndWaitAsync(mgr, _projectDir);

            File.WriteAllText(Path.Combine(subDir, "engine.dll"), "engine drifted on disk");
            List<ProjectFile> found = await IntegrityCheckAndCaptureAsync(mgr);
            Assert.Contains(found, f => f.DataRelPath == Path.Combine("sub", "engine.dll"));

            string dropSrc = Path.Combine(Path.GetTempPath(), "DA_DropSrc_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dropSrc);
            string droppedFile = Path.Combine(dropSrc, "engine.dll");
            File.WriteAllText(droppedFile, "engine v2 deployed");
            try
            {
                mgr.RequestDroppedFiles(new[] { droppedFile });
                await StageAndWaitAsync(mgr);
                Assert.True(mgr.RequestProjectUpdate("tester", "deploy engine v2", _projectDir));

                Assert.Equal("engine v2 deployed", File.ReadAllText(Path.Combine(subDir, "engine.dll")));
            }
            finally
            {
                Directory.Delete(dropSrc, recursive: true);
            }
        }

        private static async Task StageAndWaitAsync(MetaDataManager mgr, int timeoutMs = 10_000)
        {
            // Idle also fires between hashing and staging; the staged-list event marks the end.
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Action<object> handler = _ => tcs.TrySetResult(true);
            mgr.StagedChangesEventHandler += handler;
            try
            {
                mgr.RequestStageChanges();
                using var cts = new System.Threading.CancellationTokenSource(timeoutMs);
                cts.Token.Register(() => tcs.TrySetCanceled());
                await tcs.Task;
                while (mgr.CurrentState != MetaDataState.Idle && !cts.IsCancellationRequested)
                    await Task.Delay(10);
            }
            finally
            {
                mgr.StagedChangesEventHandler -= handler;
            }
        }

        private static async Task<List<ProjectFile>> IntegrityCheckAndCaptureAsync(MetaDataManager mgr, int timeoutMs = 10_000)
        {
            var tcs = new TaskCompletionSource<List<ProjectFile>>();
            Action<string, System.Collections.ObjectModel.ObservableCollection<ProjectFile>> handler =
                (log, files) => tcs.TrySetResult(new List<ProjectFile>(files));
            mgr.IntegrityCheckCompleteEventHandler += handler;
            try
            {
                mgr.RequestProjectIntegrityCheck();
                using var cts = new System.Threading.CancellationTokenSource(timeoutMs);
                cts.Token.Register(() => tcs.TrySetCanceled());
                List<ProjectFile> result = await tcs.Task;
                // The completion event fires just before the manager flips back to Idle.
                while (mgr.CurrentState != MetaDataState.Idle && !cts.IsCancellationRequested)
                    await Task.Delay(10);
                return result;
            }
            finally
            {
                mgr.IntegrityCheckCompleteEventHandler -= handler;
            }
        }
    }
}
