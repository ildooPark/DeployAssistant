#pragma warning disable CS0618  // V1 types used intentionally

using DeployAssistant.DataComponent;
using DeployAssistant.Filtering;
using DeployAssistant.Interfaces;
using DeployAssistant.Model;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using Xunit;

namespace DeployAssistant.Tests.Integration
{
    /// <summary>
    /// End-to-end test for the metadata-fallback behavior in MainProjectIntegrityCheck.
    /// Asserts that a locked file with matching size + version does NOT appear
    /// as a Modified change in the integrity-check output, and that the log
    /// surfaces the "verified by metadata" note via IntegrityCheckEventHandler.
    /// </summary>
    public class IntegrityCheckFallbackTests : IDisposable
    {
        private readonly string _tempRoot;

        public IntegrityCheckFallbackTests()
        {
            _tempRoot = Path.Combine(Path.GetTempPath(), "DA_IntegrityFallback_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempRoot);
        }

        public void Dispose()
        {
            try { if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, recursive: true); }
            catch (IOException) { }
        }

        [Fact]
        public void MainProjectIntegrityCheck_LockedFileWithMatchingMetadata_NoModifiedChange()
        {
            // Create a real on-disk file.
            string fileName = "app.bin";
            string fullPath = Path.Combine(_tempRoot, fileName);
            File.WriteAllText(fullPath, "binary-content-for-integrity-check");
            long actualSize = new FileInfo(fullPath).Length;

            // Build a snapshot whose entry matches the on-disk size + version (empty for non-PE).
            var trackedFile = new ProjectFile(
                DataType: ProjectDataType.File,
                DataSize: actualSize,
                BuildVersion: "",
                DeployedProjectVersion: "1.0",
                UpdatedTime: DateTime.Now,
                DataState: DataState.None,
                dataName: fileName, dataSrcPath: _tempRoot,
                dataRelPath: fileName,
                dataHash: "STORED_HASH_NOT_THE_REAL_MD5",
                IsDstFile: false);

            var projectFiles = new Dictionary<string, ProjectFile> { [fileName] = trackedFile };
            var projectData = new ProjectData(
                ProjectName: "FallbackIT",
                ProjectPath: _tempRoot,
                UpdaterName: "Tester", ConductedPC: "PC",
                UpdatedTime: DateTime.Now, UpdatedVersion: "1.0",
                UpdateLog: "", ChangeLog: "",
                RevisionNumber: 0, NumberOfChanges: 0,
                ChangedFiles: new List<ChangedFile>(),
                ProjectFiles: projectFiles);

            var metaData = new ProjectMetaData("FallbackIT", _tempRoot);
            metaData.SetProjectMain(projectData);

            var fileManager = new FileManager();
            fileManager.MetaDataManager_MetaDataLoadedCallBack(metaData);
            fileManager.MetaDataManager_ProjLoadedCallback(projectData);
            var ignoreData = new ProjectIgnoreData("FallbackIT");
            fileManager.MetaDataManager_ProjectContextLoadedCallBack(ProjectContext.Create(metaData, ignoreData));

            // Capture the integrity-check result via the event.
            string? capturedLog = null;
            ObservableCollection<ProjectFile>? capturedFiles = null;
            var done = new ManualResetEventSlim(false);
            fileManager.IntegrityCheckEventHandler += (log, files) =>
            {
                capturedLog = log;
                capturedFiles = new ObservableCollection<ProjectFile>(files);
                done.Set();
            };

            // Lock the file with FileShare.None so the hash retry exhausts and
            // the metadata fallback engages.  Lock for the full integrity-check duration.
            using var holdOpen = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.None);

            fileManager.MainProjectIntegrityCheck();
            bool fired = done.Wait(TimeSpan.FromSeconds(30));

            Assert.True(fired, "IntegrityCheckEventHandler did not fire within 30 seconds");
            Assert.NotNull(capturedLog);
            // Metadata fallback engaged → log mentions it.
            Assert.Contains("metadata", capturedLog!, StringComparison.OrdinalIgnoreCase);
            // The locked file should NOT be flagged as Modified — metadata matched the snapshot.
            Assert.NotNull(capturedFiles);
            bool anyModifiedForFile = capturedFiles!.Any(f =>
                f.DataRelPath == fileName && (f.DataState & DataState.Modified) != 0);
            Assert.False(anyModifiedForFile,
                "Locked file with matching metadata must not appear as Modified");
        }
    }
}
