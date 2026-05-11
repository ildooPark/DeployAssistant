#pragma warning disable CS0618  // V1 types used intentionally

using DeployAssistant.Filtering;
using DeployAssistant.Interfaces;
using DeployAssistant.Model;
using Xunit;

namespace DeployAssistant.Tests.Filtering
{
    /// <summary>
    /// <see cref="ProjectContext"/> bundles the per-project metaData with a
    /// predicate-based ignore filter (and a scanner derived from it).  It is the
    /// single object passed to FileManager via one callback, replacing the prior
    /// MetaDataLoaded + UpdateIgnoreListEventHandler two-event coordination.
    /// </summary>
    public class ProjectContextTests
    {
        [Fact]
        public void Create_ExposesFilterConsistentWithProvidedIgnoreData()
        {
            // After ConfigureDefaultIgnore("MyProj"), Backup_MyProj should be
            // filterable under Initialization scope.  The ProjectContext exposes
            // exactly that filter — anyone holding the context gets a consistent
            // view of metaData + ignore semantics.
            var metaData = new ProjectMetaData("MyProj", @"C:\Fake");
            var ignoreData = new ProjectIgnoreData("MyProj");
            ignoreData.ConfigureDefaultIgnore("MyProj");

            var ctx = ProjectContext.Create(metaData, ignoreData);

            Assert.Same(metaData, ctx.MetaData);
            Assert.NotNull(ctx.IgnoreFilter);
            Assert.NotNull(ctx.Scanner);
            Assert.True(ctx.IgnoreFilter.Matches(
                "Backup_MyProj", ProjectDataType.Directory, IgnoreType.Initialization));
        }

        [Fact]
        public void Create_ExposesUnderlyingIgnoreData()
        {
            // The context owns the ProjectIgnoreData reference because that is the
            // persisted form (DeployAssistant.ignore on disk).  Filter/Scanner are
            // the runtime views; IgnoreData is the durable view.
            var metaData = new ProjectMetaData("P", @"C:\Fake");
            var ignoreData = new ProjectIgnoreData("P");

            var ctx = ProjectContext.Create(metaData, ignoreData);

            Assert.Same(ignoreData, ctx.IgnoreData);
        }
    }
}
