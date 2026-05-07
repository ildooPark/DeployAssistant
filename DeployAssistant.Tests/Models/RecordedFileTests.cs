#pragma warning disable CS0618 // ProjectFile is [Obsolete] — tests deliberately exercise the V1 type
using DeployAssistant.DataComponent;
using DeployAssistant.Interfaces;
using DeployAssistant.Model;
using Xunit;

namespace DeployAssistant.Tests.Models
{
    public class RecordedFileTests
    {
        [Fact]
        public void Constructor_NameTypeIgnore_StampsUpdatedTime()
        {
            var rf = new RecordedFile("ProjectMetaData.bin", ProjectDataType.File, IgnoreType.All);

            Assert.Equal("ProjectMetaData.bin", rf.DataName);
            Assert.Equal(ProjectDataType.File, rf.DataType);
            Assert.Equal(IgnoreType.All, rf.IgnoreType);
            Assert.NotEqual(default, rf.UpdatedTime);
        }

        [Fact]
        public void RecordedFile_ImplementsIdentityOnly()
        {
            var rf = new RecordedFile("z", ProjectDataType.Directory, IgnoreType.Integration);

            Assert.IsAssignableFrom<IProjectDataIdentity>(rf);
            Assert.False(rf is IProjectDataContent,
                "RecordedFile must NOT implement IProjectDataContent — it has no meaningful hash/path");
        }

        [Fact]
        public void ProjectFile_ImplementsBothIdentityAndContent()
        {
            var pf = new ProjectFile(
                DataSize: 1, BuildVersion: "1.0", DataName: "a.dll",
                DataSrcPath: @"C:\X", DataRelPath: "a.dll");

            Assert.IsAssignableFrom<IProjectDataIdentity>(pf);
            Assert.IsAssignableFrom<IProjectDataContent>(pf);
        }
    }
}
#pragma warning restore CS0618
