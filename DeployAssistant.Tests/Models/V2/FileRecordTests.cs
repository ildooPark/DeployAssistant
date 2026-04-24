using DeployAssistant.Model.V2;
using System;
using System.IO;
using Xunit;

namespace DeployAssistant.Tests.Models.V2
{
    /// <summary>
    /// Unit tests for the V2 <see cref="FileRecord"/> model.
    /// </summary>
    public class FileRecordTests
    {
        // ------------------------------------------------------------------ helpers

        private static FileRecord MakeRecord(
            FileKind kind   = FileKind.File,
            string relPath  = "app.dll",
            string name     = "app.dll",
            string srcPath  = @"C:\Proj",
            long   size     = 512,
            string hash     = "DEADBEEF",
            StagingFlags flags = StagingFlags.None)
        {
            return new FileRecord(kind, relPath, name, srcPath, size, hash, "1.0", "1.0", DateTime.Now, flags);
        }

        // ------------------------------------------------------------------ constructors

        [Fact]
        public void DefaultConstructor_StringPropertiesAreEmpty()
        {
            var rec = new FileRecord();

            Assert.Equal(string.Empty, rec.RelPath);
            Assert.Equal(string.Empty, rec.Name);
            Assert.Equal(string.Empty, rec.SrcPath);
            Assert.Equal(string.Empty, rec.Hash);
            Assert.Equal(string.Empty, rec.BuildVersion);
        }

        [Fact]
        public void FullConstructor_SetsAllProperties()
        {
            var now = DateTime.UtcNow;
            var rec = new FileRecord(
                FileKind.File, @"lib\a.dll", "a.dll",
                @"C:\Proj", 1024, "CAFEBABE",
                "2.0", "1.0", now, StagingFlags.PreStaged);

            Assert.Equal(FileKind.File,          rec.Kind);
            Assert.Equal(@"lib\a.dll",           rec.RelPath);
            Assert.Equal("a.dll",                rec.Name);
            Assert.Equal(@"C:\Proj",             rec.SrcPath);
            Assert.Equal(1024,                   rec.Size);
            Assert.Equal("CAFEBABE",             rec.Hash);
            Assert.Equal("2.0",                  rec.BuildVersion);
            Assert.Equal("1.0",                  rec.SnapshotVersion);
            Assert.Equal(now,                    rec.RecordedAt);
            Assert.Equal(StagingFlags.PreStaged, rec.Flags);
        }

        [Fact]
        public void CopyConstructor_PreservesAllFields()
        {
            var original = MakeRecord(kind: FileKind.Directory, relPath: "lib", name: "lib", hash: "A1B2");
            var copy     = new FileRecord(original);

            Assert.Equal(original.Kind,    copy.Kind);
            Assert.Equal(original.RelPath, copy.RelPath);
            Assert.Equal(original.Name,    copy.Name);
            Assert.Equal(original.Hash,    copy.Hash);
            Assert.Equal(original.Size,    copy.Size);
        }

        // ------------------------------------------------------------------ computed properties

        [Fact]
        public void AbsPath_CombinesSrcPathAndRelPath()
        {
            var rec = MakeRecord(srcPath: @"C:\Root", relPath: @"sub\app.dll");

            Assert.Equal(Path.Combine(@"C:\Root", @"sub\app.dll"), rec.AbsPath);
        }

        [Fact]
        public void RelDir_ForFile_ReturnsDirectoryPart()
        {
            var rec = MakeRecord(kind: FileKind.File, relPath: @"lib\app.dll");

            Assert.Equal("lib", rec.RelDir);
        }

        [Fact]
        public void RelDir_ForRootFile_ReturnsEmpty()
        {
            var rec = MakeRecord(kind: FileKind.File, relPath: "app.dll");

            Assert.Equal(string.Empty, rec.RelDir);
        }

        [Fact]
        public void RelDir_ForDirectory_ReturnsRelPath()
        {
            var rec = MakeRecord(kind: FileKind.Directory, relPath: @"lib\sub");

            Assert.Equal(@"lib\sub", rec.RelDir);
        }

        // ------------------------------------------------------------------ mutability

        [Fact]
        public void SrcPath_CanBeMutated()
        {
            var rec = MakeRecord(srcPath: @"C:\Old");
            rec.SrcPath = @"C:\New";

            Assert.Equal(@"C:\New", rec.SrcPath);
        }

        [Fact]
        public void SnapshotVersion_CanBeMutated()
        {
            var rec = MakeRecord();
            rec.SnapshotVersion = "2.0";

            Assert.Equal("2.0", rec.SnapshotVersion);
        }

        [Fact]
        public void Flags_CanBeUpdated()
        {
            var rec = MakeRecord(flags: StagingFlags.None);
            rec.Flags = StagingFlags.PreStaged | StagingFlags.Overlapped;

            Assert.True((rec.Flags & StagingFlags.PreStaged)  != 0);
            Assert.True((rec.Flags & StagingFlags.Overlapped) != 0);
        }

        // ------------------------------------------------------------------ FileKind and StagingFlags enums

        [Fact]
        public void FileKind_FileAndDirectory_AreDistinct()
        {
            Assert.NotEqual(FileKind.File, FileKind.Directory);
        }

        [Fact]
        public void StagingFlags_NoneIsZero()
        {
            Assert.Equal(0, (int)StagingFlags.None);
        }

        [Fact]
        public void StagingFlags_PreStagedAndOverlapped_AreDifferentBits()
        {
            Assert.Equal(0, (int)(StagingFlags.PreStaged & StagingFlags.Overlapped));
        }
    }
}
