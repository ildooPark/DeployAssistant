namespace DeployAssistant.Interfaces
{
    /// <summary>
    /// Identifies a tracked entry by its name and type. The minimum surface area
    /// every IProjectData participant must satisfy. <see cref="Model.RecordedFile"/>
    /// implements only this; <see cref="Model.ProjectFile"/> additionally implements
    /// <see cref="IProjectDataContent"/>.
    /// </summary>
    public interface IProjectDataIdentity
    {
        ProjectDataType DataType { get; }
        string DataName { get; }
        DateTime UpdatedTime { get; set; }
    }
}
