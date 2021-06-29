using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;

namespace MapDownloader
{
    static class Extensions
    {
        public static bool IsValidURL(this string url)
        {
            bool result = Uri.TryCreate(url, UriKind.Absolute, out Uri uriResult) && (uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps);

            return result;
        }
    }
}
