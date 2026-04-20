using DeployAssistant.Model;
using DeployAssistant.DataComponent;
using DeployAssistant.Interfaces;
using System;
using System.Collections.Generic;
using Xunit;

namespace DeployAssistant.Tests.Models
{
    public class ProjectIgnoreDataTests
    {
        private static ProjectFile MakeFile(string relPath, string name, ProjectDataType type = ProjectDataType.File, string srcPath = @"C:\Proj")
        {
            return new ProjectFile(
                DataType: type,
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

        [Fact]
        public void Constructor_WithProjectName_InitializesDefaultIgnoreList()
        {
            var ignore = new ProjectIgnoreData("MyProj");

            Assert.Equal("MyProj", ignore.ProjectName);
            Assert.NotNull(ignore.IgnoreFileList);
            Assert.NotEmpty(ignore.IgnoreFileList);

            // Verify well-known default entries exist
            var names = new HashSet<string>();
            foreach (var f in ignore.IgnoreFileList) names.Add(f.DataName);

            Assert.Contains("ProjectMetaData.bin", names);
            Assert.Contains("*.ignore", names);
            Assert.Contains("*.deploy", names);
            Assert.Contains("*.VersionLog", names);
            Assert.Contains("Export_XLSX", names);
        }

        [Fact]
        public void ConfigureDefaultIgnore_AddsBackupAndExportDirectories()
        {
            var ignore = new ProjectIgnoreData("MyProj");
            int countBefore = ignore.IgnoreFileList.Count;

            ignore.ConfigureDefaultIgnore("MyProj");

            Assert.Equal(countBefore + 2, ignore.IgnoreFileList.Count);

            var names = new HashSet<string>();
            foreach (var f in ignore.IgnoreFileList) names.Add(f.DataName);

            Assert.Contains("Backup_MyProj", names);
            Assert.Contains("Export_MyProj", names);
        }

        [Fact]
        public void FilterChangedFileList_ExcludesIgnoredFilesByName()
        {
            var ignore = new ProjectIgnoreData("Proj");
            // "ProjectMetaData.bin" is a default ignore entry
            var ignoredFile = MakeFile(@"ProjectMetaData.bin", "ProjectMetaData.bin");
            var normalFile = MakeFile(@"app.dll", "app.dll");

            var changes = new List<ChangedFile>
            {
                new ChangedFile(ignoredFile, DataState.Added),
                new ChangedFile(normalFile, DataState.Modified)
            };

            ignore.FilterChangedFileList(changes);

            // Only the non-ignored file should remain
            Assert.Single(changes);
            Assert.Equal("app.dll", changes[0].DstFile!.DataName);
        }

        [Fact]
        public void FilterChangedFileList_NoIgnoredFiles_LeavesListUnchanged()
        {
            var ignore = new ProjectIgnoreData("Proj");
            var file1 = MakeFile(@"logic.dll", "logic.dll");
            var file2 = MakeFile(@"core.dll", "core.dll");

            var changes = new List<ChangedFile>
            {
                new ChangedFile(file1, DataState.Added),
                new ChangedFile(file2, DataState.Modified)
            };

            ignore.FilterChangedFileList(changes);

            Assert.Equal(2, changes.Count);
        }

        [Fact]
        public void DefaultIgnoreList_ContainsCorrectIgnoreTypes()
        {
            var ignore = new ProjectIgnoreData("P");

            bool foundMetaDataBin = false;
            foreach (var f in ignore.IgnoreFileList)
            {
                if (f.DataName == "ProjectMetaData.bin")
                {
                    foundMetaDataBin = true;
                    Assert.Equal(IgnoreType.All, f.IgnoreType);
                    Assert.Equal(ProjectDataType.File, f.DataType);
                }
            }
            Assert.True(foundMetaDataBin, "Default ignore list should contain 'ProjectMetaData.bin'");
        }

        [Fact]
        public void DefaultIgnoreList_ExportXLSXIsDirectory()
        {
            var ignore = new ProjectIgnoreData("P");

            bool found = false;
            foreach (var f in ignore.IgnoreFileList)
            {
                if (f.DataName == "Export_XLSX")
                {
                    found = true;
                    Assert.Equal(ProjectDataType.Directory, f.DataType);
                }
            }
            Assert.True(found);
        }
    }
}
