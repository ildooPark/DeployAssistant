using DeployAssistant.DataComponent;
using DeployAssistant.Model;
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
    /// End-to-end integration tests that drive the full MetaDataManager pipeline against
    /// temporary directories on disk (no mocking of I/O).
    /// </summary>
    public class ProjectLifecycleIntegrationTests : IDisposable
    {
        private readonly string _projectDir;
        private readonly FileHandlerTool _fileHandler = new FileHandlerTool();

        public ProjectLifecycleIntegrationTests()
        {
            _projectDir = Path.Combine(Path.GetTempPath(), "DA_IntegTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_projectDir);
            // Seed a few files
            File.WriteAllText(Path.Combine(_projectDir, "app.dll"), "binary content v1");
            File.WriteAllText(Path.Combine(_projectDir, "config.xml"), "<cfg/>");
        }

        public void Dispose()
        {
            if (Directory.Exists(_projectDir))
                Directory.Delete(_projectDir, recursive: true);
        }

        // ------------------------------------------------------------------ helpers

        private static MetaDataManager BuildAndAwakeManager()
        {
            var mgr = new MetaDataManager();
            mgr.Awake();
            return mgr;
        }

        /// <summary>
        /// Runs the async initialization and waits until the manager returns to Idle state.
        /// </summary>
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

            // Give the async void method a moment to start
            await Task.Delay(100);

            // If init already finished before we subscribed the Idle transition, unblock
            if (mgr.CurrentState == MetaDataState.Idle && mgr.ProjectMetaData != null)
                tcs.TrySetResult(true);

            using var cts = new CancellationTokenSource(timeoutMs);
            cts.Token.Register(() => tcs.TrySetCanceled());

            await tcs.Task;
        }

        // ------------------------------------------------------------------ tests

        [Fact]
        public async Task Initialize_WritesProjectMetaDataBinAndContainsSeededFiles()
        {
            var mgr = BuildAndAwakeManager();
            await InitializeAndWaitAsync(mgr, _projectDir);

            string metaPath = Path.Combine(_projectDir, "ProjectMetaData.bin");
            Assert.True(File.Exists(metaPath), "ProjectMetaData.bin should be created on disk");

            bool read = _fileHandler.TryDeserializeProjectMetaData(metaPath, out ProjectMetaData? meta);
            Assert.True(read);
            Assert.NotNull(meta);
            Assert.Equal(Path.GetFileName(_projectDir), meta!.ProjectName);
        }

        [Fact]
        public async Task Initialize_ProjectMainContainsSeededFiles()
        {
            var mgr = BuildAndAwakeManager();
            await InitializeAndWaitAsync(mgr, _projectDir);

            Assert.NotNull(mgr.MainProjectData);
            var files = mgr.MainProjectData!.ProjectFiles;
            bool hasAppDll = files.Values.Any(f => f.DataName == "app.dll");
            bool hasConfigXml = files.Values.Any(f => f.DataName == "config.xml");
            Assert.True(hasAppDll, "app.dll should be in ProjectFiles");
            Assert.True(hasConfigXml, "config.xml should be in ProjectFiles");
        }

        [Fact]
        public async Task Retrieval_RoundTrip_DeserializesEqualMetaData()
        {
            var mgr = BuildAndAwakeManager();
            await InitializeAndWaitAsync(mgr, _projectDir);

            string metaPath = Path.Combine(_projectDir, "ProjectMetaData.bin");
            Assert.True(File.Exists(metaPath));

            // Retrieval via a fresh manager instance
            var mgr2 = BuildAndAwakeManager();
            bool retrieved = mgr2.RequestProjectRetrieval(_projectDir);

            Assert.True(retrieved, "Should retrieve the project written by init");
            Assert.NotNull(mgr2.ProjectMetaData);
            Assert.Equal(mgr.ProjectMetaData!.ProjectName, mgr2.ProjectMetaData!.ProjectName);
        }

        [Fact]
        public async Task IntegrityCheck_UnmodifiedProject_ReturnsNoChanges()
        {
            var mgr = BuildAndAwakeManager();
            await InitializeAndWaitAsync(mgr, _projectDir);

            List<string> changedFiles = new();
            mgr.IntegrityCheckCompleteEventHandler += (log, files) =>
            {
                foreach (var f in files) changedFiles.Add(f.DataName);
            };

            // Run synchronously on a thread-pool thread (the method itself is synchronous internally)
            await Task.Run(() => mgr.RequestProjectIntegrityCheck());
            await Task.Delay(200); // allow callbacks to fire

            Assert.Empty(changedFiles);
        }

        [Fact]
        public async Task IntegrityCheck_ModifiedFile_DetectsChange()
        {
            var mgr = BuildAndAwakeManager();
            await InitializeAndWaitAsync(mgr, _projectDir);

            // Externally modify app.dll
            File.WriteAllText(Path.Combine(_projectDir, "app.dll"), "MODIFIED content v2");

            List<string> changedFiles = new();
            mgr.IntegrityCheckCompleteEventHandler += (log, files) =>
            {
                foreach (var f in files) changedFiles.Add(f.DataName);
            };

            await Task.Run(() => mgr.RequestProjectIntegrityCheck());
            await Task.Delay(200);

            Assert.Contains("app.dll", changedFiles);
        }
    }
}
