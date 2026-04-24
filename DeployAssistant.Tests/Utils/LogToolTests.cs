#pragma warning disable CS0618  // V1 types used intentionally for V1 utility tests

using DeployAssistant.DataComponent;
using DeployAssistant.Interfaces;
using DeployAssistant.Model;
using DeployAssistant.Utils;
using System;
using System.Text;
using Xunit;

namespace DeployAssistant.Tests.Utils
{
    public class LogToolTests
    {
        private static ProjectFile MakeFile(string relPath, string name, string srcPath = @"C:\Proj",
            string buildVersion = "1.0", string hash = "abc123")
        {
            return new ProjectFile(
                DataType: ProjectDataType.File,
                DataSize: 128,
                BuildVersion: buildVersion,
                DeployedProjectVersion: "1.0",
                UpdatedTime: new DateTime(2024, 6, 1, 12, 0, 0, DateTimeKind.Local),
                DataState: DataState.Modified,
                dataName: name,
                dataSrcPath: srcPath,
                dataRelPath: relPath,
                dataHash: hash,
                IsDstFile: false);
        }

        #region RegisterUpdate

        [Fact]
        public void RegisterUpdate_AppendsVersionHeader()
        {
            var log = new StringBuilder();
            LogTool.RegisterUpdate(log, "1.0", "2.0");
            var result = log.ToString();
            Assert.Contains("From 1.0", result);
            Assert.Contains("To 2.0", result);
            Assert.Contains("Updating Project", result);
        }

        [Fact]
        public void RegisterUpdate_EmptyVersions_StillAppends()
        {
            var log = new StringBuilder();
            LogTool.RegisterUpdate(log, "", "");
            var result = log.ToString();
            Assert.Contains("Updating Project", result);
        }

        [Fact]
        public void RegisterUpdate_AppendsToExistingLog()
        {
            var log = new StringBuilder();
            log.AppendLine("Existing entry");
            LogTool.RegisterUpdate(log, "3.0", "4.0");
            var result = log.ToString();
            Assert.Contains("Existing entry", result);
            Assert.Contains("From 3.0", result);
        }

        #endregion

        #region RegisterChange (single file)

        [Fact]
        public void RegisterChange_SingleFile_ContainsStateAndRelPath()
        {
            var log = new StringBuilder();
            var file = MakeFile(@"src\MyFile.dll", "MyFile.dll");
            LogTool.RegisterChange(log, DataState.Modified, file);
            var result = log.ToString();
            Assert.Contains("Modified", result);
            Assert.Contains(@"src\MyFile.dll", result);
        }

        [Fact]
        public void RegisterChange_AddedState_ReflectedInLog()
        {
            var log = new StringBuilder();
            var file = MakeFile(@"new\Added.dll", "Added.dll");
            LogTool.RegisterChange(log, DataState.Added, file);
            Assert.Contains("Added", log.ToString());
        }

        [Fact]
        public void RegisterChange_DeletedState_ReflectedInLog()
        {
            var log = new StringBuilder();
            var file = MakeFile(@"old\Removed.dll", "Removed.dll");
            LogTool.RegisterChange(log, DataState.Deleted, file);
            Assert.Contains("Deleted", log.ToString());
        }

        [Fact]
        public void RegisterChange_SingleFile_ContainsTimestamp()
        {
            var log = new StringBuilder();
            var file = MakeFile(@"file.dll", "file.dll");
            LogTool.RegisterChange(log, DataState.Modified, file);
            Assert.Contains("2024", log.ToString());
        }

        #endregion

        #region RegisterChange (src + dst pair)

        [Fact]
        public void RegisterChange_BothFiles_ContainsBuildVersions()
        {
            var log = new StringBuilder();
            var src = MakeFile(@"lib\A.dll", "A.dll", buildVersion: "1.0", hash: "hash1");
            var dst = MakeFile(@"lib\A.dll", "A.dll", buildVersion: "2.0", hash: "hash2");
            LogTool.RegisterChange(log, DataState.Modified, src, dst);
            var result = log.ToString();
            Assert.Contains("1.0", result);
            Assert.Contains("2.0", result);
        }

        [Fact]
        public void RegisterChange_BothFiles_ContainsHashes()
        {
            var log = new StringBuilder();
            var src = MakeFile(@"lib\B.dll", "B.dll", hash: "aabbcc");
            var dst = MakeFile(@"lib\B.dll", "B.dll", hash: "ddeeff");
            LogTool.RegisterChange(log, DataState.Modified, src, dst);
            var result = log.ToString();
            Assert.Contains("aabbcc", result);
            Assert.Contains("ddeeff", result);
        }

        [Fact]
        public void RegisterChange_NullSrc_FallsBackToSingleFileOverload()
        {
            var log = new StringBuilder();
            var dst = MakeFile(@"lib\C.dll", "C.dll");
            LogTool.RegisterChange(log, DataState.Added, null, dst);
            var result = log.ToString();
            Assert.Contains("Added", result);
            Assert.Contains(@"lib\C.dll", result);
            // The "From / To" detail lines must NOT appear for null-src
            Assert.DoesNotContain("From : Build Version:", result);
        }

        [Fact]
        public void RegisterChange_BothFiles_ContainsRelPath()
        {
            var log = new StringBuilder();
            var src = MakeFile(@"sub\dir\File.dll", "File.dll", buildVersion: "1.0");
            var dst = MakeFile(@"sub\dir\File.dll", "File.dll", buildVersion: "1.5");
            LogTool.RegisterChange(log, DataState.Modified, src, dst);
            Assert.Contains(@"sub\dir\File.dll", log.ToString());
        }

        [Fact]
        public void RegisterChange_BothFiles_AppendsEmptyLineAtEnd()
        {
            var log = new StringBuilder();
            var src = MakeFile(@"file.dll", "file.dll");
            var dst = MakeFile(@"file.dll", "file.dll");
            LogTool.RegisterChange(log, DataState.Modified, src, dst);
            // The overload appends a blank line at the end
            Assert.EndsWith(Environment.NewLine, log.ToString());
        }

        #endregion

        #region Multiple calls accumulate

        [Fact]
        public void MultipleRegisterChange_AccumulatesAllEntries()
        {
            var log = new StringBuilder();
            var file1 = MakeFile(@"a.dll", "a.dll");
            var file2 = MakeFile(@"b.dll", "b.dll");
            LogTool.RegisterChange(log, DataState.Added, file1);
            LogTool.RegisterChange(log, DataState.Deleted, file2);
            var result = log.ToString();
            Assert.Contains("a.dll", result);
            Assert.Contains("b.dll", result);
        }

        #endregion
    }
}
