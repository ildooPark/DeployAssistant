using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace DeployAssistant.Model
{
    public class LocalConfigData
    {
        public string? LastOpenedDstPath { get; set; }
        /// <summary>UI language ("ko-KR" / "en-US"); additive so 3.6.1-era configs still load.</summary>
        public string? Language { get; set; }
        
        public LocalConfigData(string? LastOpenedDstPath) 
        {
            this.LastOpenedDstPath = LastOpenedDstPath;
        }
    }
}