using System;

namespace DeployAssistant.DataComponent
{
    /// <summary>
    /// V1 combined state enum that mixes change-kind bits and lifecycle bits.
    /// Use <see cref="DeployAssistant.Model.V2.ChangeKind"/> and
    /// <see cref="DeployAssistant.Model.V2.StagingFlags"/> instead.
    /// </summary>
    [Obsolete("DataState mixes change-kind and lifecycle concerns. " +
              "Use DeployAssistant.Model.V2.ChangeKind for change kind and " +
              "DeployAssistant.Model.V2.StagingFlags for lifecycle flags.")]
    [Flags]
    public enum DataState
    {
        None = 0,
        Added = 1,
        Deleted = 1 << 1,
        Restored = 1 << 2,
        Modified = 1 << 3,
        PreStaged = 1 << 4,
        IntegrityChecked = 1 << 5,
        Backup = 1 << 6,
        Overlapped = 1 << 7
    }
}
