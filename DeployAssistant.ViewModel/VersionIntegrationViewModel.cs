using DeployAssistant.DataComponent;
using DeployAssistant.Model;

namespace DeployAssistant.ViewModel
{
    public class VersionIntegrationViewModel : ViewModelBase
    {
        private readonly MetaDataManager _metaDataManager;

        public VersionIntegrationViewModel(MetaDataManager metaDataManager, ProjectData srcProject, ProjectData dstProject, List<ChangedFile> diff)
        {
            _metaDataManager = metaDataManager;
        }
    }
}
