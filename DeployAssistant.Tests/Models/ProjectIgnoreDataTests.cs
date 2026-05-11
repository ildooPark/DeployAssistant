#pragma warning disable CS0618  // V1 types used intentionally for V1 model tests

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

        // FilterChangedFileList tests removed — predicate-based filtering moved to
        // DeployAssistant.Filtering.IgnoreFilter, exhaustively tested in IgnoreFilterTests.

        [Fact]
        public void EnsureDefaultFlags_AddsIntegrationFlagToLegacyDeployBackupAndExport()
        {
            // Simulate a .ignore file that was persisted before the Integration flag
            // was added to these three default entries.  EnsureDefaultFlags must
            // self-heal them up to the current default-flag set.
            var legacy = new ProjectIgnoreData("MyProj");
            legacy.IgnoreFileList.Clear();
            legacy.IgnoreFileList.Add(new RecordedFile("*.deploy", ProjectDataType.File,
                IgnoreType.Deploy | IgnoreType.Initialization));
            legacy.IgnoreFileList.Add(new RecordedFile("Backup_MyProj", ProjectDataType.Directory,
                IgnoreType.IntegrityCheck | IgnoreType.Initialization));
            legacy.IgnoreFileList.Add(new RecordedFile("Export_MyProj", ProjectDataType.Directory,
                IgnoreType.IntegrityCheck | IgnoreType.Initialization));

            bool changed = legacy.EnsureDefaultFlags();

            Assert.True(changed);
            var deploy = legacy.IgnoreFileList.First(e => e.DataName == "*.deploy");
            var backup = legacy.IgnoreFileList.First(e => e.DataName == "Backup_MyProj");
            var export = legacy.IgnoreFileList.First(e => e.DataName == "Export_MyProj");
            Assert.True((deploy.IgnoreType & IgnoreType.Integration) != 0,
                "*.deploy must carry Integration after EnsureDefaultFlags");
            Assert.True((backup.IgnoreType & IgnoreType.Integration) != 0,
                "Backup_<Name> must carry Integration after EnsureDefaultFlags");
            Assert.True((export.IgnoreType & IgnoreType.Integration) != 0,
                "Export_<Name> must carry Integration after EnsureDefaultFlags");

            // Original flags must be preserved (heal is additive)
            Assert.True((deploy.IgnoreType & IgnoreType.Deploy) != 0);
            Assert.True((backup.IgnoreType & IgnoreType.IntegrityCheck) != 0);
        }

        [Fact]
        public void EnsureDefaultFlags_AlreadyCurrent_ReturnsFalse()
        {
            // A freshly constructed ProjectIgnoreData with ConfigureDefaultIgnore
            // already carries the current flag set.  Heal must be idempotent —
            // return false so the caller does not pointlessly re-serialize.
            var fresh = new ProjectIgnoreData("MyProj");
            fresh.ConfigureDefaultIgnore("MyProj");

            bool changed = fresh.EnsureDefaultFlags();

            Assert.False(changed);
        }

        [Fact]
        public void EnsureDefaultFlags_DoesNotTouchUserCustomEntries()
        {
            // Heal targets only well-known default entries by name.  A user-added
            // custom entry must be left exactly as-is, regardless of its flags.
            var data = new ProjectIgnoreData("MyProj");
            data.IgnoreFileList.Add(new RecordedFile("custom.tool", ProjectDataType.File, IgnoreType.Deploy));

            data.EnsureDefaultFlags();

            var custom = data.IgnoreFileList.First(e => e.DataName == "custom.tool");
            Assert.Equal(IgnoreType.Deploy, custom.IgnoreType);
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
