using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DeployAssistant.DataComponent;
using Xunit;

namespace DeployAssistant.Tests.DataComponent
{
    public class ProjectRegistryTests : IDisposable
    {
        private readonly string _testDir;

        public ProjectRegistryTests()
        {
            _testDir = Path.Combine(Path.GetTempPath(), "DeployAssistant_ProjectRegistryTests_" + Guid.NewGuid().ToString());
            Directory.CreateDirectory(_testDir);
            ProjectRegistry.RegistryDirectory = _testDir;
        }

        public void Dispose()
        {
            if (Directory.Exists(_testDir))
            {
                Directory.Delete(_testDir, true);
            }
        }

        [Fact]
        public void Load_WhenFileDoesNotExist_ReturnsEmptyList()
        {
            var entries = ProjectRegistry.Load();
            Assert.Empty(entries);
        }

        [Fact]
        public void SaveOrUpdate_AddsNewEntry()
        {
            ProjectRegistry.SaveOrUpdate("C:\\test\\path", "TestProject");

            var entries = ProjectRegistry.Load();
            Assert.Single(entries);
            Assert.Equal("TestProject", entries[0].Name);
            Assert.Equal("C:\\test\\path", entries[0].Path);
        }

        [Fact]
        public void SaveOrUpdate_UpdatesExistingEntry()
        {
            ProjectRegistry.SaveOrUpdate("C:\\test\\path", "TestProject");
            ProjectRegistry.SaveOrUpdate("C:\\test\\path", "UpdatedProject");

            var entries = ProjectRegistry.Load();
            Assert.Single(entries);
            Assert.Equal("UpdatedProject", entries[0].Name);
        }

        [Fact]
        public void Remove_RemovesExistingEntry()
        {
            ProjectRegistry.SaveOrUpdate("C:\\test\\path1", "Project1");
            ProjectRegistry.SaveOrUpdate("C:\\test\\path2", "Project2");

            ProjectRegistry.Remove("C:\\test\\path1");

            var entries = ProjectRegistry.Load();
            Assert.Single(entries);
            Assert.Equal("Project2", entries[0].Name);
        }
    }
}
