using DeployAssistant.DataComponent;
using DeployAssistant.Model;
using DeployAssistant.Utils;
using System.Windows.Input;

namespace DeployAssistant.ViewModel
{
    public class OverlapFileViewModel : ViewModelBase
    {
        private List<ChangedFile>? overlapFilesList;
        public List<ChangedFile>? OverlapFilesList
        {
            get => overlapFilesList ??= new List<ChangedFile>();
            set
            {
                overlapFilesList = value;
                OnPropertyChanged("OverlapFilesList");
            }
        }
        private List<ChangedFile>? newFilesList;
        public List<ChangedFile>? NewFilesList
        {
            get => newFilesList ??= new List<ChangedFile>();
            set
            {
                newFilesList = value;
                OnPropertyChanged("NewFilesList");
            }
        }
        private ICommand? confirmCommand;
        public ICommand ConfirmCommand => confirmCommand ??= new RelayCommand(ConfirmChoices);
        public event Action TaskFinishedEventHandler; 
        private MetaDataManager metaDataManager;

        public OverlapFileViewModel(List<ChangedFile> registeredOverlaps, List<ChangedFile>? registeredNew = null)
        {
            this.OverlapFilesList = registeredOverlaps;
            this.NewFilesList = registeredNew;
            metaDataManager = App.MetaDataManager;
        }

        private void ConfirmChoices(object? obj)
        {
            metaDataManager.RequestOverlappedFileAllocation(overlapFilesList, newFilesList);
            TaskFinishedEventHandler?.Invoke();
        }
    }
}