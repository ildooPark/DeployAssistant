using DeployAssistant.DataComponent;
using DeployAssistant.Interfaces;
using DeployAssistant.Model.V2;

namespace DeployAssistant.Migration.Steps
{
    /// <summary>
    /// Migrates a single V1 <c>ProjectData</c> instance to a V2
    /// <see cref="SnapshotData"/> instance.  Used internally by
    /// <see cref="ProjectMetaDataMigrationStep_1to2"/> when converting the
    /// history list and the current snapshot.
    ///
    /// <para>This step can also be registered independently as an
    /// <see cref="IMigrationStep{TIn,TOut}"/> when per-snapshot migration is
    /// needed (e.g. for <c>*.VersionLog</c> files).</para>
    ///
    /// <para><b>Field translation (V1 ProjectData → V2 SnapshotData)</b></para>
    /// <list type="table">
    ///   <listheader><term>V2</term><description>V1</description></listheader>
    ///   <item><term>SnapshotId</term><description>UpdatedVersion</description></item>
    ///   <item><term>Revision</term><description>RevisionNumber</description></item>
    ///   <item><term>ProjectName</term><description>ProjectName</description></item>
    ///   <item><term>ProjectPath</term><description>ProjectPath</description></item>
    ///   <item><term>UpdaterName</term><description>UpdaterName</description></item>
    ///   <item><term>MachineId</term><description>ConductedPC</description></item>
    ///   <item><term>TakenAt</term><description>UpdatedTime</description></item>
    ///   <item><term>UpdateLog</term><description>UpdateLog</description></item>
    ///   <item><term>ChangeLog</term><description>ChangeLog</description></item>
    ///   <item><term>ChangeCount</term><description>NumberOfChanges</description></item>
    ///   <item><term>Files</term><description>ProjectFiles</description></item>
    ///   <item><term>Diffs</term><description>ChangedFiles</description></item>
    /// </list>
    ///
    /// <para><b>Field translation (V1 ProjectFile → V2 FileRecord)</b></para>
    /// <list type="table">
    ///   <listheader><term>V2 FileRecord</term><description>V1 ProjectFile</description></listheader>
    ///   <item><term>Kind</term><description>DataType (ProjectDataType → FileKind)</description></item>
    ///   <item><term>RelPath</term><description>DataRelPath</description></item>
    ///   <item><term>Name</term><description>DataName</description></item>
    ///   <item><term>SrcPath</term><description>DataSrcPath</description></item>
    ///   <item><term>Size</term><description>DataSize</description></item>
    ///   <item><term>Hash</term><description>DataHash</description></item>
    ///   <item><term>BuildVersion</term><description>BuildVersion</description></item>
    ///   <item><term>SnapshotVersion</term><description>DeployedProjectVersion</description></item>
    ///   <item><term>RecordedAt</term><description>UpdatedTime</description></item>
    ///   <item><term>Flags</term><description>DataState (lifecycle bits: PreStaged|IntegrityChecked|Backup|Overlapped)</description></item>
    ///   <item><term><em>removed</em></term><description>IsDstFile — UI concern, not persisted in V2</description></item>
    /// </list>
    ///
    /// <para><b>Field translation (V1 ChangedFile → V2 FileDiff)</b></para>
    /// <list type="table">
    ///   <listheader><term>V2 FileDiff</term><description>V1 ChangedFile</description></listheader>
    ///   <item><term>Kind</term><description>DataState (change-kind bits → ChangeKind enum)</description></item>
    ///   <item><term>Before</term><description>SrcFile</description></item>
    ///   <item><term>After</term><description>DstFile</description></item>
    /// </list>
    /// </summary>
    public sealed class ProjectDataMigrationStep_1to2
        : IMigrationStep<Model.ProjectData, SnapshotData>
    {
        public int FromVersion => 1;
        public int ToVersion   => 2;

        public SnapshotData Migrate(Model.ProjectData source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));

            return new SnapshotData
            {
                SnapshotId  = source.UpdatedVersion  ?? string.Empty,
                Revision    = source.RevisionNumber,
                ProjectName = source.ProjectName      ?? string.Empty,
                ProjectPath = source.ProjectPath      ?? string.Empty,
                UpdaterName = source.UpdaterName      ?? string.Empty,
                MachineId   = source.ConductedPC      ?? string.Empty,
                TakenAt     = source.UpdatedTime,
                UpdateLog   = source.UpdateLog        ?? string.Empty,
                ChangeLog   = source.ChangeLog        ?? string.Empty,
                ChangeCount = source.NumberOfChanges,
                Files       = MigrateFiles(source.ProjectFiles),
                Diffs       = MigrateDiffs(source.ChangedFiles),
            };
        }

        public Model.ProjectData Rollback(SnapshotData migrated)
        {
            if (migrated == null) throw new ArgumentNullException(nameof(migrated));

            var result = new Model.ProjectData(migrated.ProjectPath)
            {
                ProjectName    = migrated.ProjectName,
                UpdaterName    = migrated.UpdaterName,
                ConductedPC    = migrated.MachineId,
                UpdatedTime    = migrated.TakenAt,
                UpdatedVersion = migrated.SnapshotId,
                UpdateLog      = migrated.UpdateLog,
                ChangeLog      = migrated.ChangeLog,
                NumberOfChanges = migrated.ChangeCount,
                RevisionNumber  = migrated.Revision,
                ProjectFiles    = RollbackFiles(migrated.Files),
                ChangedFiles    = RollbackDiffs(migrated.Diffs),
            };
            return result;
        }

        // ------------------------------------------------------------------ helpers: forward

        internal static Dictionary<string, FileRecord> MigrateFiles(
            Dictionary<string, Model.ProjectFile> v1Files)
        {
            var result = new Dictionary<string, FileRecord>(v1Files.Count);
            foreach (var kv in v1Files)
                result[kv.Key] = MigrateFile(kv.Value);
            return result;
        }

        internal static FileRecord MigrateFile(Model.ProjectFile v1)
        {
            return new FileRecord(
                Kind:            v1.DataType == ProjectDataType.Directory ? FileKind.Directory : FileKind.File,
                RelPath:         v1.DataRelPath         ?? string.Empty,
                Name:            v1.DataName            ?? string.Empty,
                SrcPath:         v1.DataSrcPath         ?? string.Empty,
                Size:            v1.DataSize,
                Hash:            v1.DataHash            ?? string.Empty,
                BuildVersion:    v1.BuildVersion        ?? string.Empty,
                SnapshotVersion: v1.DeployedProjectVersion ?? string.Empty,
                RecordedAt:      v1.UpdatedTime,
                Flags:           MapStagingFlags(v1.DataState));
        }

        internal static List<FileDiff> MigrateDiffs(List<Model.ChangedFile> v1Diffs)
        {
            var result = new List<FileDiff>(v1Diffs.Count);
            foreach (var cf in v1Diffs)
                result.Add(MigrateDiff(cf));
            return result;
        }

        internal static FileDiff MigrateDiff(Model.ChangedFile v1)
        {
            ChangeKind kind = MapChangeKind(v1.DataState);
            FileRecord? before = v1.SrcFile != null ? MigrateFile(v1.SrcFile) : null;
            FileRecord? after  = v1.DstFile != null ? MigrateFile(v1.DstFile) : null;
            return new FileDiff(kind, before, after);
        }

        private static StagingFlags MapStagingFlags(DataState state)
        {
            StagingFlags flags = StagingFlags.None;
            if ((state & DataState.PreStaged)        != 0) flags |= StagingFlags.PreStaged;
            if ((state & DataState.IntegrityChecked) != 0) flags |= StagingFlags.IntegrityChecked;
            if ((state & DataState.Backup)           != 0) flags |= StagingFlags.Backup;
            if ((state & DataState.Overlapped)       != 0) flags |= StagingFlags.Overlapped;
            return flags;
        }

        private static ChangeKind MapChangeKind(DataState state)
        {
            if ((state & DataState.Deleted)  != 0) return ChangeKind.Deleted;
            if ((state & DataState.Modified) != 0) return ChangeKind.Modified;
            if ((state & DataState.Restored) != 0) return ChangeKind.Restored;
            return ChangeKind.Added;  // Added is the default / fallback
        }

        // ------------------------------------------------------------------ helpers: rollback

        private static Dictionary<string, Model.ProjectFile> RollbackFiles(
            Dictionary<string, FileRecord> v2Files)
        {
            var result = new Dictionary<string, Model.ProjectFile>(v2Files.Count);
            foreach (var kv in v2Files)
                result[kv.Key] = RollbackFile(kv.Value);
            return result;
        }

        internal static Model.ProjectFile RollbackFile(FileRecord v2)
        {
            DataState state = RollbackStagingFlags(v2.Flags);
            ProjectDataType type = v2.Kind == FileKind.Directory
                ? ProjectDataType.Directory
                : ProjectDataType.File;

            return new Model.ProjectFile(
                DataType:              type,
                DataSize:              v2.Size,
                BuildVersion:          v2.BuildVersion,
                DeployedProjectVersion: v2.SnapshotVersion,
                UpdatedTime:           v2.RecordedAt,
                DataState:             state,
                dataName:              v2.Name,
                dataSrcPath:           v2.SrcPath,
                dataRelPath:           v2.RelPath,
                dataHash:              v2.Hash,
                IsDstFile:             false  // IsDstFile was a UI concern; default to false on rollback
            );
        }

        private static List<Model.ChangedFile> RollbackDiffs(List<FileDiff> v2Diffs)
        {
            var result = new List<Model.ChangedFile>(v2Diffs.Count);
            foreach (var diff in v2Diffs)
                result.Add(RollbackDiff(diff));
            return result;
        }

        private static Model.ChangedFile RollbackDiff(FileDiff v2)
        {
            DataState state = RollbackChangeKind(v2.Kind);
            Model.ProjectFile? src = v2.Before != null ? RollbackFile(v2.Before) : null;
            Model.ProjectFile? dst = v2.After  != null ? RollbackFile(v2.After)  : null;
            // The V1 ChangedFile JSON constructor declares both parameters as non-nullable
            // but accepts null at runtime (tests confirm this pattern).  The null-forgiving
            // operators suppress the compiler warning; the V1 type's nullable handling is
            // preserved as-is rather than coupling the migration step to its internals.
            return new Model.ChangedFile(src!, dst!, state);
        }

        private static DataState RollbackStagingFlags(StagingFlags flags)
        {
            DataState state = DataState.None;
            if ((flags & StagingFlags.PreStaged)        != 0) state |= DataState.PreStaged;
            if ((flags & StagingFlags.IntegrityChecked) != 0) state |= DataState.IntegrityChecked;
            if ((flags & StagingFlags.Backup)           != 0) state |= DataState.Backup;
            if ((flags & StagingFlags.Overlapped)       != 0) state |= DataState.Overlapped;
            return state;
        }

        private static DataState RollbackChangeKind(ChangeKind kind) => kind switch
        {
            ChangeKind.Added    => DataState.Added,
            ChangeKind.Deleted  => DataState.Deleted,
            ChangeKind.Modified => DataState.Modified,
            ChangeKind.Restored => DataState.Restored,
            _                   => DataState.None
        };
    }
}
