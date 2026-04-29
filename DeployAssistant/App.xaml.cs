using DeployAssistant.DataComponent;
using DeployAssistant.View;

namespace DeployAssistant
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : System.Windows.Application
    {
        private static MetaDataManager? _metaDataManager;
        public static MetaDataManager MetaDataManager => _metaDataManager ??= new MetaDataManager();

        private void App_Startup(object sender, System.Windows.StartupEventArgs e)
        {
            MetaDataManager.Awake();
            var mainWindow = new MainWindow(MetaDataManager);
            mainWindow.Show();
        }
    }
}
