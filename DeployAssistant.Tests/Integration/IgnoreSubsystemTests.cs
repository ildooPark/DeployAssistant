#pragma warning disable CS0618  // V1 types used intentionally

using DeployAssistant.DataComponent;
using DeployAssistant.Interfaces;
using DeployAssistant.Model;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
            // Inject ignore data so _projIgnoreData != null
            var ignoreData = new ProjectIgnoreData("TestProj");
            ignoreData.ConfigureDefaultIgnore("TestProj");
            fileManager.MetaDataManager_UpdateIgnoreListCallBack(ignoreData);

            var result = fileManager.FindVersionDifferencesForIntegration(srcData, dstData, out int significantDiff);

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
            Assert.Equal(1, result!.Count);
            Assert.Equal(significantDiff, result.Count);
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
            fileManager.MetaDataManager_UpdateIgnoreListCallBack(ignoreData);

            var result = fileManager.FindVersionDifferencesForIntegration(srcData, dstData, out int significantDiff);

            Assert.NotNull(result);
            Assert.Equal(significantDiff, result!.Count);
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
            fileManager.MetaDataManager_UpdateIgnoreListCallBack(ignoreData);

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

        // ------------------------------------------------------------------ Fix 4

        /// <summary>
        /// GetIgnoreFilesAndDirPaths with IgnoreType.Initialization must exclude
        /// *.deploy files, Backup_<Name>/ and Export_<Name>/ after ConfigureDefaultIgnore.
        /// </summary>
        [Fact]
        public void Fix4_GetIgnoreFilesAndDirPaths_InitializationScope_ExcludesDeployAndBackupAndExport()
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

            var (excludedFiles, excludedDirs) = ignoreData.GetIgnoreFilesAndDirPaths(projDir, IgnoreType.Initialization);

            // *.deploy must be excluded (Initialization flag added in Fix 4)
            Assert.Contains(excludedFiles, f => f.EndsWith("DeployAssistant.deploy", StringComparison.OrdinalIgnoreCase));

            // Backup dir and its contents must be excluded (Initialization flag added in Fix 4)
            Assert.Contains(excludedDirs, d => d.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .EndsWith("Backup_InitScopeProj", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(excludedFiles, f => f.EndsWith("old.dll", StringComparison.OrdinalIgnoreCase));

            // Export dir and its contents must be excluded (Initialization flag added in Fix 4)
            Assert.Contains(excludedDirs, d => d.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .EndsWith("Export_InitScopeProj", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(excludedFiles, f => f.EndsWith("report.xlsx", StringComparison.OrdinalIgnoreCase));

            // Normal app.dll must NOT be excluded
            Assert.DoesNotContain(excludedFiles, f => f.EndsWith("app.dll", StringComparison.OrdinalIgnoreCase));
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
