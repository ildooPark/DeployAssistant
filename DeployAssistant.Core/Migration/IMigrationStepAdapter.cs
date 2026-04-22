namespace DeployAssistant.Migration
{
    /// <summary>
    /// Type-erased adapter that lets <see cref="MigrationPipeline{T}"/> chain
    /// heterogeneous <see cref="IMigrationStep{TIn,TOut}"/> instances at runtime
    /// without needing the concrete generic parameters.
    /// </summary>
    public interface IMigrationStepAdapter
    {
        /// <summary>Schema version this step migrates <em>from</em>.</summary>
        int FromVersion { get; }

        /// <summary>Schema version this step migrates <em>to</em>.</summary>
        int ToVersion { get; }

        /// <summary>Forward migration on a boxed source object.</summary>
        object Migrate(object source);

        /// <summary>
        /// Inverse migration on a boxed migrated object.
        /// The <paramref name="migrated"/> argument must be the value
        /// that was produced by the most recent call to <see cref="Migrate"/>.
        /// </summary>
        object Rollback(object migrated);
    }
}
