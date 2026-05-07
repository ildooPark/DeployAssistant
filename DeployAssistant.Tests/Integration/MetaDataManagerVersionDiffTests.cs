#pragma warning disable CS0618  // V1 types used intentionally for V1 integration tests

using DeployAssistant.DataComponent;
using DeployAssistant.Model;
using DeployAssistant.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace DeployAssistant.Tests.Integration
{
    /// <summary>
    /// Tests that exercise the RequestProjVersionDiff path, including the
    /// null-MainProjectData guard introduced to fix the compare-with-main crash.
    /// </summary>
    public class MetaDataManagerVersionDiffTests : IDisposable
    {
        private readonly string _projectDir;

        public MetaDataManagerVersionDiffTests()
        {
            _projectDir = Path.Combine(Path.GetTempPath(), "DA_DiffTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_projectDir);
            File.WriteAllText(Path.Combine(_projectDir, "app.dll"), "binary content v1");
            File.WriteAllText(Path.Combine(_projectDir, "config.xml"), "<cfg/>");
        }

        public void Dispose()
        {
            if (Directory.Exists(_projectDir))
                Directory.Delete(_projectDir, recursive: true);
        }

        private static MetaDataManager BuildAndAwakeManager()
        {
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

        private static ProjectData MakeMinimalProjectData(string path)
        {
            var data = new ProjectData(path);
            data.ProjectName = "TestProject";
            data.UpdaterName = "Tester";
            data.ConductedPC = "PC01";
            data.UpdatedVersion = "1.0";
            data.UpdateLog = "test";
            data.ChangeLog = "test";
            return data;
        }

        // ------------------------------------------------------------------ tests

        /// <summary>
        /// Calling RequestProjVersionDiff when no main project has been loaded
        /// must not throw — the crash path introduced after the V2 PR.
        /// </summary>
        [Fact]
        public void RequestProjVersionDiff_WithNullMainProject_DoesNotThrow()
        {
            var mgr = BuildAndAwakeManager();
            // No project loaded — MainProjectData is null

            var srcData = MakeMinimalProjectData(_projectDir);

            bool handlerCalled = false;
            mgr.ProjComparisonCompleteEventHandler += (src, dst, diff) => handlerCalled = true;

            // Must not throw
            var ex = Record.Exception(() => mgr.RequestProjVersionDiff(srcData));

            Assert.Null(ex);
            Assert.False(handlerCalled, "Handler must not be called when MainProjectData is null");
        }

        /// <summary>
        /// Calling RequestProjVersionDiff after a project is initialised must
        /// invoke ProjComparisonCompleteEventHandler with a non-null diff list.
        /// </summary>
        [Fact]
        public async Task RequestProjVersionDiff_WithMainProject_InvokesHandlerWithNonNullDiff()
        {
            var mgr = BuildAndAwakeManager();
            await InitializeAndWaitAsync(mgr, _projectDir);
            Assert.NotNull(mgr.MainProjectData);

            List<ChangedFile>? capturedDiff = null;
            mgr.ProjComparisonCompleteEventHandler += (src, dst, diff) => capturedDiff = diff;

            mgr.RequestProjVersionDiff(mgr.MainProjectData!);

            Assert.NotNull(capturedDiff);
        }

        /// <summary>
        /// After a project is initialised, comparing the main snapshot against itself
        /// should produce an empty diff (no changes).
        /// </summary>
        [Fact]
        public async Task RequestProjVersionDiff_SameVersion_ProducesEmptyDiff()
        {
            var mgr = BuildAndAwakeManager();
            await InitializeAndWaitAsync(mgr, _projectDir);
            Assert.NotNull(mgr.MainProjectData);

            List<ChangedFile>? capturedDiff = null;
            mgr.ProjComparisonCompleteEventHandler += (src, dst, diff) => capturedDiff = diff;

            mgr.RequestProjVersionDiff(mgr.MainProjectData!);

            Assert.NotNull(capturedDiff);
            Assert.Empty(capturedDiff!);
        }
    }
}
