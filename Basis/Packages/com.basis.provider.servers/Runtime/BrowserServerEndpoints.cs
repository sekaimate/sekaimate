using System;
using System.Net;
using System.Net.Sockets;

namespace Basis.BasisUI
{
    public static class BrowserServerEndpoints
    {
        public static string WebSocketUri(string address) => $"wss://{Authority(address)}/basis";

        public static string ServerInfoUri(string address) => $"https://{Authority(address)}/server-info";

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
