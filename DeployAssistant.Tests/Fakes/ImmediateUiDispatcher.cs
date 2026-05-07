using System;
using DeployAssistant.Services;

namespace DeployAssistant.Tests.Fakes
{
    /// <summary>Runs callbacks synchronously on the calling thread. Use for headless ViewModel tests.</summary>
    public sealed class ImmediateUiDispatcher : IUiDispatcher
    {
        public void Post(Action work) => work?.Invoke();
        public void Invoke(Action work) => work?.Invoke();
    }
}
