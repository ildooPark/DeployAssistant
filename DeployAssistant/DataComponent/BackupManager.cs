using DeployAssistant.Interfaces;
using DeployAssistant.Model;
using DeployAssistant.Utils;
using System.Collections.ObjectModel;
using System.IO;

namespace DeployAssistant.DataComponent
{
    public class BackupManager : IManager
    {
        public ProjectMetaData? ProjectMetaData { get; set; }
        public Dictionary<string, ProjectFile>? BackupFiles => ProjectMetaData?.BackupFiles;
        private LinkedList<ProjectData>? BackupProjectDataList => ProjectMetaData?.ProjectDataList;
        public ObservableCollection<ProjectData>? ProjectBackupListObservable
        {
            get
            {
                if (BackupProjectDataList == null) return null; 
                return new ObservableCollection<ProjectData>(BackupProjectDataList);
            }
        }

        //public event Action? ExportBackupEventHandler; 
        public event Action<object>? ProjectRevertEventHandler;
        public event Action<object>? FetchCompleteEventHandler;
        public event Action<MetaDataState> ManagerStateEventHandler;

        private FileHandlerTool _fileHandlerTool;
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        public BackupManager()
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        {
            _fileHandlerTool = new FileHandlerTool();
        }
        public void Awake(){}
        private void BackupProject(ProjectData projectData)
        {
            if (BackupProjectDataList == null || ProjectMetaData == null)
            { 
                Console.WriteLine("Failed to Load ProjectMetaData: BackupProjectList is Null"); 
                return; 
            }
            bool hasBackup = BackupProjectDataList.Contains(projectData);
            if (!hasBackup)
            {
                RegisterBackupFiles(projectData);
                ProjectMetaData.ProjectDataList.AddFirst(new ProjectData(projectData));
            }

            string projectMetaDataPath = $"{ProjectMetaData.ProjectPath}\\ProjectMetaData.bin";
            bool serializeSuccess = _fileHandlerTool.TrySerializeProjectMetaData(ProjectMetaData, projectMetaDataPath);
            if (serializeSuccess)
            {
                ProjectMetaData.SetProjectMain(projectData);
            }
            FetchCompleteEventHandler?.Invoke(ProjectBackupListObservable);
        }
        #region Callbacks
        /// <summary>
        /// By Default should Point to ProjectMain, Make backup of the ucrrent project main before applying new changes. 
        /// </summary>
        public void MetaDataManager_ProjLoadedCallback (object? projectObj)
        {
            if (projectObj is not ProjectData newMainProject) return;
            if (ProjectMetaData == null || BackupProjectDataList == null || BackupFiles == null) return;
            BackupProject(newMainProject);
        }
        public void MetaDataManager_MetaDataLoadedCallBack(object metaDataObj)
        {
            if (metaDataObj is not ProjectMetaData projectMetaData) return;
            if (projectMetaData == null) return;
            this.ProjectMetaData = projectMetaData;
        }

        #endregion
        private void RegisterBackupFiles(ProjectData projectData)
        {
            if (BackupFiles == null) return;
            string backupSrcPath = GetFileBackupSrcPath(projectData);
            int backupCount = 0; 
            if (!Directory.Exists(backupSrcPath)) Directory.CreateDirectory(backupSrcPath);
            foreach (ChangedFile changes in projectData.ChangedFiles)
            {
                if (changes.DstFile == null) continue; 
                if (changes.DstFile.DataType == ProjectDataType.Directory) continue;
                if (!BackupFiles.TryGetValue(changes.DstFile.DataHash, out ProjectFile? backupFile))
                {
                    ProjectFile newBackupFile = new ProjectFile(changes.DstFile, DataState.Backup, backupSrcPath);
                    BackupFiles.Add(newBackupFile.DataHash, newBackupFile);
                    _fileHandlerTool.HandleData(changes.DstFile.DataAbsPath, newBackupFile.DataAbsPath, ProjectDataType.File, DataState.Backup);
                    newBackupFile.DataSrcPath = backupSrcPath;
                    if (changes.SrcFile != null)
                        changes.SrcFile.DataSrcPath = backupSrcPath;
                    changes.DstFile.DeployedProjectVersion = projectData.UpdatedVersion;
                    backupCount++;
                }
                else
                {
                    if (changes.SrcFile != null)
                        changes.SrcFile.DataSrcPath = backupFile.DataSrcPath;
                    changes.DstFile.DataSrcPath = projectData.ProjectPath;
                    changes.DstFile.DeployedProjectVersion = projectData.UpdatedVersion;
                }
            }
            if (backupCount <= 0) Directory.Delete(backupSrcPath, true);
        }

        public string GetFileBackupSrcPath(ProjectData projectData)
        {
            string backupPath = $"{projectData.ProjectPath}\\Backup_{Path.GetFileName(projectData.ProjectName)}\\Backup_{projectData.UpdatedVersion}";
            return backupPath; 
        }

        #region Link To View Model 
        public bool FetchBackupProjectList()
        {
            if (ProjectMetaData == null || ProjectMetaData.ProjectDataList == null) return false;
            FetchCompleteEventHandler?.Invoke(ProjectBackupListObservable);
            return true;
        }
        public string GetBackupFilePath(ProjectFile projectFile)
        {
            if (BackupFiles == null)
            {
                MessageBox.Show("Backupfiles is null for BackupManager");
                return "";
            }
            BackupFiles.TryGetValue(projectFile.DataHash, out ProjectFile? backupFile);
            return backupFile != null ? backupFile.DataAbsPath : "";
        }
        public void RevertProject(ProjectData revertingProjectData, List<ChangedFile>? FileDifferences)
        {
            try
            {
                bool revertSuccess = false; 
                ProjectData revertedData = new ProjectData(revertingProjectData, true);
                while (!revertSuccess)
                {
                    revertSuccess = _fileHandlerTool.TryApplyFileChanges(FileDifferences);
                    if (!revertSuccess)
                    {
                        var response = MessageBox.Show("Update Failed, Would you like to Retry?", "Update Project",
                            MessageBoxButtons.YesNo);
                        if (response == DialogResult.Yes)
                        {
                            continue;
                        }
                        else
                        {
                            MessageBox.Show("Revert Failed"); return;
                        }
                    }
                }
                ProjectRevertEventHandler?.Invoke(revertingProjectData);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"BUVM RevertProject {ex.Message}");
            }
        }
        #endregion
    }
}