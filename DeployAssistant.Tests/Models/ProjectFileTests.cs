#pragma warning disable CS0618  // V1 types used intentionally for V1 model tests

using DeployAssistant.DataComponent;
using DeployAssistant.Interfaces;
using DeployAssistant.Model;
using System;
using System.Collections.Generic;
using Xunit;

namespace DeployAssistant.Tests.Models
{
    public class ProjectFileTests
    {
        #region Constructor Tests

        [Fact]
        public void Constructor_JsonConstructor_SetsAllProperties()
        {
            var now = new DateTime(2024, 1, 15, 10, 0, 0, DateTimeKind.Local);
            var file = new ProjectFile(
                DataType: ProjectDataType.File,
                DataSize: 1024,
                BuildVersion: "1.0.0",
                DeployedProjectVersion: "2.0.0",
                UpdatedTime: now,
                DataState: DataState.Modified,
                dataName: "test.dll",
                dataSrcPath: @"C:\Project",
                dataRelPath: @"bin\test.dll",
                dataHash: "ABCDEF123456",
                IsDstFile: true
            );

            Assert.Equal(ProjectDataType.File, file.DataType);
            Assert.Equal(1024, file.DataSize);
            Assert.Equal("1.0.0", file.BuildVersion);
            Assert.Equal("2.0.0", file.DeployedProjectVersion);
            Assert.Equal(now, file.UpdatedTime);
            Assert.Equal(DataState.Modified, file.DataState);
            Assert.Equal("test.dll", file.DataName);
            Assert.Equal(@"C:\Project", file.DataSrcPath);
            Assert.Equal(@"bin\test.dll", file.DataRelPath);
            Assert.Equal("ABCDEF123456", file.DataHash);
            Assert.True(file.IsDstFile);
        }

        [Fact]
        public void Constructor_PreStagedFile_SetsFileTypeDefaults()
        {
            var file = new ProjectFile(
                DataSize: 512,
                BuildVersion: "1.2.3",
                DataName: "app.exe",
                DataSrcPath: @"C:\Source",
                DataRelPath: @"app.exe"
            );

            Assert.Equal(ProjectDataType.File, file.DataType);
            Assert.Equal(DataState.PreStaged, file.DataState);
            Assert.Equal(512, file.DataSize);
            Assert.Equal("1.2.3", file.BuildVersion);
            Assert.Equal("app.exe", file.DataName);
            Assert.Equal(@"C:\Source", file.DataSrcPath);
            Assert.Equal(@"app.exe", file.DataRelPath);
            Assert.Equal("", file.DataHash);
            Assert.Equal("", file.DeployedProjectVersion);
            Assert.Equal(DateTime.MinValue, file.UpdatedTime);
        }

        [Fact]
        public void Constructor_PreStagedDirectory_SetsDirectoryTypeDefaults()
        {
            var file = new ProjectFile(
                DataName: "subdir",
                DataSrcPath: @"C:\Source",
                DataRelPath: @"subdir"
            );

            Assert.Equal(ProjectDataType.Directory, file.DataType);
            Assert.Equal(DataState.PreStaged, file.DataState);
            Assert.Equal(0, file.DataSize);
            Assert.Equal("", file.BuildVersion);
            Assert.Equal("subdir", file.DataName);
            Assert.Equal(@"C:\Source", file.DataSrcPath);
            Assert.Equal(@"subdir", file.DataRelPath);
        }

        [Fact]
        public void Constructor_DeepCopy_CopiesAllProperties()
        {
            var original = new ProjectFile(
                DataType: ProjectDataType.File,
                DataSize: 2048,
                BuildVersion: "3.0",
                DeployedProjectVersion: "4.0",
                UpdatedTime: new DateTime(2023, 6, 1),
                DataState: DataState.Added,
                dataName: "copy.dll",
                dataSrcPath: @"C:\Src",
                dataRelPath: @"lib\copy.dll",
                dataHash: "DEADBEEF",
                IsDstFile: false
            );

            var copy = new ProjectFile(original);

            Assert.Equal(original.DataType, copy.DataType);
            Assert.Equal(original.DataSize, copy.DataSize);
            Assert.Equal(original.BuildVersion, copy.BuildVersion);
            Assert.Equal(original.DeployedProjectVersion, copy.DeployedProjectVersion);
            Assert.Equal(original.UpdatedTime, copy.UpdatedTime);
            Assert.Equal(original.DataState, copy.DataState);
            Assert.Equal(original.DataName, copy.DataName);
            Assert.Equal(original.DataSrcPath, copy.DataSrcPath);
            Assert.Equal(original.DataRelPath, copy.DataRelPath);
            Assert.Equal(original.DataHash, copy.DataHash);
        }

        [Fact]
        public void Constructor_WithDeployedVersion_SetsDeployedVersionAndNewSrcPath()
        {
            var original = new ProjectFile(
                DataType: ProjectDataType.File,
                DataSize: 100,
                BuildVersion: "1.0",
                DeployedProjectVersion: "old",
                UpdatedTime: DateTime.MinValue,
                DataState: DataState.Added,
                dataName: "file.dll",
                dataSrcPath: @"C:\Old",
                dataRelPath: @"file.dll",
                dataHash: "HASH",
                IsDstFile: false
            );

            string newPath = @"C:\New";
            string newVersion = "2.0";
            var updated = new ProjectFile(original, newVersion, newPath);

            Assert.Equal(newVersion, updated.DeployedProjectVersion);
            Assert.Equal(newPath, updated.DataSrcPath);
            Assert.Equal(original.DataName, updated.DataName);
            Assert.Equal(original.DataRelPath, updated.DataRelPath);
        }

        [Fact]
        public void Constructor_WithState_OverridesDataState()
        {
            var original = new ProjectFile(
                DataType: ProjectDataType.File,
                DataSize: 100,
                BuildVersion: "1.0",
                DeployedProjectVersion: "1.0",
                UpdatedTime: DateTime.MinValue,
                DataState: DataState.Added,
                dataName: "f.dll",
                dataSrcPath: @"C:\S",
                dataRelPath: @"f.dll",
                dataHash: "H",
                IsDstFile: false
            );

            var withState = new ProjectFile(original, DataState.Deleted);

            Assert.Equal(DataState.Deleted, withState.DataState);
            Assert.Equal(original.DataName, withState.DataName);
        }

        [Fact]
        public void Constructor_WithStateAndSrcPath_OverridesStateAndSrcPath()
        {
            var original = new ProjectFile(
                DataType: ProjectDataType.File,
                DataSize: 100,
                BuildVersion: "1.0",
                DeployedProjectVersion: "1.0",
                UpdatedTime: DateTime.MinValue,
                DataState: DataState.Added,
                dataName: "g.dll",
                dataSrcPath: @"C:\Old",
                dataRelPath: @"g.dll",
                dataHash: "HH",
                IsDstFile: false
            );

            var newSrc = @"C:\NewSrc";
            var withPath = new ProjectFile(original, DataState.Backup, newSrc);

            Assert.Equal(DataState.Backup, withPath.DataState);
            Assert.Equal(newSrc, withPath.DataSrcPath);
        }

        [Fact]
        public void Constructor_WithDataTypeOnly_SetsEmptyDefaults()
        {
            var file = new ProjectFile(ProjectDataType.Directory);

            Assert.Equal(ProjectDataType.Directory, file.DataType);
            Assert.Equal(0, file.DataSize);
            Assert.Equal("", file.BuildVersion);
            Assert.Equal("", file.DeployedProjectVersion);
            Assert.Equal(DateTime.MaxValue, file.UpdatedTime);
            Assert.Equal(DataState.None, file.DataState);
            Assert.Equal("", file.DataName);
            Assert.Equal("", file.DataSrcPath);
            Assert.Equal("", file.DataRelPath);
            Assert.Equal("", file.DataHash);
        }

        [Fact]
        public void Constructor_WithSixParams_SetsPropertiesAndEmptyHash()
        {
            var file = new ProjectFile(
                fileSize: 300,
                fileVersion: "5.0",
                fileName: "module.dll",
                fileSrcPath: @"C:\Deploy",
                fileRelPath: @"module.dll",
                changedState: DataState.Modified
            );

            Assert.Equal(300, file.DataSize);
            Assert.Equal("5.0", file.BuildVersion);
            Assert.Equal("module.dll", file.DataName);
            Assert.Equal(@"C:\Deploy", file.DataSrcPath);
            Assert.Equal(@"module.dll", file.DataRelPath);
            Assert.Equal(DataState.Modified, file.DataState);
            Assert.Equal("", file.DataHash);
        }

        #endregion

        #region Computed Property Tests

        [Fact]
        public void DataAbsPath_ReturnsCorrectCombination()
        {
            var file = new ProjectFile(
                DataType: ProjectDataType.File,
                DataSize: 0,
                BuildVersion: "",
                DeployedProjectVersion: "",
                UpdatedTime: DateTime.Now,
                DataState: DataState.None,
                dataName: "a.dll",
                dataSrcPath: @"C:\Root",
                dataRelPath: @"sub\a.dll",
                dataHash: "",
                IsDstFile: false
            );

            Assert.Equal(@"C:\Root\sub\a.dll", file.DataAbsPath);
        }

        [Fact]
        public void DataRelDir_ForFile_ReturnsParentDirectory()
        {
            var file = new ProjectFile(
                DataType: ProjectDataType.File,
                DataSize: 0,
                BuildVersion: "",
                DeployedProjectVersion: "",
                UpdatedTime: DateTime.Now,
                DataState: DataState.None,
                dataName: "a.dll",
                dataSrcPath: @"C:\Root",
                dataRelPath: @"sub\dir\a.dll",
                dataHash: "",
                IsDstFile: false
            );

            Assert.Equal(@"sub\dir", file.DataRelDir);
        }

        [Fact]
        public void DataRelDir_ForDirectory_ReturnsRelPath()
        {
            var dir = new ProjectFile(
                DataType: ProjectDataType.Directory,
                DataSize: 0,
                BuildVersion: "",
                DeployedProjectVersion: "",
                UpdatedTime: DateTime.Now,
                DataState: DataState.None,
                dataName: "mydir",
                dataSrcPath: @"C:\Root",
                dataRelPath: @"mydir",
                dataHash: "",
                IsDstFile: false
            );

            Assert.Equal(@"mydir", dir.DataRelDir);
        }

        #endregion

        #region Comparison / Equality Tests

        [Fact]
        public void CompareTo_LaterTimestamp_ReturnsPositive()
        {
            var earlier = new ProjectFile(
                DataType: ProjectDataType.File, DataSize: 100,
                BuildVersion: "", DeployedProjectVersion: "",
                UpdatedTime: new DateTime(2024, 1, 1),
                DataState: DataState.None,
                dataName: "a.dll", dataSrcPath: "", dataRelPath: "a.dll",
                dataHash: "", IsDstFile: false);

            var later = new ProjectFile(
                DataType: ProjectDataType.File, DataSize: 100,
                BuildVersion: "", DeployedProjectVersion: "",
                UpdatedTime: new DateTime(2024, 6, 1),
                DataState: DataState.None,
                dataName: "b.dll", dataSrcPath: "", dataRelPath: "b.dll",
                dataHash: "", IsDstFile: false);

            Assert.True(later.CompareTo(earlier) > 0);
            Assert.True(earlier.CompareTo(later) < 0);
        }

        [Fact]
        public void CompareTo_SameTimestamp_FallsBackToDataSize()
        {
            var fixedTime = new DateTime(2024, 1, 1);

            var small = new ProjectFile(
                DataType: ProjectDataType.File, DataSize: 100,
                BuildVersion: "", DeployedProjectVersion: "",
                UpdatedTime: fixedTime,
                DataState: DataState.None,
                dataName: "a.dll", dataSrcPath: "", dataRelPath: "a.dll",
                dataHash: "", IsDstFile: false);

            var large = new ProjectFile(
                DataType: ProjectDataType.File, DataSize: 9999,
                BuildVersion: "", DeployedProjectVersion: "",
                UpdatedTime: fixedTime,
                DataState: DataState.None,
                dataName: "b.dll", dataSrcPath: "", dataRelPath: "b.dll",
                dataHash: "", IsDstFile: false);

            Assert.True(large.CompareTo(small) > 0);
        }

        [Fact]
        public void Equals_SameDataName_ReturnsTrue()
        {
            var a = new ProjectFile(
                DataType: ProjectDataType.File, DataSize: 1,
                BuildVersion: "", DeployedProjectVersion: "",
                UpdatedTime: DateTime.Now,
                DataState: DataState.None,
                dataName: "same.dll", dataSrcPath: @"C:\A", dataRelPath: "same.dll",
                dataHash: "", IsDstFile: false);

            var b = new ProjectFile(
                DataType: ProjectDataType.File, DataSize: 2,
                BuildVersion: "", DeployedProjectVersion: "",
                UpdatedTime: DateTime.Now,
                DataState: DataState.None,
                dataName: "same.dll", dataSrcPath: @"C:\B", dataRelPath: "same.dll",
                dataHash: "", IsDstFile: false);

            Assert.True(a.Equals(b));
        }

        [Fact]
        public void Equals_DifferentDataName_ReturnsFalse()
        {
            var a = new ProjectFile(
                DataType: ProjectDataType.File, DataSize: 1,
                BuildVersion: "", DeployedProjectVersion: "",
                UpdatedTime: DateTime.Now,
                DataState: DataState.None,
                dataName: "alpha.dll", dataSrcPath: "", dataRelPath: "alpha.dll",
                dataHash: "", IsDstFile: false);

            var b = new ProjectFile(
                DataType: ProjectDataType.File, DataSize: 1,
                BuildVersion: "", DeployedProjectVersion: "",
                UpdatedTime: DateTime.Now,
                DataState: DataState.None,
                dataName: "beta.dll", dataSrcPath: "", dataRelPath: "beta.dll",
                dataHash: "", IsDstFile: false);

            Assert.False(a.Equals(b));
        }

        #endregion
    }

    public class ProjectFileTreeNodeTests
    {
        private static ProjectFile File(string relPath, long size = 0, string version = "", string hash = "")
        {
            var f = new ProjectFile(size, version, System.IO.Path.GetFileName(relPath), @"C:\P", relPath);
            f.DataHash = hash;
            return f;
        }

        private static ProjectFile Dir(string relPath)
            => new ProjectFile(System.IO.Path.GetFileName(relPath), @"C:\P", relPath);

        [Fact]
        public void Build_NestsFilesByFolder_FoldersFirstAlphabetical()
        {
            var roots = ProjectFileTreeNode.Build(new[]
            {
                File(@"bin\x64\a.dll"),
                File(@"bin\b.dll"),
                File("zroot.exe"),
            });

            Assert.Equal(2, roots.Count);
            Assert.Equal("bin", roots[0].Name);
            Assert.True(roots[0].IsDirectory);
            Assert.Equal("zroot.exe", roots[1].Name);

            var bin = roots[0];
            Assert.Equal("x64", bin.Children[0].Name);
            Assert.Equal("b.dll", bin.Children[1].Name);
            Assert.Equal("a.dll", bin.Children[0].Children[0].Name);
        }

        [Fact]
        public void Build_ExplicitDirectoryEntries_BecomeFolderNodesNotLeaves()
        {
            var roots = ProjectFileTreeNode.Build(new[]
            {
                Dir("bin"),
                File(@"bin\a.dll"),
                Dir("empty"),
            });

            Assert.Equal(2, roots.Count);
            Assert.True(roots[0].IsDirectory);
            Assert.Single(roots[0].Children);
            Assert.True(roots[1].IsDirectory);
            Assert.Empty(roots[1].Children);
        }

        [Fact]
        public void Build_AggregatesRecursiveFileCountAndSize()
        {
            var roots = ProjectFileTreeNode.Build(new[]
            {
                File(@"bin\x64\a.dll", size: 100),
                File(@"bin\b.dll", size: 50),
            });

            var bin = roots[0];
            Assert.Equal(2, bin.FileCount);
            Assert.Equal(150, bin.TotalSize);
            Assert.Equal(1, bin.Children[0].FileCount);
            Assert.Equal(100, bin.Children[0].TotalSize);
        }

        [Fact]
        public void SizeDisplay_FormatsHumanReadable()
        {
            Assert.Equal("532 B", ProjectFileTreeNode.FormatSize(532));
            Assert.Equal("1.5 KB", ProjectFileTreeNode.FormatSize(1536));
            Assert.Equal("2.3 MB", ProjectFileTreeNode.FormatSize(2_400_000));
            Assert.Equal("5.0 GB", ProjectFileTreeNode.FormatSize(5_400_000_000));
        }

        [Fact]
        public void HighlightMatchingHash_MarksOnlyFilesWithSameHash()
        {
            var roots = ProjectFileTreeNode.Build(new[]
            {
                File(@"bin\a.dll", hash: "AAA"),
                File(@"bin\b.dll", hash: "BBB"),
                File("c.dll", hash: "AAA"),
            });

            int marked = ProjectFileTreeNode.HighlightMatchingHash(roots, "AAA");

            Assert.Equal(2, marked);
            var bin = roots[0];
            Assert.True(bin.Children[0].IsHighlighted);
            Assert.False(bin.Children[1].IsHighlighted);
            Assert.True(roots[1].IsHighlighted);
            Assert.False(bin.IsHighlighted);
        }

        [Fact]
        public void HighlightMatchingHash_NullOrEmptyHash_ClearsAll()
        {
            var roots = ProjectFileTreeNode.Build(new[]
            {
                File(@"bin\a.dll", hash: "AAA"),
                File("c.dll", hash: ""),
            });
            ProjectFileTreeNode.HighlightMatchingHash(roots, "AAA");

            int marked = ProjectFileTreeNode.HighlightMatchingHash(roots, null);

            Assert.Equal(0, marked);
            Assert.False(roots[0].Children[0].IsHighlighted);
            Assert.False(roots[1].IsHighlighted);

            Assert.Equal(0, ProjectFileTreeNode.HighlightMatchingHash(roots, ""));
        }

        [Fact]
        public void HighlightMatchingHash_ExpandsAncestorFoldersOfMatches()
        {
            var roots = ProjectFileTreeNode.Build(new[]
            {
                File(@"bin\x64\a.dll", hash: "AAA"),
                File(@"other\b.dll", hash: "BBB"),
            });

            ProjectFileTreeNode.HighlightMatchingHash(roots, "AAA");

            var bin = roots[0];
            var other = roots[1];
            Assert.True(bin.IsExpanded);
            Assert.True(bin.Children[0].IsExpanded);
            Assert.False(other.IsExpanded);
        }

        [Fact]
        public void HighlightMatchingHash_DoesNotCollapseAlreadyExpandedFolders()
        {
            var roots = ProjectFileTreeNode.Build(new[]
            {
                File(@"bin\a.dll", hash: "AAA"),
                File(@"other\b.dll", hash: "BBB"),
            });
            ProjectFileTreeNode.HighlightMatchingHash(roots, "AAA");

            ProjectFileTreeNode.HighlightMatchingHash(roots, "BBB");

            Assert.True(roots[0].IsExpanded);
            Assert.True(roots[1].IsExpanded);
        }

        [Fact]
        public void IsHighlighted_RaisesPropertyChanged()
        {
            var roots = ProjectFileTreeNode.Build(new[] { File("a.dll", hash: "AAA") });
            var changed = new List<string?>();
            roots[0].PropertyChanged += (_, e) => changed.Add(e.PropertyName);

            ProjectFileTreeNode.HighlightMatchingHash(roots, "AAA");
            ProjectFileTreeNode.HighlightMatchingHash(roots, "AAA");

            Assert.Equal(new[] { nameof(ProjectFileTreeNode.IsHighlighted) }, changed);
        }

        [Fact]
        public void Build_LeafExposesUnderlyingFile()
        {
            var roots = ProjectFileTreeNode.Build(new[] { File(@"bin\a.dll", size: 10, version: "1.2.3", hash: "ABC") });

            var leaf = roots[0].Children[0];
            Assert.False(leaf.IsDirectory);
            Assert.Equal("1.2.3", leaf.File!.BuildVersion);
            Assert.Equal("ABC", leaf.File.DataHash);
            Assert.Equal("10 B", leaf.SizeDisplay);
        }
    }
}
