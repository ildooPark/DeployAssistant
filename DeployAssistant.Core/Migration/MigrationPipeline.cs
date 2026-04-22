namespace DeployAssistant.Migration
{
    /// <summary>
    /// Concrete implementation of <see cref="IMigrationPipeline{T}"/> that
    /// chains <see cref="IMigrationStepAdapter"/> instances ordered by version.
    /// On a forward migration failure it automatically executes rollback steps
    /// in reverse and throws <see cref="MigrationRollbackException"/>.
    /// </summary>
    /// <typeparam name="T">
    /// The final target schema type that implements <see cref="ISchemaVersion"/>.
    /// </typeparam>
    public sealed class MigrationPipeline<T> : IMigrationPipeline<T>
        where T : ISchemaVersion
    {
        private readonly IReadOnlyList<IMigrationStepAdapter> _steps;

        /// <param name="steps">
        /// All step adapters that belong to this pipeline. They do not need to
        /// be pre-sorted; the pipeline sorts them internally by
        /// <see cref="IMigrationStepAdapter.FromVersion"/>.
        /// </param>
        public MigrationPipeline(IEnumerable<IMigrationStepAdapter> steps)
        {
            if (steps == null) throw new ArgumentNullException(nameof(steps));
            _steps = steps.OrderBy(s => s.FromVersion).ToList().AsReadOnly();
        }

        /// <inheritdoc />
        public T MigrateTo(object rawSource, int fromVersion, int targetVersion)
        {
            if (rawSource == null) throw new ArgumentNullException(nameof(rawSource));

            if (fromVersion == targetVersion)
                return (T)rawSource;

            if (fromVersion > targetVersion)
                throw new ArgumentException(
                    $"fromVersion ({fromVersion}) must be less than or equal to targetVersion ({targetVersion}).");

            object current = rawSource;
            int currentVersion = fromVersion;

            // Track applied steps + their outputs for rollback
            var applied = new List<(IMigrationStepAdapter Step, object Output)>();

            try
            {
                while (currentVersion < targetVersion)
                {
                    IMigrationStepAdapter step = FindForwardStep(currentVersion);
                    object output = step.Migrate(current);
                    applied.Add((step, output));
                    current = output;
                    currentVersion = step.ToVersion;
                }

                return (T)current;
            }
            catch (MigrationRollbackException)
            {
                // Already wrapped; re-throw without double-wrapping
                throw;
            }
            catch (Exception migrationEx)
            {
                // Attempt rollback of already-applied steps in reverse order
                Exception? rollbackEx = null;
                try
                {
                    RollbackApplied(applied);
                }
                catch (Exception rex)
                {
                    rollbackEx = rex;
                }

                throw new MigrationRollbackException(
                    $"Migration from version {fromVersion} to {targetVersion} failed at version {currentVersion}. " +
                    (rollbackEx == null ? "Rollback succeeded." : "Rollback also failed — see InnerExceptions."),
                    migrationEx,
                    rollbackEx);
            }
        }

        /// <inheritdoc />
        public object RollbackTo(T current, int targetVersion)
        {
            if (current == null) throw new ArgumentNullException(nameof(current));

            int currentVersion = current.SchemaVersion;
            if (currentVersion == targetVersion) return current;

            if (targetVersion > currentVersion)
                throw new ArgumentException(
                    $"targetVersion ({targetVersion}) must be less than or equal to " +
                    $"the current schema version ({currentVersion}).");

            object result = current;
            int version = currentVersion;

            while (version > targetVersion)
            {
                IMigrationStepAdapter step = FindReverseStep(version);
                result = step.Rollback(result);
                version = step.FromVersion;
            }

            return result;
        }

        // ------------------------------------------------------------------ helpers

        private IMigrationStepAdapter FindForwardStep(int fromVersion)
        {
            IMigrationStepAdapter? step = _steps.FirstOrDefault(s => s.FromVersion == fromVersion);
            if (step == null)
                throw new InvalidOperationException(
                    $"No migration step registered for schema version {fromVersion}. " +
                    $"Registered steps: [{string.Join(", ", _steps.Select(s => $"{s.FromVersion}→{s.ToVersion}"))}].");
            return step;
        }

        private IMigrationStepAdapter FindReverseStep(int toVersion)
        {
            IMigrationStepAdapter? step = _steps.FirstOrDefault(s => s.ToVersion == toVersion);
            if (step == null)
                throw new InvalidOperationException(
                    $"No migration step registered with target version {toVersion}.");
            return step;
        }

        private static void RollbackApplied(List<(IMigrationStepAdapter Step, object Output)> applied)
        {
            for (int i = applied.Count - 1; i >= 0; i--)
            {
                applied[i].Step.Rollback(applied[i].Output);
            }
        }
    }
}
