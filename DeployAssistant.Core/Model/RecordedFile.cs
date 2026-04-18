using DeployAssistant.DataComponent;
using DeployAssistant.Interfaces;
using System.Text.Json.Serialization;

namespace DeployAssistant.Model
{
    public class RecordedFile : IProjectData
    {
        public ProjectDataType DataType { get; set; }

        public IgnoreType IgnoreType { get; set; }

        public DateTime UpdatedTime { get; set; }

        public string DataName { get; set; }

        #region [JsonIgnore]
        [JsonIgnore]
        public DataState DataState { get; set; } = DataState.None;
        [JsonIgnore]
        public string DataRelPath { get; set; } = string.Empty;
        [JsonIgnore]
        public string DataSrcPath { get; set; } = string.Empty;
        [JsonIgnore]
        public string DataAbsPath { get; set; } = string.Empty;
        [JsonIgnore]
        public string DataHash { get; set; } = string.Empty;
        #endregion

        [JsonConstructor]
        public RecordedFile(ProjectDataType dataType, IgnoreType ignoreType, DateTime updatedTime, string dataName) 
        {
            DataType = dataType;
            IgnoreType = ignoreType;
            UpdatedTime = updatedTime;
            DataName = dataName;
        }
        
        public RecordedFile(string DataName, ProjectDataType DataType, IgnoreType IgnoreType)
        {
            this.DataType = DataType;
            this.DataName = DataName;
            this.IgnoreType = IgnoreType;
            UpdatedTime = DateTime.Now;
        }
    }
}