namespace DeployAssistant.Migration
{
    /// <summary>
    /// No-operation implementation of <see cref="IMigrationPipeline{T}"/> used as
    /// a default when no migration pipeline is explicitly injected.
    /// <see cref="MigrateTo"/> requires that <paramref name="fromVersion"/> equals
    /// <paramref name="targetVersion"/> (i.e. no actual migration is needed); if
    /// they differ it throws <see cref="InvalidOperationException"/> to surface the
    /// misconfiguration rather than silently returning a wrong object.
    /// </summary>
    public sealed class NullMigrationPipeline<T> : IMigrationPipeline<T>
        where T : ISchemaVersion
    {
        /// <inheritdoc />
        public T MigrateTo(object rawSource, int fromVersion, int targetVersion)
        {
            if (fromVersion != targetVersion)
                throw new InvalidOperationException(
                    $"NullMigrationPipeline<{typeof(T).Name}> cannot migrate from " +
                    $"version {fromVersion} to {targetVersion}. " +
                    "Inject a real IMigrationPipeline<T> to enable schema migration.");

            if (rawSource is not T typed)
                throw new InvalidCastException(
                    $"Expected an instance of {typeof(T).Name} but received {rawSource?.GetType().Name ?? "null"}.");

            return typed;
        }

        /// <inheritdoc />
        public object RollbackTo(T current, int targetVersion)
        {
            if (current.SchemaVersion != targetVersion)
                throw new InvalidOperationException(
                    $"NullMigrationPipeline<{typeof(T).Name}> cannot roll back from " +
                    $"version {current.SchemaVersion} to {targetVersion}. " +
                    "Inject a real IMigrationPipeline<T> to enable rollback.");

            return current;
        }
    }
}
