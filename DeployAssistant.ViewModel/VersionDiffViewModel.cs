using DeployAssistant.DataComponent;
using DeployAssistant.Model;
using DeployAssistant.ViewModel.Utils;
using System.Windows.Input;

namespace DeployAssistant.ViewModel
{
    public class VersionDiffViewModel : ViewModelBase
    {
        private ProjectData? _srcProject;
        public ProjectData? SrcProject
        {
            get => _srcProject;
            set
            {
                _srcProject = value;
                OnPropertyChanged(nameof(SrcProject));
            }
        }

        private ProjectData? _dstProject;
        public ProjectData? DstProject
        {
            get => _dstProject;
            set
            {
                _dstProject = value;
                OnPropertyChanged(nameof(DstProject));
            }
        }

        private List<ChangedFile>? _diff;
        public List<ChangedFile> Diff
        {
            get => _diff ??= new List<ChangedFile>();
            set
            {
                _diff = value;
                OnPropertyChanged(nameof(Diff));
                OnPropertyChanged(nameof(TotalCount));
                OnPropertyChanged(nameof(AddedCount));
                OnPropertyChanged(nameof(ModifiedCount));
                OnPropertyChanged(nameof(DeletedCount));
            }
        }

        // Summary counts shown in the header bar
        public int TotalCount    => Diff.Count;
        public int AddedCount    => Diff.Count(f => f.DataState == DataState.Added);
        public int ModifiedCount => Diff.Count(f => f.DataState == DataState.Modified);
        public int DeletedCount  => Diff.Count(f => f.DataState == DataState.Deleted);

        private readonly MetaDataManager _metaDataManager;

        public VersionDiffViewModel(MetaDataManager metaDataManager, ProjectData srcProject, ProjectData dstProject, List<ChangedFile> diff)
        {
            _metaDataManager = metaDataManager;
            _srcProject = srcProject;
            _dstProject = dstProject;
            _diff = diff;
        }

        private ICommand? _exportDiffFiles;
        public ICommand ExportDiffFiles => _exportDiffFiles ??= new RelayCommand(ExportDiff, CanExportDiff);

        private bool CanExportDiff(object obj)
        {
            if (Diff.Count <= 0) return false;
            return true;
        }

        private void ExportDiff(object obj)
        {
            _metaDataManager.RequestExportProjectVersionDiffFiles(Diff);
        }
    }
}
