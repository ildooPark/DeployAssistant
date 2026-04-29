namespace DeployAssistant.Migration
{
    /// <summary>
    /// Marks a data type as carrying an integer schema version so the migration
    /// pipeline can discover its current version without out-of-band metadata.
    /// </summary>
    public interface ISchemaVersion
    {
        /// <summary>Schema version of this instance. V2 types set this to 2.</summary>
        int SchemaVersion { get; }
    }
}
