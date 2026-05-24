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

        // -----------------------------------------------------------------------
        // String-overload resilience: the GetFileMD5CheckSum(projectPath, relPath)
        // overload at HashTool.cs:48 was documented as "swallows exceptions" but
        // didn't.  A locked or missing file would throw IOException straight to
        // the caller, aborting MainProjectIntegrityCheck.added-files loop and
        // ProjectIntegrityCheck.intersect loop via the outer catch.  These tests
        // pin the new resilient behavior: return "" on failure, never throw.
        // -----------------------------------------------------------------------

        [Fact]
        public void StringOverload_MissingFile_ReturnsEmptyString_NotThrows()
        {
            // Sibling of HashFailureOnInaccessibleFile_LeavesDataHashEmpty but for
            // the (projectPath, relPath) overload that the integrity-check loops use.
            var ex = Record.Exception(() =>
            {
                string result = _hashTool.GetFileMD5CheckSum(_tempDir, "does_not_exist.dll");
                Assert.Equal("", result);
            });
            Assert.Null(ex);
        }

        [Fact]
        public void StringOverload_LockedFile_ReturnsEmptyString_NotThrows()
        {
            // Hold the file open with FileShare.None — the worst-case lock encountered
            // during a deploy when another process has the binary loaded.  The hash
            // attempt must return "" rather than throw IOException up the stack.
            string fileName = "locked.dll";
            string fullPath = Path.Combine(_tempDir, fileName);
            File.WriteAllText(fullPath, "MZ-fake-binary");

            using var holdOpen = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.None);

            var ex = Record.Exception(() =>
            {
                string result = _hashTool.GetFileMD5CheckSum(_tempDir, fileName);
                Assert.Equal("", result);
            });
            Assert.Null(ex);
        }

        [Fact]
        public void StringOverload_PersistentLock_AllRetriesFail_ReturnsEmpty_AndActuallyRetries()
        {
            // Hold the file with FileShare.None for the entire duration.
            // With maxRetries=2 and retryDelayMs=50, the call must:
            //   1. Return "" (all retries failed)
            //   2. Take at least 100 ms (= 2 retries × 50 ms — proves retries actually ran, not short-circuited)
            string fileName = "persistent-lock.dll";
            string fullPath = Path.Combine(_tempDir, fileName);
            File.WriteAllText(fullPath, "MZ-fake-binary");

            using var holdOpen = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.None);

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            string result = _hashTool.GetFileMD5CheckSum(_tempDir, fileName, maxRetries: 2, retryDelayMs: 50);
            stopwatch.Stop();

            Assert.Equal("", result);
            Assert.True(stopwatch.ElapsedMilliseconds >= 100,
                $"Expected at least 100 ms elapsed (2 retries × 50 ms), actually {stopwatch.ElapsedMilliseconds} ms");
        }

        [Fact]
        public void StringOverload_LockReleasedAfterOneRetry_HashEventuallySucceeds()
        {
            // Background task holds the file for ~150 ms then releases.
            // With maxRetries=3 retryDelayMs=100, the second or third attempt should succeed.
            string fileName = "temp-lock.dll";
            string fullPath = Path.Combine(_tempDir, fileName);
            File.WriteAllText(fullPath, "MZ-fake-binary");

            var holdStarted = new System.Threading.ManualResetEventSlim(false);
            var holderTask = System.Threading.Tasks.Task.Run(() =>
            {
                using var fs = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.None);
                holdStarted.Set();
                System.Threading.Thread.Sleep(150);
                // fs disposes here, releasing the lock
            });

            holdStarted.Wait(TimeSpan.FromSeconds(5));  // ensure the lock is established before we call

            string result = _hashTool.GetFileMD5CheckSum(_tempDir, fileName, maxRetries: 3, retryDelayMs: 100);
            holderTask.Wait();

            Assert.NotEqual("", result);
            Assert.Equal(32, result.Length);  // MD5 hex = 32 chars
        }

        [Fact]
        public void StringOverload_MaxRetriesZero_SkipsRetryEntirely()
        {
            // maxRetries=0 means "try once, no retries" — total elapsed should be near-zero.
            string fileName = "no-retry-lock.dll";
            string fullPath = Path.Combine(_tempDir, fileName);
            File.WriteAllText(fullPath, "MZ-fake-binary");

            using var holdOpen = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.None);

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            string result = _hashTool.GetFileMD5CheckSum(_tempDir, fileName, maxRetries: 0, retryDelayMs: 500);
            stopwatch.Stop();

            Assert.Equal("", result);
            Assert.True(stopwatch.ElapsedMilliseconds < 100,
                $"Expected near-zero elapsed time with maxRetries=0, actually {stopwatch.ElapsedMilliseconds} ms");
        }

        [Fact]
        public void StringOverload_NegativeRetriesAndDelay_ClampedToZero_SingleAttempt()
        {
            // Negative inputs are documented to clamp to zero: single attempt, no delay.
            string fileName = "negative-clamp.dll";
            string fullPath = Path.Combine(_tempDir, fileName);
            File.WriteAllText(fullPath, "MZ-fake-binary");

            using var holdOpen = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.None);

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            string result = _hashTool.GetFileMD5CheckSum(_tempDir, fileName, maxRetries: -5, retryDelayMs: -100);
            stopwatch.Stop();

            Assert.Equal("", result);
            Assert.True(stopwatch.ElapsedMilliseconds < 100,
                $"Expected near-zero elapsed time with clamped inputs, actually {stopwatch.ElapsedMilliseconds} ms");
        }

        [Fact]
        public void InstanceOverload_PersistentLock_AllRetriesFail_LeavesDataHashEmpty_AndActuallyRetries()
        {
            // Same retry contract as the string overload, but the instance overload
            // mutates the ProjectFile in place (sets DataHash on success).  On
            // persistent failure, DataHash stays "" and total elapsed time proves
            // retries ran.
            string fileName = "instance-persistent.dll";
            string fullPath = Path.Combine(_tempDir, fileName);
            File.WriteAllText(fullPath, "MZ-fake-binary");

            using var holdOpen = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.None);

            var file = new ProjectFile(
                DataType: ProjectDataType.File,
                DataSize: 14, BuildVersion: "1.0", DeployedProjectVersion: "1.0",
                UpdatedTime: DateTime.Now, DataState: DataState.None,
                dataName: fileName, dataSrcPath: _tempDir,
                dataRelPath: fileName, dataHash: "",
                IsDstFile: false);

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            _hashTool.GetFileMD5CheckSum(file, maxRetries: 2, retryDelayMs: 50);
            stopwatch.Stop();

            Assert.Equal("", file.DataHash);
            Assert.True(stopwatch.ElapsedMilliseconds >= 100,
                $"Expected at least 100 ms elapsed (2 retries × 50 ms), actually {stopwatch.ElapsedMilliseconds} ms");
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
