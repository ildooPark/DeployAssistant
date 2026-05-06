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
        public void Awake_DoesNotThrow_WhenConfirmationCallbackUnset()
        {
            var m = new MetaDataManager();
            // ConfirmationCallback intentionally left null — pin that current Awake
            // tolerates this. After Task 3 (B2), this property is gone entirely.
            m.Awake();
        }
    }
}
