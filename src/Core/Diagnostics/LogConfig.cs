using System.Collections.Generic;

namespace Ludots.Core.Diagnostics
{
    public class LogConfig
    {
        public string GlobalLevel { get; set; } = "Info";
        public Dictionary<string, string> ChannelLevels { get; set; } = new Dictionary<string, string>();
        public bool FileLogging { get; set; }
        public string LogFilePath { get; set; } = "ludots.log";
    }
}
