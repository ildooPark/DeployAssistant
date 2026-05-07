using DeployAssistant.DataComponent;
using DeployAssistant.Services;
using DeployAssistant.Services.Wpf;

namespace DeployAssistant
{
    /// <summary>
    /// Composition root for the WPF GUI. Constructed once in App.OnStartup and
    /// passed down to MainWindow, then to child windows. Replaces the old
    /// App.MetaDataManager static singleton.
    /// </summary>
    public sealed class AppServices
    {
        public MetaDataManager MetaDataManager { get; }
        public IDialogService DialogService { get; }
        public IUiDispatcher UiDispatcher { get; }

        public AppServices()
        {
            DialogService = new WpfDialogService();
            UiDispatcher = new WpfUiDispatcher();
            MetaDataManager = new MetaDataManager(DialogService);
            MetaDataManager.Awake();
        }
    }
}
