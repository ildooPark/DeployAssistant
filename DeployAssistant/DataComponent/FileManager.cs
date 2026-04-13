using DeployAssistant.Model;
using DeployAssistant.Interfaces;
using DeployAssistant.Model;
using DeployAssistant.Utils;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using WPF = System.Windows;

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
    public class FileManager : IManager
    {
        #region Class Variables 
        private Dictionary<string, ProjectFile> _backupFilesDict;
        /// <summary>
        /// Imported ProjectMainFilesDict
        /// </summary>
        private Dictionary<string, ProjectFile> _projectFilesDict;
        /// <summary>
        /// Imported ProjectMainFilesDict List of ProjectFiles with Identical Name
        /// </summary>
        private Dictionary<string, List<ProjectFile>> _projectFilesDict_namesSorted;
        private Dictionary<string, List<ProjectFile>> _projectFilesDict_relDirSorted;
        private List<ProjectFile> _projDirFileList;
        /// <summary>
        /// Key: DataRelPath Value: ProjectFile
        /// </summary>
        private Dictionary<string, ProjectFile> _preStagedFilesDict;
        /// <summary>
        /// Key: DstFile DataRelPath Value: ChangedFile
        /// </summary>
        private Dictionary<string, ChangedFile> _registeredChangesDict; 
        private SemaphoreSlim _asyncControl;
        private FileHandlerTool _fileHandlerTool;
        private HashTool _hashTool; 
        private ProjectData? _dstProjectData;
        private ProjectData? _srcProjectData;
        private ProjectIgnoreData? _projIgnoreData;
        #endregion

        #region Manager Events 
        public event Action<ProjectData?>? SrcProjectDataLoadedEventHandler;
        public event Action<List<ChangedFile>, List<ChangedFile>>? OverlappedFileFoundEventHandler;
        public event Action<object>? DataStagedEventHandler;
        public event Action<object>? DataPreStagedEventHandler;
        public event Action<object>? PreStagedDataOverlapEventHandler;
        public event Action<string, List<ProjectFile>>? IntegrityCheckEventHandler;
        public event Action<MetaDataState> ManagerStateEventHandler;
        #endregion

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        public FileManager()
        {
            _projectFilesDict = new Dictionary<string, ProjectFile>();
            _projectFilesDict_namesSorted = new Dictionary<string, List<ProjectFile>>();
            _projDirFileList = new List<ProjectFile>();
            _preStagedFilesDict = new Dictionary<string, ProjectFile>();
            _registeredChangesDict = new Dictionary<string, ChangedFile>();
            _fileHandlerTool = App.FileHandlerTool;
            _hashTool = App.HashTool;
            _asyncControl = new SemaphoreSlim(12);
        }
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        public void Awake()
        {
        }
        #region Calls For File Differences
        public async void MainProjectIntegrityCheck()
        {
            ManagerStateEventHandler?.Invoke(MetaDataState.IntegrityChecking);
            if (_dstProjectData == null || _projIgnoreData == null)
            {
                ManagerStateEventHandler?.Invoke(MetaDataState.Idle);
                MessageBox.Show("Main Project is Missing");
                return;
            }
            _preStagedFilesDict.Clear();

            try
            {
                Stopwatch sw = new Stopwatch(); 
                sw.Start();
                StringBuilder fileIntegrityLog = new StringBuilder();
                List<ChangedFile> fileChanges = new List<ChangedFile>();
                fileIntegrityLog.AppendLine($"Conducting Version Integrity Check on {_dstProjectData.UpdatedVersion}");

                Dictionary<string, ProjectFile> projectFilesDict = _dstProjectData.ProjectFiles;
                List<string> recordedFiles = _dstProjectData.ProjectRelFilePathsList;
                List<string> recordedDirs = _dstProjectData.ProjectRelDirsList;

                List<string> directoryRelFiles = [];
                List<string> directoryRelDirs = [];

                var ignoringFilesAndDirsTask = Task.Run(() => 
                    _projIgnoreData.GetIgnoreFilesAndDirPaths(_dstProjectData.ProjectPath, IgnoreType.IntegrityCheck)); 

                var rawFilesTask = Task.Run(() => Directory.GetFiles(_dstProjectData.ProjectPath, "*", SearchOption.AllDirectories));
                var rawDirsTask = Task.Run(() => Directory.GetDirectories(_dstProjectData.ProjectPath, "*", SearchOption.AllDirectories));

                string[]? rawFiles = await rawFilesTask; rawFiles ??= [];
                string[]? rawDirs = await rawDirsTask; rawDirs ??= [];
                (List<string> excludingFiles, List<string> excludingDirs) = await ignoringFilesAndDirsTask;

                IEnumerable<string> directoryFiles = rawFiles.ToList().Except(excludingFiles);
                IEnumerable<string> directoryDirs = rawDirs.ToList().Except(excludingDirs); 
                fileIntegrityLog.AppendLine(sw.Elapsed.ToString());

                foreach (string absPathFile in directoryFiles)
                {
                    directoryRelFiles.Add(Path.GetRelativePath(_dstProjectData.ProjectPath, absPathFile));
                }

                foreach (string absPathDir in directoryDirs)
                {
                    directoryRelDirs.Add(Path.GetRelativePath(_dstProjectData.ProjectPath, absPathDir));
                }

                IEnumerable<string> addedFiles = directoryRelFiles.Except(recordedFiles);
                IEnumerable<string> addedDirs = directoryRelDirs.Except(recordedDirs);
                IEnumerable<string> deletedFiles = recordedFiles.Except(directoryRelFiles);
                IEnumerable<string> deletedDirs = recordedDirs.Except(directoryRelDirs);
                IEnumerable<string> intersectFiles = recordedFiles.Intersect(directoryRelFiles);

                foreach (string dirRelPath in addedDirs)
                {
                    ProjectFile dstFile = new ProjectFile(_dstProjectData.ProjectPath, dirRelPath, null, DataState.Added | DataState.IntegrityChecked, ProjectDataType.Directory);
                    ChangedFile newChange = new ChangedFile(dstFile, DataState.Added | DataState.IntegrityChecked);
                    _registeredChangesDict.TryAdd(dstFile.DataRelPath, newChange);
                    _preStagedFilesDict.TryAdd(dirRelPath, dstFile);
                }

                foreach (string dirRelPath in deletedDirs)
                {
                    ProjectFile dstFile = new ProjectFile(_dstProjectData.ProjectPath, dirRelPath, null, DataState.Deleted | DataState.IntegrityChecked, ProjectDataType.Directory);
                    ChangedFile newChange = new ChangedFile(dstFile, DataState.Deleted | DataState.IntegrityChecked);
                    _preStagedFilesDict.TryAdd(dirRelPath, dstFile);
                    _registeredChangesDict.TryAdd(dstFile.DataRelPath, newChange);
                }

                foreach (string fileRelPath in addedFiles)
                {
                    fileIntegrityLog.AppendLine($"{fileRelPath} has been Added");
                    string? fileHash = _hashTool.GetFileMD5CheckSum(_dstProjectData.ProjectPath, fileRelPath);
                    ProjectFile dstFile = new ProjectFile(_dstProjectData.ProjectPath, fileRelPath, fileHash, DataState.Added | DataState.IntegrityChecked, ProjectDataType.File);
                    _preStagedFilesDict.TryAdd(fileRelPath, dstFile);
                    _registeredChangesDict.TryAdd(dstFile.DataRelPath, new ChangedFile(dstFile, DataState.Added | DataState.IntegrityChecked));
                }

                foreach (string fileRelPath in deletedFiles)
                {
                    fileIntegrityLog.AppendLine($"{fileRelPath} has been Deleted");
                    ProjectFile srcFile = new ProjectFile(projectFilesDict[fileRelPath], DataState.None);
                    ProjectFile dstFile = new ProjectFile(projectFilesDict[fileRelPath], DataState.Deleted | DataState.IntegrityChecked);
                    dstFile.UpdatedTime = DateTime.Now;
                    _preStagedFilesDict.TryAdd(fileRelPath, dstFile);
                    _registeredChangesDict.TryAdd(dstFile.DataRelPath, new ChangedFile(srcFile, dstFile, DataState.Deleted | DataState.IntegrityChecked, true));
                }

                ConcurrentDictionary<string, ProjectFile> projectFilesConcurrent = new ConcurrentDictionary<string, ProjectFile> ();
                var maxConcurrency = new ParallelOptions { MaxDegreeOfParallelism = Convert.ToInt32(Math.Ceiling((Environment.ProcessorCount * 0.75) * 1.0)) };
                Parallel.ForEach(intersectFiles, maxConcurrency, fileRelPath =>
                {
                    //TODO : Resolve Hard coded issue -> Setting Manager Ignore
                    if (projectFilesDict[fileRelPath].DataType == ProjectDataType.Directory) return;
                    ProjectFile intersectedFile = new ProjectFile(projectFilesDict[fileRelPath]);
                    if (!projectFilesConcurrent.TryAdd(fileRelPath, intersectedFile))
                    {
                        ManagerStateEventHandler?.Invoke(MetaDataState.Idle);
                        MessageBox.Show($"Couldn't Run File Integrity Check, Couldn't Hash Intersected File on {fileRelPath}");
                        return;
                    }
                    try
                    {
                        _hashTool.GetFileMD5CheckSum(intersectedFile);

                    }
                    catch (Exception ex)
                    {
                        ManagerStateEventHandler?.Invoke(MetaDataState.Idle);
                        MessageBox.Show($"Couldn't Run File Integrity Check: File async Hashing Failed\n{ex.Message}");
                        return;
                    }
                });

                List<Task> asyncTask = []; 
                foreach (ProjectFile intersectedFile in projectFilesConcurrent.Values)
                {
                    asyncTask.Add(Task.Run(async () =>
                    {
                        await _asyncControl.WaitAsync(); 
                        try
                        {
                            if (!projectFilesDict.TryGetValue(intersectedFile.DataRelPath, out ProjectFile? projectFile))
                            {
                                ManagerStateEventHandler?.Invoke(MetaDataState.Idle);
                                MessageBox.Show($"Couldn't Run File Integrity Check, project File does not exist in Intersected file list {intersectedFile.DataName}");
                                return;
                            }
                            if (projectFile.DataHash != intersectedFile.DataHash)
                            {
                                fileIntegrityLog.AppendLine($"File {projectFile.DataName} on {projectFile.DataRelPath} has been modified");

                                ProjectFile srcFile = new ProjectFile(projectFile, DataState.None);
                                ProjectFile dstFile = new ProjectFile(projectFile, DataState.Modified | DataState.IntegrityChecked);
                                dstFile.BuildVersion = FileVersionInfo.GetVersionInfo(Path.Combine(_dstProjectData.ProjectPath, projectFile.DataRelPath)).FileVersion ?? "";
                                dstFile.DataSize = new FileInfo(Path.Combine(_dstProjectData.ProjectPath, projectFile.DataRelPath)).Length;
                                dstFile.DataHash = intersectedFile.DataHash;
                                dstFile.UpdatedTime = new FileInfo(srcFile.DataAbsPath).LastAccessTime;

                                _preStagedFilesDict.TryAdd(projectFile.DataRelPath, dstFile);
                                _registeredChangesDict.TryAdd(dstFile.DataRelPath, new ChangedFile(srcFile, dstFile, DataState.Modified | DataState.IntegrityChecked, true));
                            }
                        }
                        catch (Exception Ex)
                        {
                            MessageBox.Show($"Failed During Integrity Test {Ex.Message}");
                            return; 
                        }
                        finally
                        {
                            _asyncControl.Release(); 
                        }
                    })); 
                }

                fileIntegrityLog.Append($"Integrity Check Took: {sw.Elapsed.ToString()}s \n");
                fileIntegrityLog.AppendLine("Integrity Check Complete");
                DataStagedEventHandler?.Invoke(_registeredChangesDict.Values.ToList());
                IntegrityCheckEventHandler?.Invoke(fileIntegrityLog.ToString(), _preStagedFilesDict.Values.ToList());
                ManagerStateEventHandler?.Invoke(MetaDataState.Idle);
                Console.Write(sw.ToString());
                sw.Stop();
            }

            catch (Exception ex)
            {
                ManagerStateEventHandler?.Invoke(MetaDataState.Idle);
                System.Windows.MessageBox.Show($"{ex.Message}. Couldn't Run File Integrity Check");
            }
        }
        public List<ChangedFile>? ProjectIntegrityCheck(ProjectData targetProject)
        {
            if (targetProject == null)
            {
                ManagerStateEventHandler?.Invoke(MetaDataState.Idle);
                MessageBox.Show("Main Project is Missing");
                return null;
            }
            ManagerStateEventHandler?.Invoke(MetaDataState.CleanRestoring);
            _preStagedFilesDict.Clear();
            _registeredChangesDict.Clear();
            try
            {
                List<ChangedFile> fileChanges = new List<ChangedFile>();

                Dictionary<string, ProjectFile> projectFilesDict = targetProject.ProjectFiles;
                string backupPath = $"{targetProject.ProjectPath}\\Backup_{targetProject.ProjectName}";
                string exportPath = $"{targetProject.ProjectPath}\\Export_{targetProject.ProjectName}";
                List<string> recordedFiles = targetProject.ProjectRelFilePathsList;
                List<string> recordedDirs = targetProject.ProjectRelDirsList;

                List<string> directoryRelFiles = new List<string>();
                List<string> directoryRelDirs = new List<string>();

                if (!Directory.Exists(backupPath)) Directory.CreateDirectory(backupPath);
                if (!Directory.Exists(exportPath)) Directory.CreateDirectory(exportPath);
                string[]? backupFiles = Directory.GetFiles(backupPath, "*", SearchOption.AllDirectories);
                string[]? backupDirs = Directory.GetDirectories(backupPath, "*", SearchOption.AllDirectories);
                backupFiles ??= [];
                backupDirs ??= [];
                string[]? exportFiles = Directory.GetFiles(exportPath, "*", SearchOption.AllDirectories);
                string[]? exportDirs = Directory.GetDirectories(exportPath, "*", SearchOption.AllDirectories);
                if (exportFiles == null) exportFiles = new string[0];
                if (exportDirs == null) exportDirs = new string[0];

                string[]? rawFiles = Directory.GetFiles(targetProject.ProjectPath, "*", SearchOption.AllDirectories);
                string[]? rawDirs = Directory.GetDirectories(targetProject.ProjectPath, "*", SearchOption.AllDirectories);
                if (rawFiles == null) backupFiles = new string[0];
                if (rawDirs == null) backupDirs = new string[0];

                IEnumerable<string> directoryFiles = rawFiles.ToList().Except(backupFiles.ToList()); directoryFiles = directoryFiles.Except(exportFiles.ToList());
                IEnumerable<string> directoryDirs = rawDirs.ToList().Except(backupDirs.ToList()); directoryDirs = directoryDirs.Except(exportDirs.ToList());

                foreach (string absPathFile in directoryFiles)
                {
                    directoryRelFiles.Add(Path.GetRelativePath(targetProject.ProjectPath, absPathFile));
                }

                foreach (string absPathDir in directoryDirs)
                {
                    directoryRelDirs.Add(Path.GetRelativePath(targetProject.ProjectPath, absPathDir));
                }

                IEnumerable<string> filesToDelete = directoryRelFiles.Except(recordedFiles);
                IEnumerable<string> dirsToDelete = directoryRelDirs.Except(recordedDirs);
                IEnumerable<string> filesToAdd = recordedFiles.Except(directoryRelFiles);
                IEnumerable<string> dirsToAdd = recordedDirs.Except(directoryRelDirs);
                IEnumerable<string> intersectFiles = recordedFiles.Intersect(directoryRelFiles);

                foreach (string dirRelPath in dirsToDelete)
                {
                    if (dirRelPath == $"Backup_{targetProject.ProjectName}" || dirRelPath == $"Export_{targetProject.ProjectName}") continue;
                    ProjectFile dstFile = new ProjectFile(targetProject.ProjectPath, dirRelPath, null, DataState.Deleted, ProjectDataType.Directory);
                    ChangedFile newChange = new ChangedFile(dstFile, DataState.Deleted);
                    fileChanges.Add(newChange);
                }

                foreach (string dirRelPath in dirsToAdd)
                {
                    ProjectFile dstFile = new ProjectFile(targetProject.ProjectPath, dirRelPath, null, DataState.Added, ProjectDataType.Directory);
                    ChangedFile newChange = new ChangedFile(dstFile, DataState.Added);
                    fileChanges.Add(newChange);
                }

                foreach (string fileRelPath in filesToDelete)
                {
                    if (fileRelPath == "ProjectMetaData.bin") continue;
                    ProjectFile dstFile = new ProjectFile(targetProject.ProjectPath, fileRelPath, null, DataState.Deleted, ProjectDataType.File);
                    ChangedFile newChange = new ChangedFile(dstFile, DataState.Deleted);
                    fileChanges.Add(newChange);
                }

                foreach (string fileRelPath in filesToAdd)
                {
                    if (!_backupFilesDict.TryGetValue(projectFilesDict[fileRelPath].DataHash, out ProjectFile? backupFile))
                    {
                        MessageBox.Show($"Failed To Retrieve File {projectFilesDict[fileRelPath].DataName} For Restoration");
                        return null;
                    }
                    ProjectFile srcFile = new ProjectFile(backupFile, DataState.None);
                    ProjectFile dstFile = new ProjectFile(projectFilesDict[fileRelPath], DataState.Added);
                    ChangedFile newChange = new ChangedFile(srcFile, dstFile, DataState.Added, true);
                    fileChanges.Add(newChange);
                }

                foreach (string fileRelPath in intersectFiles)
                {
                    string? dirFileHash = _hashTool.GetFileMD5CheckSum(targetProject.ProjectPath, fileRelPath);
                    if (projectFilesDict[fileRelPath].DataHash != dirFileHash)
                    {
                        if (!_backupFilesDict.TryGetValue(projectFilesDict[fileRelPath].DataHash, out ProjectFile? backupFile))
                        {
                            MessageBox.Show($"Failed To Retrieve File {projectFilesDict[fileRelPath].DataName} For Restoration");
                            return null; 
                        }
                        ProjectFile srcFile = new ProjectFile(backupFile, DataState.None);
                        ProjectFile dstFile = new ProjectFile(backupFile, DataState.Restored, targetProject.ProjectPath);
                        dstFile.DataRelPath = fileRelPath;
                        ChangedFile newChange = new ChangedFile(srcFile, dstFile, DataState.Modified, true);
                        fileChanges.Add(newChange);
                    }
                }

                ManagerStateEventHandler?.Invoke(MetaDataState.Idle);
                return fileChanges;
            }

            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"{ex.Message}. Couldn't Run Version Clearn Restoring File Check");
                ManagerStateEventHandler?.Invoke(MetaDataState.Idle);
                return null;
            }
        }
        /// <summary>
        /// Merging Src => Dst, Occurs in version Reversion or Merging from outer source. 
        /// </summary>
        /// <param name="isRevert"> True if Reverting, else (Merge) false.</param>
        public List<ChangedFile>? FindVersionDifferences(ProjectData srcData, ProjectData dstData, bool isProjectRevert)
        {
            if (!isProjectRevert)
                return FindVersionDifferences(srcData, dstData);
            ManagerStateEventHandler?.Invoke(MetaDataState.Processing);
            try
            {
                List<ChangedFile> fileChanges = [];
                
                List<string> recordedFiles = [];
                List<string> directoryFiles = [];

                Dictionary<string, ProjectFile> srcDict = srcData.ProjectFiles;
                Dictionary<string, ProjectFile> dstDict = dstData.ProjectFiles;

                // Files which is not on the Dst 
                IEnumerable<string> filesToAdd = srcData.ProjectRelFilePathsList.Except(dstData.ProjectRelFilePathsList);
                // Files which is not on the Src
                IEnumerable<string> filesToDelete = dstData.ProjectRelFilePathsList.Except(srcData.ProjectRelFilePathsList);
                // Directories which is not on the Src
                IEnumerable<string> dirsToAdd = srcData.ProjectRelDirsList.Except(dstData.ProjectRelDirsList);
                // Directories which is not on the Dst
                IEnumerable<string> dirsToDelete = dstData.ProjectRelDirsList.Except(srcData.ProjectRelDirsList);
                // Files to Overwrite
                IEnumerable<string> intersectFiles = srcData.ProjectRelFilePathsList.Intersect(dstData.ProjectRelFilePathsList);

                //1. Directories 
                foreach (string dirRelPath in dirsToAdd)
                {
                    ProjectFile dstDir = new ProjectFile(srcDict[dirRelPath], DataState.Added, dstData.ProjectPath);
                    ChangedFile newChange = new ChangedFile(dstDir, DataState.Added);
                    fileChanges.Add(newChange);
                }

                foreach (string dirRelPath in dirsToDelete)
                {
                    ProjectFile dstDir = new ProjectFile(dstDict[dirRelPath], DataState.Deleted);
                    fileChanges.Add(new ChangedFile(dstDir, DataState.Deleted));
                }

                foreach (string fileRelPath in filesToAdd)
                {
                    if (!_backupFilesDict.TryGetValue(srcDict[fileRelPath].DataHash, out ProjectFile? backupFile))
                    {
                        MessageBox.Show($"Following Previous Project Version {srcData.UpdatedVersion}\n" +
                            $"Lacks Backup File {srcDict[fileRelPath].DataName}");
                        return null;
                    }
                    ProjectFile srcFile = new ProjectFile(backupFile, DataState.Backup, backupFile.DataSrcPath);
                    srcDict.TryGetValue(fileRelPath, out ProjectFile? recordedSrcFile);
                    ProjectFile dstFile = new ProjectFile(recordedSrcFile, DataState.Restored, dstData.ProjectPath);
                    fileChanges.Add(new ChangedFile(srcFile, dstFile, DataState.Restored, true));
                }

                foreach (string fileRelPath in filesToDelete)
                {
                    ProjectFile dstFile = new ProjectFile(dstDict[fileRelPath], DataState.Deleted);
                    fileChanges.Add(new ChangedFile(dstFile, DataState.Deleted));
                }

                foreach (string fileRelPath in intersectFiles)
                {
                    if (srcDict[fileRelPath].DataHash != dstDict[fileRelPath].DataHash)
                    {
                        if (!_backupFilesDict.TryGetValue(srcDict[fileRelPath].DataHash, out ProjectFile? backupFile))
                        {
                            MessageBox.Show($"Following Previous Project Version {srcData.UpdatedVersion} Lacks Backup File {srcDict[fileRelPath].DataName}");
                            return null;
                        }
                        ProjectFile srcFile = new ProjectFile(backupFile, DataState.Backup, backupFile.DataSrcPath);
                        srcFile.DataHash = dstDict[fileRelPath].DataHash;
                        ProjectFile dstFile = new ProjectFile(backupFile, DataState.Restored, dstDict[fileRelPath].DataSrcPath);
                        dstFile.DataRelPath = fileRelPath;
                        fileChanges.Add(new ChangedFile(srcFile, dstFile, DataState.Restored, true));
                    }
                }
                ManagerStateEventHandler?.Invoke(MetaDataState.Idle);
                DataStagedEventHandler?.Invoke(fileChanges);
                return fileChanges;
            }
            catch (Exception ex)
            {
                ManagerStateEventHandler?.Invoke(MetaDataState.Idle);
                System.Windows.MessageBox.Show($"{ex.Message}. Couldn't Run Find Version Differences For Backup");
                return null;
            }
        }
        public List<ChangedFile>? FindVersionDifferencesForIntegration(ProjectData srcData, ProjectData dstData, out int significantDiff)
        {
            if (_projIgnoreData == null)
            {
                significantDiff = -1; 
                return null;
            }
            try
            {
                ManagerStateEventHandler?.Invoke(MetaDataState.Processing);
                List<ChangedFile> fileChanges = new List<ChangedFile>();

                List<string> recordedFiles = new List<string>();
                List<string> directoryFiles = new List<string>();

                Dictionary<string, ProjectFile> srcDict = srcData.ProjectFiles;
                Dictionary<string, ProjectFile> dstDict = dstData.ProjectFiles;

                // Files which is not on the Dst 
                IEnumerable<string> filesOnSrc = srcData.ProjectRelFilePathsList.Except(dstData.ProjectRelFilePathsList);
                // Files which is not on the Src
                IEnumerable<string> filesOnDst = dstData.ProjectRelFilePathsList.Except(srcData.ProjectRelFilePathsList);
                // Directories which is not on the Src
                IEnumerable<string> dirsOnSrc = srcData.ProjectRelDirsList.Except(dstData.ProjectRelDirsList);
                // Directories which is not on the Dst
                IEnumerable<string> dirsOnDst = dstData.ProjectRelDirsList.Except(srcData.ProjectRelDirsList);
                // Files to Overwrite
                IEnumerable<string> intersectFiles = srcData.ProjectRelFilePathsList.Intersect(dstData.ProjectRelFilePathsList);

                foreach (string dirRelPath in dirsOnSrc)
                {
                    ProjectFile srcDir = new ProjectFile(srcDict[dirRelPath], DataState.Added, dstData.ProjectPath);
                    ProjectFile dstDir = new ProjectFile(ProjectDataType.Directory);
                    ChangedFile newChange = new ChangedFile(srcDir, dstDir, DataState.Added);
                    fileChanges.Add(newChange);
                }

                foreach (string dirRelPath in dirsOnDst)
                {
                    ProjectFile srcFile = new ProjectFile(ProjectDataType.Directory);
                    ProjectFile dstFile = new ProjectFile(dstDict[dirRelPath], DataState.Deleted);
                    ChangedFile newChange = new ChangedFile(srcFile, dstFile, DataState.Added);
                    fileChanges.Add(new ChangedFile(srcFile, dstFile, DataState.Deleted));
                }

                foreach (string fileRelPath in filesOnSrc)
                {
                    ProjectFile srcFile = new ProjectFile(srcDict[fileRelPath], DataState.None);
                    ProjectFile dstFile = new ProjectFile(ProjectDataType.File);
                    fileChanges.Add(new ChangedFile(srcFile, dstFile, DataState.Added, true));
                }

                foreach (string fileRelPath in filesOnDst)
                {
                    ProjectFile srcFile = new ProjectFile(ProjectDataType.File);
                    ProjectFile dstFile = new ProjectFile(dstDict[fileRelPath], DataState.Deleted);
                    fileChanges.Add(new ChangedFile(srcFile, dstFile, DataState.Deleted));
                }

                foreach (string fileRelPath in intersectFiles)
                {
                    if (srcDict[fileRelPath].DataHash != dstDict[fileRelPath].DataHash)
                    {
                        ProjectFile srcFile = new ProjectFile(srcDict[fileRelPath], DataState.None);
                        ProjectFile dstFile = new ProjectFile(dstDict[fileRelPath], DataState.Modified);
                        fileChanges.Add(new ChangedFile(srcFile, dstFile, DataState.Modified, true));
                    }
                }
                List<ChangedFile> filteredChangedList = new List<ChangedFile>(fileChanges);
                _projIgnoreData.FilterChangedFileList(filteredChangedList);
                ManagerStateEventHandler?.Invoke(MetaDataState.Idle);
                significantDiff = filteredChangedList.Count;
                return fileChanges;
            }
            catch (Exception ex)
            {
                ManagerStateEventHandler?.Invoke(MetaDataState.Idle);
                System.Windows.MessageBox.Show($"{ex.Message}. Couldn't Run Find Version Differences Against Given Src");
                significantDiff = -1; 
                return null;
            }
        }
        public List<ChangedFile>? FindVersionDifferences(ProjectData srcData, ProjectData dstData)
        {
            try
            {
                ManagerStateEventHandler?.Invoke(MetaDataState.Processing);
                List<ChangedFile> fileChanges = new List<ChangedFile>();

                List<string> recordedFiles = new List<string>();
                List<string> directoryFiles = new List<string>();

                Dictionary<string, ProjectFile> srcDict = srcData.ProjectFiles;
                Dictionary<string, ProjectFile> dstDict = dstData.ProjectFiles;

                // Files which is not on the Dst 
                IEnumerable<string> filesOnSrc = srcData.ProjectRelFilePathsList.Except(dstData.ProjectRelFilePathsList);
                // Files which is not on the Src
                IEnumerable<string> filesOnDst = dstData.ProjectRelFilePathsList.Except(srcData.ProjectRelFilePathsList);
                // Directories which is not on the Src
                IEnumerable<string> dirsOnSrc = srcData.ProjectRelDirsList.Except(dstData.ProjectRelDirsList);
                // Directories which is not on the Dst
                IEnumerable<string> dirsOnDst = dstData.ProjectRelDirsList.Except(srcData.ProjectRelDirsList);
                // Files to Overwrite
                IEnumerable<string> intersectFiles = srcData.ProjectRelFilePathsList.Intersect(dstData.ProjectRelFilePathsList);
                // TODO: Filter out the Ignore File List 

                foreach (string dirRelPath in dirsOnSrc)
                {
                    ProjectFile srcDir = new ProjectFile(srcDict[dirRelPath], DataState.Added, dstData.ProjectPath);
                    ProjectFile dstDir = new ProjectFile(ProjectDataType.Directory);

                    ChangedFile newChange = new ChangedFile(srcDir, dstDir, DataState.Added);
                    fileChanges.Add(newChange);
                }

                foreach (string dirRelPath in dirsOnDst)
                {
                    ProjectFile srcFile = new ProjectFile(ProjectDataType.Directory);
                    ProjectFile dstFile = new ProjectFile(dstDict[dirRelPath], DataState.Deleted);
                    ChangedFile newChange = new ChangedFile(srcFile, dstFile, DataState.Added);
                    fileChanges.Add(new ChangedFile(srcFile, dstFile, DataState.Deleted));
                }

                foreach (string fileRelPath in filesOnSrc)
                {
                    ProjectFile srcFile = new ProjectFile(srcDict[fileRelPath], DataState.None);
                    ProjectFile dstFile = new ProjectFile(ProjectDataType.File);
                    fileChanges.Add(new ChangedFile(srcFile, dstFile, DataState.Added, true));
                }

                foreach (string fileRelPath in filesOnDst)
                {
                    ProjectFile srcFile = new ProjectFile(ProjectDataType.File);
                    ProjectFile dstFile = new ProjectFile(dstDict[fileRelPath], DataState.Deleted);
                    fileChanges.Add(new ChangedFile(srcFile, dstFile, DataState.Deleted));
                }

                foreach (string fileRelPath in intersectFiles)
                {
                    if (srcDict[fileRelPath].DataHash != dstDict[fileRelPath].DataHash)
                    {
                        ProjectFile srcFile = new ProjectFile(srcDict[fileRelPath], DataState.None);
                        ProjectFile dstFile = new ProjectFile(dstDict[fileRelPath], DataState.Modified);
                        fileChanges.Add(new ChangedFile(srcFile, dstFile, DataState.Modified, true));
                    }
                }

                ManagerStateEventHandler?.Invoke(MetaDataState.Idle);
                return fileChanges;
            }
            catch (Exception ex)
            {
                ManagerStateEventHandler?.Invoke(MetaDataState.Idle);
                MessageBox.Show($"{ex.Message}. Couldn't Run Find Version Differences Against Given Src");
                return null;
            }
        }
        #endregion

        #region Calls For File PreStage Update
        public void RetrieveDataSrc(string srcPath)
        {
            try
            {
                ManagerStateEventHandler?.Invoke(MetaDataState.Retrieving);
                string[] binFiles = Directory.GetFiles(srcPath, "*.VersionLog", SearchOption.AllDirectories);
                if (binFiles.Length == 1)
                {
                    var stream = File.ReadAllBytes(binFiles[0]);
                    bool result = _fileHandlerTool.TryDeserializeProjectData(binFiles[0], out ProjectData? srcProjectData);
                    if (srcProjectData != null)
                    {
                        srcProjectData.ProjectPath = srcPath;
                        _srcProjectData = srcProjectData;
                        SrcProjectDataLoadedEventHandler?.Invoke(srcProjectData);
                        RegisterNewData(srcPath);
                    }
                }
                else
                {
                    SrcProjectDataLoadedEventHandler?.Invoke(null);
                    RegisterNewData(srcPath);
                }
            }
            catch (Exception ex)
            {
                ManagerStateEventHandler?.Invoke(MetaDataState.Idle);
                MessageBox.Show($"File Manager RetrieveDataSrc Error: {ex.Message}");
            }
            ManagerStateEventHandler?.Invoke(MetaDataState.Idle);
        }
        private void RegisterNewData(string srcDirPath)
        {
            if (TryGetDeployMetaFile(srcDirPath, out DeployData? deployData))
            {
                var useDeployResponse = MessageBox.Show("Deploy Data Found, Allocate file using previous settings or Reconfigure Allocation?", "Source File Allocation",
                            MessageBoxButtons.YesNo);
                if (useDeployResponse == DialogResult.Yes)
                {
                    if (TryValidateDeployMetaFile(srcDirPath, deployData))
                    {
                        RegisterFilesFromDeployData(srcDirPath, deployData);
                        RegisterFilesUnderSubDirectory(srcDirPath); 
                        return; 
                    }
                    else
                    {
                        MessageBox.Show("Failed to Allocate src files using previous settings. Allocate Manually");
                    }
                }
                else
                {
                    TryRemovePreRegisteredAllocation(srcDirPath, deployData); 
                }
            }
            try
            {
                RegisterAllSrcFiles(srcDirPath);
            }
            catch (Exception ex)
            {
                WPF.MessageBox.Show($"FileManager RegisterNewData Error {ex.Message}");
                return;
            }
        }
        private async void RegisterAllSrcFiles(string srcDirPath)
        {
            string[]? filesAllDirectories;
            string[]? filesTopDirectories;
            string[]? dirsAllDirectories;

            try
            {
                var filesAllDirTask = Task.Run(() => Directory.GetFiles(srcDirPath, "*", SearchOption.AllDirectories));
                var filesTopDirTask = Task.Run(() => Directory.GetFiles(srcDirPath, "*", SearchOption.TopDirectoryOnly));
                var dirsAllTask = Task.Run(() => Directory.GetDirectories(srcDirPath, "*", SearchOption.AllDirectories));
                filesAllDirectories = await filesAllDirTask;
                filesTopDirectories = await filesTopDirTask;
                dirsAllDirectories = await dirsAllTask;     


            }
            catch (Exception ex)
            {
                MessageBox.Show($"FileManager RegisterNewData Error {ex.Message}");
                filesAllDirectories = null;
                filesTopDirectories = null;
                dirsAllDirectories = null;
                return; 
            }
            if (filesAllDirectories == null || filesTopDirectories == null || dirsAllDirectories == null)
            {
                WPF.MessageBox.Show($"Couldn't get files or dirrectories from given Directory {srcDirPath}");
                return;
            }

            try
            {
                var filesSubDirectories = filesAllDirectories.Except(filesTopDirectories);
                RegisterFilesUnderSubDirectory(srcDirPath, filesSubDirectories.ToArray(), dirsAllDirectories);
                HandleAbnormalFiles(srcDirPath, filesTopDirectories.ToArray());
            }
            catch (Exception ex)
            {
                WPF.MessageBox.Show($"FileManager RegisterNewData Error {ex.Message}");
                return;
            }
        }
        private void RegisterFilesUnderSubDirectory(string srcDirPath, string[] filesSubDirs, string[] dirsAllDirs)
        {
            try
            {
                foreach (string subDirFileAbsPath in filesSubDirs)
                {
                    ProjectFile newFile = new ProjectFile
                        (
                        new FileInfo(subDirFileAbsPath).Length,
                        FileVersionInfo.GetVersionInfo(subDirFileAbsPath).FileVersion,
                        Path.GetFileName(subDirFileAbsPath),
                        srcDirPath,
                        Path.GetRelativePath(srcDirPath, subDirFileAbsPath)
                        );
                    _preStagedFilesDict.TryAdd(newFile.DataRelPath, newFile);
                }

                foreach (string dirAbsPath in dirsAllDirs)
                {
                    if (Path.GetExtension(dirAbsPath) == ".VersionLog") continue;
                    ProjectFile newFile = new ProjectFile
                        (
                        Path.GetFileName(dirAbsPath),
                        srcDirPath,
                        Path.GetRelativePath(srcDirPath, dirAbsPath)
                        );
                    _preStagedFilesDict.TryAdd(newFile.DataRelPath, newFile);
                }
                DataPreStagedEventHandler?.Invoke(_preStagedFilesDict.Values.ToList());
            }
            catch (Exception ex)
            {
                WPF.MessageBox.Show($"FileManager RegisterNewData Error {ex.Message}");
                return;
            }
            
        }
        private void RegisterFilesUnderSubDirectory(string srcDirPath)
        {
            try
            {
                string[]? filesAllDirectories;
                string[]? filesTopDirectories;
                string[]? dirsAllDirectories;

                try
                {
                    filesAllDirectories = Directory.GetFiles(srcDirPath, "*", SearchOption.AllDirectories);
                    filesTopDirectories = Directory.GetFiles(srcDirPath, "*", SearchOption.TopDirectoryOnly);
                    dirsAllDirectories = Directory.GetDirectories(srcDirPath, "*", SearchOption.AllDirectories);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"FileManager RegisterNewData Error {ex.Message}");
                    filesAllDirectories = null;
                    filesTopDirectories = null;
                    dirsAllDirectories = null;
                }
                if (filesAllDirectories == null || filesTopDirectories == null || dirsAllDirectories == null)
                {
                    WPF.MessageBox.Show($"Couldn't get files or dirrectories from given Directory {srcDirPath}");
                    return;
                }

                var filesSubDirectories = filesAllDirectories.Except(filesTopDirectories);
                foreach (string subDirFileAbsPath in filesSubDirectories)
                {
                    ProjectFile newFile = new ProjectFile
                        (
                        new FileInfo(subDirFileAbsPath).Length,
                        FileVersionInfo.GetVersionInfo(subDirFileAbsPath).FileVersion,
                        Path.GetFileName(subDirFileAbsPath),
                        srcDirPath,
                        Path.GetRelativePath(srcDirPath, subDirFileAbsPath)
                        );
                    _preStagedFilesDict.TryAdd(newFile.DataRelPath, newFile); 
                }

                foreach (string dirAbsPath in dirsAllDirectories)
                {
                    if (Path.GetExtension(dirAbsPath) == ".VersionLog") continue;
                    ProjectFile newFile = new ProjectFile
                        (
                        Path.GetFileName(dirAbsPath),
                        srcDirPath,
                        Path.GetRelativePath(srcDirPath, dirAbsPath)
                        );
                    _preStagedFilesDict.TryAdd(newFile.DataRelPath, newFile);
                }
                DataPreStagedEventHandler?.Invoke(_preStagedFilesDict.Values.ToList());
            }
            catch (Exception ex)
            {
                WPF.MessageBox.Show($"FileManager RegisterNewData Error {ex.Message}");
                return;
            }

        }
        private void RegisterNewData(ProjectData srcProjectData)
        {
            try
            {
                foreach (ProjectFile srcFile in srcProjectData.ProjectFiles.Values)
                {
                    srcFile.DataState |= DataState.PreStaged;
                    if (!_preStagedFilesDict.TryAdd(srcFile.DataRelPath, srcFile))
                    {
                        WPF.MessageBox.Show($"Already Enlisted File {srcFile.DataName}: for Update");
                    }
                    else continue;
                }
                DataPreStagedEventHandler?.Invoke(_preStagedFilesDict.Values.ToList());
            }
            catch (Exception ex)
            {
                WPF.MessageBox.Show(ex.Message);
                return;
            }
        }
        private void HandleAbnormalFiles(string srcPath, string[] topDirFilePaths)
        {
            List<ChangedFile> registeredOverlapsList = [];
            List<ChangedFile> registeredNewList = [];

            for (int i = 0; i < topDirFilePaths.Length; i++)
            {
                int count = 0; 
                string? fileName = Path.GetFileName(topDirFilePaths[i]);
                List<ProjectFile>? overlappingFiles = null;
                if (fileName == null)
                {
                    MessageBox.Show($"Couldn't Process file name for overlapping file check on {topDirFilePaths[i]}");
                    return;
                }
                if (Path.GetExtension(topDirFilePaths[i]) == ".VersionLog") continue;
                if (Path.GetExtension(topDirFilePaths[i]) == ".deploy") continue; 
                if (_projectFilesDict_namesSorted.TryGetValue(fileName, out List<ProjectFile>? fileList))
                {
                    count = fileList.Count;
                    overlappingFiles = fileList;
                }
                // File Overlaps
                if (count >= 2 && overlappingFiles != null)
                {
                    ProjectFile newFile = new ProjectFile
                        (
                        new FileInfo(topDirFilePaths[i]).Length,
                        FileVersionInfo.GetVersionInfo(topDirFilePaths[i]).FileVersion,
                        Path.GetFileName(topDirFilePaths[i]),
                        srcPath,
                        Path.GetRelativePath(srcPath, topDirFilePaths[i])
                        );
                    //Filter process 
                    List<ProjectFile> filteredFileList = [];
                    foreach (ProjectFile file in overlappingFiles)
                    {
                        if (_preStagedFilesDict.TryGetValue(file.DataRelPath, out ProjectFile? projFile))
                        {
                            continue;
                        }
                        filteredFileList.Add(file);
                    }
                    if (filteredFileList.Count == 1)
                    {
                        string newSrcFilePath = Path.Combine(srcPath, filteredFileList[0].DataRelPath);
                        _fileHandlerTool.HandleFile(newFile.DataAbsPath, newSrcFilePath, DataState.PreStaged);
                        newFile.DataRelPath = filteredFileList[0].DataRelPath;
                        _preStagedFilesDict.TryAdd(newFile.DataRelPath, newFile);
                    }
                    else
                    {
                        foreach (ProjectFile file in filteredFileList)
                        {
                            ChangedFile newOverlap = new ChangedFile(newFile, new ProjectFile(file), DataState.Overlapped);
                            registeredOverlapsList.Add(newOverlap);
                        }
                    }
                }
                // Modification => Recreate Appropriate Folder Directory
                else if (count == 1 && overlappingFiles != null)
                {
                    string newSrcFilePath = Path.Combine(srcPath, overlappingFiles[0].DataRelPath);
                    _fileHandlerTool.HandleFile(topDirFilePaths[i], newSrcFilePath, DataState.PreStaged);

                    ProjectFile newFile = new ProjectFile
                        (
                        new FileInfo(newSrcFilePath).Length,
                        FileVersionInfo.GetVersionInfo(newSrcFilePath).FileVersion,
                        Path.GetFileName(newSrcFilePath),
                        srcPath,
                        Path.GetRelativePath(srcPath, newSrcFilePath)
                        );

                    _preStagedFilesDict.TryAdd(newFile.DataRelPath, newFile);
                }
                // New File
                else
                {
                    ProjectFile newFile = new ProjectFile
                        (
                        new FileInfo(topDirFilePaths[i]).Length,
                        FileVersionInfo.GetVersionInfo(topDirFilePaths[i]).FileVersion,
                        Path.GetFileName(topDirFilePaths[i]),
                        srcPath,
                        Path.GetRelativePath(srcPath, topDirFilePaths[i])
                        );
                    foreach (ProjectFile projDir in _projDirFileList)
                    {
                        ChangedFile potentialNew = new ChangedFile(newFile, projDir, DataState.Overlapped);
                        registeredNewList.Add(potentialNew);
                    }
                }
            }
            if (registeredOverlapsList.Count >= 1)
            {
                OverlappedFileFoundEventHandler?.Invoke(registeredOverlapsList, registeredNewList);
            }
            DataPreStagedEventHandler?.Invoke(_preStagedFilesDict.Values.ToList());
        }
        public void RegisterNewfile(ProjectFile projectFile, DataState fileState)
        {
            ProjectFile newfile = new ProjectFile(projectFile, fileState | DataState.PreStaged);
            if (!_preStagedFilesDict.TryAdd(newfile.DataRelPath, newfile))
            {
                PreStagedDataOverlapEventHandler?.Invoke(newfile);
                return;
            }
            DataPreStagedEventHandler?.Invoke(_preStagedFilesDict.Values.ToList());
        }
        public void RegisterAbnormalFiles(List<ChangedFile> sortedOverlaps, List<ChangedFile> sortedNew)
        {
            Dictionary<string, ProjectFile> newlyAllocatedFiles = []; 
            foreach (ChangedFile overlappedFile in sortedOverlaps)
            {
                if (overlappedFile.DstFile.IsDstFile)
                {
                    string newSrcFilePath = Path.Combine(overlappedFile.SrcFile.DataSrcPath, overlappedFile.DstFile.DataRelPath);

                    _fileHandlerTool.HandleFile(overlappedFile.SrcFile.DataAbsPath, newSrcFilePath, DataState.PreStaged);
                    ProjectFile newPreStagedFile = new ProjectFile(overlappedFile.SrcFile, DataState.PreStaged);
                    newPreStagedFile.DataRelPath = overlappedFile.DstFile.DataRelPath;
                    newlyAllocatedFiles.TryAdd(newPreStagedFile.DataRelPath, newPreStagedFile); 
                    _preStagedFilesDict.TryAdd(newPreStagedFile.DataRelPath, newPreStagedFile);
                }
            }
            foreach (ChangedFile newFile in sortedNew)
            {
                if (newFile.DstFile == null || newFile.SrcFile == null) continue;
                if (newFile.DstFile.IsDstFile)
                {
                    string newSrcFileRelPath = Path.Combine(newFile.DstFile.DataRelPath, newFile.SrcFile.DataName);
                    string newSrcFilePath = Path.Combine(newFile.SrcFile.DataSrcPath, newSrcFileRelPath);

                    _fileHandlerTool.HandleFile(newFile.SrcFile.DataAbsPath, newSrcFilePath, DataState.Added);
                    ProjectFile newPreStagedFile = new ProjectFile(newFile.SrcFile, DataState.Added);
                    newPreStagedFile.DataRelPath = newSrcFileRelPath;
                    _preStagedFilesDict.TryAdd(newPreStagedFile.DataRelPath, newPreStagedFile);
                    newlyAllocatedFiles.TryAdd(newPreStagedFile.DataRelPath, new ProjectFile(newPreStagedFile, DataState.PreStaged));
                }
            }
            RegisterDeployData(_dstProjectData.ProjectName, newlyAllocatedFiles);
            DataPreStagedEventHandler?.Invoke(_preStagedFilesDict.Values.ToList());
        }
        private void RegisterDeployData(string projectName, Dictionary<string, ProjectFile> registeredDeployment)
        {
            try
            {
                string srcPath = registeredDeployment.Values.First().DataSrcPath;
                const string deployFilename = "DeployAssistant.deploy";
                string deployfilePath = Path.Combine(srcPath, deployFilename);
                DeployData deployData = new DeployData(projectName, registeredDeployment);
                _fileHandlerTool.TrySerializeJsonData(deployfilePath, deployData);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed Deployment {ex.Message}");
            }
        }
        private void RegisterFilesFromDeployData(string srcPath, DeployData deployedData)
        {
            foreach (ProjectFile registeredFile in deployedData.SortedTopFiles.Values)
            {
                ProjectFile newPreStagedFile = new ProjectFile(registeredFile, DataState.PreStaged);
                newPreStagedFile.DataSrcPath = srcPath;
                _preStagedFilesDict.TryAdd(newPreStagedFile.DataRelPath, newPreStagedFile);
            }
            DataPreStagedEventHandler?.Invoke(_preStagedFilesDict.Values.ToList());
        }
        private bool TryRemovePreRegisteredAllocation(string dstSrcPath, DeployData deployedData)
        {
            try
            {
                foreach (ProjectFile registeredFile in deployedData.SortedTopFiles.Values)
                {
                    string fileOriginalSrcPath = Path.Combine(dstSrcPath, registeredFile.DataName);

                    if (File.Exists(registeredFile.DataAbsPath) && fileOriginalSrcPath != registeredFile.DataAbsPath) 
                        File.Delete(registeredFile.DataAbsPath);
                }
                return true; 
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to remove existing registered files in src folder {ex.Message}");
                return false;
            }
        }
        #endregion

        #region Calls for File Stage Update
        public async void StageNewFilesAsync()
        {
            if (_dstProjectData == null)
            {
                MessageBox.Show("Project Data is unreachable from FileManager");
                return;
            }
            ManagerStateEventHandler?.Invoke(MetaDataState.Processing);
            await HashPreStagedFilesAsync();
            UpdateStageFileList();
            ManagerStateEventHandler?.Invoke(MetaDataState.Idle);
        }

        private async Task HashPreStagedFilesAsync()
        {
            try
            {
                if (_preStagedFilesDict.Count <= 0) return;
                List<Task> asyncTasks = new List<Task>();
                //Update changedFilesDict
                foreach (ProjectFile file in _preStagedFilesDict.Values)
                {
                    Console.WriteLine(_asyncControl.CurrentCount);
                    if (file.DataType == ProjectDataType.Directory || file.DataHash != "") continue;
                    asyncTasks.Add(Task.Run(async () =>
                    {
                        await _asyncControl.WaitAsync();
                        try
                        {
                            _hashTool.GetFileMD5CheckSum(file);
                        }
                        finally
                        {
                            _asyncControl.Release();
                            Console.WriteLine(_asyncControl.CurrentCount);
                        }
                    }));
                }
                await Task.WhenAll(asyncTasks);
            }
            catch (Exception ex)
            {
                ManagerStateEventHandler?.Invoke(MetaDataState.Idle);
                WPF.MessageBox.Show($"File Manager UpdateHashFromChangedList Error: {ex.Message}");
            }
            finally
            {
                ManagerStateEventHandler?.Invoke(MetaDataState.Idle);
                _asyncControl.Release();
                Console.WriteLine(_asyncControl.CurrentCount);
            }
        }
        
        private void UpdateStageFileList()
        {
            if (_preStagedFilesDict.Count <= 0) return;

            foreach (ProjectFile registerdFile in _preStagedFilesDict.Values)
            {
                ManagerStateEventHandler?.Invoke(MetaDataState.Processing);
                registerdFile.DataState &= ~DataState.PreStaged;
                //If File is being Restored
                if ((registerdFile.DataState & DataState.Restored) != 0 && registerdFile.DataType != ProjectDataType.Directory)
                {
                    _backupFilesDict.TryGetValue(registerdFile.DataHash, out ProjectFile? backupFile);
                    if (backupFile != null)
                    {
                        //File Modified 
                        if (_projectFilesDict.TryGetValue(backupFile.DataRelPath, out ProjectFile? projectFile))
                        {
                            if (backupFile.DataHash == projectFile.DataHash) continue;
                            ProjectFile srcFile = new ProjectFile(projectFile, DataState.Backup, backupFile.DataSrcPath);
                            ProjectFile dstFile = new ProjectFile(registerdFile, DataState.Modified, projectFile.DataSrcPath);
                            ChangedFile newChange = new ChangedFile(srcFile, dstFile, DataState.Restored, true);
                            _registeredChangesDict.TryAdd(registerdFile.DataRelPath, newChange);
                        }
                        else
                        {
                            ProjectFile srcFile = new ProjectFile(registerdFile, DataState.Backup, backupFile.DataSrcPath);
                            ProjectFile dstFile = new ProjectFile(registerdFile, DataState.Restored, _dstProjectData.ProjectPath);
                            ChangedFile newChange = new ChangedFile(srcFile, dstFile, DataState.Restored, true);
                            _registeredChangesDict.TryAdd(registerdFile.DataRelPath, newChange);
                        }
                    }
                    continue;
                    //Reset SrcPath to BackupPath
                }
                //If File is being Deleted
                if ((registerdFile.DataState & DataState.Deleted) != 0)
                {
                    ChangedFile newChange = new ChangedFile(new ProjectFile(registerdFile), DataState.Deleted);
                    _registeredChangesDict.TryAdd(registerdFile.DataRelPath, newChange);
                    continue;
                }
                // If File is Modified or Added
                // Modified
                if (_projectFilesDict.TryGetValue(registerdFile.DataRelPath, out var dstProjectFile))
                {
                    if (dstProjectFile.DataHash != registerdFile.DataHash)
                    {
                        ProjectFile srcFile = new ProjectFile(dstProjectFile, DataState.None, registerdFile.DataSrcPath);
                        ProjectFile dstFile = new ProjectFile(registerdFile, DataState.Modified, dstProjectFile.DataSrcPath);
                        ChangedFile newChange = new ChangedFile(srcFile, dstFile, DataState.Modified, true);
                        _registeredChangesDict.TryAdd(registerdFile.DataRelPath, newChange);
                    }
                    else
                        continue;
                }
                else // Added
                {
                    ProjectFile srcFile = new ProjectFile(registerdFile, DataState.None);
                    ProjectFile dstFile = new ProjectFile(registerdFile, DataState.Added, _dstProjectData.ProjectPath);
                    _registeredChangesDict.TryAdd(registerdFile.DataRelPath, new ChangedFile(srcFile, dstFile, DataState.Added));
                }
            }

            _preStagedFilesDict.Clear();
            ManagerStateEventHandler?.Invoke(MetaDataState.Idle);
            DataStagedEventHandler?.Invoke(_registeredChangesDict.Values.ToList());
        }
        
        /// <summary>
        /// Clears StagedFiles Except those registered as IntegrityChecked
        /// </summary>
        public void ClearDeployedFileChanges()
        {
            _srcProjectData = null;
            _preStagedFilesDict.Clear();
            List<ChangedFile> clearChangedList = new List<ChangedFile>();
            foreach (ChangedFile changedFile in _registeredChangesDict.Values)
            {
                if ((changedFile.DataState & DataState.IntegrityChecked) == 0)
                {
                    clearChangedList.Add(changedFile);
                }
            }
            foreach (ChangedFile idenfitiedChange in clearChangedList)
            {
                if (idenfitiedChange.DstFile != null)
                    _registeredChangesDict.Remove(idenfitiedChange.DstFile.DataRelPath);
            }
            SrcProjectDataLoadedEventHandler?.Invoke(_srcProjectData); 
            DataStagedEventHandler?.Invoke(_registeredChangesDict.Values.ToList());
        }
        #endregion

        #region CallBacks From Parent Model 
        public void MetaDataManager_ProjLoadedCallback(object projObj)
        {
            if (projObj is not ProjectData loadedProject) return;

            _preStagedFilesDict.Clear();
            _registeredChangesDict.Clear();
            _dstProjectData = loadedProject;
            _projectFilesDict = _dstProjectData.ProjectFiles;
            _projDirFileList = _dstProjectData.ProjectDirFileList;
            _projectFilesDict_namesSorted = _dstProjectData.ProjectFilesDict_NameSorted;
            _projectFilesDict_relDirSorted = _dstProjectData.ProjectFilesDict_RelDirSorted; 
            DataStagedEventHandler?.Invoke(_registeredChangesDict.Values.ToList());
        }
        public void MetaDataManager_MetaDataLoadedCallBack(object metaDataObj)
        {
            if (metaDataObj is not ProjectMetaData projectMetaData) return;
            if (projectMetaData == null) return;
            _backupFilesDict = projectMetaData.BackupFiles;
        }
        public void MetaDataManager_UpdateIgnoreListCallBack(object projIgnoreDataObj)
        {
            if (projIgnoreDataObj is not ProjectIgnoreData projectIgnoreData) return;
            _projIgnoreData = projectIgnoreData;
        }
        #endregion

        #region Util Calls 
        private bool TryGetDeployMetaFile(string srcPath, out DeployData? deployData)
        {
            const string deployFilename = "DeployAssistant.deploy";
            string deployfilePath = Path.Combine(srcPath, deployFilename);
            if (_fileHandlerTool.TryDeserializeJsonData(deployfilePath, out DeployData? existingDeployData))
            {
                if (existingDeployData.ProjectName != _dstProjectData.ProjectName)
                {
                    deployData = null; 
                    return false;
                }

                if (!TryValidateDeployMetaFile(srcPath, existingDeployData)) 
                {
                    deployData = null; 
                    return false; 
                }
                deployData = existingDeployData;
                return true;
            }
            else
            {
                if (File.Exists(deployfilePath)) File.Delete(deployfilePath);
                deployData = null;
                return false;
            }
        }

        private bool TryValidateDeployMetaFile(string srcPath, DeployData deployData)
        {
            try
            {
                foreach (ProjectFile registeredFile in deployData.SortedTopFiles.Values)
                {
                    if (registeredFile.DataType == ProjectDataType.Directory) continue; 
                    if (registeredFile == null || registeredFile.DataName == "") return false; 
                    string fileSrcPath = Path.Combine(srcPath, registeredFile.DataName);
                    if (!File.Exists(fileSrcPath)) return false; 
                }
                return true; 
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Critical Error while validating registered deploy meta files {ex.Message}");
                return false; 
            }
        }
        #endregion

        #region Planned 
        public async void StageNewFilesAsync(string deployPath)
        {
            if (_dstProjectData == null)
            {
                MessageBox.Show("Project Data is unreachable from FileManager");
                return;
            }
            await HashPreStagedFilesAsync();
            UpdateStageFileList();
            (bool result, List<string>? failedDataList) = DeployFileIntegrityCheck(deployPath);
            if (!result)
            {
                MessageBox.Show("Failed to Stage Changes: DeployFiles Integrity Check Failed");
                return;
            }
        }

        private (bool, List<string>? failedFileList) DeployFileIntegrityCheck(string deployPath)
        {
            try
            {
                List<string> failedList = new List<string>();
                foreach (ChangedFile changes in _registeredChangesDict.Values)
                {
                    if (changes.SrcFile == null && changes.DstFile != null) failedList.Add(changes.DstFile.DataName);

                }
                return (true, failedList);
            }
            catch (Exception ex)
            {
                WPF.MessageBox.Show($"{ex.Message}, Failed SrcFileIntegrityCheck On FileManager");
                return (true, null);
            }
        }

        public void RevertChange(ProjectFile file)
        {
            if ((file.DataState & DataState.IntegrityChecked) == 0) return;
            file.DataState &= ~DataState.IntegrityChecked;
            switch (file.DataState)
            {
                case DataState.Added:
                    _fileHandlerTool.HandleFile(null, file.DataAbsPath, DataState.Deleted);
                    break;
                case DataState.Modified:
                    if (!_projectFilesDict.TryGetValue(file.DataRelPath, out ProjectFile? projectFile_M))
                    {
                        MessageBox.Show("Couldn't revert change since recorded project file does not exist");
                        file.DataState |= DataState.IntegrityChecked;
                        return;
                    }
                    if (!_backupFilesDict.TryGetValue(projectFile_M.DataHash, out ProjectFile? backupFile_M))
                    {
                        MessageBox.Show("Couldn't revert change since backup does not exist");
                        file.DataState |= DataState.IntegrityChecked;
                        return;
                    }
                    _fileHandlerTool.HandleFile(backupFile_M.DataAbsPath, projectFile_M.DataAbsPath, DataState.Modified);
                    break;
                case DataState.Deleted:
                    if (!_projectFilesDict.TryGetValue(file.DataRelPath, out ProjectFile? projectFile_D))
                    {
                        MessageBox.Show("Coudln't Revert Change for Recorded Project file");
                        file.DataState |= DataState.IntegrityChecked;
                        return;
                    }
                    if (file.DataType == ProjectDataType.Directory)
                    {
                        _fileHandlerTool.HandleDirectory(null, projectFile_D.DataAbsPath, DataState.None);
                        break;
                    }
                    if (!_backupFilesDict.TryGetValue(projectFile_D.DataHash, out ProjectFile? backupFile_D))
                    {
                        MessageBox.Show("Couldn't revert change since backup does not exist");
                        file.DataState |= DataState.IntegrityChecked;
                        return;
                    }
                    _fileHandlerTool.HandleFile(backupFile_D.DataAbsPath, projectFile_D.DataAbsPath, DataState.Added);
                    break;
                default:
                    break;
            }

            if (_preStagedFilesDict.TryGetValue(file.DataRelPath, out ProjectFile? recordedPreFile))
                _preStagedFilesDict.Remove(file.DataRelPath);
            if (_registeredChangesDict.TryGetValue(file.DataRelPath, out ChangedFile? recordedChange))
                _registeredChangesDict.Remove(file.DataRelPath);

            DataStagedEventHandler?.Invoke(_registeredChangesDict.Values.ToList());
        }
        #endregion
    }
}