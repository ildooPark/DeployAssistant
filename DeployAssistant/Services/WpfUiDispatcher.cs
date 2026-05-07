using System;
using System.Windows;
using System.Windows.Threading;
using DeployAssistant.Services;

namespace DeployAssistant.Services.Wpf
{
    public sealed class WpfUiDispatcher : IUiDispatcher
    {
        private readonly Dispatcher _dispatcher;
        public WpfUiDispatcher(Dispatcher dispatcher) => _dispatcher = dispatcher;
        public WpfUiDispatcher() : this(Application.Current.Dispatcher) { }

        public void Post(Action work) => _dispatcher.BeginInvoke(work);
        public void Invoke(Action work) => _dispatcher.Invoke(work);
    }
}
