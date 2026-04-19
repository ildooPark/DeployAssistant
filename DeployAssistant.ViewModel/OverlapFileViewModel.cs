using DeployAssistant.DataComponent;
using DeployAssistant.Model;
using DeployAssistant.ViewModel.Utils;
using System.Windows.Input;

namespace DeployAssistant.ViewModel
{
    public class OverlapFileViewModel : ViewModelBase
    {
        private List<ChangedFile>? _overlapFilesList;
        public List<ChangedFile>? OverlapFilesList
        {
            get => _overlapFilesList ??= new List<ChangedFile>();
            set
            {
                _overlapFilesList = value;
                OnPropertyChanged(nameof(OverlapFilesList));
            }
        }

        private List<ChangedFile>? _newFilesList;
        public List<ChangedFile>? NewFilesList
        {
            get => _newFilesList ??= new List<ChangedFile>();
            set
            {
                _newFilesList = value;
                OnPropertyChanged(nameof(NewFilesList));
            }
        }

        private ICommand? _confirmCommand;
        public ICommand ConfirmCommand => _confirmCommand ??= new RelayCommand(ConfirmChoices);

        public event Action? TaskFinishedEventHandler;

        private readonly MetaDataManager _metaDataManager;

        public OverlapFileViewModel(MetaDataManager metaDataManager, List<ChangedFile> registeredOverlaps, List<ChangedFile>? registeredNew = null)
        {
            _metaDataManager = metaDataManager;
            OverlapFilesList = registeredOverlaps;
            NewFilesList = registeredNew;
        }

        private void ConfirmChoices(object? obj)
        {
            _metaDataManager.RequestOverlappedFileAllocation(_overlapFilesList, _newFilesList);
            TaskFinishedEventHandler?.Invoke();
        }
    }
}
