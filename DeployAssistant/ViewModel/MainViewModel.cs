using DeployAssistant.Interfaces;

namespace DeployAssistant.ViewModel
{
    public class MainViewModel : ViewModelBase
    {
        private MetaDataViewModel metaDataVM;
        private FileTrackViewModel fileTrackVM;
        private BackupViewModel backupVM;
        public MetaDataViewModel MetaDataVM => metaDataVM;
        public FileTrackViewModel FileTrackVM => fileTrackVM;
        public BackupViewModel BackupVM => backupVM; 
        public MainViewModel()
        {
            metaDataVM = new MetaDataViewModel();
            fileTrackVM = new FileTrackViewModel();
            backupVM = new BackupViewModel();
            App.AwakeModel();
        }
    }
}
