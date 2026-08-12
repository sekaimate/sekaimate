using System;
using System.Net;

namespace Basis.Network.Core
{
    public static class ServerInfoHttpUri
    {
        public static Uri Parse(string value)
        {
            if (string.IsNullOrWhiteSpace(value)
                || value.IndexOf('#') >= 0
                || !Uri.TryCreate(value, UriKind.Absolute, out Uri uri)
                || string.IsNullOrEmpty(uri.Host)
                || !string.IsNullOrEmpty(uri.UserInfo)
                || !IsSupportedScheme(uri.Scheme)
                || !IsSecureOrLoopback(uri))
            {
                throw new FormatException("A valid HTTPS server-info URI is required; HTTP is allowed only for loopback endpoints.");
            }

            return uri;
        }

        private static bool IsSupportedScheme(string scheme)
        {
            return string.Equals(scheme, Uri.UriSchemeHttp, StringComparison.Ordinal)
                || string.Equals(scheme, Uri.UriSchemeHttps, StringComparison.Ordinal);
        }

        private static bool IsSecureOrLoopback(Uri uri)
        {
            if (string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal))
            {
                return true;
            }
            return string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)
                || IPAddress.TryParse(uri.Host, out IPAddress address) && IPAddress.IsLoopback(address);
        }
    }
}
