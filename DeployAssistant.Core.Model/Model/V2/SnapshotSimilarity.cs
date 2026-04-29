namespace DeployAssistant.Model.V2
{
    /// <summary>
    /// Holds the result of comparing a source snapshot against one of the stored
    /// history snapshots.  Replaces the V1 <c>ProjectSimilarity</c> class.
    ///
    /// <para><b>V1 → V2 property translation table</b></para>
    /// <list type="table">
    ///   <listheader><term>V2 property</term><description>V1 property</description></listheader>
    ///   <item><term><see cref="Snapshot"/></term><description>projData (PascalCase rename)</description></item>
    ///   <item><term><see cref="SigDiffCount"/></term><description>numDiffWithoutResources (PascalCase rename)</description></item>
    ///   <item><term><see cref="ResourceDiffCount"/></term><description>numDiffWithResources (PascalCase rename)</description></item>
    ///   <item><term><see cref="Differences"/></term><description>fileDifferences (PascalCase rename + type change)</description></item>
    /// </list>
    /// </summary>
    public sealed class SnapshotSimilarity
    {
        public SnapshotData    Snapshot           { get; set; } = new SnapshotData();
        /// <summary>Number of differing files excluding resource-only files.</summary>
        public int             SigDiffCount       { get; set; }
        /// <summary>Total number of differing files including resource files.</summary>
        public int             ResourceDiffCount  { get; set; }
        public List<FileDiff>  Differences        { get; set; } = new List<FileDiff>();

        public SnapshotSimilarity() { }

        public SnapshotSimilarity(
            SnapshotData snapshot,
            int sigDiffCount,
            int resourceDiffCount,
            List<FileDiff> differences)
        {
            Snapshot          = snapshot;
            SigDiffCount      = sigDiffCount;
            ResourceDiffCount = resourceDiffCount;
            Differences       = differences;
        }
    }
}
