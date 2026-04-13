using DeployAssistant.DataComponent;
using DeployAssistant.Utils;

namespace DeployAssistant
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : System.Windows.Application
    {
        private static MetaDataManager? _metaDataManager; 
        public static MetaDataManager MetaDataManager => _metaDataManager ??= new MetaDataManager();

        private static FileHandlerTool? _fileHandlerTool; 
        public static FileHandlerTool FileHandlerTool => _fileHandlerTool ??= new FileHandlerTool();

        private static HashTool? _hashTool;
        public static HashTool HashTool => _hashTool ??= new HashTool();
        public static void AwakeModel()
        {
            MetaDataManager.Awake(); 
        }
    }
}