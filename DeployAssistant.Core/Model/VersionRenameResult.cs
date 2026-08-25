#pragma warning disable CS0618  // ProjectData is a V1 type; version management is V1-shaped.

namespace DeployAssistant.Model
{
    /// <summary>
    /// Outcome of <c>MetaDataManager.RequestRenameVersion</c>.  Renaming re-tags a
    /// snapshot's <c>UpdatedVersion</c> and/or its <c>UpdateLog</c>; it never moves
    /// backup bytes (see the notes on <c>RequestRenameVersion</c>).
    /// </summary>
    public sealed class VersionRenameResult
    {
        public bool Success { get; }
        public ProjectData? Target { get; }
        public string PreviousVersionName { get; }
        public string VersionName { get; }
        public bool VersionNameChanged { get; }
        public bool UpdateLogChanged { get; }
        public IReadOnlyList<string> Messages { get; }

        public VersionRenameResult(bool success, ProjectData? target,
            string previousVersionName, string versionName,
            bool versionNameChanged, bool updateLogChanged,
            IReadOnlyList<string>? messages = null)
        {
            Success = success;
            Target = target;
            PreviousVersionName = previousVersionName;
            VersionName = versionName;
            VersionNameChanged = versionNameChanged;
            UpdateLogChanged = updateLogChanged;
            Messages = messages ?? new List<string>();
        }
    }
}
