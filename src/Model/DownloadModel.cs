using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;

namespace MapDownloader.Model
{
    public class DownloadModel
    {
        public string MapName { get; set; }
        public string OutputDir { get; set; }
        public HttpResponseMessage FileResult { get; set; }
    }
}
