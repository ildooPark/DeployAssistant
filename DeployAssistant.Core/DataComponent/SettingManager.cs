using DeployAssistant.DataComponent;
using DeployAssistant.Model;
using DeployAssistant.Services;
using DeployAssistant.Utils;
using System.Diagnostics;
using System.IO;

namespace DeployAssistant.DataComponent
{
    [Flags]
    public enum IgnoreFileType
    {
        Directory = 0, 
        File = 1, 
        Integration = 1 << 1, 
        Deploy = 1 << 2
    }
    public class SettingManager
    {
        public event Action<MetaDataState>? ManagerStateEventHandler;
        public event Action<string>? SetPrevProjectEventHandler;
        /// <summary>
        /// Fired after the per-project <see cref="ProjectIgnoreData"/> is loaded
        /// (or freshly constructed and persisted).  Carries both the metaData
        /// that triggered the load and the resolved ignoreData so the consumer
        /// can compose them into a single <c>ProjectContext</c>.
        /// </summary>
        public event Action<ProjectMetaData, ProjectIgnoreData>? IgnoreDataLoadedEventHandler;
        public ProjectIgnoreData _projectIgnoreData; 

        public IDialogService DialogService { get; set; } = new NullDialogService();

        /// <summary>
        /// Test seam: redirects DeployAssistant.config away from the user's real Documents
        /// folder. Without it, test runs read the developer's live settings (a saved
        /// fast-check sample percent broke version-cut tests nondeterministically) and
        /// pollute them back.
        /// </summary>
        public static string? ConfigDirectoryOverride { get; set; }

        private readonly string? DAMetaFilePath;
        private string? ignoreMetaFilePath;
        
        private const string _configFilename = "DeployAssistant.config";
        private const string _projIgnoreFilename = "DeployAssistant.ignore";
        private const string _projDeployFilename = "DeployAssistant.deploy";
        private FileHandlerTool _fileHandlerTool;
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        public SettingManager()
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        {
            try
            {
                string configDir = ConfigDirectoryOverride
                    ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                DAMetaFilePath = Path.Combine(configDir, _configFilename);
            }
            catch (Exception ex)
            {
                Trace.TraceWarning("SettingManager ctor: " + ex.Message);
            }
            _fileHandlerTool = new FileHandlerTool();
        }
        public void Awake()
        {
            // Wiring only. The previous-project prompt runs later via
            // PromptPreviousProjectRestore(), after the UI has subscribed to
            // ProjLoadedEventHandler — prompting here loads the project before
            // any ViewModel exists to hear about it.
        }

        public void PromptPreviousProjectRestore()
        {
            try
            {
                if (!File.Exists(DAMetaFilePath)) return;
                if (!_fileHandlerTool.TryDeserializeJsonData(DAMetaFilePath, out LocalConfigData? localConfigData)) return;

                string? lastPath = localConfigData?.LastOpenedDstPath;
                if (string.IsNullOrWhiteSpace(lastPath))
                {
                    Trace.TraceWarning("SettingManager.Awake: stored LastOpenedDstPath is empty");
                    return;
                }

                // Reject obviously-invalid stored paths (e.g. drive root like "C:" left over
                // from earlier tests). A real project directory contains a ProjectMetaData.bin.
                string projectMetaFile = Path.Combine(lastPath, "ProjectMetaData.bin");
                if (!Directory.Exists(lastPath) || !File.Exists(projectMetaFile))
                {
                    Trace.TraceWarning($"SettingManager.Awake: stored path '{lastPath}' is not a valid project directory; skipping restore");
                    return;
                }

                bool proceed = DialogService.Confirm(
                    "Import Previous Destination Project",
                    $"Recent Destination Project Path Found: Proceed with this Destination? {lastPath}") == DialogChoice.Yes;
                if (proceed)
                {
                    SetPrevProjectEventHandler?.Invoke(lastPath);
                }
                else
                {
                    Trace.TraceWarning("SettingManager.Awake: user declined previous-project restore");
                }
            }
            catch (Exception ex)
            {
                Trace.TraceWarning("SettingManager.Awake: " + ex.ToString());
            }
        }
        public void RegisterDefaultSettings(string dstPath)
        {
            SetRecentDstDirectory(dstPath);

        }

        private const int RecentProjectsCap = 8;

        public void SetRecentDstDirectory(string dstPath)
        {
            LocalConfigData localConfig = ReadConfigOrNew();
            localConfig.LastOpenedDstPath = dstPath;
            List<string> recents = localConfig.RecentProjects ?? new List<string>();
            recents.RemoveAll(p => string.Equals(p, dstPath, StringComparison.OrdinalIgnoreCase));
            recents.Insert(0, dstPath);
            if (recents.Count > RecentProjectsCap) recents.RemoveRange(RecentProjectsCap, recents.Count - RecentProjectsCap);
            localConfig.RecentProjects = recents;
            _fileHandlerTool.TrySerializeJsonData(DAMetaFilePath, localConfig);
        }

        public List<string> GetRecentProjects() => ReadConfigOrNew().RecentProjects ?? new List<string>();

        public int GetFastIntegritySamplePercent()
        {
            int value = ReadConfigOrNew().FastIntegritySamplePercent ?? 100;
            return value < 1 ? 1 : value > 100 ? 100 : value;
        }

        public void SaveFastIntegritySamplePercent(int percent)
        {
            LocalConfigData localConfig = ReadConfigOrNew();
            localConfig.FastIntegritySamplePercent = percent < 1 ? 1 : percent > 100 ? 100 : percent;
            _fileHandlerTool.TrySerializeJsonData(DAMetaFilePath, localConfig);
        }

        public string? GetSavedLanguage() => ReadConfigOrNew().Language;

        public void SaveLanguage(string languageCode)
        {
            LocalConfigData localConfig = ReadConfigOrNew();
            localConfig.Language = languageCode;
            _fileHandlerTool.TrySerializeJsonData(DAMetaFilePath, localConfig);
        }

        private LocalConfigData ReadConfigOrNew()
        {
            try
            {
                if (File.Exists(DAMetaFilePath)
                    && _fileHandlerTool.TryDeserializeJsonData(DAMetaFilePath, out LocalConfigData? cfg)
                    && cfg != null)
                    return cfg;
            }
            catch (Exception ex)
            {
                Trace.TraceWarning("SettingManager.ReadConfigOrNew: " + ex.Message);
            }
            return new LocalConfigData(null);
        }

        public void GenerateDefaultProjIgnore(ProjectData projData)
        {

        }
        #region Request Calls
        public void RequestIgnore(string ignoreObj, IgnoreFileType ignoreType)
        {

        }

        public void RegisterSrcDeploy(string deployPath, Dictionary<string, ProjectFile> registeredFiles)
        {
            try
            {
                string deployFilePath = Path.Combine(deployPath, _projDeployFilename);
                DeployData deployData = new DeployData(_projectIgnoreData.ProjectName, registeredFiles);
                _fileHandlerTool.TrySerializeJsonData(deployFilePath, deployData);
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"Could not Generate Deployed Mark, {ex.Message}");
            }
        }
        #endregion

        #region CallBacks 
        public void MetaDataManager_MetaDataLoadedCallBack(object projectMetaDataObj)
        {
            if (projectMetaDataObj is not ProjectMetaData projectMetaData) return;
            ignoreMetaFilePath = Path.Combine(projectMetaData.ProjectPath, _projIgnoreFilename);
            
            try
            {
                if (File.Exists(ignoreMetaFilePath))
                {
                    if (!_fileHandlerTool.TryDeserializeJsonData(ignoreMetaFilePath, out ProjectIgnoreData? projectIgnoreData)
                        || projectIgnoreData == null)
                    {
                        // An unreadable .ignore must never leave the session without an ignore
                        // context: every later integrity check would silently never complete.
                        // Preserve the corrupt file for inspection, rebuild defaults, tell the user.
                        Trace.TraceWarning($"Setting Manager Project Ignore Error, Couldn't Deserialize IgnoreData; regenerating defaults");
                        try { File.Copy(Utils.PathCompat.ToNetFrameworkLongPath(ignoreMetaFilePath), Utils.PathCompat.ToNetFrameworkLongPath(ignoreMetaFilePath + ".bak"), overwrite: true); }
                        catch (Exception bakEx) { Trace.TraceWarning($"Could not back up corrupt ignore file: {bakEx.Message}"); }
                        projectIgnoreData = new ProjectIgnoreData(projectMetaData.ProjectName);
                        projectIgnoreData.ConfigureDefaultIgnore(projectMetaData.ProjectName);
                        _fileHandlerTool.TrySerializeJsonData(ignoreMetaFilePath, projectIgnoreData);
                        _projectIgnoreData = projectIgnoreData;
                        DialogService.Inform("Ignore File",
                            $"DeployAssistant.ignore could not be read and was regenerated with defaults.\n" +
                            $"Custom entries were lost; the unreadable file was kept as DeployAssistant.ignore.bak.\n{ignoreMetaFilePath}");
                    }
                    else
                    {
                        // Self-heal legacy .ignore files that were persisted before the
                        // default flag set was expanded.  Re-persist only if anything
                        // actually changed, to keep file mtime stable for unchanged files.
                        if (projectIgnoreData.EnsureDefaultFlags())
                        {
                            if (!_fileHandlerTool.TrySerializeJsonData(ignoreMetaFilePath, projectIgnoreData))
                            {
                                Trace.TraceWarning($"Setting Manager Project Ignore: heal succeeded in memory but failed to re-persist {ignoreMetaFilePath}");
                            }
                        }
                        _projectIgnoreData = projectIgnoreData;
                    }
                }
                else
                {
                    _projectIgnoreData = new ProjectIgnoreData(projectMetaData.ProjectName);
                    _projectIgnoreData.ConfigureDefaultIgnore(projectMetaData.ProjectName);
                    if (!_fileHandlerTool.TrySerializeJsonData(ignoreMetaFilePath, _projectIgnoreData))
                    {
                        Trace.TraceWarning($"Setting Manager Project Ignore Error, Couldn't initialize IgnoreData");
                        return;
                    }
                }
                IgnoreDataLoadedEventHandler?.Invoke(projectMetaData, _projectIgnoreData);
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"Setting Manager Project Ignore Error {ex.Message}");
            }
        }
        #endregion
    }
}