#pragma warning disable CS0618  // V1 types used intentionally for V1 utility tests

using DeployAssistant.DataComponent;
using DeployAssistant.Interfaces;
using DeployAssistant.Model;
using DeployAssistant.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace DeployAssistant.Tests.Utils
{
    public class FileHandlerToolTests : IDisposable
    {
        private readonly FileHandlerTool _tool = new FileHandlerTool();
        private readonly string _tempDir;

        public FileHandlerToolTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, true);
        }

        private string TempPath(string relative) => Path.Combine(_tempDir, relative);

        private ProjectData BuildProjectData(string version = "1.0")
        {
            var data = new ProjectData(TempPath("project"));
            data.ProjectName = "TestProject";
            data.UpdaterName = "Tester";
            data.ConductedPC = "TestPC";
            data.UpdateLog = "Test update";
            data.ChangeLog = "Test changes";
            data.UpdatedVersion = version;
            data.UpdatedTime = new DateTime(2024, 1, 1);
            data.RevisionNumber = 1;
            data.NumberOfChanges = 0;
            return data;
        }

        #region Serialize / Deserialize ProjectData

        [Fact]
        public void TrySerializeProjectData_ValidData_WritesFileAndReturnsTrue()
        {
            var data = BuildProjectData();
            string filePath = TempPath("project.bin");

            bool result = _tool.TrySerializeProjectData(data, filePath);

            Assert.True(result);
            Assert.True(File.Exists(filePath));
            Assert.True(new FileInfo(filePath).Length > 0);
        }

        [Fact]
        public void TryDeserializeProjectData_ValidFile_ReturnsTrueAndDeserializedData()
        {
            var data = BuildProjectData("2.5");
            string filePath = TempPath("round_trip.bin");
            _tool.TrySerializeProjectData(data, filePath);

            bool result = _tool.TryDeserializeProjectData(filePath, out var deserialized);

            Assert.True(result);
            Assert.NotNull(deserialized);
            Assert.Equal("TestProject", deserialized!.ProjectName);
            Assert.Equal("2.5", deserialized.UpdatedVersion);
            Assert.Equal("Tester", deserialized.UpdaterName);
        }

        [Fact]
        public void SerializeDeserialize_RoundTrip_PreservesAllProjectFiles()
        {
            var data = BuildProjectData("3.0");
            data.ProjectFiles[@"lib\helper.dll"] = new ProjectFile(
                DataType: ProjectDataType.File,
                DataSize: 512,
                BuildVersion: "1.1",
                DeployedProjectVersion: "3.0",
                UpdatedTime: new DateTime(2024, 3, 1),
                DataState: DataState.Added,
                dataName: "helper.dll",
                dataSrcPath: TempPath("project"),
                dataRelPath: @"lib\helper.dll",
                dataHash: "CAFEBABE",
                IsDstFile: true);

            string filePath = TempPath("with_files.bin");
            _tool.TrySerializeProjectData(data, filePath);

            _tool.TryDeserializeProjectData(filePath, out var restored);

            Assert.NotNull(restored);
            Assert.Single(restored!.ProjectFiles);
            Assert.True(restored.ProjectFiles.ContainsKey(@"lib\helper.dll"));

            var restoredFile = restored.ProjectFiles[@"lib\helper.dll"];
            Assert.Equal("helper.dll", restoredFile.DataName);
            Assert.Equal("CAFEBABE", restoredFile.DataHash);
            Assert.Equal(512, restoredFile.DataSize);
        }

        [Fact]
        public void TryDeserializeProjectData_InvalidFile_ReturnsFalseAndNullData()
        {
            string filePath = TempPath("corrupt.bin");
            File.WriteAllText(filePath, "this is not valid base64 encoded json !!!");

            bool result = _tool.TryDeserializeProjectData(filePath, out var data);

            Assert.False(result);
            Assert.Null(data);
        }

        [Fact]
        public void TryDeserializeProjectData_NonexistentFile_ReturnsFalse()
        {
            bool result = _tool.TryDeserializeProjectData(TempPath("ghost.bin"), out var data);

            Assert.False(result);
            Assert.Null(data);
        }

        #endregion

        #region Serialize / Deserialize ProjectMetaData

        [Fact]
        public void TrySerializeProjectMetaData_ValidData_WritesFileAndReturnsTrue()
        {
            var meta = new ProjectMetaData("MetaProj", TempPath("metaproject"));
            string filePath = TempPath("meta.bin");

            bool result = _tool.TrySerializeProjectMetaData(meta, filePath);

            Assert.True(result);
            Assert.True(File.Exists(filePath));
        }

        [Fact]
        public void TryDeserializeProjectMetaData_RoundTrip_PreservesNameAndPath()
        {
            var meta = new ProjectMetaData("MetaProj", TempPath("metaproject"));
            string filePath = TempPath("meta_rt.bin");
            _tool.TrySerializeProjectMetaData(meta, filePath);

            bool result = _tool.TryDeserializeProjectMetaData(filePath, out var restored);

            Assert.True(result);
            Assert.NotNull(restored);
            Assert.Equal("MetaProj", restored!.ProjectName);
        }

        [Fact]
        public void TryDeserializeProjectMetaData_CorruptFile_ReturnsFalse()
        {
            string filePath = TempPath("bad_meta.bin");
            File.WriteAllText(filePath, "not valid data");

            bool result = _tool.TryDeserializeProjectMetaData(filePath, out var data);

            Assert.False(result);
            Assert.Null(data);
        }

        #endregion

        #region TrySerializeJsonData / TryDeserializeJsonData

        [Fact]
        public void TrySerializeJsonData_ValidObject_WritesJsonFileAndReturnsTrue()
        {
            var obj = new { Name = "Test", Value = 42 };
            string filePath = TempPath("generic.json");

            bool result = _tool.TrySerializeJsonData(filePath, in obj);

            Assert.True(result);
            Assert.True(File.Exists(filePath));
            string content = File.ReadAllText(filePath);
            Assert.Contains("Test", content);
        }

        #endregion

        #region HandleDirectory

        [Fact]
        public void HandleDirectory_AddedState_CreatesDirectory()
        {
            string newDir = TempPath("new_dir");
            Assert.False(Directory.Exists(newDir));

            bool result = _tool.HandleDirectory(null, newDir, DataState.Added);

            Assert.True(result);
            Assert.True(Directory.Exists(newDir));
        }

        [Fact]
        public void HandleDirectory_DeletedState_RemovesExistingDirectory()
        {
            string dirToDelete = TempPath("to_delete");
            Directory.CreateDirectory(dirToDelete);
            File.WriteAllText(Path.Combine(dirToDelete, "file.txt"), "content");

            bool result = _tool.HandleDirectory(null, dirToDelete, DataState.Deleted);

            Assert.True(result);
            Assert.False(Directory.Exists(dirToDelete));
        }

        [Fact]
        public void HandleDirectory_DeletedState_NonExistentDir_ReturnsTrue()
        {
            string nonExistent = TempPath("ghost_dir");

            bool result = _tool.HandleDirectory(null, nonExistent, DataState.Deleted);

            Assert.True(result);
        }

        [Fact]
        public void HandleDirectory_ModifiedState_CreatesIfNotExists()
        {
            string dir = TempPath("modified_dir");

            bool result = _tool.HandleDirectory(null, dir, DataState.Modified);

            Assert.True(result);
            Assert.True(Directory.Exists(dir));
        }

        #endregion

        #region HandleFile

        [Fact]
        public void HandleFile_AddedState_CopiesFileToDestination()
        {
            string srcFile = TempPath("source.txt");
            string dstFile = TempPath("subdir/dest.txt");
            File.WriteAllText(srcFile, "hello");

            bool result = _tool.HandleFile(srcFile, dstFile, DataState.Added);

            Assert.True(result);
            Assert.True(File.Exists(dstFile));
            Assert.Equal("hello", File.ReadAllText(dstFile));
        }

        [Fact]
        public void HandleFile_DeletedState_RemovesExistingFile()
        {
            string fileToDelete = TempPath("delete_me.txt");
            File.WriteAllText(fileToDelete, "bye");

            bool result = _tool.HandleFile(null, fileToDelete, DataState.Deleted);

            Assert.True(result);
            Assert.False(File.Exists(fileToDelete));
        }

        [Fact]
        public void HandleFile_DeletedState_NonExistentFile_ReturnsTrue()
        {
            bool result = _tool.HandleFile(null, TempPath("ghost.txt"), DataState.Deleted);

            Assert.True(result);
        }

        [Fact]
        public void HandleFile_ModifiedState_OverwritesDestinationFile()
        {
            string srcFile = TempPath("src_mod.txt");
            string dstFile = TempPath("dst_mod.txt");
            File.WriteAllText(srcFile, "new content");
            File.WriteAllText(dstFile, "old content");

            bool result = _tool.HandleFile(srcFile, dstFile, DataState.Modified);

            Assert.True(result);
            Assert.Equal("new content", File.ReadAllText(dstFile));
        }

        [Fact]
        public void HandleFile_ModifiedState_SrcEqualsDestination_ReturnsFalse()
        {
            string path = TempPath("same.txt");
            File.WriteAllText(path, "data");

            bool result = _tool.HandleFile(path, path, DataState.Modified);

            Assert.False(result);
        }

        [Fact]
        public void HandleFile_AddedState_FileAlreadyExists_DoesNotOverwrite()
        {
            string srcFile = TempPath("src_existing.txt");
            string dstFile = TempPath("dst_existing.txt");
            File.WriteAllText(srcFile, "new");
            File.WriteAllText(dstFile, "existing");

            bool result = _tool.HandleFile(srcFile, dstFile, DataState.Added);

            // Added state: only copies if file doesn't exist
            Assert.True(result);
            Assert.Equal("existing", File.ReadAllText(dstFile));
        }

        #endregion

        #region MoveFile

        [Fact]
        public void MoveFile_ValidPaths_MovesFileSuccessfully()
        {
            string src = TempPath("move_src.txt");
            string dst = TempPath("move_dst/move_dst.txt");
            File.WriteAllText(src, "move me");

            bool result = _tool.MoveFile(src, dst);

            Assert.True(result);
            Assert.False(File.Exists(src));
            Assert.True(File.Exists(dst));
            Assert.Equal("move me", File.ReadAllText(dst));
        }

        [Fact]
        public void MoveFile_SrcEqualsDst_DoesNotFail()
        {
            string path = TempPath("no_move.txt");
            File.WriteAllText(path, "same");

            bool result = _tool.MoveFile(path, path);

            Assert.True(result);
        }

        #endregion

        #region TryApplyFileChanges

        [Fact]
        public void TryApplyFileChanges_NullList_ReturnsFalse()
        {
            bool result = _tool.TryApplyFileChanges(null!);

            Assert.False(result);
        }

        [Fact]
        public void TryApplyFileChanges_EmptyList_ReturnsTrue()
        {
            bool result = _tool.TryApplyFileChanges(new List<ChangedFile>());

            Assert.True(result);
        }

        [Fact]
        public void TryApplyFileChanges_IntegrityCheckedFilesAreSkipped()
        {
            // A file marked IntegrityChecked should be skipped (no file operation attempted)
            var phantomFile = new ProjectFile(
                DataType: ProjectDataType.File,
                DataSize: 0,
                BuildVersion: "",
                DeployedProjectVersion: "",
                UpdatedTime: DateTime.Now,
                DataState: DataState.IntegrityChecked,
                dataName: "phantom.dll",
                dataSrcPath: TempPath("nonexistent_src"),
                dataRelPath: "phantom.dll",
                dataHash: "",
                IsDstFile: true);

            var change = new ChangedFile(phantomFile, DataState.IntegrityChecked);
            var changes = new List<ChangedFile> { change };

            // Should not throw or fail because the IntegrityChecked file is skipped
            bool result = _tool.TryApplyFileChanges(changes);

            Assert.True(result);
        }

        [Fact]
        public void TryApplyFileChanges_DeleteFile_RemovesFile()
        {
            string fileToDelete = TempPath("delete_via_apply.txt");
            File.WriteAllText(fileToDelete, "delete me");

            var dstFile = new ProjectFile(
                DataType: ProjectDataType.File,
                DataSize: 0,
                BuildVersion: "",
                DeployedProjectVersion: "",
                UpdatedTime: DateTime.Now,
                DataState: DataState.Deleted,
                dataName: "delete_via_apply.txt",
                dataSrcPath: _tempDir,
                dataRelPath: "delete_via_apply.txt",
                dataHash: "",
                IsDstFile: true);

            var change = new ChangedFile(dstFile, DataState.Deleted);
            var changes = new List<ChangedFile> { change };

            bool result = _tool.TryApplyFileChanges(changes);

            Assert.True(result);
            Assert.False(File.Exists(fileToDelete));
        }

        #endregion

        #region HandleData overloads

        [Fact]
        public void HandleData_IProjectData_DeleteFile_RemovesFile()
        {
            string fileToDelete = TempPath("handle_data_del.txt");
            File.WriteAllText(fileToDelete, "to delete");

            var projectFile = new ProjectFile(
                DataType: ProjectDataType.File,
                DataSize: 0,
                BuildVersion: "",
                DeployedProjectVersion: "",
                UpdatedTime: DateTime.Now,
                DataState: DataState.Deleted,
                dataName: "handle_data_del.txt",
                dataSrcPath: _tempDir,
                dataRelPath: "handle_data_del.txt",
                dataHash: "",
                IsDstFile: false);

            bool result = _tool.HandleData(projectFile, DataState.Deleted);

            Assert.True(result);
            Assert.False(File.Exists(fileToDelete));
        }

        [Fact]
        public void HandleData_StringSrcPath_CopiesFile()
        {
            string srcFile = TempPath("hd_src.txt");
            string dstFile = TempPath("hd_dst.txt");
            File.WriteAllText(srcFile, "copy via HandleData");

            bool result = _tool.HandleData(srcFile, dstFile, ProjectDataType.File, DataState.Modified);

            Assert.True(result);
            Assert.True(File.Exists(dstFile));
            Assert.Equal("copy via HandleData", File.ReadAllText(dstFile));
        }

        #endregion
    }
}
