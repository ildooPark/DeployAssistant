using DeployAssistant.DataComponent;

namespace DeployAssistant.Interfaces
{
    public interface IManager
    {
        public void Awake();
        event Action<MetaDataState> ManagerStateEventHandler;
    }
}
