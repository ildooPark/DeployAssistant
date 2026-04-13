using DeployAssistant.Model;
using DeployManager.Model;
using DeployAssistant;
using DeployAssistant.DataComponent;
using DeployAssistant.Interfaces;
using DeployAssistant.Model;
using DeployAssistant.Utils;
using System.IO;

namespace DeployManager.DataComponent
{
    [Flags]
    public enum IgnoreFileType
    {
        Directory = 0, 
        File = 1, 
        Integration = 1 << 1, 
        Deploy = 1 << 2
    }
    public class SettingManager : IManager
    {
        public event Action<MetaDataState>? ManagerStateEventHandler;
        public event Action<string>? SetPrevProjectEventHandler;
        public event Action<ProjectIgnoreData>? UpdateIgnoreListEventHandler;
        public ProjectIgnoreData _projectIgnoreData; 

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
                string defaultWindowDocumentPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                DAMetaFilePath = Path.Combine(defaultWindowDocumentPath, _configFilename);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
            _fileHandlerTool = App.FileHandlerTool;
        }
        public void Awake()
        {
            try
            {
                if (File.Exists(DAMetaFilePath))
                {
                    if (!_fileHandlerTool.TryDeserializeJsonData(DAMetaFilePath, out LocalConfigData? localConfigData)) return;
                    var result = MessageBox.Show($"Recent Destination Project Path Found: Proceed with this Destination? {localConfigData.LastOpenedDstPath}",
                        "Import Previous Destination Project", MessageBoxButtons.YesNo);
                    if (result == DialogResult.Yes)
                    {
                        SetPrevProjectEventHandler?.Invoke(localConfigData.LastOpenedDstPath);
                    }
                    else
                    {
                        MessageBox.Show("Couldn't Retrieve ");
                        return;
                    }
                }
            }
            //GetConfigData
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }

        }
        public void RegisterDefaultSettings(string dstPath)
        {
            SetRecentDstDirectory(dstPath);

        }

        public void SetRecentDstDirectory(string dstPath)
        {
            LocalConfigData localConfig = new LocalConfigData(dstPath);
            _fileHandlerTool.TrySerializeJsonData(DAMetaFilePath, localConfig);
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
                MessageBox.Show($"Could not Generate Deployed Mark, {ex.Message}");
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
                    if (!_fileHandlerTool.TryDeserializeJsonData(ignoreMetaFilePath, out ProjectIgnoreData? projectIgnoreData))
                    {
                        MessageBox.Show($"Setting Manager Project Ignore Error, Couldn't Deserialize IgnoreData");
                        return;
                    }
                    else
                    {
                        if (projectIgnoreData == null)
                        {
                            throw new ArgumentNullException(nameof(projectIgnoreData));
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
                        MessageBox.Show($"Setting Manager Project Ignore Error, Couldn't initialize IgnoreData");
                        return;
                    }
                }
                UpdateIgnoreListEventHandler?.Invoke(_projectIgnoreData);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Setting Manager Project Ignore Error {ex.Message}");
            }
        }
        #endregion
    }
}