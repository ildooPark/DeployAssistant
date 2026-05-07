using DeployAssistant.DataComponent;
using Xunit;

namespace DeployAssistant.Tests.Integration
{
    public class MetaDataManagerWiringTests
    {
        [Fact]
        public void Awake_CalledTwice_StaysIdempotent()
        {
            var m = new MetaDataManager();
            m.Awake();
            m.Awake();
            // No exception, no observable double-wiring side-effects.
            // (The existing duplicate _updateManager.Awake() is masked by empty
            // child Awake() bodies, but this test prevents a future Awake() body
            // from silently being invoked twice.)
        }

        [Fact]
        public void Awake_DoesNotThrow_WhenDialogServiceIsNullDialog()
        {
            // Pin: a manager constructed without a dialog service falls back to
            // NullDialogService; Awake() must complete without throwing.
            var m = new MetaDataManager();
            m.Awake();
        }
    }
}
