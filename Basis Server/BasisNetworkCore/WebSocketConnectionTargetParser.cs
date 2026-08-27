using System;
using System.Globalization;
using System.Net;

namespace Basis.Network.Core
{
    public sealed class WebSocketConnectionTargetParser : IConnectionTargetParser
    {
        private const string WebSocketScheme = "ws";
        private const string SecureWebSocketScheme = "wss";

        public void Parse(ConnectionTarget target)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            string raw = target.Raw;
            if (string.IsNullOrWhiteSpace(raw)
                || raw.IndexOf('#') >= 0
                || !Uri.TryCreate(raw, UriKind.Absolute, out Uri uri)
                || !IsSupportedScheme(uri.Scheme)
                || string.IsNullOrEmpty(uri.Host)
                || !string.IsNullOrEmpty(uri.UserInfo))
            {
                throw new FormatException("A valid ws or wss URI without user information or a fragment is required.");
            }
            if (!IsSecureOrLoopback(uri))
            {
                throw new FormatException("A wss URI is required outside loopback development endpoints.");
            }

            target.Set(ConnectionTarget.Keys.Scheme, uri.Scheme);
            target.Set(ConnectionTarget.Keys.Address, NormalizeHost(uri.Host));
            target.Set(ConnectionTarget.Keys.Port, uri.Port.ToString(CultureInfo.InvariantCulture));
            target.Set(ConnectionTarget.Keys.Path, uri.PathAndQuery);
            target.Set(
                ConnectionTarget.Keys.Secure,
                string.Equals(uri.Scheme, SecureWebSocketScheme, StringComparison.Ordinal).ToString().ToLowerInvariant());
        }

        public string Format(ConnectionTarget target)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));

            string scheme = Required(target, ConnectionTarget.Keys.Scheme);
            string address = Required(target, ConnectionTarget.Keys.Address);
            string portText = Required(target, ConnectionTarget.Keys.Port);
            string pathAndQuery = Required(target, ConnectionTarget.Keys.Path);
            string secureText = Required(target, ConnectionTarget.Keys.Secure);

            if (!IsSupportedScheme(scheme)
                || !ushort.TryParse(portText, NumberStyles.None, CultureInfo.InvariantCulture, out ushort port)
                || port == 0
                || !bool.TryParse(secureText, out bool secure)
                || secure != string.Equals(scheme, SecureWebSocketScheme, StringComparison.Ordinal)
                || pathAndQuery.Length == 0
                || pathAndQuery[0] != '/'
                || pathAndQuery.IndexOf('#') >= 0)
            {
                throw new FormatException("The connection target contains invalid WebSocket properties.");
            }

            int queryIndex = pathAndQuery.IndexOf('?');
            string path = queryIndex < 0 ? pathAndQuery : pathAndQuery.Substring(0, queryIndex);
            string query = queryIndex < 0 ? string.Empty : pathAndQuery.Substring(queryIndex + 1);
            UriBuilder builder;
            try
            {
                builder = new UriBuilder(scheme, address, port, path) { Query = query };
            }
            catch (UriFormatException exception)
            {
                throw new FormatException("The connection target contains an invalid host.", exception);
            }

            Uri uri = builder.Uri;
            if (string.IsNullOrEmpty(uri.Host))
            {
                throw new FormatException("The connection target contains an invalid host.");
            }
            return uri.AbsoluteUri;
        }

        private static string Required(ConnectionTarget target, string key)
        {
            if (!target.TryGet(key, out string value) || string.IsNullOrEmpty(value))
            {
                throw new FormatException($"Connection target property '{key}' is required.");
            }
            return value;
        }

        private static bool IsSupportedScheme(string scheme)
        {
            return string.Equals(scheme, WebSocketScheme, StringComparison.Ordinal)
                || string.Equals(scheme, SecureWebSocketScheme, StringComparison.Ordinal);
        }

        private static bool IsSecureOrLoopback(Uri uri)
        {
            if (string.Equals(uri.Scheme, SecureWebSocketScheme, StringComparison.Ordinal))
            {
                return true;
            }
            return string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)
                || (IPAddress.TryParse(uri.Host, out IPAddress address) && IPAddress.IsLoopback(address));
        }

        private static string NormalizeHost(string host)
        {
            return host.Length >= 2 && host[0] == '[' && host[host.Length - 1] == ']'
                ? host.Substring(1, host.Length - 2)
                : host;
        }
    }
}
