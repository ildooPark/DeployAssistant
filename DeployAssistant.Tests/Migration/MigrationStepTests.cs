#pragma warning disable CS0618  // using V1 obsolete types intentionally for migration tests

using DeployAssistant.DataComponent;
using DeployAssistant.Interfaces;
using DeployAssistant.Migration;
using DeployAssistant.Migration.Steps;
using DeployAssistant.Model;
using DeployAssistant.Model.V2;
using System;
using System.Collections.Generic;
using Xunit;

namespace DeployAssistant.Tests.Migration
{
    public class MigrationStepTests
    {
        // ------------------------------------------------------------------ helpers

        private static ProjectFile MakeV1File(
            string relPath,
            string name,
            string srcPath       = @"C:\Proj",
            DataState state      = DataState.None,
            ProjectDataType type = ProjectDataType.File)
        {
            return new ProjectFile(
                DataType:              type,
                DataSize:              256,
                BuildVersion:          "2.0.0",
                DeployedProjectVersion: "1.0",
                UpdatedTime:           new DateTime(2024, 6, 1),
                DataState:             state,
                dataName:              name,
                dataSrcPath:           srcPath,
                dataRelPath:           relPath,
                dataHash:              "DEADBEEF",
                IsDstFile:             false);
        }

        private static ProjectData MakeV1ProjectData(string version = "1.0")
        {
            var pd = new ProjectData(@"C:\Proj")
            {
                ProjectName     = "Proj",
                UpdaterName     = "Bob",
                ConductedPC     = "WORKSTATION",
                UpdatedVersion  = version,
                UpdateLog       = "log",
                ChangeLog       = "cl",
                NumberOfChanges = 2,
                RevisionNumber  = 3,
            };
            pd.ProjectFiles["a.dll"] = MakeV1File("a.dll", "a.dll");
            pd.ProjectFiles["b.dll"] = MakeV1File("b.dll", "b.dll");
            var dstFile = MakeV1File("c.dll", "c.dll");
            pd.ChangedFiles.Add(new ChangedFile(dstFile, DataState.Added));
            return pd;
        }

        // ================================================================== ProjectDataMigrationStep_1to2

        private readonly ProjectDataMigrationStep_1to2 _pdStep = new();

        [Fact]
        public void StepVersions_AreCorrect()
        {
            Assert.Equal(1, _pdStep.FromVersion);
            Assert.Equal(2, _pdStep.ToVersion);
        }

        [Fact]
        public void Migrate_NullSource_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => _pdStep.Migrate(null!));
        }

        [Fact]
        public void Migrate_SnapshotId_MapsFromUpdatedVersion()
        {
            var result = _pdStep.Migrate(MakeV1ProjectData("3.5"));
            Assert.Equal("3.5", result.SnapshotId);
        }

        [Fact]
        public void Migrate_Revision_MapsFromRevisionNumber()
        {
            var pd     = MakeV1ProjectData();
            pd.RevisionNumber = 7;
            var result = _pdStep.Migrate(pd);
            Assert.Equal(7, result.Revision);
        }

        [Fact]
        public void Migrate_MachineId_MapsFromConductedPC()
        {
            var result = _pdStep.Migrate(MakeV1ProjectData());
            Assert.Equal("WORKSTATION", result.MachineId);
        }

        [Fact]
        public void Migrate_FileCount_MatchesProjectFilesCount()
        {
            var pd     = MakeV1ProjectData();
            var result = _pdStep.Migrate(pd);
            Assert.Equal(pd.ProjectFiles.Count, result.Files.Count);
        }

        [Fact]
        public void Migrate_DiffCount_MatchesChangedFilesCount()
        {
            var pd     = MakeV1ProjectData();
            var result = _pdStep.Migrate(pd);
            Assert.Equal(pd.ChangedFiles.Count, result.Diffs.Count);
        }

        [Fact]
        public void Migrate_FileRecord_KindMapsCorrectly()
        {
            var pd = new ProjectData(@"C:\P");
            pd.ProjectFiles["dir"]   = MakeV1File("dir",   "dir",   type: ProjectDataType.Directory);
            pd.ProjectFiles["f.dll"] = MakeV1File("f.dll", "f.dll", type: ProjectDataType.File);
            var result = _pdStep.Migrate(pd);

            Assert.Equal(FileKind.Directory, result.Files["dir"].Kind);
            Assert.Equal(FileKind.File,      result.Files["f.dll"].Kind);
        }

        [Fact]
        public void Migrate_FileRecord_HashAndNamePreserved()
        {
            var result = _pdStep.Migrate(MakeV1ProjectData());
            Assert.Equal("DEADBEEF", result.Files["a.dll"].Hash);
            Assert.Equal("a.dll",    result.Files["a.dll"].Name);
        }

        [Fact]
        public void Migrate_StagingFlags_PreStagedMapped()
        {
            var pd = new ProjectData(@"C:\P");
            pd.ProjectFiles["x.dll"] = MakeV1File("x.dll", "x.dll", state: DataState.PreStaged);
            var result = _pdStep.Migrate(pd);
            Assert.True((result.Files["x.dll"].Flags & StagingFlags.PreStaged) != 0);
        }

        [Fact]
        public void Migrate_StagingFlags_OverlappedMapped()
        {
            var pd = new ProjectData(@"C:\P");
            pd.ProjectFiles["y.dll"] = MakeV1File("y.dll", "y.dll", state: DataState.Overlapped);
            var result = _pdStep.Migrate(pd);
            Assert.True((result.Files["y.dll"].Flags & StagingFlags.Overlapped) != 0);
        }

        [Fact]
        public void Migrate_DiffKind_AddedMapped()
        {
            var pd = new ProjectData(@"C:\P");
            pd.ChangedFiles.Add(new ChangedFile(MakeV1File("a.dll", "a.dll"), DataState.Added));
            var result = _pdStep.Migrate(pd);
            Assert.Equal(ChangeKind.Added, result.Diffs[0].Kind);
        }

        [Fact]
        public void Migrate_DiffKind_DeletedMapped()
        {
            var pd = new ProjectData(@"C:\P");
            pd.ChangedFiles.Add(new ChangedFile(MakeV1File("d.dll", "d.dll"), DataState.Deleted));
            var result = _pdStep.Migrate(pd);
            Assert.Equal(ChangeKind.Deleted, result.Diffs[0].Kind);
        }

        [Fact]
        public void Migrate_DiffKind_ModifiedMapped()
        {
            var pd = new ProjectData(@"C:\P");
            var src = MakeV1File("m.dll", "m.dll", @"C:\Src");
            var dst = MakeV1File("m.dll", "m.dll", @"C:\Dst");
            pd.ChangedFiles.Add(new ChangedFile(src, dst, DataState.Modified, RegisterChanges: true));
            var result = _pdStep.Migrate(pd);
            Assert.Equal(ChangeKind.Modified, result.Diffs[0].Kind);
        }

        [Fact]
        public void Migrate_Added_DiffHasNullBefore()
        {
            var pd = new ProjectData(@"C:\P");
            pd.ChangedFiles.Add(new ChangedFile(MakeV1File("a.dll", "a.dll"), DataState.Added));
            var result = _pdStep.Migrate(pd);
            Assert.Null(result.Diffs[0].Before);
            Assert.NotNull(result.Diffs[0].After);
        }

        // ------------------------------------------------------------------ Rollback

        [Fact]
        public void Rollback_NullSource_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => _pdStep.Rollback(null!));
        }

        [Fact]
        public void Rollback_RoundTrip_ProjectNamePreserved()
        {
            var v1     = MakeV1ProjectData();
            var v2     = _pdStep.Migrate(v1);
            var rolled = _pdStep.Rollback(v2);
            Assert.Equal(v1.ProjectName, rolled.ProjectName);
        }

        [Fact]
        public void Rollback_RoundTrip_FileCountPreserved()
        {
            var v1     = MakeV1ProjectData();
            var v2     = _pdStep.Migrate(v1);
            var rolled = _pdStep.Rollback(v2);
            Assert.Equal(v1.ProjectFiles.Count, rolled.ProjectFiles.Count);
        }

        [Fact]
        public void Rollback_RoundTrip_UpdatedVersionPreserved()
        {
            var v1     = MakeV1ProjectData("5.5");
            var v2     = _pdStep.Migrate(v1);
            var rolled = _pdStep.Rollback(v2);
            Assert.Equal("5.5", rolled.UpdatedVersion);
        }

        // ================================================================== ProjectMetaDataMigrationStep_1to2

        private readonly ProjectMetaDataMigrationStep_1to2 _pmStep = new();

        private static ProjectMetaData MakeV1Meta()
        {
            var meta = new ProjectMetaData("TestMeta", @"C:\Meta") { LocalUpdateCount = 5 };
            var pd   = MakeV1ProjectData("2.0");
            meta.ProjectDataList.AddLast(pd);
            meta.SetProjectMain(pd);
            return meta;
        }

        [Fact]
        public void MetaStep_VersionsAreCorrect()
        {
            Assert.Equal(1, _pmStep.FromVersion);
            Assert.Equal(2, _pmStep.ToVersion);
        }

        [Fact]
        public void MetaStep_Migrate_NullSource_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => _pmStep.Migrate(null!));
        }

        [Fact]
        public void MetaStep_Migrate_SchemaVersion_Is2()
        {
            var result = _pmStep.Migrate(MakeV1Meta());
            Assert.Equal(2, result.SchemaVersion);
        }

        [Fact]
        public void MetaStep_Migrate_ProjectName_Preserved()
        {
            var result = _pmStep.Migrate(MakeV1Meta());
            Assert.Equal("TestMeta", result.ProjectName);
        }

        [Fact]
        public void MetaStep_Migrate_LocalUpdateCount_Preserved()
        {
            var result = _pmStep.Migrate(MakeV1Meta());
            Assert.Equal(5, result.LocalUpdateCount);
        }

        [Fact]
        public void MetaStep_Migrate_HistoryCount_MatchesLinkedListCount()
        {
            var v1     = MakeV1Meta();
            var result = _pmStep.Migrate(v1);
            Assert.Equal(v1.ProjectDataList.Count, result.History.Count);
        }

        [Fact]
        public void MetaStep_Rollback_NullSource_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => _pmStep.Rollback(null!));
        }

        [Fact]
        public void MetaStep_Rollback_RoundTrip_ProjectNamePreserved()
        {
            var v1     = MakeV1Meta();
            var v2     = _pmStep.Migrate(v1);
            var rolled = _pmStep.Rollback(v2);
            Assert.Equal("TestMeta", rolled.ProjectName);
        }

        [Fact]
        public void MetaStep_Rollback_RoundTrip_LocalUpdateCountPreserved()
        {
            var v1     = MakeV1Meta();
            var v2     = _pmStep.Migrate(v1);
            var rolled = _pmStep.Rollback(v2);
            Assert.Equal(5, rolled.LocalUpdateCount);
        }

        [Fact]
        public void MetaStep_Rollback_RoundTrip_ProjectDataListCountPreserved()
        {
            var v1     = MakeV1Meta();
            var v2     = _pmStep.Migrate(v1);
            var rolled = _pmStep.Rollback(v2);
            Assert.Equal(v1.ProjectDataList.Count, rolled.ProjectDataList.Count);
        }

        // ================================================================== NullMigrationPipeline

        [Fact]
        public void NullPipeline_MigrateTo_SameVersion_ReturnsCast()
        {
            var pipeline = new NullMigrationPipeline<ProjectStore>();
            var store    = new ProjectStore("P", @"C:\P");
            var result   = pipeline.MigrateTo(store, 2, 2);
            Assert.Same(store, result);
        }

        [Fact]
        public void NullPipeline_MigrateTo_DifferentVersion_ThrowsInvalidOperation()
        {
            var pipeline = new NullMigrationPipeline<ProjectStore>();
            var store    = new ProjectStore("P", @"C:\P");
            Assert.Throws<InvalidOperationException>(() =>
                pipeline.MigrateTo(store, 1, 2));
        }

        [Fact]
        public void NullPipeline_RollbackTo_SameVersion_ReturnsSame()
        {
            var pipeline = new NullMigrationPipeline<ProjectStore>();
            var store    = new ProjectStore("P", @"C:\P");
            var result   = pipeline.RollbackTo(store, 2);
            Assert.Same(store, result);
        }

        [Fact]
        public void NullPipeline_RollbackTo_DifferentVersion_ThrowsInvalidOperation()
        {
            var pipeline = new NullMigrationPipeline<ProjectStore>();
            var store    = new ProjectStore("P", @"C:\P");
            Assert.Throws<InvalidOperationException>(() =>
                pipeline.RollbackTo(store, 1));
        }

        // ================================================================== MigrationStepAdapter

        [Fact]
        public void Adapter_Migrate_WrongInputType_ThrowsInvalidCastException()
        {
            var adapter = new MigrationStepAdapter<ProjectMetaData, ProjectStore>(_pmStep);
            Assert.Throws<InvalidCastException>(() => adapter.Migrate("not a ProjectMetaData"));
        }

        [Fact]
        public void Adapter_Rollback_WrongInputType_ThrowsInvalidCastException()
        {
            var adapter = new MigrationStepAdapter<ProjectMetaData, ProjectStore>(_pmStep);
            Assert.Throws<InvalidCastException>(() => adapter.Rollback("not a ProjectStore"));
        }
    }
}
