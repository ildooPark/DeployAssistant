using System;
using DeployAssistant.Services;

namespace DeployAssistant.ViewModel.Internal
{
    /// <summary>
    /// Synchronous IUiDispatcher used by the scaffold [Obsolete] ctor overloads on each
    /// ViewModel. Deleted in Task 4 when AppServices wiring replaces the scaffold.
    /// </summary>
    internal sealed class SyncFallbackDispatcher : IUiDispatcher
    {
        public void Post(Action work) => work?.Invoke();
        public void Invoke(Action work) => work?.Invoke();
    }
}
