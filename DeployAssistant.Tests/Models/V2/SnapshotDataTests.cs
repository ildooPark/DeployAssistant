using DeployAssistant.Model.V2;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace DeployAssistant.Tests.Models.V2
{
    /// <summary>
    /// Unit tests for the V2 <see cref="SnapshotData"/> model.
    /// </summary>
    public class SnapshotDataTests
    {
        // ------------------------------------------------------------------ helpers

        private static FileRecord MakeFileRecord(
            string relPath = "app.dll",
            string name    = "app.dll",
            string hash    = "DEADBEEF",
            FileKind kind  = FileKind.File)
        {
            return new FileRecord(
                kind, relPath, name,
                @"C:\Proj", 256, hash,
                "1.0", "1.0", new DateTime(2024, 1, 1),
                StagingFlags.None);
        }

        // ------------------------------------------------------------------ constructors

        [Fact]
        public void DefaultConstructor_StringPropertiesAreEmpty()
        {
            var snap = new SnapshotData();

            Assert.Equal(string.Empty, snap.SnapshotId);
            Assert.Equal(string.Empty, snap.ProjectName);
            Assert.Equal(string.Empty, snap.MachineId);
        }

        [Fact]
        public void DefaultConstructor_FilesIsEmpty()
        {
            var snap = new SnapshotData();
            Assert.NotNull(snap.Files);
            Assert.Empty(snap.Files);
        }

        [Fact]
        public void DefaultConstructor_DiffsIsEmpty()
        {
            var snap = new SnapshotData();
            Assert.NotNull(snap.Diffs);
            Assert.Empty(snap.Diffs);
        }

        // ------------------------------------------------------------------ lazy-cached lookups

        [Fact]
        public void FilesByName_GroupsFilesByName()
        {
            var snap = new SnapshotData();
            snap.Files["app.dll"] = MakeFileRecord("app.dll", "app.dll");
            snap.Files["sub/app.dll"] = MakeFileRecord("sub/app.dll", "app.dll");

            var byName = snap.FilesByName;

            Assert.True(byName.ContainsKey("app.dll"));
            Assert.Equal(2, byName["app.dll"].Count);
        }

        [Fact]
        public void FilesByDir_GroupsFilesByRelativeDirectory()
        {
            var snap = new SnapshotData();
            snap.Files["lib/a.dll"] = MakeFileRecord("lib/a.dll", "a.dll");
            snap.Files["lib/b.dll"] = MakeFileRecord("lib/b.dll", "b.dll");
            snap.Files["c.dll"]     = MakeFileRecord("c.dll", "c.dll");

            var byDir = snap.FilesByDir;

            Assert.True(byDir.ContainsKey("lib"));
            Assert.Equal(2, byDir["lib"].Count);
        }

        [Fact]
        public void FilesByName_InvalidatedWhenFilesIsReplaced()
        {
            var snap = new SnapshotData();
            snap.Files["old.dll"] = MakeFileRecord("old.dll", "old.dll");
            _ = snap.FilesByName;  // populate cache

            snap.Files = new Dictionary<string, FileRecord>
            {
                ["new.dll"] = MakeFileRecord("new.dll", "new.dll")
            };

            Assert.False(snap.FilesByName.ContainsKey("old.dll"));
            Assert.True(snap.FilesByName.ContainsKey("new.dll"));
        }

        // ------------------------------------------------------------------ deep-copy constructor

        [Fact]
        public void CopyConstructor_CopiesSnapshotId()
        {
            var source = new SnapshotData { SnapshotId = "2.5" };
            var copy   = new SnapshotData(source);

            Assert.Equal("2.5", copy.SnapshotId);
        }

        [Fact]
        public void CopyConstructor_FilesAreDeepCopied()
        {
            var source = new SnapshotData();
            source.Files["a.dll"] = MakeFileRecord("a.dll", "a.dll", "HASH1");

            var copy = new SnapshotData(source);
            // Mutating the copy should not affect the source
            copy.Files["a.dll"] = MakeFileRecord("a.dll", "a.dll", "MODIFIED");

            Assert.Equal("HASH1", source.Files["a.dll"].Hash);
        }

        [Fact]
        public void CopyConstructor_DiffsAreDeepCopied()
        {
            var source = new SnapshotData();
            source.Diffs.Add(new FileDiff(ChangeKind.Added, null, MakeFileRecord()));

            var copy = new SnapshotData(source);
            copy.Diffs.Clear();

            Assert.Single(source.Diffs);
        }

        // ------------------------------------------------------------------ field values

        [Fact]
        public void Revision_CanBeSetAndRead()
        {
            var snap = new SnapshotData { Revision = 7 };
            Assert.Equal(7, snap.Revision);
        }

        [Fact]
        public void ChangeCount_CanBeSetAndRead()
        {
            var snap = new SnapshotData { ChangeCount = 42 };
            Assert.Equal(42, snap.ChangeCount);
        }
    }
}
