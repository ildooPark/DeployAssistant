namespace DeployAssistant.Model
{
    public enum VersionDeleteOutcome
    {
        /// <summary>Version removed from the list, metadata persisted.</summary>
        Deleted,
        /// <summary>Refused before any mutation — see <see cref="VersionDeleteResult.Messages"/>.</summary>
        Blocked,
        /// <summary>Attempted but rolled back; metadata and disk are unchanged.</summary>
        Failed
    }

    /// <summary>
    /// Outcome of <c>BackupManager.DeleteVersion</c>.  Delivered through
    /// <c>MetaDataManager.VersionDeleteCompleteEventHandler</c> on every path
    /// (refusal, failure and success) so callers never wait on a silent request.
    /// </summary>
    public sealed class VersionDeleteResult
    {
        public VersionDeleteOutcome Outcome { get; }
        public string VersionName { get; }
        public long BytesReclaimed { get; }
        public int BackupEntriesRemoved { get; }
        public int FilesRelocated { get; }
        public IReadOnlyList<string> Messages { get; }
        public bool Success => Outcome == VersionDeleteOutcome.Deleted;

        public VersionDeleteResult(VersionDeleteOutcome outcome, string versionName,
            long bytesReclaimed = 0, int backupEntriesRemoved = 0, int filesRelocated = 0,
            IReadOnlyList<string>? messages = null)
        {
            Outcome = outcome;
            VersionName = versionName;
            BytesReclaimed = bytesReclaimed;
            BackupEntriesRemoved = backupEntriesRemoved;
            FilesRelocated = filesRelocated;
            Messages = messages ?? new List<string>();
        }
    }
}
