using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace DeployManager.Model
{
    public class LocalConfigData
    {
        public string? LastOpenedDstPath { get; set; }
        
        public LocalConfigData(string? LastOpenedDstPath) 
        {
            this.LastOpenedDstPath = LastOpenedDstPath;
        }
    }
}