using System;

namespace DeployAssistant.DataComponent
{
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
