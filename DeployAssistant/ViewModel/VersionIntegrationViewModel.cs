using DeployAssistant.DataComponent;
using DeployAssistant.Model;
using DeployAssistant;
using DeployAssistant.ViewModel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DeployAssistant.ViewModel
{
    public class VersionIntegrationViewModel : ViewModelBase
    {
        private MetaDataManager _metaDataManager;
        public VersionIntegrationViewModel(ProjectData srcProject, ProjectData dstProject, List<ChangedFile> diff)
        {
            this._metaDataManager = App.MetaDataManager;
        }
    }
}