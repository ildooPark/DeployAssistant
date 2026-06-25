using System;
using System.IO;
using DeployAssistant.DataComponent;
using DeployAssistant.Services;
using DeployAssistant.Tests.Fakes;
using DeployAssistant.ViewModel;
using Xunit;

namespace DeployAssistant.Tests.ViewModel
{
    public class ProjectSelectionViewModelTests : IDisposable
    {
        private readonly string _testDir;
        private readonly FakeDialogService _dialogService;

        public ProjectSelectionViewModelTests()
        {
            _testDir = Path.Combine(Path.GetTempPath(), "DeployAssistant_ProjectSelectionVMTests_" + Guid.NewGuid().ToString());
            Directory.CreateDirectory(_testDir);
            ProjectRegistry.RegistryDirectory = _testDir;

            _dialogService = new FakeDialogService();
        }

        public void Dispose()
        {
            if (Directory.Exists(_testDir))
            {
                Directory.Delete(_testDir, true);
            }
        }

        [Fact]
        public void Projects_AreLoadedOnInitialization()
        {
            ProjectRegistry.SaveOrUpdate("C:\\path1", "Project1");
            ProjectRegistry.SaveOrUpdate("C:\\path2", "Project2");

            var vm = new ProjectSelectionViewModel(_dialogService);

            Assert.Equal(2, vm.Projects.Count);
        }

        [Fact]
        public void OpenCommand_SetsSelectedPathAndInvokesClose()
        {
            ProjectRegistry.SaveOrUpdate("C:\\path1", "Project1");
            var vm = new ProjectSelectionViewModel(_dialogService);
            vm.SelectedProject = vm.Projects[0];

            bool closeInvoked = false;
            vm.RequestCloseWithSuccess = () => closeInvoked = true;

            Assert.True(vm.OpenCommand.CanExecute(null));
            vm.OpenCommand.Execute(null);

            Assert.True(closeInvoked);
            Assert.Equal("C:\\path1", vm.SelectedPath);
        }

        [Fact]
        public void RemoveCommand_RemovesProjectWhenConfirmed()
        {
            ProjectRegistry.SaveOrUpdate("C:\\path1", "Project1");
            var vm = new ProjectSelectionViewModel(_dialogService);
            vm.SelectedProject = vm.Projects[0];

            _dialogService.EnqueueConfirm(DialogChoice.Yes);

            Assert.True(vm.RemoveCommand.CanExecute(null));
            vm.RemoveCommand.Execute(null);

            Assert.Empty(vm.Projects);
            Assert.Null(vm.SelectedProject);
            Assert.Empty(ProjectRegistry.Load());
        }

        [Fact]
        public void BrowseCommand_SetsSelectedPathWhenFolderPicked()
        {
            var vm = new ProjectSelectionViewModel(_dialogService);
            _dialogService.EnqueueFolder("C:\\picked\\folder");

            bool closeInvoked = false;
            vm.RequestCloseWithSuccess = () => closeInvoked = true;

            Assert.True(vm.BrowseCommand.CanExecute(null));
            vm.BrowseCommand.Execute(null);

            Assert.True(closeInvoked);
            Assert.Equal("C:\\picked\\folder", vm.SelectedPath);
        }
    }
}
