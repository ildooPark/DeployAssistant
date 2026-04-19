using DeployAssistant.DataComponent;
using DeployAssistant.Model;
using DeployAssistant.ViewModel.Utils;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace DeployAssistant.ViewModel
{
    public class VersionCheckViewModel : ViewModelBase
    {
        private string? _changeLog;
        public string ChangeLog
        {
            get { return _changeLog ??= ""; }
            set
            {
                _changeLog = value;
                OnPropertyChanged(nameof(ChangeLog));
            }
        }

        private string? _updateLog;
        public string UpdateLog
        {
            get { return _updateLog ??= ""; }
            set
            {
                _updateLog = value;
                OnPropertyChanged(nameof(UpdateLog));
            }
        }

        private ICommand? _exportToXLSX;
        public ICommand ExportToXLSX => _exportToXLSX ??= new RelayCommand(ExportFile, CanExport);

        private void ExportFile(object obj)
        {
            _metaDataManager.RequestExportProjectFilesXLSX(FileList, _projectData);
        }

        private bool CanExport(object obj)
        {
            return FileList.Count > 0;
        }

        private ObservableCollection<ProjectFile>? _fileList;
        public ObservableCollection<ProjectFile> FileList
        {
            get => _fileList ??= new ObservableCollection<ProjectFile>();
            set
            {
                _fileList = value;
                OnPropertyChanged(nameof(FileList));
            }
        }

        private Dictionary<string, object>? _projectDataReview;
        public Dictionary<string, object> ProjectDataReview
        {
            get => _projectDataReview ??= new Dictionary<string, object>();
            set
            {
                _projectDataReview = value;
                OnPropertyChanged(nameof(ProjectDataReview));
            }
        }

        private readonly MetaDataManager _metaDataManager;
        private readonly ProjectData _projectData;

        public VersionCheckViewModel(MetaDataManager metaDataManager, ProjectData projectData, string versionLog, ObservableCollection<ProjectFile> fileList)
        {
            _metaDataManager = metaDataManager;
            _projectData = projectData;
            _updateLog = "Integrity Checking";
            _changeLog = versionLog;
            _fileList = fileList;
        }

        public VersionCheckViewModel(MetaDataManager metaDataManager, ProjectData projectData)
        {
            _metaDataManager = metaDataManager;
            _projectData = projectData;
            _projectDataReview = new Dictionary<string, object>();
            _projectData.RegisterProjectInfo(ProjectDataReview);
            FileList = _projectData.ProjectFilesObs;
            ChangeLog = _projectData.ChangeLog ?? "Undefined";
            UpdateLog = _projectData.UpdateLog ?? "Undefined";
        }
    }
}
