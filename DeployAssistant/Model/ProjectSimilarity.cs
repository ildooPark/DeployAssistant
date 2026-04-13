using DeployAssistant.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DeployAssistant.Model
{
    public class ProjectSimilarity
    {
        public ProjectData projData { get; set; }
        public int numDiffWithoutResources { get; set; }
        public int numDiffWithResources { get; set; }
        public List<ChangedFile> fileDifferences { get; set; }
        public ProjectSimilarity(ProjectData projData, int numDiffWithoutResources, int numDiffWithResources, List<ChangedFile> fileDifferences)
        {
            this.projData = projData;
            this.numDiffWithoutResources = numDiffWithoutResources;
            this.numDiffWithResources = numDiffWithResources;
            this.fileDifferences = fileDifferences;
        }

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        public ProjectSimilarity() { }
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
    }
}