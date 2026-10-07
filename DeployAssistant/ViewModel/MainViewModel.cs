using DeployAssistant.ViewModel.Utils;
using System.Windows.Input;

namespace DeployAssistant.ViewModel
{
    public class MainViewModel : ViewModelBase
    {
        private readonly MetaDataViewModel _metaDataVM;
        private readonly FileTrackViewModel _fileTrackVM;
        private readonly BackupViewModel _backupVM;
        private readonly MetaFileDiffViewModel _metaFileDiffVM;
        private readonly IgnoreViewModel _ignoreVM;
        private readonly CompactViewModel _compactVM;

        public MetaDataViewModel MetaDataVM => _metaDataVM;
        public FileTrackViewModel FileTrackVM => _fileTrackVM;
        public BackupViewModel BackupVM => _backupVM;
        public MetaFileDiffViewModel MetaFileDiffVM => _metaFileDiffVM;
        public IgnoreViewModel IgnoreVM => _ignoreVM;
        public CompactViewModel CompactVM => _compactVM;

        public MainViewModel(AppServices services)
        {
            _metaDataVM     = new MetaDataViewModel(services.MetaDataManager, services.DialogService, services.UiDispatcher);
            _fileTrackVM    = new FileTrackViewModel(services.MetaDataManager, services.DialogService, services.UiDispatcher);
            _backupVM       = new BackupViewModel(services.MetaDataManager, services.DialogService, services.UiDispatcher);
            _metaFileDiffVM = new MetaFileDiffViewModel(services.MetaDataManager, services.DialogService, services.UiDispatcher);
            _ignoreVM       = new IgnoreViewModel(services.MetaDataManager, services.DialogService, services.UiDispatcher);
            _compactVM      = new CompactViewModel(services.MetaDataManager, services.UiDispatcher);
        }

        // ------------------------------------------------------------------ compact mode

        private bool _isCompact;
        /// <summary>
        /// Compact layout on/off. The window watches this to swap bounds, minimum size and
        /// Topmost; the full layout's state is untouched while compact is shown.
        /// </summary>
        public bool IsCompact
        {
            get => _isCompact;
            set
            {
                if (!SetField(ref _isCompact, value)) return;
                OnPropertyChanged(nameof(IsFull));
                if (!value) _compactVM.IsRevisionsOpen = false;
            }
        }

        public bool IsFull => !_isCompact;

        private bool _isCompactPinned;
        /// <summary>Keeps the compact window above other windows. Ignored in the full layout.</summary>
        public bool IsCompactPinned
        {
            get => _isCompactPinned;
            set => SetField(ref _isCompactPinned, value);
        }

        private ICommand? _toggleCompact;
        public ICommand ToggleCompact => _toggleCompact ??= new RelayCommand(_ => IsCompact = !IsCompact);
    }
}
