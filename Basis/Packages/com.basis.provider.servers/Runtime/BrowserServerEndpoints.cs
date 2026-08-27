using System;
using System.Net;
using System.Net.Sockets;

namespace Basis.BasisUI
{
    public static class BrowserServerEndpoints
    {
        public static string WebSocketUri(string address)
        {
            string scheme = IsLoopback(address) ? "ws" : "wss";
            string authority = Authority(address);
            string port = IsLoopback(address) ? ":4297" : string.Empty;
            return $"{scheme}://{authority}{port}/basis";
        }

        public static string ServerInfoUri(string address)
        {
            string scheme = IsLoopback(address) ? "http" : "https";
            string authority = Authority(address);
            string port = IsLoopback(address) ? ":4297" : string.Empty;
            return $"{scheme}://{authority}{port}/server-info";
        }

        private static bool IsLoopback(string address)
        {
            string trimmed = address?.Trim() ?? string.Empty;
            return string.Equals(trimmed, "localhost", StringComparison.OrdinalIgnoreCase)
                || IPAddress.TryParse(trimmed, out IPAddress parsed) && IPAddress.IsLoopback(parsed);
        }

        private static string Authority(string address)
        {
            string trimmed = address?.Trim() ?? string.Empty;
            if (trimmed.Length == 0) throw new ArgumentException("A server address is required.", nameof(address));
            return IPAddress.TryParse(trimmed, out IPAddress parsed)
                && parsed.AddressFamily == AddressFamily.InterNetworkV6
                ? $"[{trimmed}]"
                : trimmed;
        }
    }
}
