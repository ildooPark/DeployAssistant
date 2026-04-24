#pragma warning disable CS0618  // V1 types used intentionally for V1 model tests

using DeployAssistant.DataComponent;
using DeployAssistant.Interfaces;
using DeployAssistant.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace DeployAssistant.Tests.Models
{
    public class ProjectDataTests
    {
        // Helper to create a minimal ProjectFile
        private static ProjectFile MakeFile(string relPath, string name, ProjectDataType type = ProjectDataType.File, string srcPath = @"C:\Proj")
        {
            return new ProjectFile(
                DataType: type,
                DataSize: 100,
                BuildVersion: "1.0",
                DeployedProjectVersion: "1.0",
                UpdatedTime: DateTime.Now,
                DataState: DataState.None,
                dataName: name,
                dataSrcPath: srcPath,
                dataRelPath: relPath,
                dataHash: "HASH_" + name,
                IsDstFile: false);
        }

        [Fact]
        public void Constructor_WithProjectPath_InitializesEmptyCollections()
        {
            var data = new ProjectData(@"C:\Project");

            Assert.Equal(@"C:\Project", data.ProjectPath);
            Assert.Equal(0, data.RevisionNumber);
            Assert.NotNull(data.ProjectFiles);
            Assert.Empty(data.ProjectFiles);
            Assert.NotNull(data.ChangedFiles);
            Assert.Empty(data.ChangedFiles);
        }

        [Fact]
        public void Constructor_JsonConstructor_SetsAllProperties()
        {
            var now = new DateTime(2024, 3, 10);
            var projectFiles = new Dictionary<string, ProjectFile>
            {
                { @"bin\app.dll", MakeFile(@"bin\app.dll", "app.dll") }
            };
            var changedFiles = new List<ChangedFile>();

            var data = new ProjectData(
                ProjectName: "TestProj",
                ProjectPath: @"C:\TestProj",
                UpdaterName: "Alice",
                ConductedPC: "PC01",
                UpdatedTime: now,
                UpdatedVersion: "3.0",
                UpdateLog: "Initial",
                ChangeLog: "No changes",
                RevisionNumber: 5,
                NumberOfChanges: 1,
                ChangedFiles: changedFiles,
                ProjectFiles: projectFiles
            );

            Assert.Equal("TestProj", data.ProjectName);
            Assert.Equal(@"C:\TestProj", data.ProjectPath);
            Assert.Equal("Alice", data.UpdaterName);
            Assert.Equal("PC01", data.ConductedPC);
            Assert.Equal(now, data.UpdatedTime);
            Assert.Equal("3.0", data.UpdatedVersion);
            Assert.Equal("Initial", data.UpdateLog);
            Assert.Equal("No changes", data.ChangeLog);
            Assert.Equal(5, data.RevisionNumber);
            Assert.Equal(1, data.NumberOfChanges);
        }

        [Fact]
        public void Constructor_DeepCopy_ClonesFilesIndependently()
        {
            var src = new ProjectData(@"C:\Proj");
            src.ProjectName = "Proj";
            src.UpdaterName = "Bob";
            src.UpdatedVersion = "1.0";
            src.ConductedPC = "PC";
            src.UpdateLog = "log";
            src.ChangeLog = "cl";
            src.ProjectFiles[@"a.dll"] = MakeFile(@"a.dll", "a.dll");

            var copy = new ProjectData(src);

            Assert.Equal(src.ProjectName, copy.ProjectName);
            Assert.Equal(src.UpdaterName, copy.UpdaterName);
            Assert.Equal(src.UpdatedVersion, copy.UpdatedVersion);
            Assert.Single(copy.ProjectFiles);

            // Mutating the copy should not affect the original
            copy.ProjectFiles[@"b.dll"] = MakeFile(@"b.dll", "b.dll");
            Assert.Single(src.ProjectFiles);
        }

        [Fact]
        public void Constructor_DeepCopyIsReverting_ClonesChangedFiles()
        {
            var src = new ProjectData(@"C:\Proj");
            src.ProjectName = "P";
            src.UpdaterName = "C";
            src.UpdatedVersion = "1";
            src.ConductedPC = "PC";
            src.UpdateLog = "l";
            src.ChangeLog = "cl";
            var srcFile = MakeFile(@"a.dll", "a.dll");
            src.ChangedFiles.Add(new ChangedFile(srcFile, DataState.Added));

            var reverting = new ProjectData(src, IsReverting: true);
            var notReverting = new ProjectData(src, IsReverting: false);

            Assert.Single(reverting.ChangedFiles);
            Assert.Empty(notReverting.ChangedFiles);
        }

        [Fact]
        public void Equals_SameVersion_ReturnsTrue()
        {
            var a = new ProjectData(@"C:\A");
            a.UpdatedVersion = "2.5";
            var b = new ProjectData(@"C:\B");
            b.UpdatedVersion = "2.5";

            Assert.True(a.Equals(b));
        }

        [Fact]
        public void Equals_DifferentVersion_ReturnsFalse()
        {
            var a = new ProjectData(@"C:\A");
            a.UpdatedVersion = "1.0";
            var b = new ProjectData(@"C:\B");
            b.UpdatedVersion = "2.0";

            Assert.False(a.Equals(b));
        }

        [Fact]
        public void Equals_Null_ReturnsFalse()
        {
            var a = new ProjectData(@"C:\A");
            a.UpdatedVersion = "1.0";

            Assert.False(a.Equals(null));
        }

        [Fact]
        public void Compare_OrdersByUpdatedTime()
        {
            var data = new ProjectData(@"C:\P");
            var earlier = new ProjectData(@"C:\A");
            earlier.UpdatedTime = new DateTime(2023, 1, 1);
            var later = new ProjectData(@"C:\B");
            later.UpdatedTime = new DateTime(2024, 1, 1);

            Assert.True(data.Compare(earlier, later) < 0);
            Assert.True(data.Compare(later, earlier) > 0);
            Assert.Equal(0, data.Compare(earlier, earlier));
        }

        [Fact]
        public void CompareTo_ComparesByUpdatedTime()
        {
            var earlier = new ProjectData(@"C:\A");
            earlier.UpdatedTime = new DateTime(2023, 1, 1);
            var later = new ProjectData(@"C:\B");
            later.UpdatedTime = new DateTime(2024, 1, 1);

            Assert.True(earlier.CompareTo(later) < 0);
            Assert.True(later.CompareTo(earlier) > 0);
        }

        [Fact]
        public void ProjectRelDirsList_ReturnsOnlyNonRootDirectories()
        {
            var data = new ProjectData(@"C:\Proj");
            data.ProjectFiles[@"subdir"] = MakeFile(@"subdir", "subdir", ProjectDataType.Directory);
            data.ProjectFiles[@""] = MakeFile(@"", "", ProjectDataType.Directory); // root directory (excluded)
            data.ProjectFiles[@"a.dll"] = MakeFile(@"a.dll", "a.dll", ProjectDataType.File);

            var dirs = data.ProjectRelDirsList;

            Assert.Single(dirs);
            Assert.Contains(@"subdir", dirs);
        }

        [Fact]
        public void ProjectRelFilePathsList_ReturnsOnlyFiles()
        {
            var data = new ProjectData(@"C:\Proj");
            data.ProjectFiles[@"a.dll"] = MakeFile(@"a.dll", "a.dll", ProjectDataType.File);
            data.ProjectFiles[@"b.dll"] = MakeFile(@"b.dll", "b.dll", ProjectDataType.File);
            data.ProjectFiles[@"subdir"] = MakeFile(@"subdir", "subdir", ProjectDataType.Directory);

            var files = data.ProjectRelFilePathsList;

            Assert.Equal(2, files.Count);
            Assert.Contains(@"a.dll", files);
            Assert.Contains(@"b.dll", files);
        }

        [Fact]
        public void ChangedDstFileList_ReturnsNonNullDstFiles()
        {
            var data = new ProjectData(@"C:\Proj");
            var dst1 = MakeFile(@"a.dll", "a.dll");
            var dst2 = MakeFile(@"b.dll", "b.dll");

            data.ChangedFiles.Add(new ChangedFile(dst1, DataState.Added));
            data.ChangedFiles.Add(new ChangedFile(dst2, DataState.Modified));
            // ChangedFile with null DstFile
            data.ChangedFiles.Add(new ChangedFile(null!, null!, DataState.None));

            var dstList = data.ChangedDstFileList;

            Assert.Equal(2, dstList.Count);
        }

        [Fact]
        public void ProjectFilesDict_NameSorted_GroupsByFileName()
        {
            var data = new ProjectData(@"C:\Proj");
            data.ProjectFiles[@"dir1\util.dll"] = MakeFile(@"dir1\util.dll", "util.dll");
            data.ProjectFiles[@"dir2\util.dll"] = MakeFile(@"dir2\util.dll", "util.dll");
            data.ProjectFiles[@"app.exe"] = MakeFile(@"app.exe", "app.exe");

            var sorted = data.ProjectFilesDict_NameSorted;

            Assert.Equal(2, sorted.Count);
            Assert.Equal(2, sorted["util.dll"].Count);
            Assert.Single(sorted["app.exe"]);
        }

        [Fact]
        public void ProjectFilesDict_RelDirSorted_GroupsByRelativeDirectory()
        {
            var data = new ProjectData(@"C:\Proj");
            data.ProjectFiles[@"lib\a.dll"] = MakeFile(@"lib\a.dll", "a.dll");
            data.ProjectFiles[@"lib\b.dll"] = MakeFile(@"lib\b.dll", "b.dll");
            data.ProjectFiles[@"app.exe"] = MakeFile(@"app.exe", "app.exe");

            var sorted = data.ProjectFilesDict_RelDirSorted;

            Assert.True(sorted.ContainsKey(@"lib"));
            Assert.Equal(2, sorted[@"lib"].Count);
        }

        [Fact]
        public void RegisterProjectInfo_PopulatesDictionaryWithAllFields()
        {
            var data = new ProjectData(@"C:\Proj");
            data.ProjectName = "MyProj";
            data.UpdaterName = "Carol";
            data.ConductedPC = "PC02";
            data.UpdatedTime = new DateTime(2024, 5, 1);
            data.UpdatedVersion = "7.0";
            data.NumberOfChanges = 3;

            var dict = new Dictionary<string, object>();
            data.RegisterProjectInfo(dict);

            Assert.True(dict.ContainsKey("ProjectName"));
            Assert.True(dict.ContainsKey("ProjectPath"));
            Assert.True(dict.ContainsKey("UpdaterName"));
            Assert.True(dict.ContainsKey("ConductedPC"));
            Assert.True(dict.ContainsKey("UpdatedTime"));
            Assert.True(dict.ContainsKey("UpdatedVersion"));
            Assert.True(dict.ContainsKey("NumberOfChanges"));
            Assert.Equal("MyProj", dict["ProjectName"]);
            Assert.Equal(3, dict["NumberOfChanges"]);
        }

        [Fact]
        public void SetProjectFilesSrcPath_UpdatesAllFilesToProjectPath()
        {
            var data = new ProjectData(@"C:\NewPath");
            data.ProjectFiles[@"a.dll"] = MakeFile(@"a.dll", "a.dll", ProjectDataType.File, @"C:\OldPath");
            data.ProjectFiles[@"b.dll"] = MakeFile(@"b.dll", "b.dll", ProjectDataType.File, @"C:\OldPath");

            data.SetProjectFilesSrcPath();

            foreach (var file in data.ProjectFiles.Values)
            {
                Assert.Equal(@"C:\NewPath", file.DataSrcPath);
            }
        }

        [Fact]
        public void Constructor_WithNewDeployParams_IncrementsRevisionAndSetsFields()
        {
            var src = new ProjectData(@"C:\Proj");
            src.ProjectName = "P";
            src.UpdaterName = "A";
            src.UpdatedVersion = "1.0";
            src.ConductedPC = "PC";
            src.UpdateLog = "old log";
            src.ChangeLog = "old cl";
            src.RevisionNumber = 2;
            src.ProjectFiles[@"a.dll"] = MakeFile(@"a.dll", "a.dll");
            var changedFile = MakeFile(@"b.dll", "b.dll");
            src.ChangedFiles.Add(new ChangedFile(changedFile, DataState.Added));

            var newData = new ProjectData(
                srcProjectData: src,
                projectPath: @"C:\NewProj",
                updaterName: "Bob",
                updateTime: new DateTime(2024, 8, 1),
                updatedVersion: "2.0",
                conductedPC: "PC99",
                updateLog: "new log",
                changeLog: "new cl"
            );

            Assert.Equal(3, newData.RevisionNumber); // incremented from 2
            Assert.Equal(@"C:\NewProj", newData.ProjectPath);
            Assert.Equal("Bob", newData.UpdaterName);
            Assert.Equal("2.0", newData.UpdatedVersion);
            Assert.Equal("PC99", newData.ConductedPC);
            Assert.Equal("new log", newData.UpdateLog);
            Assert.Equal("new cl", newData.ChangeLog);
            Assert.Equal(1, newData.NumberOfChanges);
        }
    }
}
