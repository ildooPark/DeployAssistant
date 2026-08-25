#pragma warning disable CS0618  // ProjectData is a V1 type; version management is V1-shaped.

namespace DeployAssistant.Model
{
    /// <summary>
    /// Read-only description of what deleting one version would do, produced by
    /// <c>BackupManager.BuildVersionDeletePlan</c>.  Building a plan never touches
    /// disk or metadata — it exists so the GUI/CLI can show the user the blast
    /// radius (and any <see cref="Blockers"/>) before anything is destroyed.
    /// </summary>
    public sealed class VersionDeletePlan
    {
        private readonly List<string> _blockers;

        /// <summary>The version being considered for deletion.</summary>
        public ProjectData Target { get; }
        public string VersionName { get; }
        /// <summary><c>Backup_&lt;ProjectName&gt;\Backup_&lt;UpdatedVersion&gt;</c> for <see cref="Target"/>.</summary>
        public string BackupFolderPath { get; }
        /// <summary>Backup blob hashes referenced ONLY by the target — these get unregistered and their bytes deleted.</summary>
        public IReadOnlyList<string> ExclusiveHashes { get; }
        /// <summary>Backup blob hashes also referenced by a surviving snapshot — these must survive untouched.</summary>
        public IReadOnlyList<string> SharedHashes { get; }
        /// <summary>
        /// Shared hashes whose only physical copy currently lives inside
        /// <see cref="BackupFolderPath"/>.  They must be relocated into a surviving
        /// folder before the target's folder can be removed, otherwise the survivors
        /// would point at deleted bytes.
        /// </summary>
        public IReadOnlyList<string> RelocationHashes { get; }
        public long ReclaimableBytes { get; }
        /// <summary>True when <see cref="BackupFolderPath"/> exists and can be removed once relocation is done.</summary>
        public bool BackupFolderRemovable { get; }
        public IReadOnlyList<string> Blockers => _blockers;
        public bool CanDelete => _blockers.Count == 0;

        public VersionDeletePlan(
            ProjectData target,
            string backupFolderPath,
            IReadOnlyList<string> exclusiveHashes,
            IReadOnlyList<string> sharedHashes,
            IReadOnlyList<string> relocationHashes,
            long reclaimableBytes,
            bool backupFolderRemovable,
            IEnumerable<string>? blockers = null)
        {
            Target = target;
            VersionName = target?.UpdatedVersion ?? "";
            BackupFolderPath = backupFolderPath;
            ExclusiveHashes = exclusiveHashes;
            SharedHashes = sharedHashes;
            RelocationHashes = relocationHashes;
            ReclaimableBytes = reclaimableBytes;
            BackupFolderRemovable = backupFolderRemovable;
            _blockers = blockers == null ? new List<string>() : new List<string>(blockers);
        }

        /// <summary>
        /// Adds a refusal reason.  Used by <c>MetaDataManager</c> to fold in conditions
        /// <c>BackupManager</c> cannot see (e.g. the manager not being Idle).
        /// </summary>
        public void AddBlocker(string reason)
        {
            if (string.IsNullOrWhiteSpace(reason)) return;
            if (_blockers.Contains(reason)) return;
            _blockers.Add(reason);
        }
    }
}
