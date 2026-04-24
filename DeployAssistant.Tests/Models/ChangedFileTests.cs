#pragma warning disable CS0618  // V1 types used intentionally for V1 model tests

using DeployAssistant.DataComponent;
using DeployAssistant.Interfaces;
using DeployAssistant.Model;
using System;
using Xunit;

namespace DeployAssistant.Tests.Models
{
    public class ChangedFileTests
    {
        private static ProjectFile MakeFile(string relPath, string name, string srcPath = @"C:\Proj")
        {
            return new ProjectFile(
                DataType: ProjectDataType.File,
                DataSize: 128,
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
        public void Constructor_WithDstFileOnly_SetsNullSrcFileAndCorrectDstFile()
        {
            var dst = MakeFile(@"app.dll", "app.dll");
            var changed = new ChangedFile(dst, DataState.Added);

            Assert.Null(changed.SrcFile);
            Assert.Same(dst, changed.DstFile);
            Assert.Equal(DataState.Added, changed.DataState);
        }

        [Fact]
        public void Constructor_NonOverlappedState_SetsDstFileIsDstFileTrue()
        {
            var dst = MakeFile(@"app.dll", "app.dll");
            var changed = new ChangedFile(dst, DataState.Added);

            Assert.True(changed.DstFile!.IsDstFile);
        }

        [Fact]
        public void Constructor_OverlappedState_SetsDstFileIsDstFileFalse()
        {
            var dst = MakeFile(@"app.dll", "app.dll");
            var changed = new ChangedFile(dst, DataState.Overlapped);

            Assert.False(changed.DstFile!.IsDstFile);
        }

        [Fact]
        public void Constructor_WithBothFiles_SetsSrcAndDstCorrectly()
        {
            var src = MakeFile(@"app.dll", "app.dll", @"C:\Backup");
            var dst = MakeFile(@"app.dll", "app.dll", @"C:\Proj");

            var changed = new ChangedFile(src, dst, DataState.Modified, RegisterChanges: true);

            Assert.Same(src, changed.SrcFile);
            Assert.Same(dst, changed.DstFile);
            Assert.Equal(DataState.Modified, changed.DataState);
            Assert.False(changed.SrcFile!.IsDstFile);
            Assert.True(changed.DstFile!.IsDstFile);
        }

        [Fact]
        public void Constructor_WithBothFilesOverlapped_SetsDstIsDstFileFalse()
        {
            var src = MakeFile(@"a.dll", "a.dll");
            var dst = MakeFile(@"a.dll", "a.dll");

            var changed = new ChangedFile(src, dst, DataState.Overlapped, RegisterChanges: true);

            Assert.False(changed.DstFile!.IsDstFile);
        }

        [Fact]
        public void Constructor_JsonConstructor_SetsAllThreeDirectly()
        {
            var src = MakeFile(@"x.dll", "x.dll");
            var dst = MakeFile(@"x.dll", "x.dll");

            var changed = new ChangedFile(src, dst, DataState.Restored);

            Assert.Same(src, changed.SrcFile);
            Assert.Same(dst, changed.DstFile);
            Assert.Equal(DataState.Restored, changed.DataState);
        }

        [Fact]
        public void Constructor_DeepCopy_WithBothFiles_ClonesIndependently()
        {
            var src = MakeFile(@"f.dll", "f.dll", @"C:\Src");
            var dst = MakeFile(@"f.dll", "f.dll", @"C:\Dst");
            var original = new ChangedFile(src, dst, DataState.Modified, RegisterChanges: true);

            var copy = new ChangedFile(original);

            Assert.Equal(original.DataState, copy.DataState);
            Assert.NotSame(original.SrcFile, copy.SrcFile);
            Assert.NotSame(original.DstFile, copy.DstFile);
            Assert.Equal(original.SrcFile!.DataName, copy.SrcFile!.DataName);
            Assert.Equal(original.DstFile!.DataName, copy.DstFile!.DataName);
        }

        [Fact]
        public void Constructor_DeepCopy_WithNullSrcFile_PreservesNullSrc()
        {
            var dst = MakeFile(@"g.dll", "g.dll");
            var original = new ChangedFile(dst, DataState.Added);
            var copy = new ChangedFile(original);

            Assert.Null(copy.SrcFile);
            Assert.NotNull(copy.DstFile);
            Assert.Equal(DataState.Added, copy.DataState);
        }

        [Fact]
        public void Constructor_DefaultEmpty_AllPropertiesAreDefault()
        {
            var changed = new ChangedFile();

            Assert.Null(changed.SrcFile);
            Assert.Null(changed.DstFile);
            Assert.Equal(DataState.None, changed.DataState);
        }
    }
}
