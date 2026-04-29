namespace DeployAssistant.Model.V2
{
    /// <summary>
    /// Discriminates between file-system entries tracked in a <see cref="FileRecord"/>.
    /// Replaces the V1 <c>ProjectDataType</c> enum in <c>DeployAssistant.Interfaces</c>.
    /// </summary>
    public enum FileKind
    {
        File,
        Directory
    }
}
