using System.IO;
using System.Text.Json.Serialization;

namespace DeployAssistant.Model
{
    public class ProjectMetaData
    {
        private int localUpdateCount;
        public int LocalUpdateCount { get => localUpdateCount; set => localUpdateCount = value; }  
        private string projectName;
        public string ProjectName { get => projectName; set => projectName = value; }
        private string projectPath; 
        public string ProjectPath { get => projectPath; set => projectPath = value;}
        public ProjectData ProjectMain {  get; set; }
        public LinkedList<ProjectData> ProjectDataList {  get; set; }   
        public Dictionary<string, ProjectFile> BackupFiles { get; set; }
        #region Constructor 
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        [JsonConstructor]
        public ProjectMetaData() { }

        public ProjectMetaData(string projectName, string projectPath)
        {
            this.LocalUpdateCount = 0;
            this.ProjectName = projectName;
            this.ProjectPath = projectPath;
            this.ProjectMain = new ProjectData();
            this.ProjectDataList = new LinkedList<ProjectData>();
            this.BackupFiles = new Dictionary<string, ProjectFile>();
        }

        public void ReconfigureProjectPath(string projectPath)
        {
            this.projectPath = projectPath;
            if (ProjectDataList != null && ProjectDataList.Count > 0)
            {
                foreach (ProjectData backupProjData in ProjectDataList)
                {
                    backupProjData.ProjectPath = projectPath;
                    backupProjData.SetProjectFilesSrcPath();
                }
            }
            ProjectMain.ProjectPath = projectPath;
            ProjectMain.SetProjectFilesSrcPath();
            SetBackupFilesPath();
        }

        public void SetBackupFilesPath()
        {
            string newBackupPath = $"{this.projectPath}\\Backup_{this.projectName}";
            foreach (ProjectFile backupFile in this.BackupFiles.Values )
            {
                string? fileBackupVersion = Path.GetFileName(backupFile.DataSrcPath);
                if (fileBackupVersion == null)
                {
                    MessageBox.Show("BackupPath Invalid!"); return;
                }
                backupFile.DataSrcPath = Path.Combine(newBackupPath, fileBackupVersion); 
            }
        }

        public void SetProjectMain(ProjectData projectData)
        {
            foreach (ProjectData backupData in ProjectDataList)
            {
                backupData.IsProjectMain = false; 
                if (backupData.Equals(projectData))
                    backupData.IsProjectMain = true;
            }
            ProjectMain = projectData;
        }
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        #endregion
        
    }
}