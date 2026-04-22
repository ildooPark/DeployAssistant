namespace DeployAssistant.Migration
{
    /// <summary>
    /// Chains zero or more <see cref="IMigrationStepAdapter"/> instances to
    /// transform a versioned data object from one schema version to another and,
    /// when needed, to roll the transformation back.
    /// </summary>
    /// <typeparam name="T">The final target type produced after all forward steps.</typeparam>
    public interface IMigrationPipeline<T>
    {
        /// <summary>
        /// Migrate a raw (possibly boxed) source object from
        /// <paramref name="fromVersion"/> to <paramref name="targetVersion"/>
        /// by executing the appropriate chain of migration steps.
        /// </summary>
        /// <param name="rawSource">The source object to migrate.</param>
        /// <param name="fromVersion">Schema version of <paramref name="rawSource"/>.</param>
        /// <param name="targetVersion">Desired schema version on return.</param>
        /// <returns>A fully-migrated instance of <typeparamref name="T"/>.</returns>
        T MigrateTo(object rawSource, int fromVersion, int targetVersion);

        /// <summary>
        /// Roll back <paramref name="current"/> from its current schema version
        /// to <paramref name="targetVersion"/> by executing migration steps in
        /// reverse order, calling each step's <c>Rollback</c> method.
        /// </summary>
        /// <returns>
        /// The de-migrated object (type depends on <paramref name="targetVersion"/>).
        /// </returns>
        object RollbackTo(T current, int targetVersion);
    }
}
