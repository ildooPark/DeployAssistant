#pragma warning disable CS0618  // V1 types used intentionally for V1 utility tests

using DeployAssistant.DataComponent;
using DeployAssistant.Interfaces;
using DeployAssistant.Model;
using DeployAssistant.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Xunit;

namespace DeployAssistant.Tests.Utils
{
    public class HashToolTests : IDisposable
    {
        private readonly HashTool _hashTool = new HashTool();
        private readonly string _tempDir;

        public HashToolTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, true);
        }

        private string WriteTemp(string name, string content)
        {
            var path = Path.Combine(_tempDir, name);
            File.WriteAllText(path, content, Encoding.UTF8);
            return path;
        }

        #region GetUniqueComputerID

        [Fact]
        public void GetUniqueComputerID_SameInput_ReturnsSameHash()
        {
            var result1 = _hashTool.GetUniqueComputerID("user@machine");
            var result2 = _hashTool.GetUniqueComputerID("user@machine");

            Assert.Equal(result1, result2);
        }

        [Fact]
        public void GetUniqueComputerID_DifferentInputs_ReturnsDifferentHashes()
        {
            var result1 = _hashTool.GetUniqueComputerID("alice@PC01");
            var result2 = _hashTool.GetUniqueComputerID("bob@PC02");

            Assert.NotEqual(result1, result2);
        }

        [Fact]
        public void GetUniqueComputerID_Returns10CharHexString()
        {
            var result = _hashTool.GetUniqueComputerID("anyuser");

            // 5 bytes * 2 hex chars each = 10
            Assert.Equal(10, result.Length);
            Assert.Matches("^[0-9a-f]{10}$", result);
        }

        [Fact]
        public void GetUniqueComputerID_EmptyInput_ReturnsConsistentResult()
        {
            var result1 = _hashTool.GetUniqueComputerID("");
            var result2 = _hashTool.GetUniqueComputerID("");

            Assert.Equal(result1, result2);
            Assert.Equal(10, result1.Length);
        }

        #endregion

        #region GetUniqueProjectDataID

        [Fact]
        public void GetUniqueProjectDataID_SameProjectData_ReturnsSameID()
        {
            var data = BuildProjectData();

            var id1 = _hashTool.GetUniqueProjectDataID(data);
            var id2 = _hashTool.GetUniqueProjectDataID(data);

            Assert.Equal(id1, id2);
        }

        [Fact]
        public void GetUniqueProjectDataID_DifferentProjectFiles_ReturnsDifferentIDs()
        {
            var data1 = BuildProjectData("HASH_A");
            var data2 = BuildProjectData("HASH_B");

            var id1 = _hashTool.GetUniqueProjectDataID(data1);
            var id2 = _hashTool.GetUniqueProjectDataID(data2);

            Assert.NotEqual(id1, id2);
        }

        [Fact]
        public void GetUniqueProjectDataID_EmptyProjectData_ReturnsString()
        {
            var data = new ProjectData(@"C:\Proj");
            data.ProjectName = "empty";
            data.UpdaterName = "U";
            data.UpdatedVersion = "1";
            data.ConductedPC = "PC";
            data.UpdateLog = "";
            data.ChangeLog = "";

            var id = _hashTool.GetUniqueProjectDataID(data);

            Assert.NotNull(id);
        }

        [Fact]
        public void GetUniqueProjectDataID_Returns64CharUppercaseHexString()
        {
            var data = BuildProjectData();

            var id = _hashTool.GetUniqueProjectDataID(data);

            // SHA-256 produces 32 bytes => 64 uppercase hex characters
            Assert.Equal(64, id.Length);
            Assert.Matches("^[0-9A-F]{64}$", id);
        }

        private static ProjectData BuildProjectData(string hash = "DEADBEEF")
        {
            var data = new ProjectData(@"C:\Proj");
            data.ProjectName = "P";
            data.UpdaterName = "U";
            data.UpdatedVersion = "1";
            data.ConductedPC = "PC";
            data.UpdateLog = "";
            data.ChangeLog = "";

            var file = new ProjectFile(
                DataType: ProjectDataType.File,
                DataSize: 100,
                BuildVersion: "1.0",
                DeployedProjectVersion: "1.0",
                UpdatedTime: DateTime.Now,
                DataState: DataState.None,
                dataName: "app.dll",
                dataSrcPath: @"C:\Proj",
                dataRelPath: @"app.dll",
                dataHash: hash,
                IsDstFile: false);

            data.ProjectFiles[@"app.dll"] = file;
            return data;
        }

        #endregion

        #region GetFileMD5CheckSum (path-based)

        [Fact]
        public void GetFileMD5CheckSum_KnownContent_ReturnsConsistentHash()
        {
            var file = WriteTemp("test.txt", "Hello, World!");

            var hash1 = _hashTool.GetFileMD5CheckSum(_tempDir, "test.txt");
            var hash2 = _hashTool.GetFileMD5CheckSum(_tempDir, "test.txt");

            Assert.Equal(hash1, hash2);
            Assert.NotEmpty(hash1);
            Assert.Matches("^[0-9A-F]+$", hash1);
        }

        [Fact]
        public void GetFileMD5CheckSum_DifferentContent_ReturnsDifferentHashes()
        {
            var fileA = WriteTemp("a.txt", "content A");
            var fileB = WriteTemp("b.txt", "content B");

            var hashA = _hashTool.GetFileMD5CheckSum(_tempDir, "a.txt");
            var hashB = _hashTool.GetFileMD5CheckSum(_tempDir, "b.txt");

            Assert.NotEqual(hashA, hashB);
        }

        #endregion

        #region TryCompareMD5CheckSum

        [Fact]
        public void TryCompareMD5CheckSum_SameContent_ReturnsTrue()
        {
            var pathA = WriteTemp("same_a.txt", "identical content");
            var pathB = WriteTemp("same_b.txt", "identical content");

            bool result = _hashTool.TryCompareMD5CheckSum(pathA, pathB, out var hashes);

            Assert.True(result);
            Assert.Equal(hashes.Item1, hashes.Item2);
        }

        [Fact]
        public void TryCompareMD5CheckSum_DifferentContent_ReturnsFalse()
        {
            var pathA = WriteTemp("diff_a.txt", "content A");
            var pathB = WriteTemp("diff_b.txt", "content B -- different");

            bool result = _hashTool.TryCompareMD5CheckSum(pathA, pathB, out var hashes);

            Assert.False(result);
            Assert.NotEqual(hashes.Item1, hashes.Item2);
        }

        [Fact]
        public void TryCompareMD5CheckSum_NullSrcPath_ReturnsFalse()
        {
            bool result = _hashTool.TryCompareMD5CheckSum(null, @"C:\nonexistent.txt", out var hashes);

            Assert.False(result);
            Assert.Null(hashes.Item1);
            Assert.Null(hashes.Item2);
        }

        [Fact]
        public void TryCompareMD5CheckSum_NullDstPath_ReturnsFalse()
        {
            var path = WriteTemp("exist.txt", "data");

            bool result = _hashTool.TryCompareMD5CheckSum(path, null, out var hashes);

            Assert.False(result);
        }

        #endregion

        #region GetFileMD5CheckSum (ProjectFile overloads)

        [Fact]
        public void GetFileMD5CheckSum_ProjectFile_PopulatesDataHash()
        {
            var filePath = WriteTemp("proj.txt", "project file content");
            var projectFile = new ProjectFile(
                DataType: ProjectDataType.File,
                DataSize: 0,
                BuildVersion: "",
                DeployedProjectVersion: "",
                UpdatedTime: DateTime.Now,
                DataState: DataState.None,
                dataName: "proj.txt",
                dataSrcPath: _tempDir,
                dataRelPath: "proj.txt",
                dataHash: "",
                IsDstFile: false);

            _hashTool.GetFileMD5CheckSum(projectFile);

            Assert.NotEmpty(projectFile.DataHash);
            Assert.Matches("^[0-9A-F]+$", projectFile.DataHash);
        }

        [Fact]
        public async Task GetFileMD5CheckSumAsync_ProjectFile_PopulatesDataHash()
        {
            WriteTemp("async_proj.txt", "async content");
            var projectFile = new ProjectFile(
                DataType: ProjectDataType.File,
                DataSize: 0,
                BuildVersion: "",
                DeployedProjectVersion: "",
                UpdatedTime: DateTime.Now,
                DataState: DataState.None,
                dataName: "async_proj.txt",
                dataSrcPath: _tempDir,
                dataRelPath: "async_proj.txt",
                dataHash: "",
                IsDstFile: false);

            await _hashTool.GetFileMD5CheckSumAsync(projectFile);

            Assert.NotEmpty(projectFile.DataHash);
        }

        [Fact]
        public async Task GetFileMD5CheckSumAsync_StringPath_ReturnsHash()
        {
            var filePath = WriteTemp("async_path.txt", "some data for async");

            var hash = await _hashTool.GetFileMD5CheckSumAsync(filePath);

            Assert.NotNull(hash);
            Assert.NotEmpty(hash!);
            Assert.Matches("^[0-9A-F]+$", hash);
        }

        #endregion
    }
}
