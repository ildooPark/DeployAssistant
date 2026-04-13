using DeployAssistant.Interfaces;
using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using System.Windows.Media.Converters;

namespace DeployAssistant.Model
{
    public class ProjectData : IEquatable<ProjectData>, IComparer<ProjectData>, IComparable<ProjectData>
    {
        public string ProjectName { get; set; }
        public string ProjectPath { get; set; }
        public string UpdaterName { get; set; }
        public string ConductedPC { get; set; }
        public DateTime UpdatedTime { get; set; }
        public string UpdatedVersion { get; set; }
        public string UpdateLog { get; set; }
        public string ChangeLog { get; set; }
        public int RevisionNumber { get; set; } = 0;
        public int NumberOfChanges { get; set; }
        public List<ChangedFile> ChangedFiles {  get; set; }
        public Dictionary<string, ProjectFile> ProjectFiles { get; set; }

        [JsonIgnore]
        public bool IsProjectMain { get; set; } = false;  
        public ObservableCollection<ProjectFile> ProjectFilesObs => new ObservableCollection<ProjectFile>(ProjectFiles.Values.ToList());
        [JsonIgnore]
        public List<string> ProjectRelDirsList => ProjectFiles.Values.ToList()
            .Where(file => file.DataType == ProjectDataType.Directory && file.DataRelPath != "")
            .Select(file => file.DataRelPath)
            .ToList();
        [JsonIgnore]
        public List<ProjectFile> ProjectDirFileList => ProjectFiles.Values.ToList()
            .Where(file => file.DataType == ProjectDataType.Directory)
            .ToList();
        [JsonIgnore]
        public List<string> ProjectRelFilePathsList => ProjectFiles.Values.ToList()
            .Where(file => file.DataType == ProjectDataType.File)
            .Select(file => file.DataRelPath)
            .ToList();
        [JsonIgnore]
        public List<ProjectFile> ChangedDstFileList
        {
            get
            {
                List<ProjectFile> changedDstFileList = new List<ProjectFile>();
                foreach (ChangedFile changes in ChangedFiles)
                {
                    if (changes.DstFile != null) changedDstFileList.Add(changes.DstFile);
                }
                return changedDstFileList;
            }
        }
        [JsonIgnore]
        public ObservableCollection<ProjectFile> ChangedProjectFileObservable
        {
            get
            {
                ObservableCollection<ProjectFile> changedFilesObservable = new ObservableCollection<ProjectFile>();
                foreach (ChangedFile changes in ChangedFiles)
                {
                    if (changes.DstFile != null) changedFilesObservable.Add(changes.DstFile);
                    if (changes.SrcFile != null) changedFilesObservable.Add(changes.SrcFile);
                }
                return changedFilesObservable;
            }
        }
        [JsonIgnore]
        public Dictionary<string, List<ProjectFile>> ProjectFilesDict_NameSorted
        {
            get
            {
                Dictionary<string, List<ProjectFile>> namesSorted = new Dictionary<string, List<ProjectFile>>();
                foreach (ProjectFile projFile in ProjectFiles.Values)
                {
                    if (namesSorted.TryGetValue(projFile.DataName, out List<ProjectFile>? projFileList))
                    {
                        projFileList.Add(projFile);
                    }
                    else
                    {
                        namesSorted.Add(projFile.DataName, new List<ProjectFile> { projFile });
                    }
                }
                return namesSorted;
            }
        }
        [JsonIgnore]
        public Dictionary<string, List<ProjectFile>> ProjectFilesDict_RelDirSorted
        {
            get
            {
                Dictionary<string, List<ProjectFile>> relDirSorted = new Dictionary<string, List<ProjectFile>>();
                foreach (ProjectFile projFile in ProjectFiles.Values)
                {
                    if (relDirSorted.TryGetValue(projFile.DataRelDir, out List<ProjectFile>? projFileList))
                    {
                        projFileList.Add(projFile);
                    }
                    else
                    {
                        relDirSorted.Add(projFile.DataRelDir, new List<ProjectFile> { projFile });
                    }
                }
                return relDirSorted;
            }
        }
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        public ProjectData() { }
        [JsonConstructor]
        public ProjectData(string ProjectName, string ProjectPath, string UpdaterName, string ConductedPC, 
            DateTime UpdatedTime, string UpdatedVersion, string UpdateLog, string ChangeLog, 
            int RevisionNumber, int NumberOfChanges, List<ChangedFile> ChangedFiles, Dictionary<string, ProjectFile> ProjectFiles)
        {
            this.ProjectName = ProjectName;
            this.ProjectPath = ProjectPath;
            this.UpdaterName = UpdaterName;
            this.ConductedPC = ConductedPC;
            this.UpdatedTime = UpdatedTime;
            this.UpdatedVersion = UpdatedVersion;
            this.UpdateLog = UpdateLog;
            this.ChangeLog = ChangeLog;
            this.RevisionNumber = RevisionNumber;
            this.NumberOfChanges = NumberOfChanges;
            this.ChangedFiles = ChangedFiles;
            this.ProjectFiles = ProjectFiles;
        }
        public ProjectData(string projectPath)
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        {
            this.ProjectPath = projectPath;
            this.RevisionNumber = 0;
            this.ProjectFiles = new Dictionary<string, ProjectFile>();
            this.ChangedFiles = new List<ChangedFile>();
        }
        public ProjectData(ProjectData srcProjectData)
        {
            this.ProjectName = srcProjectData.ProjectName;
            this.ProjectPath = srcProjectData.ProjectPath;
            this.UpdaterName = srcProjectData.UpdaterName;
            this.UpdatedTime = srcProjectData.UpdatedTime;
            this.UpdatedVersion = srcProjectData.UpdatedVersion;
            this.ConductedPC = srcProjectData.ConductedPC;
            this.UpdateLog = srcProjectData.UpdateLog;
            this.ChangeLog = srcProjectData.ChangeLog;
            this.NumberOfChanges = srcProjectData.NumberOfChanges;
            this.RevisionNumber = srcProjectData.RevisionNumber;
            this.ProjectFiles = srcProjectData.CloneProjectFiles();
            this.ChangedFiles = srcProjectData.CloneChangedFiles();
        }
        public ProjectData(ProjectData srcProjectData, string projectPath, string updaterName, 
            DateTime updateTime, string updatedVersion, string conductedPC, string updateLog, string? changeLog)
        {
            this.ProjectName = srcProjectData.ProjectName;
            this.ProjectPath = projectPath;
            this.UpdaterName = updaterName;
            this.UpdatedTime = updateTime;
            this.UpdatedVersion = updatedVersion;
            this.ConductedPC = conductedPC;
            this.UpdateLog = updateLog;
            this.ChangeLog = changeLog ?? "";
            this.NumberOfChanges = srcProjectData.ChangedFiles.Count;
            this.RevisionNumber = ++srcProjectData.RevisionNumber;
            this.ProjectFiles = srcProjectData.CloneProjectFiles();
            this.ChangedFiles = srcProjectData.CloneChangedFiles();
        }
        public ProjectData(ProjectData srcProjectData, bool IsReverting)
        {
            this.ProjectName = srcProjectData.ProjectName;
            this.ProjectPath = srcProjectData.ProjectPath;
            this.UpdaterName = srcProjectData.UpdaterName;
            this.UpdatedTime = srcProjectData.UpdatedTime;
            this.UpdatedVersion = srcProjectData.UpdatedVersion;
            this.ConductedPC = srcProjectData.ConductedPC;
            this.UpdateLog = srcProjectData.UpdateLog;
            this.ChangeLog = srcProjectData.ChangeLog;
            this.NumberOfChanges = srcProjectData.NumberOfChanges;
            this.RevisionNumber = srcProjectData.RevisionNumber;
            this.ProjectFiles = srcProjectData.CloneProjectFiles();
            if (IsReverting)
            {
                this.ChangedFiles = srcProjectData.CloneChangedFiles();
            }
            else
            {
                this.ChangedFiles = new List<ChangedFile>();
            }
        }

        public bool Equals(ProjectData? other)
        {
            if (other == null) return false;
            return this.UpdatedVersion == other.UpdatedVersion; 
        }

        public int Compare(ProjectData? x, ProjectData? y)
        {
            if (x == null || y == null)
            {
                MessageBox.Show("Invalid Comparison, ProjectData Cannot be Null");
                return 0;
            }
            return x.UpdatedTime.CompareTo(y.UpdatedTime);
        }
        private Dictionary<string, ProjectFile> CloneProjectFiles()
        {
            Dictionary<string, ProjectFile> clone = new Dictionary<string, ProjectFile>();
            foreach (ProjectFile srcFile in ProjectFiles.Values)
            {
                clone.Add(srcFile.DataRelPath, new ProjectFile(srcFile));
            }
            return clone;
        }

        private List<ChangedFile> CloneChangedFiles()
        {
            List<ChangedFile> clone = new List<ChangedFile>();
            foreach (ChangedFile srcChangedFile in ChangedFiles)
            {
                clone.Add(new ChangedFile(srcChangedFile));
            }
            return clone;
        }
        public int CompareTo(ProjectData? other)
        {
            if (other == null)
            {
                MessageBox.Show("Invalid Comparison, ProjectData Cannot be Null");
                return 0;
            }
            return this.UpdatedTime.CompareTo(other.UpdatedTime);
        }

        public void RegisterProjectInfo(Dictionary<string, object> dict)
        {
            dict.Add(nameof(this.ProjectName), this.ProjectName);
            dict.Add(nameof(this.ProjectPath), this.ProjectPath);
            dict.Add(nameof(this.UpdaterName), this.UpdaterName);
            dict.Add(nameof(this.ConductedPC), this.ConductedPC);
            dict.Add(nameof(this.UpdatedTime), this.UpdatedTime);
            dict.Add(nameof(this.UpdatedVersion), this.UpdatedVersion);
            dict.Add(nameof(this.NumberOfChanges), this.NumberOfChanges);
        }

        public void SetProjectFilesSrcPath()
        {
            if (ProjectPath == null)
            {
                MessageBox.Show("Project Path is Null, Couldn't Set Source Data Path for all Project Files");
                return;
            }
            foreach (ProjectFile file in ProjectFiles.Values)
            {
                file.DataSrcPath = ProjectPath;
            }
        }
    }
}