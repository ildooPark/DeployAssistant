using System.IO;
using System.Text.Json.Serialization;

namespace DeployAssistant.Model.V2
{
    /// <summary>
    /// Simplified, immutable record of a single tracked file or directory.
    /// Replaces the V1 <c>ProjectFile</c> class.
    ///
    /// <para><b>V1 → V2 field translation table</b></para>
    /// <list type="table">
    ///   <listheader><term>V2 property</term><description>V1 property</description></listheader>
    ///   <item><term><see cref="Kind"/></term><description>DataType (ProjectDataType enum → FileKind enum)</description></item>
    ///   <item><term><see cref="RelPath"/></term><description>DataRelPath</description></item>
    ///   <item><term><see cref="Name"/></term><description>DataName</description></item>
    ///   <item><term><see cref="SrcPath"/></term><description>DataSrcPath (mutable)</description></item>
    ///   <item><term><see cref="Size"/></term><description>DataSize</description></item>
    ///   <item><term><see cref="Hash"/></term><description>DataHash</description></item>
    ///   <item><term><see cref="BuildVersion"/></term><description>BuildVersion (unchanged)</description></item>
    ///   <item><term><see cref="SnapshotVersion"/></term><description>DeployedProjectVersion</description></item>
    ///   <item><term><see cref="RecordedAt"/></term><description>UpdatedTime</description></item>
    ///   <item><term><see cref="Flags"/></term><description>DataState (lifecycle bits only)</description></item>
    ///   <item><term><em>removed</em></term><description>IsDstFile — UI concern moved to ViewModel layer</description></item>
    ///   <item><term><em>removed</em></term><description>DataState change-kind bits — moved to <see cref="FileDiff.Kind"/></description></item>
    /// </list>
    /// </summary>
    public sealed class FileRecord
    {
        // ------------------------------------------------------------------ persisted

        public FileKind   Kind            { get; init; }
        public string     RelPath         { get; init; }
        public string     Name            { get; init; }
        /// <summary>Mutable: the physical root path changes when a project is moved.</summary>
        public string     SrcPath         { get; set; }
        public long       Size            { get; init; }
        public string     Hash            { get; init; }
        public string     BuildVersion    { get; init; }
        /// <summary>Version label of the snapshot in which this record was deployed.</summary>
        public string     SnapshotVersion { get; set; }
        public DateTime   RecordedAt      { get; init; }
        public StagingFlags Flags         { get; set; }

        // ------------------------------------------------------------------ computed (not persisted)

        [JsonIgnore]
        public string AbsPath => Path.Combine(SrcPath, RelPath);

        [JsonIgnore]
        public string RelDir => Kind == FileKind.Directory
            ? RelPath
            : Path.GetDirectoryName(RelPath) ?? string.Empty;

        // ------------------------------------------------------------------ constructors

#pragma warning disable CS8618
        /// <summary>Parameterless constructor for JSON deserialisation.</summary>
        public FileRecord() { }
#pragma warning restore CS8618

        [JsonConstructor]
        public FileRecord(
            FileKind    Kind,
            string      RelPath,
            string      Name,
            string      SrcPath,
            long        Size,
            string      Hash,
            string      BuildVersion,
            string      SnapshotVersion,
            DateTime    RecordedAt,
            StagingFlags Flags)
        {
            this.Kind            = Kind;
            this.RelPath         = RelPath;
            this.Name            = Name;
            this.SrcPath         = SrcPath;
            this.Size            = Size;
            this.Hash            = Hash;
            this.BuildVersion    = BuildVersion;
            this.SnapshotVersion = SnapshotVersion;
            this.RecordedAt      = RecordedAt;
            this.Flags           = Flags;
        }

        /// <summary>Deep-copy constructor.</summary>
        public FileRecord(FileRecord source)
            : this(source.Kind, source.RelPath, source.Name, source.SrcPath,
                   source.Size, source.Hash, source.BuildVersion,
                   source.SnapshotVersion, source.RecordedAt, source.Flags)
        { }
    }
}
