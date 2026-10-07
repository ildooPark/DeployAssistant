using DeployAssistant.DataComponent;
using DeployAssistant.Services;
using DeployAssistant.Tests.Fakes;
using DeployAssistant.ViewModel;
using Xunit;

namespace DeployAssistant.Tests.ViewModel
{
    public class MetaDataViewModelDialogTests
    {
        [Fact]
        public void Construct_WithFakeDialogAndDispatcher_DoesNotTouchWpf()
        {
            // Given: a real MetaDataManager and the fake services.
            var manager = new MetaDataManager(new FakeDialogService());
            manager.Awake();

            var fakeDialog = new FakeDialogService();
            var dispatcher = new ImmediateUiDispatcher();

            // When: VM is constructed without a live WPF Application.
            var vm = new MetaDataViewModel(manager, fakeDialog, dispatcher);

            // Then: construction completes; no Application.Current dependency.
            Assert.NotNull(vm);
        }
    }

    public class FileTrackViewModelRefreshTests : System.IDisposable
    {
        private readonly string _projectDir;
        private readonly string _dropSrc;
        private readonly string _scanSrc;

        public FileTrackViewModelRefreshTests()
        {
            string stamp = System.Guid.NewGuid().ToString("N");
            _projectDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "DA_RefreshProj_" + stamp);
            System.IO.Directory.CreateDirectory(System.IO.Path.Combine(_projectDir, "sub"));
            System.IO.File.WriteAllText(System.IO.Path.Combine(_projectDir, "sub", "engine.dll"), "engine v1");
            _dropSrc = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "DA_RefreshDrop_" + stamp);
            System.IO.Directory.CreateDirectory(_dropSrc);
            System.IO.File.WriteAllText(System.IO.Path.Combine(_dropSrc, "engine.dll"), "engine v2");
            _scanSrc = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "DA_RefreshScan_" + stamp);
            System.IO.Directory.CreateDirectory(System.IO.Path.Combine(_scanSrc, "sub"));
            System.IO.File.WriteAllText(System.IO.Path.Combine(_scanSrc, "sub", "scanned.dll"), "scanned");
        }

        public void Dispose()
        {
            foreach (string dir in new[] { _projectDir, _dropSrc, _scanSrc })
            {
                try { if (System.IO.Directory.Exists(dir)) System.IO.Directory.Delete(dir, recursive: true); }
                catch (System.IO.IOException) { }
            }
        }

        private static async System.Threading.Tasks.Task InitAsync(MetaDataManager mgr, string dir)
        {
            mgr.RequestProjectInitialization(dir);
            for (int i = 0; i < 100 && (mgr.CurrentState != MetaDataState.Idle || mgr.ProjectMetaData == null); i++)
                await System.Threading.Tasks.Task.Delay(100);
            Assert.NotNull(mgr.ProjectMetaData);
        }

        [Fact]
        public async System.Threading.Tasks.Task RefreshCommand_NoSrcSelected_IsDisabledAndExecuteKeepsDroppedFiles()
        {
            var mgr = new MetaDataManager(new FakeDialogService());
            mgr.Awake();
            var vm = new FileTrackViewModel(mgr, new FakeDialogService(), new ImmediateUiDispatcher());
            await InitAsync(mgr, _projectDir);

            object? lastView = null;
            mgr.FileChangesEventHandler += p => lastView = p;
            mgr.RequestDroppedFiles(new[] { System.IO.Path.Combine(_dropSrc, "engine.dll") });

            Assert.False(vm.RefreshDeployFileList.CanExecute(null));

            vm.RefreshDeployFileList.Execute(null);
            await System.Threading.Tasks.Task.Delay(200);

            var view = Assert.IsAssignableFrom<System.Collections.ObjectModel.ObservableCollection<DeployAssistant.Model.ProjectFile>>(lastView);
            Assert.Contains(view, f => f.DataRelPath == System.IO.Path.Combine("sub", "engine.dll"));
        }

        [Fact]
        public async System.Threading.Tasks.Task RefreshCommand_WithSrcSelected_IsEnabledAndPreservesDroppedFiles()
        {
            var mgr = new MetaDataManager(new FakeDialogService());
            mgr.Awake();
            var dialog = new FakeDialogService();
            var vm = new FileTrackViewModel(mgr, dialog, new ImmediateUiDispatcher());
            await InitAsync(mgr, _projectDir);

            dialog.EnqueueFolder(_scanSrc);
            vm.GetDeploySrcDir.Execute(null);
            await System.Threading.Tasks.Task.Delay(300);

            object? lastView = null;
            mgr.FileChangesEventHandler += p => lastView = p;
            mgr.RequestDroppedFiles(new[] { System.IO.Path.Combine(_dropSrc, "engine.dll") });

            Assert.True(vm.RefreshDeployFileList.CanExecute(null));

            vm.RefreshDeployFileList.Execute(null);
            await System.Threading.Tasks.Task.Delay(300);

            var view = Assert.IsAssignableFrom<System.Collections.ObjectModel.ObservableCollection<DeployAssistant.Model.ProjectFile>>(lastView);
            Assert.Contains(view, f => f.DataRelPath == System.IO.Path.Combine("sub", "engine.dll"));
            Assert.Contains(view, f => f.DataName == "scanned.dll");
        }
    }
}
