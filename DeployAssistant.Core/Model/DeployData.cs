using DeployAssistant.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace DeployAssistant.Model
{
    /// <summary>
    /// On-disk shape of the retired <c>DeployAssistant.deploy</c> sidecar. 4.x neither
    /// writes nor reads these; the class is kept only to document the format that 3.6.1
    /// still produces in the field. Scans keep ignoring the file itself.
    /// </summary>
    [Obsolete("DeployAssistant.deploy sidecars are deprecated as of 4.x; nothing reads or writes them.")]
    public class DeployData
    {
        public DateTime RegisteredTime { get; set; }
        public string ProjectName { get; set; }
        /// <summary>
        /// key = DataRelPath 
        /// Value = ProjectFile
        /// </summary>
        public Dictionary<string, ProjectFile> SortedTopFiles { get; set; }
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        [JsonConstructor]
        public DeployData() { }
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
    
        public DeployData(string ProjectName, Dictionary<string, ProjectFile> registeredFiles)
        {
            this.ProjectName = ProjectName;
            RegisteredTime = DateTime.Now;
            SortedTopFiles = registeredFiles;
        }
    }
}