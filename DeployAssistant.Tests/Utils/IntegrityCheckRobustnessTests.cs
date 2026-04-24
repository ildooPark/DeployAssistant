#pragma warning disable CS0618  // V1 types used intentionally for V1 robustness tests

using DeployAssistant.DataComponent;
using DeployAssistant.Interfaces;
using DeployAssistant.Model;
using DeployAssistant.Utils;
using System;
using System.IO;
using System.Text;
using Xunit;

namespace DeployAssistant.Tests.Utils
{
    /// <summary>
    /// Tests that verify hash-failure and version-read-failure scenarios do not silently skip
    /// files during the integrity check.
    /// </summary>
    public class IntegrityCheckRobustnessTests : IDisposable
    {
        private readonly HashTool _hashTool = new HashTool();
        private readonly string _tempDir;

        public IntegrityCheckRobustnessTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, true);
        }

        private string WriteTemp(string name, string content = "test content")
        {
            var path = Path.Combine(_tempDir, name);
            File.WriteAllText(path, content, Encoding.UTF8);
            return path;
        }

        private static ProjectFile MakeStoredFile(string relPath, string name, string storedHash, string srcPath)
        {
            return new ProjectFile(
                DataType: ProjectDataType.File,
                DataSize: 100,
                BuildVersion: "1.0",
                DeployedProjectVersion: "1.0",
                UpdatedTime: DateTime.Now,
                DataState: DataState.None,
                dataName: name,
                dataSrcPath: srcPath,
                dataRelPath: relPath,
                dataHash: storedHash,
                IsDstFile: false);
        }

        // -----------------------------------------------------------------------
        // Hash-failure detection: clearing DataHash before hashing
        // -----------------------------------------------------------------------

        [Fact]
        public void DeepCopiedFile_DataHashRetainedFromOriginal_MatchesStoredHash()
        {
            // Demonstrates the original bug: a deep copy of a stored ProjectFile retains
            // the stored hash. If we forget to clear it before computing the on-disk hash,
            // a hashing failure would leave DataHash == stored hash, making the file
            // appear unchanged even when it has been modified.
            var stored = MakeStoredFile("app.dll", "app.dll", "STORED_HASH_1234", _tempDir);
            var copy = new ProjectFile(stored);

            Assert.Equal("STORED_HASH_1234", copy.DataHash);
        }

        [Fact]
        public void ClearingHashBeforeComputation_EnablesFailureDetection()
        {
            // After clearing DataHash to "" before calling GetFileMD5CheckSum, if hashing
            // fails silently (returns without setting DataHash), DataHash stays "".
            // The caller can then detect the failure by checking for empty hash.
            var stored = MakeStoredFile("app.dll", "app.dll", "STORED_HASH_1234", _tempDir);
            var copy = new ProjectFile(stored);

            // Simulate the fix: clear before hashing
            copy.DataHash = "";

            // Verify the clear took effect (hashing hasn't been called yet)
            Assert.Equal("", copy.DataHash);

            // Now simulate a successful hash
            WriteTemp("app.dll", "real file content");
            _hashTool.GetFileMD5CheckSum(copy);

            // After a successful hash the value must be non-empty
            Assert.NotEmpty(copy.DataHash);
            Assert.NotEqual("STORED_HASH_1234", copy.DataHash);
        }

        [Fact]
        public void HashFailureOnInaccessibleFile_LeavesDataHashEmpty()
        {
            // When GetFileMD5CheckSum cannot open the file, it swallows the exception
            // and leaves DataHash unchanged. Starting from "" lets callers detect the failure.
            var file = new ProjectFile(
                DataType: ProjectDataType.File,
                DataSize: 0,
                BuildVersion: "",
                DeployedProjectVersion: "",
                UpdatedTime: DateTime.Now,
                DataState: DataState.None,
                dataName: "missing.dll",
                dataSrcPath: _tempDir,
                dataRelPath: "missing.dll",  // does not exist on disk
                dataHash: "",
                IsDstFile: false);

            // Starting from empty hash
            Assert.Equal("", file.DataHash);
            _hashTool.GetFileMD5CheckSum(file);

            // DataHash is still empty: hashing failed silently
            Assert.Equal("", file.DataHash);
        }

        [Fact]
        public void AfterClearingHash_SuccessfulHash_IsDetectableAsNonEmpty()
        {
            var filePath = WriteTemp("real.txt", "hello");
            var file = new ProjectFile(
                DataType: ProjectDataType.File,
                DataSize: 0,
                BuildVersion: "",
                DeployedProjectVersion: "",
                UpdatedTime: DateTime.Now,
                DataState: DataState.None,
                dataName: "real.txt",
                dataSrcPath: _tempDir,
                dataRelPath: "real.txt",
                dataHash: "",
                IsDstFile: false);

            _hashTool.GetFileMD5CheckSum(file);

            Assert.NotEmpty(file.DataHash);
        }

        // -----------------------------------------------------------------------
        // ProjectFile constructor: FileVersionInfo failure on non-PE files
        // -----------------------------------------------------------------------

        [Fact]
        public void ProjectFileConstructor_NonPeFile_DoesNotThrow()
        {
            // A plain text file is not a PE binary; FileVersionInfo.GetVersionInfo used to
            // throw for such files and abort the whole integrity check. After the fix it
            // should succeed with BuildVersion = "".
            var path = WriteTemp("config.json", "{ \"key\": \"value\" }");

            var ex = Record.Exception(() =>
            {
                var file = new ProjectFile(_tempDir, "config.json", null, DataState.Added, ProjectDataType.File);
                Assert.Equal("", file.BuildVersion);
                Assert.True(file.DataSize > 0);
            });

            Assert.Null(ex);
        }

        [Fact]
        public void ProjectFileConstructor_NonPeFile_SetsEmptyBuildVersion()
        {
            WriteTemp("data.csv", "col1,col2\n1,2");
            var file = new ProjectFile(_tempDir, "data.csv", "HASH_X", DataState.Added, ProjectDataType.File);

            Assert.Equal("", file.BuildVersion);
            Assert.Equal("HASH_X", file.DataHash);
            Assert.Equal(ProjectDataType.File, file.DataType);
        }

        [Fact]
        public void ProjectFileConstructor_Directory_DoesNotCallFileVersionInfo()
        {
            // Directories never call FileVersionInfo; constructor should always succeed.
            var dirName = "some_dir";
            var dirPath = Path.Combine(_tempDir, dirName);
            Directory.CreateDirectory(dirPath);

            var ex = Record.Exception(() =>
            {
                var file = new ProjectFile(_tempDir, dirName, null, DataState.Added, ProjectDataType.Directory);
                Assert.Equal("", file.BuildVersion);
                Assert.Equal(0, file.DataSize);
            });

            Assert.Null(ex);
        }
    }
}
