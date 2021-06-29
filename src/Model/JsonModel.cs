using System.Collections.Generic;

namespace MapDownloader.Model
{
    public class JsonModel
    {
        public string? MapList { get; set; }
        public string? FastDL { get; set; }
        public string? OutputDirectory { get; set; }

        public List<string>? MapNames { get; set; }
    }
}
