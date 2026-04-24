#pragma warning disable CS0618  // V1 types used intentionally for V1 model tests

using DeployAssistant.DataComponent;
using DeployAssistant.Interfaces;
using DeployAssistant.Model;
using System;
using System.Collections.Generic;
using Xunit;

namespace DeployAssistant.Tests.Models
{
    public class ProjectMetaDataTests
    {
        private static ProjectFile MakeFile(string relPath, string name, string srcPath)
        {
            return new ProjectFile(
                DataType: ProjectDataType.File,
                DataSize: 50,
                BuildVersion: "1.0",
                DeployedProjectVersion: "1.0",
                UpdatedTime: DateTime.Now,
                DataState: DataState.None,
                dataName: name,
                dataSrcPath: srcPath,
                dataRelPath: relPath,
                dataHash: "H",
                IsDstFile: false);
        }

        private static ProjectData MakeProjectData(string version, string path)
        {
            var d = new ProjectData(path);
            d.ProjectName = "TestProj";
            d.UpdaterName = "User";
            d.ConductedPC = "PC";
            d.UpdateLog = "";
            d.ChangeLog = "";
            d.UpdatedVersion = version;
            d.UpdatedTime = DateTime.Now;
            return d;
        }

        [Fact]
        public void Constructor_WithNameAndPath_InitializesCorrectly()
        {
            var meta = new ProjectMetaData("MyProj", @"C:\Proj");

            Assert.Equal("MyProj", meta.ProjectName);
            Assert.Equal(@"C:\Proj", meta.ProjectPath);
            Assert.Equal(0, meta.LocalUpdateCount);
            Assert.NotNull(meta.ProjectMain);
            Assert.NotNull(meta.ProjectDataList);
            Assert.Empty(meta.ProjectDataList);
            Assert.NotNull(meta.BackupFiles);
            Assert.Empty(meta.BackupFiles);
        }

        [Fact]
        public void SetProjectMain_UpdatesProjectMainAndIsProjectMainFlag()
        {
            var meta = new ProjectMetaData("MyProj", @"C:\Proj");
            var dataV1 = MakeProjectData("1.0", @"C:\Proj");
            var dataV2 = MakeProjectData("2.0", @"C:\Proj");

            meta.ProjectDataList.AddLast(dataV1);
            meta.ProjectDataList.AddLast(dataV2);

            meta.SetProjectMain(dataV2);

            Assert.Same(dataV2, meta.ProjectMain);
            Assert.True(dataV2.IsProjectMain);
            Assert.False(dataV1.IsProjectMain);
        }

        [Fact]
        public void SetProjectMain_MarksOnlyMatchingVersionAsMain()
        {
            var meta = new ProjectMetaData("Proj", @"C:\Root");
            var a = MakeProjectData("1.0", @"C:\Root");
            var b = MakeProjectData("2.0", @"C:\Root");
            var c = MakeProjectData("3.0", @"C:\Root");

            meta.ProjectDataList.AddLast(a);
            meta.ProjectDataList.AddLast(b);
            meta.ProjectDataList.AddLast(c);

            meta.SetProjectMain(b);

            Assert.False(a.IsProjectMain);
            Assert.True(b.IsProjectMain);
            Assert.False(c.IsProjectMain);
        }

        [Fact]
        public void ReconfigureProjectPath_UpdatesProjectPathAndFilesSrcPaths()
        {
            var meta = new ProjectMetaData("MyProj", @"C:\OldPath");
            var data = MakeProjectData("1.0", @"C:\OldPath");
            data.ProjectFiles[@"a.dll"] = MakeFile(@"a.dll", "a.dll", @"C:\OldPath");

            meta.ProjectDataList.AddLast(data);
            meta.ProjectMain = MakeProjectData("2.0", @"C:\OldPath");
            meta.ProjectMain.ProjectFiles[@"b.dll"] = MakeFile(@"b.dll", "b.dll", @"C:\OldPath");

            meta.ReconfigureProjectPath(@"C:\NewPath");

            Assert.Equal(@"C:\NewPath", meta.ProjectPath);
            Assert.Equal(@"C:\NewPath", data.ProjectPath);
            Assert.Equal(@"C:\NewPath", data.ProjectFiles[@"a.dll"].DataSrcPath);
            Assert.Equal(@"C:\NewPath", meta.ProjectMain.ProjectPath);
            Assert.Equal(@"C:\NewPath", meta.ProjectMain.ProjectFiles[@"b.dll"].DataSrcPath);
        }

        [Fact]
        public void SetBackupFilesPath_UpdatesBackupFileSrcPaths()
        {
            var meta = new ProjectMetaData("MyProj", @"C:\NewRoot");
            // Simulate a backup file with an old versioned src path
            var backupFile = MakeFile(@"app.dll", "app.dll", @"C:\OldRoot\Backup_MyProj\Backup_1.0");
            meta.BackupFiles["H"] = backupFile;

            meta.SetBackupFilesPath();

            Assert.Equal(@"C:\NewRoot\Backup_MyProj\Backup_1.0", backupFile.DataSrcPath);
        }
    }
}
