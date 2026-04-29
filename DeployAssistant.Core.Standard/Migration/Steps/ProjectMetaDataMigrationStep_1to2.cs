using DeployAssistant.Model.V2;

namespace DeployAssistant.Migration.Steps
{
    /// <summary>
    /// Migrates the top-level project store from V1 (<c>ProjectMetaData</c>)
    /// to V2 (<see cref="ProjectStore"/>).
    ///
    /// <para>
    /// This step uses <see cref="ProjectDataMigrationStep_1to2"/> internally for
    /// each <c>ProjectData</c> object found in the V1 history list and the current
    /// snapshot.
    /// </para>
    ///
    /// <para><b>V1 → V2 store field translation table</b></para>
    /// <list type="table">
    ///   <listheader><term>V2 ProjectStore</term><description>V1 ProjectMetaData</description></listheader>
    ///   <item><term>SchemaVersion</term><description>(new, value = 2)</description></item>
    ///   <item><term>ProjectName</term><description>ProjectName</description></item>
    ///   <item><term>ProjectPath</term><description>ProjectPath</description></item>
    ///   <item><term>LocalUpdateCount</term><description>LocalUpdateCount</description></item>
    ///   <item><term>Current</term><description>ProjectMain</description></item>
    ///   <item><term>History</term><description>ProjectDataList (LinkedList → List)</description></item>
    ///   <item><term>BackupFiles</term><description>BackupFiles (Dictionary&lt;string,ProjectFile&gt; → Dictionary&lt;string,FileRecord&gt;)</description></item>
    /// </list>
    /// </summary>
    public sealed class ProjectMetaDataMigrationStep_1to2
        : IMigrationStep<Model.ProjectMetaData, ProjectStore>
    {
        private readonly ProjectDataMigrationStep_1to2 _snapshotStep = new();

        public int FromVersion => 1;
        public int ToVersion   => 2;

        public ProjectStore Migrate(Model.ProjectMetaData source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));

            var history = new List<SnapshotData>(source.ProjectDataList.Count);
            foreach (Model.ProjectData pd in source.ProjectDataList)
                history.Add(_snapshotStep.Migrate(pd));

            return new ProjectStore(
                SchemaVersion:    2,
                ProjectName:      source.ProjectName      ?? string.Empty,
                ProjectPath:      source.ProjectPath      ?? string.Empty,
                LocalUpdateCount: source.LocalUpdateCount,
                Current:          _snapshotStep.Migrate(source.ProjectMain),
                History:          history,
                BackupFiles:      ProjectDataMigrationStep_1to2.MigrateFiles(source.BackupFiles));
        }

        public Model.ProjectMetaData Rollback(ProjectStore migrated)
        {
            if (migrated == null) throw new ArgumentNullException(nameof(migrated));

            var meta = new Model.ProjectMetaData(migrated.ProjectName, migrated.ProjectPath)
            {
                LocalUpdateCount = migrated.LocalUpdateCount,
                BackupFiles      = RollbackBackupFiles(migrated.BackupFiles),
            };

            // Rebuild the LinkedList history
            foreach (SnapshotData snap in migrated.History)
                meta.ProjectDataList.AddLast(_snapshotStep.Rollback(snap));

            // Restore the current project main
            Model.ProjectData rolledBackMain = _snapshotStep.Rollback(migrated.Current);
            meta.SetProjectMain(rolledBackMain);

            return meta;
        }

        private static Dictionary<string, Model.ProjectFile> RollbackBackupFiles(
            Dictionary<string, FileRecord> v2)
        {
            var result = new Dictionary<string, Model.ProjectFile>(v2.Count);
            foreach (var kv in v2)
                result[kv.Key] = ProjectDataMigrationStep_1to2.RollbackFile(kv.Value);
            return result;
        }
    }
}
