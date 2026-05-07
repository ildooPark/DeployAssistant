using DeployAssistant.DataComponent;
using DeployAssistant.Model;

namespace DeployAssistant.ViewModel
{
    public class VersionDiffViewModel : DiffViewModelBase
    {
        public VersionDiffViewModel(MetaDataManager metaDataManager, ProjectData srcProject, ProjectData dstProject, List<ChangedFile> diff)
            : base(metaDataManager, srcProject, dstProject, diff) { }
    }
}
