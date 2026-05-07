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
}
