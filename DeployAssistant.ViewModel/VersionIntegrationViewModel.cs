using DeployAssistant.DataComponent;
using DeployAssistant.Model;

namespace DeployAssistant.ViewModel
{
    public class VersionIntegrationViewModel : DiffViewModelBase
    {
        public VersionIntegrationViewModel(MetaDataManager metaDataManager, ProjectData srcProject, ProjectData dstProject, List<ChangedFile> diff)
            : base(metaDataManager, srcProject, dstProject, diff) { }
    }
}
