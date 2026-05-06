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
        public void RecordedFile_AsIProjectData_HasEmptyContentFields()
        {
            // Today's behavior: RecordedFile satisfies IProjectData but the content
            // half (Hash/RelPath/AbsPath/SrcPath/State) is meaningless. Task 2 (B3)
            // splits IProjectData so RecordedFile only implements the identity half;
            // this test pins down that the content fields default to empty strings
            // / DataState.None, so the split produces no behavioral change.
            IProjectData projData = new RecordedFile("x", ProjectDataType.File, IgnoreType.All);

            Assert.Equal(string.Empty, projData.DataHash);
            Assert.Equal(string.Empty, projData.DataRelPath);
            Assert.Equal(string.Empty, projData.DataSrcPath);
            Assert.Equal(string.Empty, projData.DataAbsPath);
            Assert.Equal(DataState.None, projData.DataState);
        }
    }
}
