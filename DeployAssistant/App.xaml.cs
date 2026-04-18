using DeployAssistant.DataComponent;

namespace DeployAssistant
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : System.Windows.Application
    {
        private static MetaDataManager? _metaDataManager; 
        public static MetaDataManager MetaDataManager => _metaDataManager ??= new MetaDataManager();

        public static void AwakeModel()
        {
            MetaDataManager.Awake(); 
        }
    }
}
