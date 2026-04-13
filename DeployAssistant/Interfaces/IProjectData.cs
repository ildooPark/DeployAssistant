using DeployAssistant.DataComponent;

namespace DeployAssistant.Interfaces
{
    public enum ProjectDataType
    {
        File,
        Directory
    }
    public interface IProjectData
    {
        ProjectDataType DataType { get; }
        public DataState DataState { get; set; }
        public DateTime UpdatedTime { get; set; }
        public string DataName { get; }
        public string DataRelPath { get; }
        public string DataSrcPath { get; set; }
        public string DataAbsPath { get; }
        public string DataHash { get; set; }
    }
}