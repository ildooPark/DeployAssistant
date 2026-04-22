namespace DeployAssistant.Model.V2
{
    /// <summary>
    /// Describes <em>what</em> kind of change a <see cref="FileDiff"/> represents.
    /// Replaces the change-kind bits of the V1 <c>DataState</c> flags enum
    /// (<c>Added | Deleted | Modified | Restored</c>).
    /// </summary>
    public enum ChangeKind
    {
        Added,
        Deleted,
        Modified,
        Restored
    }
}
