using System.Text.Json.Serialization;

namespace DeployAssistant.Model.V2
{
    /// <summary>
    /// A point-in-time snapshot of the project's file tree, together with the
    /// diff that produced it.  Replaces the V1 <c>ProjectData</c> class.
    ///
    /// <para><b>V1 → V2 field translation table</b></para>
    /// <list type="table">
    ///   <listheader><term>V2 property</term><description>V1 property</description></listheader>
    ///   <item><term><see cref="SnapshotId"/></term><description>UpdatedVersion</description></item>
    ///   <item><term><see cref="Revision"/></term><description>RevisionNumber</description></item>
    ///   <item><term><see cref="ProjectName"/></term><description>ProjectName (unchanged)</description></item>
    ///   <item><term><see cref="ProjectPath"/></term><description>ProjectPath (unchanged)</description></item>
    ///   <item><term><see cref="UpdaterName"/></term><description>UpdaterName (unchanged)</description></item>
    ///   <item><term><see cref="MachineId"/></term><description>ConductedPC</description></item>
    ///   <item><term><see cref="TakenAt"/></term><description>UpdatedTime</description></item>
    ///   <item><term><see cref="UpdateLog"/></term><description>UpdateLog (unchanged)</description></item>
    ///   <item><term><see cref="ChangeLog"/></term><description>ChangeLog (unchanged)</description></item>
    ///   <item><term><see cref="ChangeCount"/></term><description>NumberOfChanges</description></item>
    ///   <item><term><see cref="Files"/></term><description>ProjectFiles (Dictionary&lt;string, ProjectFile&gt; → Dictionary&lt;string, FileRecord&gt;)</description></item>
    ///   <item><term><see cref="Diffs"/></term><description>ChangedFiles (List&lt;ChangedFile&gt; → List&lt;FileDiff&gt;)</description></item>
    /// </list>
    ///
    /// <para>
    /// The derived lookup dictionaries (<c>ProjectFilesDict_NameSorted</c>,
    /// <c>ProjectFilesDict_RelDirSorted</c>) are replaced by lazy-cached properties
    /// <see cref="FilesByName"/> and <see cref="FilesByDir"/> that are computed once
    /// on first access and invalidated when <see cref="Files"/> is replaced.
    /// </para>
    /// </summary>
    public sealed class SnapshotData
    {
        // ------------------------------------------------------------------ persisted

        public string     SnapshotId   { get; set; } = string.Empty;
        public int        Revision     { get; set; }
        public string     ProjectName  { get; set; } = string.Empty;
        public string     ProjectPath  { get; set; } = string.Empty;
        public string     UpdaterName  { get; set; } = string.Empty;
        public string     MachineId    { get; set; } = string.Empty;
        public DateTime   TakenAt      { get; set; }
        public string     UpdateLog    { get; set; } = string.Empty;
        public string     ChangeLog    { get; set; } = string.Empty;
        public int        ChangeCount  { get; set; }

        private Dictionary<string, FileRecord> _files = new();
        /// <summary>
        /// All tracked files and directories keyed by relative path.
        /// Assigning a new dictionary invalidates the lazy lookup caches.
        /// </summary>
        public Dictionary<string, FileRecord> Files
        {
            get => _files;
            set
            {
                _files = value ?? new Dictionary<string, FileRecord>();
                _byName = null;
                _byDir  = null;
            }
        }

        public List<FileDiff> Diffs { get; set; } = new();

        // ------------------------------------------------------------------ lazy-cached lookups

        [JsonIgnore] private Dictionary<string, List<FileRecord>>? _byName;
        [JsonIgnore] private Dictionary<string, List<FileRecord>>? _byDir;

        /// <summary>Files grouped by file name (case-sensitive).</summary>
        [JsonIgnore]
        public IReadOnlyDictionary<string, List<FileRecord>> FilesByName =>
            _byName ??= BuildByName();

        /// <summary>Files grouped by their relative directory path.</summary>
        [JsonIgnore]
        public IReadOnlyDictionary<string, List<FileRecord>> FilesByDir =>
            _byDir ??= BuildByDir();

        private Dictionary<string, List<FileRecord>> BuildByName()
        {
            var result = new Dictionary<string, List<FileRecord>>();
            foreach (FileRecord r in _files.Values)
            {
                if (!result.TryGetValue(r.Name, out var list))
                    result[r.Name] = list = new List<FileRecord>();
                list.Add(r);
            }
            return result;
        }

        private Dictionary<string, List<FileRecord>> BuildByDir()
        {
            var result = new Dictionary<string, List<FileRecord>>();
            foreach (FileRecord r in _files.Values)
            {
                string dir = r.RelDir;
                if (!result.TryGetValue(dir, out var list))
                    result[dir] = list = new List<FileRecord>();
                list.Add(r);
            }
            return result;
        }

        // ------------------------------------------------------------------ constructors

        public SnapshotData() { }

        [JsonConstructor]
        public SnapshotData(
            string SnapshotId,
            int Revision,
            string ProjectName,
            string ProjectPath,
            string UpdaterName,
            string MachineId,
            DateTime TakenAt,
            string UpdateLog,
            string ChangeLog,
            int ChangeCount,
            Dictionary<string, FileRecord> Files,
            List<FileDiff> Diffs)
        {
            this.SnapshotId  = SnapshotId;
            this.Revision    = Revision;
            this.ProjectName = ProjectName;
            this.ProjectPath = ProjectPath;
            this.UpdaterName = UpdaterName;
            this.MachineId   = MachineId;
            this.TakenAt     = TakenAt;
            this.UpdateLog   = UpdateLog;
            this.ChangeLog   = ChangeLog;
            this.ChangeCount = ChangeCount;
            this.Files       = Files;
            this.Diffs       = Diffs;
        }

        /// <summary>Deep-copy constructor.</summary>
        public SnapshotData(SnapshotData source)
        {
            SnapshotId  = source.SnapshotId;
            Revision    = source.Revision;
            ProjectName = source.ProjectName;
            ProjectPath = source.ProjectPath;
            UpdaterName = source.UpdaterName;
            MachineId   = source.MachineId;
            TakenAt     = source.TakenAt;
            UpdateLog   = source.UpdateLog;
            ChangeLog   = source.ChangeLog;
            ChangeCount = source.ChangeCount;
            Files       = new Dictionary<string, FileRecord>(
                source.Files.ToDictionary(kv => kv.Key, kv => new FileRecord(kv.Value)));
            Diffs       = source.Diffs.Select(d => new FileDiff(d.Kind,
                d.Before != null ? new FileRecord(d.Before) : null,
                d.After  != null ? new FileRecord(d.After)  : null)).ToList();
        }
    }
}
