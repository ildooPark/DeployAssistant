using DeployAssistant.Migration;
using System.Text.Json.Serialization;

namespace DeployAssistant.Model.V2
{
    /// <summary>
    /// The top-level persisted container for a project's version history.
    /// Replaces the V1 <c>ProjectMetaData</c> class.
    ///
    /// <para><b>V1 → V2 field translation table</b></para>
    /// <list type="table">
    ///   <listheader><term>V2 property</term><description>V1 property / note</description></listheader>
    ///   <item><term><see cref="SchemaVersion"/></term><description>New — identifies this as a V2 document (value = 2)</description></item>
    ///   <item><term><see cref="ProjectName"/></term><description>ProjectName (unchanged)</description></item>
    ///   <item><term><see cref="ProjectPath"/></term><description>ProjectPath (unchanged)</description></item>
    ///   <item><term><see cref="LocalUpdateCount"/></term><description>LocalUpdateCount (unchanged)</description></item>
    ///   <item><term><see cref="Current"/></term><description>ProjectMain (ProjectData → SnapshotData)</description></item>
    ///   <item><term><see cref="History"/></term><description>ProjectDataList (LinkedList&lt;ProjectData&gt; → List&lt;SnapshotData&gt;)</description></item>
    ///   <item><term><see cref="BackupFiles"/></term><description>BackupFiles (Dictionary&lt;string,ProjectFile&gt; → Dictionary&lt;string,FileRecord&gt;)</description></item>
    /// </list>
    ///
    /// <para>
    /// Using <c>List&lt;SnapshotData&gt;</c> instead of <c>LinkedList&lt;ProjectData&gt;</c>
    /// avoids the node-wrapper artefacts that appear in <c>System.Text.Json</c>
    /// serialisation of <c>LinkedList&lt;T&gt;</c>.
    /// </para>
    /// </summary>
    public sealed class ProjectStore : ISchemaVersion
    {
        // ------------------------------------------------------------------ persisted

        /// <summary>
        /// Always <c>2</c> for this class.  Written into every serialised document
        /// so the deserialiser can detect the schema version without out-of-band
        /// configuration.
        /// </summary>
        public int SchemaVersion { get; init; } = 2;

        public string        ProjectName      { get; set; } = string.Empty;
        public string        ProjectPath      { get; set; } = string.Empty;
        public int           LocalUpdateCount { get; set; }
        public SnapshotData  Current          { get; set; } = new SnapshotData();
        public List<SnapshotData> History     { get; set; } = new List<SnapshotData>();
        /// <summary>
        /// Backup copies of all files seen in <see cref="History"/>, keyed by
        /// their content hash so duplicates across revisions are stored only once.
        /// </summary>
        public Dictionary<string, FileRecord> BackupFiles { get; set; } = new();

        // ------------------------------------------------------------------ constructors

        public ProjectStore() { }

        [JsonConstructor]
        public ProjectStore(
            int SchemaVersion,
            string ProjectName,
            string ProjectPath,
            int LocalUpdateCount,
            SnapshotData Current,
            List<SnapshotData> History,
            Dictionary<string, FileRecord> BackupFiles)
        {
            this.SchemaVersion    = SchemaVersion;
            this.ProjectName      = ProjectName;
            this.ProjectPath      = ProjectPath;
            this.LocalUpdateCount = LocalUpdateCount;
            this.Current          = Current;
            this.History          = History;
            this.BackupFiles      = BackupFiles;
        }

        public ProjectStore(string projectName, string projectPath)
        {
            SchemaVersion    = 2;
            ProjectName      = projectName;
            ProjectPath      = projectPath;
            LocalUpdateCount = 0;
            Current          = new SnapshotData();
            History          = new List<SnapshotData>();
            BackupFiles      = new Dictionary<string, FileRecord>();
        }
    }
}
