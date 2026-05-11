using DeployAssistant.Interfaces;
using DeployAssistant.Model;

namespace DeployAssistant.Filtering
{
    public interface IIgnoreFilter
    {
        bool Matches(string relativePath, ProjectDataType dataType, IgnoreType scope);
    }
}
