using System;
using DeployAssistant.Services;

namespace DeployAssistant.CLI
{
    /// <summary>Synchronous dispatcher — CLI has no UI thread to marshal to.</summary>
    internal sealed class ImmediateUiDispatcher : IUiDispatcher
    {
        public void Post(Action work) => work?.Invoke();
        public void Invoke(Action work) => work?.Invoke();
    }
}
