using System;
using System.IO;
using DeployAssistant.CLI.Engine;
using DeployAssistant.CLI.Screens;
using DeployAssistant.DataComponent;
using Xunit;

namespace DeployAssistant.CLI.Tests.Screens
{
    // Important: run sequentially if it modifies static global state, but in this case, 
    // we assume XUnit runs tests from different test classes in parallel, but tests 
    // within the same class sequentially. Setting global static state is risky if 
    // other test classes also run simultaneously, but it's the simplest approach.
    [Collection("Sequential")]
    public class ProjectListScreenTests : IDisposable
    {
        private readonly string _testDir;

        public ProjectListScreenTests()
        {
            _testDir = Path.Combine(Path.GetTempPath(), "DeployAssistant_ProjectListScreenTests_" + Guid.NewGuid().ToString());
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
        public void Handle_DownArrow_SelectsNextItem()
        {
            ProjectRegistry.SaveOrUpdate("C:\\path1", "Project1");
            ProjectRegistry.SaveOrUpdate("C:\\path2", "Project2");

            var screen = new ProjectListScreen();

            var result = screen.Handle(new ConsoleKeyInfo('\0', ConsoleKey.DownArrow, false, false, false));

            Assert.Equal(ScreenAction.StayAction, result);
        }

        [Fact]
        public void Handle_EscapeKey_ReturnsPopAction()
        {
            var screen = new ProjectListScreen();
            var result = screen.Handle(new ConsoleKeyInfo('\x1B', ConsoleKey.Escape, false, false, false));

            Assert.Equal(ScreenAction.PopAction, result);
        }

        [Fact]
        public void Handle_EnterOnBrowse_PushesPathPickerScreen()
        {
            var screen = new ProjectListScreen();
            
            // Initial selection is 0, which is "Browse..." since there are no projects.
            var result = screen.Handle(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));

            var pushAction = Assert.IsType<ScreenAction.Push>(result);
            Assert.IsType<PathPickerScreen>(pushAction.Next);
        }

        [Fact]
        public void Handle_Delete_RemovesProject()
        {
            ProjectRegistry.SaveOrUpdate("C:\\path1", "Project1");
            var screen = new ProjectListScreen();
            
            // Selected item 0 is the project
            var result = screen.Handle(new ConsoleKeyInfo('\0', ConsoleKey.Delete, false, false, false));

            Assert.Equal(ScreenAction.StayAction, result);
            Assert.Empty(ProjectRegistry.Load());
        }

        [Fact]
        public void Handle_R_PushesProjectNameScreen()
        {
            ProjectRegistry.SaveOrUpdate("C:\\path1", "Project1");
            var screen = new ProjectListScreen();
            
            // Selected item 0 is the project
            var result = screen.Handle(new ConsoleKeyInfo('r', ConsoleKey.R, false, false, false));

            var pushAction = Assert.IsType<ScreenAction.Push>(result);
            Assert.IsType<ProjectNameScreen>(pushAction.Next);
        }
    }
}
