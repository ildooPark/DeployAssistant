using DeployAssistant.Interfaces;
using DeployAssistant.Model;
using System.IO;
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
    public class ProjectIgnoreData
    {
        public string ProjectName { get; set; }
        //Following .ignore functionality is designed mostly for the part of Integration Process. 
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
                new RecordedFile("*.deploy" , ProjectDataType.File, IgnoreType.Deploy),
                new RecordedFile("*.VersionLog", ProjectDataType.File, IgnoreType.All),
                new RecordedFile("Export_XLSX", ProjectDataType.Directory, IgnoreType.All),
                new RecordedFile("en-US", ProjectDataType.Directory, IgnoreType.Integration),
                new RecordedFile("ko-KR", ProjectDataType.Directory, IgnoreType.Integration),
                new RecordedFile("Resources", ProjectDataType.Directory, IgnoreType.Integration)
            };
        }

        public void FilterProjectFileList(List<IProjectData> list)
        {

        }

        public void FilterChangedFileList(List<ChangedFile> changedFileList)
        {
            List<ChangedFile> excludingList = []; 
            foreach (ChangedFile file in changedFileList)
            {
                if (file.SrcFile == null || file.SrcFile.DataName == "")
                {
                    if (PartOfIgnore(file.DstFile)) excludingList.Add(file);
                }
                else
                {
                    if (PartOfIgnore(file.SrcFile)) excludingList.Add(file);
                }
            }
            changedFileList = changedFileList.Except(excludingList).ToList();
        }

        public void FilterFilePathList(List<string> filePath, string filePathRoot)
        {

        }

        public void ConfigureDefaultIgnore(string projName)
        {
            string backupDir = $"Backup_{projName}";
            IgnoreFileList.Add(new RecordedFile(backupDir, ProjectDataType.Directory, IgnoreType.IntegrityCheck));
            string exportDir = $"Export_{projName}";
            IgnoreFileList.Add(new RecordedFile(exportDir, ProjectDataType.Directory, IgnoreType.IntegrityCheck));
        }
        
        public (List<string>, List<string>) GetIgnoreFilesAndDirPaths(string searchPath, IgnoreType requestedIgnoreType)
        {
            List<string> excludedFiles = new List<string>();
            List<string> excludedDirs = new List<string>(); 
            try
            {
                foreach (RecordedFile ignoreData in IgnoreFileList)
                {
                    if ((requestedIgnoreType & ignoreData.IgnoreType) == 0) continue; 
                    if (ignoreData.DataType == ProjectDataType.File)
                    {
                        excludedFiles.AddRange(Directory.GetFiles(searchPath, ignoreData.DataName, SearchOption.AllDirectories));
                    }
                    else
                    {
                        var identifiedDirs = Directory.GetDirectories(searchPath, ignoreData.DataName, SearchOption.AllDirectories);
                        if (identifiedDirs == null) continue;
                        foreach (string identifiedDir in identifiedDirs)
                        {
                            excludedDirs.Add(identifiedDir);
                            GetFilesAndDirsFromDirectory(identifiedDir, excludedFiles, excludedDirs);
                        }
                    }
                }
                return (excludedFiles, excludedDirs);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Critical Error while trying to get ignore files for deploy, {ex.Message}");
                return (new List<string>(), new List<string>());
            }
        }
        
        private void GetFilesAndDirsFromDirectory(string directoryPath, List<string> excludedFiles, List<string> excludedDirs)
        {
            try
            {
                var files = Directory.GetFiles(directoryPath, "*.*", SearchOption.AllDirectories);
                excludedFiles.AddRange(files);
                var dirs = Directory.GetDirectories(directoryPath, "*.*", SearchOption.AllDirectories);
                excludedDirs.AddRange(dirs);
            }
            catch (Exception ex)
            {
                // Handle case where access to directory is denied
                MessageBox.Show($"Error: Access denied to directory: {directoryPath} {ex.Message}");
                return;
            }
        }

        private bool PartOfIgnore(ProjectFile projectFile)
        {
            foreach (RecordedFile file in IgnoreFileList)
            {
                if (file.DataType == ProjectDataType.File)
                {
                    if (projectFile.DataName == file.DataName) return true;
                }
                else
                {
                    if (projectFile.DataRelPath.Contains(file.DataName)) return true;
                }
            }
            return false;
        }
    }
}