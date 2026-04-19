using DeployAssistant.DataComponent;

namespace DeployAssistant.ViewModel
{
    public class MainViewModel : ViewModelBase
    {
        private readonly MetaDataViewModel _metaDataVM;
        private readonly FileTrackViewModel _fileTrackVM;
        private readonly BackupViewModel _backupVM;

        public MetaDataViewModel MetaDataVM => _metaDataVM;
        public FileTrackViewModel FileTrackVM => _fileTrackVM;
        public BackupViewModel BackupVM => _backupVM;

        public MainViewModel(MetaDataManager metaDataManager)
        {
            _metaDataVM = new MetaDataViewModel(metaDataManager);
            _fileTrackVM = new FileTrackViewModel(metaDataManager);
            _backupVM = new BackupViewModel(metaDataManager);
        }
    }
}
