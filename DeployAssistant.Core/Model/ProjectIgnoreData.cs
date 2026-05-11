using DeployAssistant.Interfaces;
using System.Text.Json.Serialization;

namespace DeployAssistant.Model
{
    [Flags]
    public enum IgnoreType
    {
        None = 0,
        Integration = 1 ,
        IntegrityCheck = 1 << 1,
        Deploy = 1 << 2,
        Initialization = 1 << 3,
        All = ~0
    }

    /// <summary>
    /// Persisted .ignore configuration for a project.  Pure data — the runtime
    /// match semantics live in <c>DeployAssistant.Filtering.IgnoreFilter</c>
    /// (predicate-based) and the enumeration in <c>ProjectScanner</c>.
    /// </summary>
    public class ProjectIgnoreData
    {
        public string ProjectName { get; set; }
        public List<RecordedFile> IgnoreFileList { get; set; }

        [JsonConstructor]
        public ProjectIgnoreData() { }

        public ProjectIgnoreData(string ProjectName)
        {
            this.ProjectName = ProjectName;
            IgnoreFileList = new List<RecordedFile>()
            {
                new RecordedFile("ProjectMetaData.bin" , ProjectDataType.File, IgnoreType.All),
                new RecordedFile("*.ignore" , ProjectDataType.File, IgnoreType.All),
                new RecordedFile("*.deploy" , ProjectDataType.File, IgnoreType.Deploy | IgnoreType.Initialization | IgnoreType.Integration),
                new RecordedFile("*.VersionLog", ProjectDataType.File, IgnoreType.All),
                new RecordedFile("Export_XLSX", ProjectDataType.Directory, IgnoreType.All),
                new RecordedFile("en-US", ProjectDataType.Directory, IgnoreType.Integration),
                new RecordedFile("ko-KR", ProjectDataType.Directory, IgnoreType.Integration),
                new RecordedFile("Resources", ProjectDataType.Directory, IgnoreType.Integration)
            };
        }

        public void ConfigureDefaultIgnore(string projName)
        {
            string backupDir = $"Backup_{projName}";
            IgnoreFileList.Add(new RecordedFile(backupDir, ProjectDataType.Directory, IgnoreType.IntegrityCheck | IgnoreType.Initialization | IgnoreType.Integration));
            string exportDir = $"Export_{projName}";
            IgnoreFileList.Add(new RecordedFile(exportDir, ProjectDataType.Directory, IgnoreType.IntegrityCheck | IgnoreType.Initialization | IgnoreType.Integration));
        }

        /// <summary>
        /// Idempotently brings well-known default entries up to the current
        /// default flag set.  Legacy <c>.ignore</c> files persisted before
        /// the all-in refactor lack <see cref="IgnoreType.Integration"/> on
        /// <c>*.deploy</c> / <c>Backup_&lt;Name&gt;</c> / <c>Export_&lt;Name&gt;</c>;
        /// without the heal those entries would silently leak into integration
        /// diffs after the refactor made the filter scope-aware.
        ///
        /// User-added custom entries are left untouched — matching is by exact
        /// name against the well-known default-entry names only.
        /// </summary>
        /// <returns>
        /// <c>true</c> if any entry was modified — callers (e.g. SettingManager
        /// on load) should re-persist when <c>true</c>.
        /// </returns>
        public bool EnsureDefaultFlags()
        {
            bool changed = false;
            string backupName = $"Backup_{ProjectName}";
            string exportName = $"Export_{ProjectName}";

            foreach (RecordedFile entry in IgnoreFileList)
            {
                IgnoreType expected = ExpectedFlagsFor(entry, backupName, exportName);
                if (expected == IgnoreType.None) continue;
                if ((entry.IgnoreType & expected) == expected) continue;
                entry.IgnoreType |= expected;
                changed = true;
            }
            return changed;
        }

        private static IgnoreType ExpectedFlagsFor(RecordedFile entry, string backupName, string exportName)
        {
            if (entry.DataType == ProjectDataType.File && entry.DataName == "*.deploy")
                return IgnoreType.Deploy | IgnoreType.Initialization | IgnoreType.Integration;
            if (entry.DataType == ProjectDataType.Directory && entry.DataName == backupName)
                return IgnoreType.IntegrityCheck | IgnoreType.Initialization | IgnoreType.Integration;
            if (entry.DataType == ProjectDataType.Directory && entry.DataName == exportName)
                return IgnoreType.IntegrityCheck | IgnoreType.Initialization | IgnoreType.Integration;
            return IgnoreType.None;
        }
    }
}