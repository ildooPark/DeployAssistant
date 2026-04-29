namespace DeployAssistant.Migration
{
    /// <summary>
    /// Thrown by <see cref="MigrationPipeline{T}"/> when a forward migration fails
    /// and the pipeline attempts (and optionally succeeds at) rolling back the
    /// already-applied steps.
    /// </summary>
    public sealed class MigrationRollbackException : Exception
    {
        /// <summary>
        /// The original exception that triggered the rollback, or <c>null</c>
        /// if the rollback was requested explicitly.
        /// </summary>
        public Exception? MigrationCause { get; }

        /// <summary>
        /// An exception thrown during the rollback itself, or <c>null</c>
        /// if the rollback completed without error.
        /// </summary>
        public Exception? RollbackCause { get; }

        /// <summary>
        /// <c>true</c> if the rollback completed successfully (even though the
        /// forward migration failed); <c>false</c> if the rollback itself also
        /// threw an exception.
        /// </summary>
        public bool RollbackSucceeded => RollbackCause == null;

        public MigrationRollbackException(
            string message,
            Exception? migrationCause,
            Exception? rollbackCause)
            : base(message, migrationCause)
        {
            MigrationCause = migrationCause;
            RollbackCause = rollbackCause;
        }
    }
}
