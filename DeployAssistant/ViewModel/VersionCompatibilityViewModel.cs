using DeployAssistant.Model;
using DeployAssistant.Model;
using DeployAssistant.ViewModel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DeployAssistant.ViewModel
{
    public class VersionCompatibilityViewModel: ViewModelBase
    {
        private string? _srcProjVersion; 
        public string SrcProjVersion
        {
            get => _srcProjVersion ??= ""; 
            set
            {
                _srcProjVersion = value;
                OnPropertyChanged("SrcProjVersion"); 
            }
        }
        private string? _srcProjConductedPC;
        public string SrcProjConductedPC
        {
            get => _srcProjConductedPC ??= "";
            set
            {
                _srcProjConductedPC = value;
                OnPropertyChanged("SrcProjConductedPC");
            }
        }
        private string? _targetProjVersion; 
        public string TargetProjVersion
        {
            get => _targetProjVersion ??= ""; 
            set
            {
                _targetProjVersion = value;
                OnPropertyChanged("TargetProjVersion"); 
            }
        }

        private ProjectData _srcProjData; 
        public ProjectData SrcProjData
        {
            get => _srcProjData;
            set
            {
                _srcProjData = value;
                OnPropertyChanged(nameof(SrcProjData));
            }
        }
        private ProjectSimilarity? _selected; 
        public ProjectSimilarity? Selected
        {
            get => _selected ??= new ProjectSimilarity();
            set
            {
                if (value == null)
                {
                    _selected = null;
                    return;
                }
                _selected = value;
                FileDifferences = value.fileDifferences;
                TargetProjVersion = value.projData.UpdatedVersion; 
                OnPropertyChanged(nameof(Selected));
            }
        }

        private List<ProjectSimilarity>? _versionSimilarities;
        public List<ProjectSimilarity> VersionSimilarities
        {
            get => _versionSimilarities ?? []; 
            set
            {
                _versionSimilarities = value;
                OnPropertyChanged("VersionSimilarities"); 
            }
        }

        private List<ChangedFile>? _fileDifferences; 
        public List<ChangedFile> FileDifferences
        {
            get => _fileDifferences ??= []; 
            set
            {
                _fileDifferences = value;
                OnPropertyChanged(nameof(FileDifferences));
            }
        }

        
        public VersionCompatibilityViewModel(ProjectData srcData, List<ProjectSimilarity> similarities)
        {
            _srcProjData = srcData;
            _versionSimilarities = similarities;
            SrcProjConductedPC = srcData.ConductedPC;
            SrcProjVersion = srcData.UpdatedVersion; 
        }
    }
}
