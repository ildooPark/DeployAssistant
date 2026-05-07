using DeployAssistant.DataComponent;
using DeployAssistant.Interfaces;

namespace DeployAssistant.Model
{
    public class RecordedFile : IProjectDataIdentity
    {
        public ProjectDataType DataType { get; set; }

        public IgnoreType IgnoreType { get; set; }

        public DateTime UpdatedTime { get; set; }

        public string DataName { get; set; }

        [System.Text.Json.Serialization.JsonConstructor]
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
