using DeployAssistant.DataComponent;

namespace DeployAssistant.Interfaces
{
    /// <summary>
    /// Carries the path/hash/state content of a tracked file or directory.
    /// Implemented by <see cref="Model.ProjectFile"/> only — not by
    /// <see cref="Model.RecordedFile"/>, whose values for these fields are meaningless.
    /// </summary>
    public interface IProjectDataContent
    {
        DataState DataState { get; set; }
        string DataRelPath { get; }
        string DataSrcPath { get; set; }
        string DataAbsPath { get; }
        string DataHash { get; set; }
    }
}
