#pragma warning disable CS0618  // V1 types used intentionally

using DeployAssistant.DataComponent;
using DeployAssistant.Filtering;
using DeployAssistant.Interfaces;
using DeployAssistant.Model;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Xunit;

namespace DeployAssistant.Tests.Integration
{
    /// <summary>
    /// Tests that cover the four .ignore subsystem fixes from PR-A:
    ///   Fix 1 — FindVersionDifferencesForIntegration returns the filtered diff.
    ///   Fix 2 — ProjectIntegrityCheck (clean-restore path) consults _projIgnoreData.
    ///   Fix 3 — RegisterAllSrcFiles / RegisterFilesUnderSubDirectory respect IgnoreType.Deploy.
    ///   Fix 4 — Default ignore entries carry IgnoreType.Initialization for *.deploy / Backup / Export.
    /// </summary>
    public class IgnoreSubsystemTests : IDisposable
    {
        private readonly string _tempRoot;

        public IgnoreSubsystemTests()
        {
            _tempRoot = Path.Combine(Path.GetTempPath(), "DA_IgnorePR-A_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempRoot);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_tempRoot))
                    Directory.Delete(_tempRoot, recursive: true);
            }
            catch (IOException) { /* locked — ignore */ }
        }

        // ------------------------------------------------------------------ helpers

        private static ProjectFile MakeFile(string relPath, string srcPath = @"C:\Proj")
        {
            string name = Path.GetFileName(relPath);
            return new ProjectFile(
                DataType: ProjectDataType.File,
                DataSize: 64,
                BuildVersion: "1.0",
                DeployedProjectVersion: "1.0",
                UpdatedTime: DateTime.Now,
                DataState: DataState.None,
                dataName: name,
                dataSrcPath: srcPath,
                dataRelPath: relPath,
                dataHash: "H_" + name,
                IsDstFile: false);
        }

        private static ProjectFile MakeDir(string relPath, string srcPath = @"C:\Proj")
        {
            string name = Path.GetFileName(relPath);
            return new ProjectFile(
                DataType: ProjectDataType.Directory,
                DataSize: 0,
                BuildVersion: "",
                DeployedProjectVersion: "",
                UpdatedTime: DateTime.Now,
                DataState: DataState.None,
                dataName: name,
                dataSrcPath: srcPath,
                dataRelPath: relPath,
                dataHash: "",
                IsDstFile: false);
        }

        private static ProjectData MakeProjectData(string projectPath, params ProjectFile[] files)
        {
            var dict = new Dictionary<string, ProjectFile>();
            foreach (var f in files)
                dict[f.DataRelPath] = f;
            return new ProjectData(
                ProjectName: "TestProj",
                ProjectPath: projectPath,
                UpdaterName: "Tester",
                ConductedPC: "PC01",
                UpdatedTime: DateTime.Now,
                UpdatedVersion: "1.0",
                UpdateLog: "test",
                ChangeLog: "test",
                RevisionNumber: 0,
                NumberOfChanges: 0,
                ChangedFiles: new List<ChangedFile>(),
                ProjectFiles: dict);
        }

        // ------------------------------------------------------------------ Fix 1

        /// <summary>
        /// FindVersionDifferencesForIntegration must return the filtered diff, not the raw diff.
        /// Locale-dir files (en-US, ko-KR, Resources) listed in the default IgnoreType.Integration
        /// scope must be absent from the returned list.
        /// </summary>
        [Fact]
        public void Fix1_FindVersionDifferencesForIntegration_ReturnsFilteredDiff()
        {
            string projPath = @"C:\FakeDst";
            string srcPath = @"C:\FakeSrc";

            // Src has locale-dir files + a normal file
            var srcFiles = new[]
            {
                MakeFile(@"en-US\main.resources.dll", srcPath),
                MakeFile(@"ko-KR\main.resources.dll", srcPath),
                MakeFile(@"Resources\app.ico", srcPath),
                MakeFile(@"app.dll", srcPath),
            };
            var srcDirFiles = new[]
            {
                MakeDir(@"en-US", srcPath),
                MakeDir(@"ko-KR", srcPath),
                MakeDir(@"Resources", srcPath),
            };
            var allSrc = srcFiles.Concat(srcDirFiles).ToArray();

            // Dst has none of these files (all would appear as Added in the diff)
            var srcData = MakeProjectData(srcPath, allSrc);
            var dstData = MakeProjectData(projPath);

            var fileManager = new FileManager();
            // Inject ignore data via the new ProjectContext callback (replaces UpdateIgnoreList chain)
            var ignoreData = new ProjectIgnoreData("TestProj");
            ignoreData.ConfigureDefaultIgnore("TestProj");
            var metaData = new ProjectMetaData("TestProj", projPath);
            fileManager.MetaDataManager_ProjectContextLoadedCallBack(ProjectContext.Create(metaData, ignoreData));

            var result = fileManager.FindVersionDifferencesForIntegration(srcData, dstData, out int significantDiff, out int rawDiff);

            Assert.NotNull(result);

            // After filtering: locale-dir dirs and their files should be excluded by IgnoreType.Integration.
            // Only app.dll should remain.
            var relPaths = result!
                .Select(c => c.SrcFile?.DataRelPath ?? c.DstFile?.DataRelPath ?? "")
                .ToList();

            Assert.DoesNotContain(relPaths, r => r.StartsWith("en-US", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(relPaths, r => r.StartsWith("ko-KR", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(relPaths, r => r.StartsWith("Resources", StringComparison.OrdinalIgnoreCase));

            // The unfiltered list had 7 entries (4 files + 3 dirs); after filtering only app.dll remains
            Assert.Single(result);
            Assert.Equal(significantDiff, result.Count);
            // rawDiff must be larger than significantDiff when resource-only changes are present
            Assert.True(rawDiff > significantDiff,
                $"rawDiff ({rawDiff}) should exceed significantDiff ({significantDiff}) because locale-dir entries were filtered out");
        }

        /// <summary>
        /// Before Fix 1 the significantDiff out-param (computed from filteredChangedList.Count)
        /// and the returned list's Count were inconsistent.  After the fix they match.
        /// </summary>
        [Fact]
        public void Fix1_FindVersionDifferencesForIntegration_SignificantDiffMatchesReturnedCount()
        {
            string projPath = @"C:\FakeDst2";
            string srcPath  = @"C:\FakeSrc2";

            var srcData = MakeProjectData(srcPath,
                MakeFile(@"en-US\strings.dll", srcPath),
                MakeFile(@"app.dll", srcPath));
            var dstData = MakeProjectData(projPath);

            var fileManager = new FileManager();
            var ignoreData = new ProjectIgnoreData("Proj2");
            ignoreData.ConfigureDefaultIgnore("Proj2");
            var metaData = new ProjectMetaData("Proj2", projPath);
            fileManager.MetaDataManager_ProjectContextLoadedCallBack(ProjectContext.Create(metaData, ignoreData));

            var result = fileManager.FindVersionDifferencesForIntegration(srcData, dstData, out int significantDiff, out int rawDiff);

            Assert.NotNull(result);
            Assert.Equal(significantDiff, result!.Count);
            // rawDiff >= significantDiff always (rawDiff includes resource-only changes that were filtered)
            Assert.True(rawDiff >= significantDiff,
                $"rawDiff ({rawDiff}) must be >= significantDiff ({significantDiff})");
        }

        // ------------------------------------------------------------------ Fix 2

        /// <summary>
        /// ProjectIntegrityCheck (clean-restore path) must not queue files that match
        /// IgnoreType.IntegrityCheck entries for deletion.  A *.VersionLog and a
        /// DeployAssistant.ignore file present on disk but absent from the snapshot
        /// must NOT appear in the "to delete" change set.
        ///
        /// The snapshot is intentionally empty (no tracked files) so that no backup
        /// lookups are required — we only verify the excluded-file behavior.
        /// </summary>
        [Fact]
        public void Fix2_ProjectIntegrityCheck_IgnoredFilesNotQueuedForDeletion()
        {
            // Build a minimal project directory
            string projDir = Path.Combine(_tempRoot, "CleanRestoreProj");
            Directory.CreateDirectory(projDir);

            // Only IgnoreType.All files on disk — snapshot tracks nothing.
            // Before Fix 2, these would appear as "to delete" entries.
            // After Fix 2, they must be absent from the change set because
            // GetIgnoreFilesAndDirPaths(IntegrityCheck) excludes them.
            string versionLog = Path.Combine(projDir, "app.VersionLog");
            File.WriteAllText(versionLog, "log");
            string ignoreFile = Path.Combine(projDir, "DeployAssistant.ignore");
            File.WriteAllText(ignoreFile, "{}");

            // Empty snapshot — no tracked files.
            var snapshot = MakeProjectData(projDir /*, no files */);

            var fileManager = new FileManager();
            // Initialize _backupFilesDict (required to avoid NullReferenceException in the restore backup lookup)
            var metaData = new ProjectMetaData("CleanRestoreProj", projDir);
            fileManager.MetaDataManager_MetaDataLoadedCallBack(metaData);

            var ignoreData = new ProjectIgnoreData("CleanRestoreProj");
            ignoreData.ConfigureDefaultIgnore("CleanRestoreProj");
            fileManager.MetaDataManager_ProjectContextLoadedCallBack(ProjectContext.Create(metaData, ignoreData));

            var changes = fileManager.ProjectIntegrityCheck(snapshot);

            Assert.NotNull(changes);

            var deletedRelPaths = changes!
                .Where(c => c.DataState == DataState.Deleted)
                .Select(c => c.DstFile?.DataRelPath ?? c.SrcFile?.DataRelPath ?? "")
                .ToList();

            // *.VersionLog is IgnoreType.All — must not be queued for deletion
            Assert.DoesNotContain(deletedRelPaths, p => p.EndsWith(".VersionLog", StringComparison.OrdinalIgnoreCase));
            // *.ignore is IgnoreType.All — must not be queued for deletion
            Assert.DoesNotContain(deletedRelPaths, p => p.EndsWith(".ignore", StringComparison.OrdinalIgnoreCase));
            // No spurious deletions at all
            Assert.Empty(deletedRelPaths);
        }

        // ------------------------------------------------------------------ Fix 3

        /// <summary>
        /// RegisterAllSrcFiles must not pre-stage files that match IgnoreType.Deploy entries.
        /// *.deploy files placed anywhere in the source folder tree must be absent from the
        /// DataPreStagedEventHandler snapshot; a real .dll in a subdirectory must be present.
        ///
        /// Design notes:
        ///   - Decoy files are named "app.deploy" (not "DeployAssistant.deploy") so that
        ///     RetrieveDataSrc's TryGetDeployMetaFile doesn't intercept the root file as a
        ///     canonical deploy meta file and short-circuit the scan.
        ///   - The control "actual.dll" is placed in a subdirectory so it is registered via
        ///     RegisterFilesUnderSubDirectory (which adds to _preStagedFilesDict directly)
        ///     rather than HandleAbnormalFiles (which defers top-level files to overlap
        ///     resolution when no matching destination project is loaded).
        ///   - *.VersionLog files are intentionally excluded from this test's source folder
        ///     because RetrieveDataSrc treats a single *.VersionLog as a project metadata file
        ///     and skips the full scan when deserialization fails — not the subject of Fix 3.
        /// </summary>
        [Fact]
        public void Fix3_SourceScan_AppliesDeployFilter()
        {
            // --- 1. Build a source directory with decoy files and one real file ---
            string srcDir = Path.Combine(_tempRoot, "DA_IgnorePR_A_Fix3_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(srcDir);

            // Root-level *.deploy file — must be filtered (IgnoreType.Deploy).
            // Not named "DeployAssistant.deploy" to avoid TryGetDeployMetaFile interception.
            string deployRoot = Path.Combine(srcDir, "app.deploy");
            File.WriteAllText(deployRoot, "{}");

            // Subdirectory with a nested *.deploy — verifies subtree filtering
            string nestedDir = Path.Combine(srcDir, "nested");
            Directory.CreateDirectory(nestedDir);
            string deployNested = Path.Combine(nestedDir, "sub.deploy");
            File.WriteAllText(deployNested, "{}");

            // Real .dll in the nested subdirectory — registered via RegisterFilesUnderSubDirectory,
            // so it lands in _preStagedFilesDict and appears in the snapshot.
            string realDll = Path.Combine(nestedDir, "actual.dll");
            File.WriteAllText(realDll, "MZ");

            // --- 2. Set up FileManager with ignore data ---
            var fileManager = new FileManager();

            var ignoreData = new ProjectIgnoreData("Fix3Proj");
            ignoreData.ConfigureDefaultIgnore("Fix3Proj");
            var metaData = new ProjectMetaData("Fix3Proj", srcDir);
            fileManager.MetaDataManager_ProjectContextLoadedCallBack(ProjectContext.Create(metaData, ignoreData));

            // --- 3. Subscribe to DataPreStagedEventHandler and capture the final snapshot ---
            // RegisterAllSrcFiles fires DataPreStagedEventHandler twice:
            //   first from RegisterFilesUnderSubDirectory (dirs + sub-files),
            //   then from HandleAbnormalFiles (top-level files).
            // We capture the LAST firing so the snapshot is complete.
            List<ProjectFile>? capturedSnapshot = null;
            var preStagedFired = new ManualResetEventSlim(initialState: false);

            fileManager.DataPreStagedEventHandler += obj =>
            {
                if (obj is List<ProjectFile> snapshot)
                {
                    capturedSnapshot = snapshot;
                    preStagedFired.Set();   // set (or re-set) on every firing
                }
            };

            // --- 4. Trigger the source scan via the public API ---
            // RetrieveDataSrc → RegisterNewData → RegisterAllSrcFiles (async void).
            // We wait briefly after the event fires to allow HandleAbnormalFiles to also complete.
            fileManager.RetrieveDataSrc(srcDir);

            bool fired = preStagedFired.Wait(TimeSpan.FromSeconds(10));
            Assert.True(fired, "DataPreStagedEventHandler did not fire within 10 seconds");

            // Allow the second firing (HandleAbnormalFiles) to complete before reading the snapshot.
            Thread.Sleep(200);
            Assert.NotNull(capturedSnapshot);

            // --- 5. Assert correct files are / are not pre-staged ---
            var relPaths = capturedSnapshot!
                .Select(f => f.DataRelPath)
                .ToList();

            // nested/actual.dll must be present (control — real deployable file in a subdir)
            Assert.Contains(relPaths, r => r.EndsWith("actual.dll", StringComparison.OrdinalIgnoreCase));

            // app.deploy must NOT be staged (root-level IgnoreType.Deploy filter)
            Assert.DoesNotContain(relPaths, r => r.EndsWith(".deploy", StringComparison.OrdinalIgnoreCase));

            // nested/sub.deploy must NOT be staged (subtree IgnoreType.Deploy filter)
            Assert.DoesNotContain(relPaths, r => r.EndsWith("sub.deploy", StringComparison.OrdinalIgnoreCase));
        }

        // ------------------------------------------------------------------ Fix 4

        /// <summary>
        /// End-to-end Initialization-scope filtering via the new ProjectScanner.
        /// After ConfigureDefaultIgnore, *.deploy / Backup_&lt;Name&gt; / Export_&lt;Name&gt;
        /// must be absent from the scanner's enumeration; the control file
        /// (app.dll) must be present.  Replaces the prior GetIgnoreFilesAndDirPaths
        /// test (the method was deleted with the refactor).
        /// </summary>
        [Fact]
        public void Fix4_Scanner_InitializationScope_ExcludesDeployAndBackupAndExport()
        {
            string projDir = Path.Combine(_tempRoot, "InitScopeProj");
            Directory.CreateDirectory(projDir);

            // Create files/dirs that should be excluded under Initialization scope
            string deployFile = Path.Combine(projDir, "DeployAssistant.deploy");
            File.WriteAllText(deployFile, "{}");

            string backupDir = Path.Combine(projDir, "Backup_InitScopeProj");
            Directory.CreateDirectory(backupDir);
            string backupFile = Path.Combine(backupDir, "old.dll");
            File.WriteAllText(backupFile, "old");

            string exportDir = Path.Combine(projDir, "Export_InitScopeProj");
            Directory.CreateDirectory(exportDir);
            string exportFile = Path.Combine(exportDir, "report.xlsx");
            File.WriteAllText(exportFile, "xlsx");

            // Normal file that should NOT be excluded
            string normalFile = Path.Combine(projDir, "app.dll");
            File.WriteAllText(normalFile, "dll");

            var ignoreData = new ProjectIgnoreData("InitScopeProj");
            ignoreData.ConfigureDefaultIgnore("InitScopeProj");
            var ctx = ProjectContext.Create(new ProjectMetaData("InitScopeProj", projDir), ignoreData);

            var includedFiles = ctx.Scanner.EnumerateFiles(projDir, IgnoreType.Initialization).ToList();
            var includedDirs = ctx.Scanner.EnumerateDirectories(projDir, IgnoreType.Initialization).ToList();

            // *.deploy must be filtered out (Initialization flag added in Fix 4)
            Assert.DoesNotContain(includedFiles, f => f.EndsWith("DeployAssistant.deploy", StringComparison.OrdinalIgnoreCase));

            // Backup dir and its contents must be filtered out
            Assert.DoesNotContain(includedDirs, d => d.EndsWith("Backup_InitScopeProj", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(includedFiles, f => f.EndsWith("old.dll", StringComparison.OrdinalIgnoreCase));

            // Export dir and its contents must be filtered out
            Assert.DoesNotContain(includedDirs, d => d.EndsWith("Export_InitScopeProj", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(includedFiles, f => f.EndsWith("report.xlsx", StringComparison.OrdinalIgnoreCase));

            // Control: normal app.dll must remain
            Assert.Contains(includedFiles, f => f.EndsWith("app.dll", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// End-to-end legacy-file heal: writing a pre-refactor <c>.ignore</c>
        /// JSON to disk and triggering <c>SettingManager.MetaDataLoaded</c>
        /// must (a) load it, (b) heal in-memory flags, and (c) re-persist the
        /// healed file back to disk.  Validates the SettingManager wiring on
        /// top of the EnsureDefaultFlags unit tests.
        /// </summary>
        [Fact]
        public void SettingManager_LoadingLegacyIgnoreFile_PersistsHealedFlags()
        {
            string projDir = Path.Combine(_tempRoot, "LegacyIgnoreProj");
            Directory.CreateDirectory(projDir);
            string ignoreFilePath = Path.Combine(projDir, "DeployAssistant.ignore");

            // Persist a legacy-format ignore file (no Integration flag on the
            // three entries the refactor cares about).
            string legacyJson = @"{
              ""ProjectName"": ""LegacyIgnoreProj"",
              ""IgnoreFileList"": [
                { ""DataName"": ""*.deploy"", ""DataType"": 0, ""IgnoreType"": 12, ""UpdatedTime"": ""2024-01-01T00:00:00"" },
                { ""DataName"": ""Backup_LegacyIgnoreProj"", ""DataType"": 1, ""IgnoreType"": 10, ""UpdatedTime"": ""2024-01-01T00:00:00"" },
                { ""DataName"": ""Export_LegacyIgnoreProj"", ""DataType"": 1, ""IgnoreType"": 10, ""UpdatedTime"": ""2024-01-01T00:00:00"" }
              ]
            }";
            File.WriteAllText(ignoreFilePath, legacyJson);

            var settingManager = new SettingManager();
            var metaData = new ProjectMetaData("LegacyIgnoreProj", projDir);

            settingManager.MetaDataManager_MetaDataLoadedCallBack(metaData);

            // Re-read from disk and assert the heal was persisted.
            var fileHandler = new DeployAssistant.Utils.FileHandlerTool();
            Assert.True(fileHandler.TryDeserializeJsonData(ignoreFilePath, out ProjectIgnoreData? healed));
            Assert.NotNull(healed);

            var deploy = healed!.IgnoreFileList.First(e => e.DataName == "*.deploy");
            var backup = healed.IgnoreFileList.First(e => e.DataName == "Backup_LegacyIgnoreProj");
            var export = healed.IgnoreFileList.First(e => e.DataName == "Export_LegacyIgnoreProj");

            Assert.True((deploy.IgnoreType & IgnoreType.Integration) != 0);
            Assert.True((backup.IgnoreType & IgnoreType.Integration) != 0);
            Assert.True((export.IgnoreType & IgnoreType.Integration) != 0);
        }

        /// <summary>
        /// Integration-scope behavioral test for the all-in refactor:
        /// after the predicate-based filter started honoring IgnoreType,
        /// the default *.deploy / Backup_&lt;Name&gt; / Export_&lt;Name&gt; entries
        /// must also carry Integration so they remain filtered from
        /// version-diff results (previously masked by scope-blind PartOfIgnore).
        /// </summary>
        [Fact]
        public void Defaults_DeployAndBackupAndExport_AreFilteredFromIntegrationDiff()
        {
            string projPath = @"C:\IntegFakeDst";
            string srcPath  = @"C:\IntegFakeSrc";

            var srcFiles = new[]
            {
                MakeFile(@"app.deploy", srcPath),
                MakeFile(@"Backup_TestProj\old.dll", srcPath),
                MakeFile(@"Export_TestProj\report.xlsx", srcPath),
                MakeFile(@"app.dll", srcPath),
            };

            var srcData = MakeProjectData(srcPath, srcFiles);
            var dstData = MakeProjectData(projPath);

            var fileManager = new FileManager();
            var ignoreData = new ProjectIgnoreData("TestProj");
            ignoreData.ConfigureDefaultIgnore("TestProj");
            var metaData = new ProjectMetaData("TestProj", projPath);
            fileManager.MetaDataManager_ProjectContextLoadedCallBack(ProjectContext.Create(metaData, ignoreData));

            var result = fileManager.FindVersionDifferencesForIntegration(srcData, dstData, out _, out _);
            Assert.NotNull(result);

            var relPaths = result!.Select(c => c.SrcFile?.DataRelPath ?? c.DstFile?.DataRelPath ?? "").ToList();

            Assert.DoesNotContain(relPaths, r => r.EndsWith(".deploy", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(relPaths, r => r.StartsWith("Backup_TestProj", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(relPaths, r => r.StartsWith("Export_TestProj", StringComparison.OrdinalIgnoreCase));
            // Control: real file remains
            Assert.Contains(relPaths, r => r.EndsWith("app.dll", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Sanity check: *.deploy default entry now carries both Deploy and Initialization flags.
        /// </summary>
        [Fact]
        public void Fix4_DefaultDeployEntry_HasInitializationFlag()
        {
            var ignoreData = new ProjectIgnoreData("P");

            var deployEntry = ignoreData.IgnoreFileList.FirstOrDefault(f => f.DataName == "*.deploy");
            Assert.NotNull(deployEntry);
            Assert.True((deployEntry!.IgnoreType & IgnoreType.Initialization) != 0,
                "*.deploy entry must carry IgnoreType.Initialization after Fix 4");
            Assert.True((deployEntry.IgnoreType & IgnoreType.Deploy) != 0,
                "*.deploy entry must still carry IgnoreType.Deploy");
        }

        /// <summary>
        /// Sanity check: Backup_<Name> and Export_<Name> entries added by ConfigureDefaultIgnore
        /// carry the Initialization flag after Fix 4.
        /// </summary>
        [Fact]
        public void Fix4_ConfigureDefaultIgnore_BackupAndExport_HaveInitializationFlag()
        {
            var ignoreData = new ProjectIgnoreData("MyProj");
            ignoreData.ConfigureDefaultIgnore("MyProj");

            var backupEntry = ignoreData.IgnoreFileList.FirstOrDefault(f => f.DataName == "Backup_MyProj");
            var exportEntry = ignoreData.IgnoreFileList.FirstOrDefault(f => f.DataName == "Export_MyProj");

            Assert.NotNull(backupEntry);
            Assert.True((backupEntry!.IgnoreType & IgnoreType.Initialization) != 0,
                "Backup_<Name> must carry IgnoreType.Initialization after Fix 4");

            Assert.NotNull(exportEntry);
            Assert.True((exportEntry!.IgnoreType & IgnoreType.Initialization) != 0,
                "Export_<Name> must carry IgnoreType.Initialization after Fix 4");
        }
    }
}
