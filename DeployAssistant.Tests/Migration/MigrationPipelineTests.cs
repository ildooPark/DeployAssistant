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
    public class MigrationPipelineTests
    {
        // ------------------------------------------------------------------ helpers

        private static MigrationPipeline<ProjectStore> BuildPipeline()
        {
            var step    = new ProjectMetaDataMigrationStep_1to2();
            var adapter = new MigrationStepAdapter<ProjectMetaData, ProjectStore>(step);
            return new MigrationPipeline<ProjectStore>(new[] { (IMigrationStepAdapter)adapter });
        }

        private static ProjectMetaData MakeV1Meta(string projectName = "TestProj", string projectPath = @"C:\Proj")
        {
            var meta = new ProjectMetaData(projectName, projectPath)
            {
                LocalUpdateCount = 3
            };

            var pd = new ProjectData(@"C:\Proj")
            {
                ProjectName    = projectName,
                UpdaterName    = "Alice",
                ConductedPC    = "PC01",
                UpdatedVersion = "1.0",
                UpdateLog      = "init",
                ChangeLog      = "initial",
                NumberOfChanges = 1,
                RevisionNumber  = 1,
            };
            pd.ProjectFiles["app.dll"] = new ProjectFile(
                DataType: ProjectDataType.File,
                DataSize: 512,
                BuildVersion: "1.0.0",
                DeployedProjectVersion: "1.0",
                UpdatedTime: new DateTime(2024, 1, 1),
                DataState: DataState.None,
                dataName: "app.dll",
                dataSrcPath: @"C:\Proj",
                dataRelPath: "app.dll",
                dataHash: "AABBCC",
                IsDstFile: false);

            meta.ProjectDataList.AddLast(pd);
            meta.SetProjectMain(pd);
            return meta;
        }

        // ------------------------------------------------------------------ pipeline construction

        [Fact]
        public void Constructor_NullSteps_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new MigrationPipeline<ProjectStore>(null!));
        }

        [Fact]
        public void Constructor_EmptySteps_CreatesValidPipeline()
        {
            // A pipeline with no steps is valid as long as we never call MigrateTo
            // with different fromVersion / targetVersion.
            var pipeline = new MigrationPipeline<ProjectStore>(Array.Empty<IMigrationStepAdapter>());
            Assert.NotNull(pipeline);
        }

        // ------------------------------------------------------------------ MigrateTo

        [Fact]
        public void MigrateTo_SameVersion_ReturnsCastSourceDirectly()
        {
            var pipeline = BuildPipeline();
            var store    = new ProjectStore("P", @"C:\P");
            var result   = pipeline.MigrateTo(store, 2, 2);
            Assert.Same(store, result);
        }

        [Fact]
        public void MigrateTo_NullSource_ThrowsArgumentNullException()
        {
            var pipeline = BuildPipeline();
            Assert.Throws<ArgumentNullException>(() =>
                pipeline.MigrateTo(null!, 1, 2));
        }

        [Fact]
        public void MigrateTo_FromHigherThanTarget_ThrowsArgumentException()
        {
            var pipeline = BuildPipeline();
            var store    = new ProjectStore("P", @"C:\P");
            Assert.Throws<ArgumentException>(() =>
                pipeline.MigrateTo(store, 3, 2));
        }

        [Fact]
        public void MigrateTo_V1ToV2_ProducesProjectStoreWithSchemaVersion2()
        {
            var pipeline = BuildPipeline();
            var v1       = MakeV1Meta();
            var result   = pipeline.MigrateTo(v1, 1, 2);

            Assert.Equal(2, result.SchemaVersion);
        }

        [Fact]
        public void MigrateTo_V1ToV2_PreservesProjectNameAndPath()
        {
            var pipeline = BuildPipeline();
            var v1       = MakeV1Meta("MyProj", @"C:\MyProj");
            var result   = pipeline.MigrateTo(v1, 1, 2);

            Assert.Equal("MyProj",      result.ProjectName);
            Assert.Equal(@"C:\MyProj",  result.ProjectPath);
        }

        [Fact]
        public void MigrateTo_V1ToV2_PreservesLocalUpdateCount()
        {
            var pipeline = BuildPipeline();
            var v1       = MakeV1Meta();
            v1.LocalUpdateCount = 7;
            var result = pipeline.MigrateTo(v1, 1, 2);

            Assert.Equal(7, result.LocalUpdateCount);
        }

        [Fact]
        public void MigrateTo_V1ToV2_HistoryCountMatchesProjectDataList()
        {
            var pipeline = BuildPipeline();
            var v1       = MakeV1Meta();
            var result   = pipeline.MigrateTo(v1, 1, 2);

            Assert.Equal(v1.ProjectDataList.Count, result.History.Count);
        }

        [Fact]
        public void MigrateTo_V1ToV2_CurrentSnapshotFieldsAreMapped()
        {
            var pipeline = BuildPipeline();
            var v1       = MakeV1Meta();
            var result   = pipeline.MigrateTo(v1, 1, 2);

            Assert.Equal("1.0",   result.Current.SnapshotId);
            Assert.Equal("Alice", result.Current.UpdaterName);
            Assert.Equal("PC01",  result.Current.MachineId);
        }

        [Fact]
        public void MigrateTo_NoStepForVersion_ThrowsInvalidOperationException()
        {
            var pipeline = new MigrationPipeline<ProjectStore>(Array.Empty<IMigrationStepAdapter>());
            var v1       = MakeV1Meta();
            Assert.Throws<InvalidOperationException>(() =>
                pipeline.MigrateTo(v1, 1, 2));
        }

        // ------------------------------------------------------------------ RollbackTo

        [Fact]
        public void RollbackTo_SameVersion_ReturnsCurrent()
        {
            var pipeline = BuildPipeline();
            var store    = new ProjectStore("P", @"C:\P");
            var result   = pipeline.RollbackTo(store, 2);
            Assert.Same(store, result);
        }

        [Fact]
        public void RollbackTo_NullCurrent_ThrowsArgumentNullException()
        {
            var pipeline = BuildPipeline();
            Assert.Throws<ArgumentNullException>(() =>
                pipeline.RollbackTo(null!, 1));
        }

        [Fact]
        public void RollbackTo_TargetHigherThanCurrentVersion_ThrowsArgumentException()
        {
            var pipeline = BuildPipeline();
            var store    = new ProjectStore("P", @"C:\P"); // SchemaVersion = 2
            Assert.Throws<ArgumentException>(() =>
                pipeline.RollbackTo(store, 3));
        }

        [Fact]
        public void RollbackTo_V2ToV1_ProducesProjectMetaData()
        {
            var pipeline = BuildPipeline();
            var v1       = MakeV1Meta("MyProj", @"C:\MyProj");
            var v2       = pipeline.MigrateTo(v1, 1, 2);
            var rolled   = pipeline.RollbackTo(v2, 1);

            Assert.IsAssignableFrom<ProjectMetaData>(rolled);
        }

        [Fact]
        public void RollbackTo_V2ToV1_PreservesProjectName()
        {
            var pipeline  = BuildPipeline();
            var v1        = MakeV1Meta("RollBackProj", @"C:\RB");
            var v2        = pipeline.MigrateTo(v1, 1, 2);
            var rolled    = (ProjectMetaData)pipeline.RollbackTo(v2, 1);

            Assert.Equal("RollBackProj", rolled.ProjectName);
        }
    }
}
