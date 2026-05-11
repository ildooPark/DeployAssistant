#pragma warning disable CS0618  // V1 types used intentionally for V1 integration tests

using DeployAssistant.DataComponent;
using DeployAssistant.Services;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace DeployAssistant.Tests.Integration
{
    /// <summary>
    /// Integration tests for MetaDataManager.RequestRevertChange (bool return).
    /// Scenarios: success path (IntegrityChecked cleared, on-disk content restored)
    /// and failure path (missing backup → flag re-applied → returns false).
    /// </summary>
    public class MetaDataManagerRevertChangeTests : IDisposable
    {
        private readonly string _projectDir;

        public MetaDataManagerRevertChangeTests()
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

        private static async Task<ObservableCollection<DeployAssistant.Model.ProjectFile>?> RunIntegrityCheckAndWaitAsync(
            MetaDataManager mgr, int timeoutMs = 10_000)
        {
            ObservableCollection<DeployAssistant.Model.ProjectFile>? captured = null;
            var tcs = new TaskCompletionSource<bool>();

            mgr.IntegrityCheckCompleteEventHandler += (log, files) =>
            {
                captured = files;
                tcs.TrySetResult(true);
            };

            await Task.Run(() => mgr.RequestProjectIntegrityCheck());
            await Task.Delay(100);

            if (captured != null) tcs.TrySetResult(true);

            using var cts = new System.Threading.CancellationTokenSource(timeoutMs);
            cts.Token.Register(() => tcs.TrySetCanceled());

            await tcs.Task;
            return captured;
        }

        // ------------------------------------------------------------------ tests

        [Fact]
        public async Task RequestRevertChange_ModifiedFile_ReturnsTrueAndRestoresContent()
        {
            var mgr = BuildAndAwakeManager();
            await InitializeAndWaitAsync(mgr, _projectDir);

            // Modify app.dll on disk (changing from v1 content)
            string appDllPath = Path.Combine(_projectDir, "app.dll");
            File.WriteAllText(appDllPath, "MODIFIED content v2");

            // Run integrity check to detect the modification
            var changedFiles = await RunIntegrityCheckAndWaitAsync(mgr);

            Assert.NotNull(changedFiles);
            var modifiedFile = changedFiles!.FirstOrDefault(f => f.DataName == "app.dll");
            Assert.NotNull(modifiedFile);
            Assert.True((modifiedFile!.DataState & DeployAssistant.DataComponent.DataState.IntegrityChecked) != 0,
                "File should have IntegrityChecked flag set after integrity check");

            // Revert the file
            bool result = mgr.RequestRevertChange(modifiedFile!);

            Assert.True(result, "RequestRevertChange should return true on success");
            Assert.True((modifiedFile.DataState & DeployAssistant.DataComponent.DataState.IntegrityChecked) == 0,
                "IntegrityChecked flag should be cleared after successful revert");

            // The on-disk content should be restored to the original
            string diskContent = File.ReadAllText(appDllPath);
            Assert.Equal("binary content v1", diskContent);
        }

        [Fact]
        public async Task RequestRevertChange_MissingBackup_ReturnsFalse()
        {
            var mgr = BuildAndAwakeManager();
            await InitializeAndWaitAsync(mgr, _projectDir);

            // Modify app.dll on disk
            string appDllPath = Path.Combine(_projectDir, "app.dll");
            File.WriteAllText(appDllPath, "MODIFIED content v2");

            // Run integrity check to detect the modification
            var changedFiles = await RunIntegrityCheckAndWaitAsync(mgr);

            Assert.NotNull(changedFiles);
            var modifiedFile = changedFiles!.FirstOrDefault(f => f.DataName == "app.dll");
            Assert.NotNull(modifiedFile);

            // Clear the in-memory BackupFiles dictionary to simulate a missing backup.
            // FileManager.RevertChange uses _backupFilesDict (a reference to ProjectMetaData.BackupFiles)
            // to locate the backup file; clearing it triggers the "backup does not exist" branch.
            Assert.NotNull(mgr.ProjectMetaData);
            mgr.ProjectMetaData!.BackupFiles.Clear();

            // Revert should fail (no backup exists in the manager's index)
            bool result = mgr.RequestRevertChange(modifiedFile!);

            Assert.False(result, "RequestRevertChange should return false when backup is missing");
            Assert.True((modifiedFile!.DataState & DeployAssistant.DataComponent.DataState.IntegrityChecked) != 0,
                "IntegrityChecked flag should be re-applied on failure");
        }
    }
}
