using DeployAssistant.Interfaces;
using DeployAssistant.Model;
using DeployAssistant.Utils;
using System.Text;

namespace DeployAssistant.DataComponent
{
    public class UpdateManager : IManager
    {
        private ProjectMetaData? _projectMetaData;
        private ProjectData? _projectMain;
        private ProjectData? _srcProjectData;
        private List<ChangedFile>? _currentProjectFileChanges;
        private FileHandlerTool _fileHandlerTool;
        private Dictionary<string, ProjectFile> _backupFiles;

        public event Action<ProjectData, ProjectData, List<ChangedFile>>? ReportFileDifferences;
        public event Action<object>? ProjectUpdateEventHandler;
        public event Action<MetaDataState>? ManagerStateEventHandler;
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        public UpdateManager() 
        {
            _fileHandlerTool = App.FileHandlerTool;
        }
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        public void Awake()
        {
        }
        
        /// <summary>
        /// Requires Updater Name, Log, and Current Project Path
        /// </summary>
        /// <param name="updaterName"></param>
        /// <param name="updateLog"></param>
        public void UpdateProjectMain(string updaterName, string updateLog, string currentProjectPath)
        {
            if (_projectMetaData == null) { MessageBox.Show("Project MetaData on Update Manager is Missing"); return; }
            if (_projectMain == null) { MessageBox.Show("Project Data on Update Manager is Missing"); return; }
            if (_currentProjectFileChanges == null || _currentProjectFileChanges.Count == 0) { MessageBox.Show("File Changes does not exist"); return; }
            if (currentProjectPath != _projectMetaData.ProjectPath) { MessageBox.Show("Project Meta Data Path and Updated Path must match"); return; }
            
            ManagerStateEventHandler?.Invoke(MetaDataState.Updating);
            
            string newVersionName = GetProjectVersionName(_projectMain, _projectMetaData.LocalUpdateCount);
            string conductedPC = Environment.MachineName;
            ProjectData updatedProjectData = new ProjectData(_projectMain);
            RegisterFileChanges(updatedProjectData, currentProjectPath, _currentProjectFileChanges, newVersionName, out StringBuilder ? changeLog);
            
            bool updateSuccess = false;
            while (!updateSuccess)
            {
                updateSuccess = _fileHandlerTool.TryApplyFileChanges(_currentProjectFileChanges);
                if (!updateSuccess)
                {
                    var response = MessageBox.Show("Update Failed, Would you like to Retry?", "Update Project", 
                        MessageBoxButtons.YesNo);
                    if (response == DialogResult.Yes)
                    {
                        continue; 
                    }
                    else
                    {
                        MessageBox.Show("Update Failed, Please Run Version Integrity Test");
                        ManagerStateEventHandler?.Invoke(MetaDataState.Idle);
                        return;
                    }
                }
            }
            ++_projectMetaData.LocalUpdateCount;
            updatedProjectData.ChangedFiles = _currentProjectFileChanges;
            updatedProjectData.ProjectPath = currentProjectPath;
            updatedProjectData.UpdaterName = updaterName;
            updatedProjectData.UpdatedTime = DateTime.Now;
            updatedProjectData.UpdatedVersion = newVersionName;
            updatedProjectData.ConductedPC = conductedPC;
            updatedProjectData.UpdateLog = updateLog;
            updatedProjectData.ChangeLog = changeLog?.ToString() ?? "";
            updatedProjectData.NumberOfChanges = _currentProjectFileChanges.Count;
            updatedProjectData.RevisionNumber = _projectMetaData.LocalUpdateCount;
            ProjectUpdateEventHandler?.Invoke(updatedProjectData);
            ManagerStateEventHandler?.Invoke(MetaDataState.Idle);
        }

        public void MergeProjectMain(string updaterName, string updateLog, string currentProjectPath)
        {
            if (_srcProjectData == null)
            {
                MessageBox.Show("Insert Source Project");
                return;
            }
            if (_currentProjectFileChanges == null || _currentProjectFileChanges.Count == 0) 
            { 
                MessageBox.Show("File Changes does not exist"); return; 
            }

            RegisterFileChanges(_srcProjectData, _projectMain, _currentProjectFileChanges, out StringBuilder? changeLog); 
            ManagerStateEventHandler?.Invoke(MetaDataState.Integrating);
            bool updateSuccess = false;
            while (!updateSuccess)
            {
                updateSuccess = _fileHandlerTool.TryApplyFileChanges(_currentProjectFileChanges);
                if (!updateSuccess)
                {
                    var response = MessageBox.Show("Integration Failed, Would you like to Retry?", "Update Project",
                        MessageBoxButtons.YesNo);
                    if (response == DialogResult.Yes)
                    {
                        continue;
                    }
                    else
                    {
                        MessageBox.Show("Integration Failed, Please Run Version Integrity Test");
                        ManagerStateEventHandler?.Invoke(MetaDataState.Idle);
                        return;
                    }
                }
            }
            ProjectData integratingProjData = new ProjectData(_srcProjectData);
            integratingProjData.ChangedFiles = _currentProjectFileChanges; 
            integratingProjData.ProjectPath = currentProjectPath;
            integratingProjData.SetProjectFilesSrcPath();
            integratingProjData.UpdaterName = updaterName;
            integratingProjData.UpdateLog = updateLog;
            integratingProjData.ChangeLog = changeLog?.ToString() ?? ""; 
            ProjectUpdateEventHandler?.Invoke(integratingProjData);
            ManagerStateEventHandler?.Invoke(MetaDataState.Idle);
        }

        public bool TryIntegrateSrcProject(ProjectData srcProject, List<ChangedFile> fileDifferences)
        {
            ManagerStateEventHandler?.Invoke(MetaDataState.IntegrationValidating);
            List<ChangedFile> validationFailedFiles = []; 
            foreach (ChangedFile changedFile in fileDifferences)
            {
                if ((changedFile.DataState & DataState.Added | DataState.Modified) != 0)
                {
                    if (changedFile.SrcFile.DataType == ProjectDataType.Directory) continue; 
                    if (!ValidateSrcFile(changedFile.SrcFile))
                        validationFailedFiles.Add(changedFile);
                }
            }
            ManagerStateEventHandler?.Invoke(MetaDataState.Idle);
            if (validationFailedFiles.Count > 0)
            {
                ReportFileDifferences?.Invoke(srcProject, _projectMain, validationFailedFiles);
                return false;
            }
            return true;
        }

        private void RegisterFileChanges(ProjectData currentProject, string currentProjectPath, List<ChangedFile> fileChanges, string newProjectVersion, out StringBuilder? changeLog)
        {
            if (fileChanges.Count <= 0)
            {
                changeLog = null;
                return;
            }
            StringBuilder newChangeLog = new StringBuilder();
            LogTool.RegisterUpdate(newChangeLog, currentProject.UpdatedVersion, newProjectVersion);
            foreach (ChangedFile changes in fileChanges)
            {
                if (changes.DstFile == null) continue;
                if (!currentProject.ProjectFiles.TryGetValue(changes.DstFile.DataRelPath, out ProjectFile? existingFile))
                {
                    currentProject.ProjectFiles.Add(changes.DstFile.DataRelPath, new ProjectFile(changes.DstFile, newProjectVersion, currentProjectPath));
                    if (changes.SrcFile != null)
                        changes.SrcFile.DeployedProjectVersion = currentProject.UpdatedVersion;
                }
                else
                {
                    if ((changes.DataState & DataState.Deleted) != 0)
                    {
                        currentProject.ProjectFiles.Remove(changes.DstFile.DataRelPath);
                    }
                    else
                    {
                        currentProject.ProjectFiles[changes.DstFile.DataRelPath] = new ProjectFile(changes.DstFile, newProjectVersion, currentProjectPath);
                    }
                }
                    LogTool.RegisterChange(newChangeLog, changes.DataState, changes.SrcFile, changes.DstFile);
                currentProject.NumberOfChanges++;
            }
            changeLog = newChangeLog; 
        }

        private void RegisterFileChanges(ProjectData srcProj, ProjectData dstProj, List<ChangedFile> fileChanges, out StringBuilder? changeLog)
        {
            if (fileChanges.Count <= 0)
            {
                changeLog = null;
                return;
            }
            srcProj.NumberOfChanges = 0; 
            StringBuilder newChangeLog = new StringBuilder();
            LogTool.RegisterUpdate(newChangeLog, dstProj.UpdatedVersion, srcProj.UpdatedVersion);
            foreach (ChangedFile changes in fileChanges)
            {
                if (changes.DstFile == null) continue;
                changes.DstFile.DeployedProjectVersion = srcProj.UpdatedVersion;
                if (srcProj.ProjectFiles.TryGetValue(changes.DstFile.DataRelPath, out ProjectFile? projectFile))
                {
                    projectFile.DataState = changes.DataState;
                }
                if ((changes.DataState & DataState.Added) != 0)
                {
                    LogTool.RegisterChange(newChangeLog, changes.DataState, changes.DstFile); 
                }
                else
                {
                    LogTool.RegisterChange(newChangeLog, changes.DataState, changes.SrcFile, changes.DstFile);
                }
                srcProj.NumberOfChanges++;
            }
            changeLog = newChangeLog;
        }
        private bool ValidateSrcFile(ProjectFile srcFile)
        {
            // Due to application for changedfile view in simplified backup log, Destination file represents deploying file. in Modified file list.
            if (_currentProjectFileChanges == null) return false;
            if (srcFile == null || srcFile.DataName == "") return false;

            foreach (ChangedFile stagedChanges in _currentProjectFileChanges)
            {
                if (stagedChanges.DstFile.DataName == srcFile.DataName
                    && stagedChanges.DstFile.DataHash == srcFile.DataHash)
                {
                    return true; 
                }
            }
            return false; 
        }
        private string GetProjectVersionName(ProjectData projData, int currentUpdateCount)
        {
            return $"{projData.ProjectName}_{Environment.MachineName}_{DateTime.Now.ToString("yyyy_MM_dd")}_v{currentUpdateCount + 1}";
        }
        #region MetaData CallBack 
        public void MetaDataManager_StagedChangesCallBack(object fileChangeListObj)
        {
            if (fileChangeListObj is not List<ChangedFile> fileChangesList) return;
            _currentProjectFileChanges = fileChangesList;
        }
        public void MetaDataManager_MetaDataLoadedCallBack(object projMetaDataObj)
        {
            if (projMetaDataObj is not ProjectMetaData projectMetaData) return;
            _projectMetaData = projectMetaData;
            _backupFiles = projectMetaData.BackupFiles;
        }
        public void MetaDataManager_ProjLoadedCallback(object obj)
        {
            if (obj is not ProjectData loadedProject)
            {
                MessageBox.Show("Invalid Parameter has entered on Update Manager");
                return;
            }
            _projectMain = loadedProject;
            _currentProjectFileChanges = null; 
            _srcProjectData = null;
        }
        public void MetaDataManager_SrcProjectLoadedCallBack(object srcProjDataObj)
        {
            if (srcProjDataObj is not ProjectData srcProjectData)
            {
                return;
            }
            this._srcProjectData = srcProjectData;
        }
        #endregion
    }
}