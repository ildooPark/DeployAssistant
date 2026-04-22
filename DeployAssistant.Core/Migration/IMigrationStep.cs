namespace DeployAssistant.Migration
{
    /// <summary>
    /// Type-safe contract for a single schema migration step that transforms
    /// an instance of <typeparamref name="TIn"/> (old schema) into
    /// <typeparamref name="TOut"/> (new schema) and provides a lossless rollback.
    /// </summary>
    /// <typeparam name="TIn">Source (older) schema type.</typeparam>
    /// <typeparam name="TOut">Target (newer) schema type.</typeparam>
    public interface IMigrationStep<TIn, TOut>
    {
        /// <summary>Schema version this step migrates <em>from</em>.</summary>
        int FromVersion { get; }

        /// <summary>Schema version this step migrates <em>to</em>.</summary>
        int ToVersion { get; }

        /// <summary>Forward migration: convert old schema to new schema.</summary>
        TOut Migrate(TIn source);

        /// <summary>
        /// Inverse migration: reconstruct the old schema from the new schema.
        /// Used by the pipeline to roll back a failed multi-step migration.
        /// </summary>
        TIn Rollback(TOut migrated);
    }
}
