using DeployAssistant.Model;

namespace DeployAssistant.Filtering
{
    /// <summary>
    /// Bundles a project's persisted metaData with the predicate-based ignore
    /// filter and scanner derived from its ignore configuration.  Created in one
    /// place (MetaDataManager after both files load) and handed to consumers
    /// (FileManager, etc.) as a single immutable reference.  Replaces the prior
    /// two-event coordination (MetaDataLoaded + UpdateIgnoreList).
    /// </summary>
    public sealed class ProjectContext
    {
        public ProjectMetaData MetaData { get; }
        public ProjectIgnoreData IgnoreData { get; }
        public IIgnoreFilter IgnoreFilter { get; }
        public ProjectScanner Scanner { get; }

        private ProjectContext(ProjectMetaData metaData, ProjectIgnoreData ignoreData, IIgnoreFilter filter, ProjectScanner scanner)
        {
            MetaData = metaData;
            IgnoreData = ignoreData;
            IgnoreFilter = filter;
            Scanner = scanner;
        }

        public static ProjectContext Create(ProjectMetaData metaData, ProjectIgnoreData ignoreData)
        {
            IIgnoreFilter filter = DeployAssistant.Filtering.IgnoreFilter.FromIgnoreData(ignoreData);
            var scanner = new ProjectScanner(filter);
            return new ProjectContext(metaData, ignoreData, filter, scanner);
        }
    }
}
