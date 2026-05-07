namespace DeployAssistant.ViewModel
{
    public class MainViewModel : ViewModelBase
    {
        private readonly MetaDataViewModel _metaDataVM;
        private readonly FileTrackViewModel _fileTrackVM;
        private readonly BackupViewModel _backupVM;
        private readonly MetaFileDiffViewModel _metaFileDiffVM;

        public MetaDataViewModel MetaDataVM => _metaDataVM;
        public FileTrackViewModel FileTrackVM => _fileTrackVM;
        public BackupViewModel BackupVM => _backupVM;
        public MetaFileDiffViewModel MetaFileDiffVM => _metaFileDiffVM;

        public MainViewModel(AppServices services)
        {
            _metaDataVM     = new MetaDataViewModel(services.MetaDataManager, services.DialogService, services.UiDispatcher);
            _fileTrackVM    = new FileTrackViewModel(services.MetaDataManager, services.DialogService, services.UiDispatcher);
            _backupVM       = new BackupViewModel(services.MetaDataManager, services.DialogService, services.UiDispatcher);
            _metaFileDiffVM = new MetaFileDiffViewModel(services.MetaDataManager, services.DialogService, services.UiDispatcher);
        }
    }
}
