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
    /// Integration tests for MetaDataManager.RequestRevertProject (bool return).
    /// Scenarios: null target returns false; valid revert returns true with
    /// LastCheckedOut populated; on-disk content matches the target version.
    /// </summary>
    public class MetaDataManagerRevertProjectTests : IDisposable
    {
        private readonly string _projectDir;

        public MetaDataManagerRevertProjectTests()
        {
            _projectDir = Path.Combine(Path.GetTempPath(), "DA_RevertCkt_" + Guid.NewGuid().ToString("N"));
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
            // Use NullDialogService so Confirm() auto-returns DialogChoice.No;
            // RequestProjectUpdate with no src project data skips the Confirm path entirely.
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

        /// <summary>
        /// Runs integrity check (which populates the pre-staged dict), then
        /// stages the detected changes (hashes and moves to registered changes dict).
        /// </summary>
        private static async Task IntegrityCheckThenStageAndWaitAsync(MetaDataManager mgr, int timeoutMs = 10_000)
        {
            // Step 1: run integrity check to populate the pre-staged dict
            var integrityTcs = new TaskCompletionSource<bool>();
            mgr.IntegrityCheckCompleteEventHandler += (log, files) => integrityTcs.TrySetResult(true);

            await Task.Run(() => mgr.RequestProjectIntegrityCheck());
            await Task.Delay(100);
            integrityTcs.TrySetResult(true); // if it fired synchronously

            using (var ctsi = new System.Threading.CancellationTokenSource(timeoutMs))
            {
                ctsi.Token.Register(() => integrityTcs.TrySetCanceled());
                await integrityTcs.Task;
            }

            // Step 2: stage the detected files (hash them, move to registered changes)
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

        private static async Task UpdateAndWaitAsync(MetaDataManager mgr, string projectDir,
            string updaterName, string updateLog, int timeoutMs = 10_000)
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

        // ------------------------------------------------------------------ tests

        [Fact]
        public void RequestRevertProject_NullTarget_ReturnsFalse()
        {
            var mgr = BuildAndAwakeManager();
            mgr.Awake();

            bool result = mgr.RequestRevertProject(null);

            Assert.False(result, "RequestRevertProject(null) should return false");
        }

        [Fact]
        public async Task RequestRevertProject_ValidPreviousVersion_ReturnsTrueAndRestoresContent()
        {
            var mgr = BuildAndAwakeManager();
            await InitializeAndWaitAsync(mgr, _projectDir);

            // Capture v1 (the initial ProjectData, before any update)
            ProjectData? v1 = mgr.MainProjectData;
            Assert.NotNull(v1);

            // Modify app.dll on disk and create v2 via stage + update
            string appDllPath = Path.Combine(_projectDir, "app.dll");
            File.WriteAllText(appDllPath, "MODIFIED content v2");

            await IntegrityCheckThenStageAndWaitAsync(mgr);
            await UpdateAndWaitAsync(mgr, _projectDir, "tester", "v2 update");

            // Verify v2 is now the main (the file content should be v2)
            string diskAfterV2 = File.ReadAllText(appDllPath);
            Assert.Equal("MODIFIED content v2", diskAfterV2);

            // Now request revert to v1
            bool result = mgr.RequestRevertProject(v1);

            Assert.True(result, "RequestRevertProject should return true for a valid previous version");
            Assert.Same(v1, mgr.LastCheckedOut);

            // On-disk content should be restored to v1
            string diskAfterRevert = File.ReadAllText(appDllPath);
            Assert.Equal("binary content v1", diskAfterRevert);
        }

        [Fact]
        public async Task RequestRevertProject_SetsLastCheckedOut_AndConsumeClears()
        {
            var mgr = BuildAndAwakeManager();
            await InitializeAndWaitAsync(mgr, _projectDir);

            ProjectData? v1 = mgr.MainProjectData;
            Assert.NotNull(v1);

            // Create v2
            string appDllPath = Path.Combine(_projectDir, "app.dll");
            File.WriteAllText(appDllPath, "MODIFIED content v2");

            await IntegrityCheckThenStageAndWaitAsync(mgr);
            await UpdateAndWaitAsync(mgr, _projectDir, "tester", "v2 update");

            // Revert to v1
            bool ok = mgr.RequestRevertProject(v1);
            Assert.True(ok);
            Assert.Same(v1, mgr.LastCheckedOut);

            // ConsumeLastCheckedOut should return v1 and clear the property
            var consumed = mgr.ConsumeLastCheckedOut();
            Assert.Same(v1, consumed);
            Assert.Null(mgr.LastCheckedOut);
        }
    }
}
