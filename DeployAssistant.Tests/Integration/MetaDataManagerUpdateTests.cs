#pragma warning disable CS0618  // V1 types used intentionally for V1 integration tests

using DeployAssistant.DataComponent;
using DeployAssistant.Model;
using DeployAssistant.Services;
using System;
using System.IO;
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
    }
}
