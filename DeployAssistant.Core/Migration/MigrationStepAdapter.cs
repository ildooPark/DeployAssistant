namespace DeployAssistant.Migration
{
    /// <summary>
    /// Bridges a strongly-typed <see cref="IMigrationStep{TIn,TOut}"/> to the
    /// type-erased <see cref="IMigrationStepAdapter"/> interface consumed by
    /// <see cref="MigrationPipeline{T}"/>.
    /// </summary>
    public sealed class MigrationStepAdapter<TIn, TOut> : IMigrationStepAdapter
    {
        private readonly IMigrationStep<TIn, TOut> _step;

        public MigrationStepAdapter(IMigrationStep<TIn, TOut> step)
        {
            _step = step ?? throw new ArgumentNullException(nameof(step));
        }

        public int FromVersion => _step.FromVersion;
        public int ToVersion   => _step.ToVersion;

        public object Migrate(object source)
        {
            if (source is not TIn typed)
                throw new InvalidCastException(
                    $"Migration step {_step.GetType().Name} expected input of type {typeof(TIn).Name} " +
                    $"but received {source?.GetType().Name ?? "null"}.");
            return _step.Migrate(typed)!;
        }

        public object Rollback(object migrated)
        {
            if (migrated is not TOut typed)
                throw new InvalidCastException(
                    $"Migration step {_step.GetType().Name} expected rollback input of type {typeof(TOut).Name} " +
                    $"but received {migrated?.GetType().Name ?? "null"}.");
            return _step.Rollback(typed)!;
        }
    }
}
