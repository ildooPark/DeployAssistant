using DeployAssistant.DataComponent;
using DeployAssistant.Interfaces;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json.Serialization;

namespace DeployAssistant.Model
{
    /// <summary>
    /// V1 file record with 8 constructor overloads and mixed state concerns.
    /// Use <see cref="DeployAssistant.Model.V2.FileRecord"/> for new code.
    /// </summary>
    [System.Obsolete("ProjectFile is a V1 type. Use DeployAssistant.Model.V2.FileRecord for new code.")]
    public class ProjectFile : IEquatable<ProjectFile>, IComparable<ProjectFile>, IProjectData
    {
        #region [JsonInclude]
        public ProjectDataType DataType { get; private set; }
        public long DataSize { get; set; }
        public string BuildVersion {  get; set; }
        /// <summary>Win32 ProductVersion, which carries the build's commit id (e.g. "0.0.1682+HEAD.f05eda3"). Serialized by the shipped 3.6.1; must round-trip.</summary>
        public string ProductVersion { get; set; } = "";
        public string DeployedProjectVersion { get; set; }
        public DateTime UpdatedTime { get; set; }
        public bool IsDstFile { get; set; }
        public DataState DataState { get; set; }
        public string DataName { get; set; }
        public string DataSrcPath { get; set; }
        public string DataRelPath { get; set; }
        public string DataHash { get; set; }
        #endregion

        #region [JsonIgnore] 
        [JsonIgnore] 
        public string VersionDisplay => string.IsNullOrEmpty(ProductVersion) ? BuildVersion : ProductVersion;
        [JsonIgnore]
        public string DataAbsPath => Path.Combine(DataSrcPath, DataRelPath);
        [JsonIgnore]
        public string DataRelDir => DataType == ProjectDataType.Directory ? DataRelPath: Path.GetDirectoryName(DataRelPath) ?? "";
        #endregion

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        public ProjectFile() { }
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        [JsonConstructor]
        public ProjectFile(ProjectDataType DataType, long DataSize, string BuildVersion, string DeployedProjectVersion, 
            DateTime UpdatedTime, DataState DataState, string dataName, string dataSrcPath, string dataRelPath, string dataHash, bool IsDstFile) 
        {
            this.DataType = DataType;
            this.DataSize = DataSize;
            this.BuildVersion = BuildVersion;
            this.DeployedProjectVersion = DeployedProjectVersion;
            this.UpdatedTime = UpdatedTime;
            this.DataState = DataState;
            this.DataName = dataName;
            this.DataSrcPath = dataSrcPath;
            this.DataRelPath = dataRelPath;
            this.DataHash = dataHash;
            this.IsDstFile = IsDstFile;
        }
        #region Overloaded Constructors
        /// <summary>
        /// Lacks FileHash, DeployedProjectVersion, FileChangedState
        /// </summary>
        public ProjectFile(long fileSize, string? fileVersion, string fileName, string fileSrcPath, string fileRelPath, DataState changedState)
        {
            this.DataSize = fileSize;
            this.BuildVersion = fileVersion ?? "";
            this.DataName = fileName;
            this.DataSrcPath = fileSrcPath;
            this.DataRelPath = fileRelPath;
            this.DataHash = "";
            this.DeployedProjectVersion = "";
            this.UpdatedTime = DateTime.Now;
            this.DataState = changedState;
        }
        /// <summary>
        /// For PreStagedProjectFile Data Type = File 
        /// </summary>
        public ProjectFile(long DataSize, string? BuildVersion, string DataName, string DataSrcPath, string DataRelPath)
        {
            this.DataType = ProjectDataType.File;
            this.DataSize = DataSize;
            this.BuildVersion = BuildVersion ?? "";
            this.DeployedProjectVersion = "";
            this.UpdatedTime = DateTime.MinValue;
            this.DataState = DataState.PreStaged;
            this.DataName = DataName;
            this.DataSrcPath = DataSrcPath;
            this.DataRelPath= DataRelPath;
            this.DataHash = "";
        }
        /// <summary>
        /// For PreStagedProjectFile Data Type = Directory 
        /// </summary>
        public ProjectFile(string DataName, string DataSrcPath, string DataRelPath)
        {
            this.DataType = ProjectDataType.Directory;
            this.DataSize = 0;
            this.BuildVersion = "";
            this.DeployedProjectVersion = "";
            this.UpdatedTime = DateTime.MinValue;
            this.DataState = DataState.PreStaged;
            this.DataName = DataName;
            this.DataSrcPath = DataSrcPath;
            this.DataRelPath = DataRelPath;
            this.DataHash = "";
        }
        /// <summary>
        /// Deep Copy of ProjectFile
        /// </summary>
        /// <param name="srcData">Project File to Copy</param>
        public ProjectFile(ProjectFile srcData)
        {
            this.DataType = srcData.DataType;
            this.DataSize = srcData.DataSize;
            this.BuildVersion = srcData.BuildVersion;
            this.ProductVersion = srcData.ProductVersion;
            this.ProductVersion = srcData.ProductVersion;
            this.DeployedProjectVersion = srcData.DeployedProjectVersion;
            this.UpdatedTime = srcData.UpdatedTime;
            this.DataState = srcData.DataState;
            this.DataName = srcData.DataName;
            this.DataSrcPath = srcData.DataSrcPath;
            this.DataRelPath = srcData.DataRelPath;
            this.DataHash = srcData.DataHash;
        }
        public ProjectFile(ProjectFile updatedData, string deployedProjectVersion, string currentProjectPath)
        {
            this.DataType = updatedData.DataType;
            this.DataSize = updatedData.DataSize;
            this.BuildVersion = updatedData.BuildVersion;
            this.ProductVersion = updatedData.ProductVersion;
            this.ProductVersion = updatedData.ProductVersion;
            this.DeployedProjectVersion = deployedProjectVersion;
            this.UpdatedTime = DateTime.Now;
            this.DataState = updatedData.DataState;
            this.DataName = updatedData.DataName;
            this.DataSrcPath = currentProjectPath;
            this.DataRelPath = updatedData.DataRelPath;
            this.DataHash = updatedData.DataHash;
        }
        public ProjectFile(ProjectFile srcData, DataState state)
        {
            this.DataType = srcData.DataType;
            this.DataSize = srcData.DataSize;
            this.BuildVersion = srcData.BuildVersion;
            this.ProductVersion = srcData.ProductVersion;
            this.ProductVersion = srcData.ProductVersion;
            this.DeployedProjectVersion = srcData.DeployedProjectVersion;
            this.UpdatedTime = DateTime.Now;
            this.DataState = state;
            this.DataName = srcData.DataName;
            this.DataSrcPath = srcData.DataSrcPath;
            this.DataRelPath = srcData.DataRelPath;
            this.DataHash = srcData.DataHash;
        }
        public ProjectFile(ProjectFile srcData, DataState DataState, string dataSrcPath)
        {
            this.DataType = srcData.DataType;
            this.DataSize = srcData.DataSize;
            this.BuildVersion = srcData.BuildVersion;
            this.ProductVersion = srcData.ProductVersion;
            this.ProductVersion = srcData.ProductVersion;
            this.DeployedProjectVersion = srcData.DeployedProjectVersion;
            this.UpdatedTime = DateTime.Now;
            this.DataState = DataState;
            this.DataName = srcData.DataName;
            this.DataSrcPath = dataSrcPath;
            this.DataRelPath = srcData.DataRelPath;
            this.DataHash = srcData.DataHash;
        }
        public ProjectFile(string fileSrcPath, string fileRelPath, string? fileHash, DataState DataState, ProjectDataType dataType)
        {
            string fileFullPath = DeployAssistant.Utils.PathCompat.ToNetFrameworkLongPath(Path.Combine(fileSrcPath, fileRelPath));
            DateTime updatedTime = DateTime.Now;
            if (dataType == ProjectDataType.File)
            {
                try
                {
                    var versionInfo = FileVersionInfo.GetVersionInfo(fileFullPath);
                    this.BuildVersion = versionInfo.FileVersion ?? "";
                    this.ProductVersion = versionInfo.ProductVersion ?? "";
                }
                catch (Exception)
                {
                    // Non-PE files (e.g. config, data) may not expose version info; default to empty.
                    this.BuildVersion = "";
                }
                var fileInfo = new FileInfo(fileFullPath);
                this.DataSize = fileInfo.Length;
                try
                {
                    // Must be the file's own write time; scan time would stamp the whole batch identically.
                    updatedTime = fileInfo.LastWriteTime;
                }
                catch (Exception)
                {
                }
            }
            else
            {
                this.DataSize = 0;
                this.BuildVersion = "";
            }
            this.DeployedProjectVersion = "";
            this.DataSrcPath = fileSrcPath;
            this.DataName = Path.GetFileName(fileFullPath);
            this.DataRelPath = fileRelPath;
            this.DataHash = fileHash ?? "";
            this.UpdatedTime = updatedTime;
            this.DataState = DataState;
            this.DataType = dataType;
        }
        // 
        /// <summary>
        /// Empty ProjectFile with Given DataType
        /// </summary>
        public ProjectFile (ProjectDataType dataType)
        {
            this.DataType = dataType;
            this.DataSize = 0;
            this.BuildVersion = "";
            this.DeployedProjectVersion = "";
            this.UpdatedTime = DateTime.MaxValue;
            this.DataState = DataState.None;
            this.DataName = "";
            this.DataSrcPath = "";
            this.DataRelPath = "";
            this.DataHash = "";
        }
        #endregion
        public int CompareTo(ProjectFile? other) 
        {
            if (this.UpdatedTime.CompareTo(other.UpdatedTime) == 0)
                return this.DataSize.CompareTo(other.DataSize);
            return this.UpdatedTime.CompareTo(other.UpdatedTime);
        }
        /// <summary>
        /// IEquatable Implementation: Checks Data Name
        /// </summary>
        public bool Equals(ProjectFile? other)
        {
            if (other == null)
            {
                Trace.TraceWarning($"Null ProjectFile presented for comparison with {this.DataName}");
                return false;
            }
            return other.DataName == this.DataName;
        }
    }

    /// <summary>
    /// Folder-hierarchy projection of a flat <see cref="ProjectFile"/> set, for
    /// tree-shaped file views. Folders sort before files, both alphabetical.
    /// </summary>
    public sealed class ProjectFileTreeNode : System.ComponentModel.INotifyPropertyChanged
    {
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

        public string Name { get; }
        public bool IsDirectory { get; }

        private bool _isHighlighted;
        public bool IsHighlighted
        {
            get => _isHighlighted;
            private set
            {
                if (_isHighlighted == value) return;
                _isHighlighted = value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsHighlighted)));
            }
        }

        /// <summary>
        /// Marks every file node whose hash equals <paramref name="hash"/> and clears the
        /// rest; a null/empty hash clears all. Returns the number of marked nodes.
        /// </summary>
        public static int HighlightMatchingHash(IEnumerable<ProjectFileTreeNode> roots, string? hash)
        {
            int marked = 0;
            bool match = !string.IsNullOrEmpty(hash);
            void Walk(ProjectFileTreeNode node)
            {
                node.IsHighlighted = match && !node.IsDirectory && node.File?.DataHash == hash;
                if (node.IsHighlighted) marked++;
                foreach (var child in node.Children) Walk(child);
            }
            foreach (var root in roots) Walk(root);
            return marked;
        }
#pragma warning disable CS0618
        public ProjectFile? File { get; private set; }
#pragma warning restore CS0618
        public List<ProjectFileTreeNode> Children { get; } = new List<ProjectFileTreeNode>();
        public int FileCount { get; private set; }
        public long TotalSize { get; private set; }

        public string SizeDisplay => FormatSize(IsDirectory ? TotalSize : File?.DataSize ?? 0);

        private ProjectFileTreeNode(string name, bool isDirectory)
        {
            Name = name;
            IsDirectory = isDirectory;
        }

#pragma warning disable CS0618
        public static List<ProjectFileTreeNode> Build(IEnumerable<ProjectFile> files)
        {
            var root = new ProjectFileTreeNode("", isDirectory: true);
            var dirNodes = new Dictionary<string, ProjectFileTreeNode>(StringComparer.OrdinalIgnoreCase) { [""] = root };

            ProjectFileTreeNode GetDir(string relDir)
            {
                if (dirNodes.TryGetValue(relDir, out var node))
                    return node;
                string parentDir = Path.GetDirectoryName(relDir) ?? "";
                var parent = GetDir(parentDir);
                node = new ProjectFileTreeNode(Path.GetFileName(relDir), isDirectory: true);
                parent.Children.Add(node);
                dirNodes[relDir] = node;
                return node;
            }

            foreach (var f in files)
            {
                if (string.IsNullOrEmpty(f.DataRelPath))
                    continue;
                if (f.DataType == ProjectDataType.Directory)
                    GetDir(f.DataRelPath).File = f;
                else
                    GetDir(Path.GetDirectoryName(f.DataRelPath) ?? "").Children.Add(
                        new ProjectFileTreeNode(f.DataName, isDirectory: false) { File = f });
            }

            root.SortAndAggregate();
            return root.Children;
        }
#pragma warning restore CS0618

        private void SortAndAggregate()
        {
            Children.Sort((a, b) => a.IsDirectory != b.IsDirectory
                ? (a.IsDirectory ? -1 : 1)
                : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            foreach (var child in Children)
            {
                if (child.IsDirectory)
                {
                    child.SortAndAggregate();
                    FileCount += child.FileCount;
                    TotalSize += child.TotalSize;
                }
                else
                {
                    FileCount++;
                    TotalSize += child.File?.DataSize ?? 0;
                }
            }
        }

        public static string FormatSize(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024L * 1024) return $"{bytes / 1024.0:0.0} KB";
            if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):0.0} MB";
            return $"{bytes / (1024.0 * 1024 * 1024):0.0} GB";
        }
    }
}