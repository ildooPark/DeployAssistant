using System;

namespace DeployAssistant.Services
{
    /// <summary>
    /// Abstracts UI-thread marshalling. WPF impl wraps Application.Current.Dispatcher;
    /// CLI/test impls invoke synchronously.
    /// </summary>
    public interface IUiDispatcher
    {
        /// <summary>Posts the work to run on the UI thread without waiting.</summary>
        void Post(Action work);

        /// <summary>Runs the work on the UI thread, blocking until it completes.</summary>
        void Invoke(Action work);
    }
}
