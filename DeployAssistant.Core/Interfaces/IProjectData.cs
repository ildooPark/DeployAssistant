namespace DeployAssistant.Interfaces
{
    public enum ProjectDataType
    {
        File,
        Directory
    }

    /// <summary>
    /// Compatibility umbrella combining identity and content. Existing call sites
    /// that take <see cref="IProjectData"/> continue to work; new code should accept
    /// <see cref="IProjectDataIdentity"/> when only identity is needed (e.g. ignore lists).
    /// </summary>
    public interface IProjectData : IProjectDataIdentity, IProjectDataContent { }
}
