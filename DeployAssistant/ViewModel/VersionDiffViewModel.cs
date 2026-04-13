using DeployAssistant.DataComponent;
using DeployAssistant.Model;
using DeployAssistant.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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
            }
        }

        private MetaDataManager _metaDataManager;
        public VersionDiffViewModel(ProjectData srcProject, ProjectData dstProject, List<ChangedFile> diff)
        {
            this._srcProject = srcProject;
            this._dstProject = dstProject;
            this._diff = diff;
            this._metaDataManager = App.MetaDataManager;
        }

        private ICommand? exportDiffFiles;
        public ICommand ExportDiffFiles => exportDiffFiles ??= new RelayCommand(ExportDiff, CanExportDiff);
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