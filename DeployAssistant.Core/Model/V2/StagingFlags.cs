namespace DeployAssistant.Model.V2
{
    /// <summary>
    /// Lifecycle marker flags carried by a <see cref="FileRecord"/>.
    /// Replaces the lifecycle bits of the V1 <c>DataState</c> flags enum
    /// (<c>PreStaged | IntegrityChecked | Backup | Overlapped</c>).
    /// These flags express <em>where</em> in the pipeline a record sits,
    /// not the nature of the change (see <see cref="ChangeKind"/> for that).
    /// </summary>
    [Flags]
    public enum StagingFlags
    {
        None             = 0,
        PreStaged        = 1 << 0,
        IntegrityChecked = 1 << 1,
        Backup           = 1 << 2,
        Overlapped       = 1 << 3
    }
}
