using DeployAssistant.Model.V2;
using System;
using System.Collections.Generic;
using Xunit;

namespace DeployAssistant.Tests.Models.V2
{
    /// <summary>
    /// Unit tests for the V2 <see cref="ProjectStore"/> model.
    /// </summary>
    public class ProjectStoreTests
    {
        // ------------------------------------------------------------------ constructors

        [Fact]
        public void DefaultConstructor_SchemaVersionIs2()
        {
            var store = new ProjectStore();
            Assert.Equal(2, store.SchemaVersion);
        }

        [Fact]
        public void ParameterisedConstructor_SetsProjectNameAndPath()
        {
            var store = new ProjectStore("MyProject", @"C:\Projects\MyProject");

            Assert.Equal("MyProject", store.ProjectName);
            Assert.Equal(@"C:\Projects\MyProject", store.ProjectPath);
        }

        [Fact]
        public void ParameterisedConstructor_LocalUpdateCountIsZero()
        {
            var store = new ProjectStore("P", @"C:\P");
            Assert.Equal(0, store.LocalUpdateCount);
        }

        [Fact]
        public void ParameterisedConstructor_CurrentIsNonNull()
        {
            var store = new ProjectStore("P", @"C:\P");
            Assert.NotNull(store.Current);
        }

        [Fact]
        public void ParameterisedConstructor_HistoryIsEmptyList()
        {
            var store = new ProjectStore("P", @"C:\P");
            Assert.NotNull(store.History);
            Assert.Empty(store.History);
        }

        [Fact]
        public void ParameterisedConstructor_BackupFilesIsEmptyDict()
        {
            var store = new ProjectStore("P", @"C:\P");
            Assert.NotNull(store.BackupFiles);
            Assert.Empty(store.BackupFiles);
        }

        // ------------------------------------------------------------------ mutability

        [Fact]
        public void ProjectName_CanBeUpdated()
        {
            var store = new ProjectStore("Old", @"C:\P");
            store.ProjectName = "New";
            Assert.Equal("New", store.ProjectName);
        }

        [Fact]
        public void LocalUpdateCount_CanBeIncremented()
        {
            var store = new ProjectStore("P", @"C:\P");
            store.LocalUpdateCount++;
            Assert.Equal(1, store.LocalUpdateCount);
        }

        [Fact]
        public void History_CanAcceptSnapshotData()
        {
            var store = new ProjectStore("P", @"C:\P");
            store.History.Add(new SnapshotData { SnapshotId = "1.0", ProjectName = "P" });

            Assert.Single(store.History);
            Assert.Equal("1.0", store.History[0].SnapshotId);
        }

        [Fact]
        public void BackupFiles_CanAddFileRecord()
        {
            var store = new ProjectStore("P", @"C:\P");
            var rec = new FileRecord(
                FileKind.File, "app.dll", "app.dll",
                @"C:\P", 512, "AABBCC", "1.0", "1.0",
                DateTime.Now, StagingFlags.None);

            store.BackupFiles["AABBCC"] = rec;

            Assert.Single(store.BackupFiles);
            Assert.Equal("app.dll", store.BackupFiles["AABBCC"].Name);
        }

        // ------------------------------------------------------------------ schema version

        [Fact]
        public void SchemaVersion_IsAlways2()
        {
            var store1 = new ProjectStore();
            var store2 = new ProjectStore("P", @"C:\P");

            Assert.Equal(2, store1.SchemaVersion);
            Assert.Equal(2, store2.SchemaVersion);
        }
    }
}
