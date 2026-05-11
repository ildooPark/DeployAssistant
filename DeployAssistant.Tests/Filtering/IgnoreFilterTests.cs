#pragma warning disable CS0618  // V1 types used intentionally for filter construction

using DeployAssistant.Filtering;
using DeployAssistant.Interfaces;
using DeployAssistant.Model;
using Xunit;

namespace DeployAssistant.Tests.Filtering
{
    /// <summary>
    /// Behavioral tests for <see cref="IIgnoreFilter"/>.  The filter is the single
    /// predicate-based primitive that replaces ProjectIgnoreData's three overlapping
    /// helpers (GetIgnoreFilesAndDirPaths / FilterChangedFileList / PartOfIgnore).
    /// </summary>
    public class IgnoreFilterTests
    {
        [Fact]
        public void Matches_LiteralFileName_InScope_ReturnsTrue()
        {
            // ProjectMetaData.bin is a default IgnoreType.All entry — it must match
            // when queried under IgnoreType.IntegrityCheck (a sub-flag of All).
            var ignoreData = new ProjectIgnoreData("AnyProj");
            IIgnoreFilter filter = IgnoreFilter.FromIgnoreData(ignoreData);

            bool result = filter.Matches(
                relativePath: "ProjectMetaData.bin",
                dataType: ProjectDataType.File,
                scope: IgnoreType.IntegrityCheck);

            Assert.True(result);
        }

        [Fact]
        public void Matches_AfterConfigureDefaultIgnore_DeployFileMatchesInitializationScope()
        {
            // End-to-end scenario covered by Fix 4 in PR-A: after ConfigureDefaultIgnore,
            // the *.deploy entry must carry IgnoreType.Initialization so a Deploy file
            // is filtered out of project-initialization scans.  This single test exercises
            // (a) glob matching, (b) flag composition (Deploy | Initialization),
            // and (c) the IgnoreFilter's "live view" of the underlying ProjectIgnoreData.
            var ignoreData = new ProjectIgnoreData("MyProj");
            ignoreData.ConfigureDefaultIgnore("MyProj");
            IIgnoreFilter filter = IgnoreFilter.FromIgnoreData(ignoreData);

            Assert.True(filter.Matches("DeployAssistant.deploy", ProjectDataType.File, IgnoreType.Initialization));
            Assert.True(filter.Matches("Backup_MyProj", ProjectDataType.Directory, IgnoreType.Initialization));
            Assert.True(filter.Matches(@"Backup_MyProj\old.dll", ProjectDataType.File, IgnoreType.Initialization));
        }

        [Fact]
        public void Matches_FileUnderIgnoredDirectory_ReturnsTrue()
        {
            // "en-US" is a default Directory entry with IgnoreType.Integration.
            // A file living inside that directory ("en-US/strings.dll") must
            // match when queried under Integration scope, even though the
            // dataType being queried is File.
            var ignoreData = new ProjectIgnoreData("AnyProj");
            IIgnoreFilter filter = IgnoreFilter.FromIgnoreData(ignoreData);

            bool result = filter.Matches(
                relativePath: @"en-US\strings.dll",
                dataType: ProjectDataType.File,
                scope: IgnoreType.Integration);

            Assert.True(result);
        }

        [Fact]
        public void Matches_GlobFileEntry_MatchesConcreteFileName()
        {
            // Default entry "*.ignore" (IgnoreType.All) must match a concrete
            // filename "DeployAssistant.ignore".  The existing ProjectIgnoreData
            // .PartOfIgnore did NOT do this — globs only resolved through the
            // filesystem helper.  The new filter unifies both code paths.
            var ignoreData = new ProjectIgnoreData("AnyProj");
            IIgnoreFilter filter = IgnoreFilter.FromIgnoreData(ignoreData);

            bool result = filter.Matches(
                relativePath: "DeployAssistant.ignore",
                dataType: ProjectDataType.File,
                scope: IgnoreType.IntegrityCheck);

            Assert.True(result);
        }
    }
}
