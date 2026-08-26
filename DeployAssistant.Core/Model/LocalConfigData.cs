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
        /// <summary>Most-recently opened project paths, newest first (capped). Additive.</summary>
        public List<string>? RecentProjects { get; set; }
        /// <summary>Share of intersecting files that get a full MD5 hash during a normal
        /// integrity check (the rest are verified by size/version metadata). 100 = full hash.
        /// Checkout gates always force 100 regardless. Additive.</summary>
        public int? FastIntegritySamplePercent { get; set; }
        
        public LocalConfigData(string? LastOpenedDstPath) 
        {
            this.LastOpenedDstPath = LastOpenedDstPath;
        }
    }
}