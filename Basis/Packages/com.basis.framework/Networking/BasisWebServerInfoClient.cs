using Basis.Network.Core;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace Basis.Scripts.Networking
{
    public static class BasisWebServerInfoClient
    {
        public static bool IsWebSocketUri(Uri uri)
        {
            return uri != null
                && (string.Equals(uri.Scheme, "ws", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(uri.Scheme, "wss", StringComparison.OrdinalIgnoreCase));
        }

        public static string BuildServerInfoUri(Uri webSocketUri)
        {
            if (!IsWebSocketUri(webSocketUri))
            {
                throw new ArgumentException("A ws:// or wss:// URI is required.", nameof(webSocketUri));
            }

            string scheme = string.Equals(webSocketUri.Scheme, "ws", StringComparison.OrdinalIgnoreCase)
                ? Uri.UriSchemeHttp
                : Uri.UriSchemeHttps;
            return $"{scheme}://{webSocketUri.Authority}/server-info";
        }

        [Serializable]
        private sealed class Response
        {
            public ushort online;
            public ushort max;
            public ushort protocolVersion;
            public string name;
            public string motd;
            public bool listening;
            public bool ready;
            public int visitors;
            public int capacity;
            public string version;
        }

        public static async Task<ServerProbeResult> ProbeAsync(
            string serverInfoUri,
            int timeoutMs,
            CancellationToken cancellationToken)
        {
            Uri uri;
            try
            {
                uri = ServerInfoHttpUri.Parse(serverInfoUri);
            }
            catch (FormatException exception)
            {
                return new ServerProbeResult { Error = exception.Message };
            }

            using UnityWebRequest request = UnityWebRequest.Get(uri.AbsoluteUri);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.timeout = Math.Max(1, (int)Math.Ceiling(timeoutMs / 1000d));
            Stopwatch roundTrip = Stopwatch.StartNew();
            UnityWebRequestAsyncOperation operation = request.SendWebRequest();
            while (!operation.isDone)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    request.Abort();
                    throw new OperationCanceledException(cancellationToken);
                }
                await Task.Yield();
            }

            if (request.result != UnityWebRequest.Result.Success)
            {
                return new ServerProbeResult
                {
                    Error = request.error,
                    TimedOut = request.result == UnityWebRequest.Result.ConnectionError
                        && string.Equals(request.error, "Request timeout", StringComparison.OrdinalIgnoreCase),
                };
            }

            ServerProbeResult result = ParseResponse(request.downloadHandler.text);
            result.RoundTripMs = (int)roundTrip.ElapsedMilliseconds;
            return result;
        }

        internal static ServerProbeResult ParseResponse(string json)
        {
            try
            {
                Response response = JsonUtility.FromJson<Response>(json);
                if (response == null)
                {
                    return new ServerProbeResult { Error = "Server-info response is empty." };
                }
                bool healthPayload = response.listening
                    || response.ready
                    || response.capacity > 0
                    || !string.IsNullOrEmpty(response.version);
                ServerProbeResult result = new ServerProbeResult
                {
                    Reachable = true,
                    Online = healthPayload
                        ? (ushort)Math.Min(ushort.MaxValue, Math.Max(0, response.visitors))
                        : response.online,
                    Max = healthPayload
                        ? (ushort)Math.Min(ushort.MaxValue, Math.Max(0, response.capacity))
                        : response.max,
                    ProtocolVersion = response.protocolVersion,
                    Name = response.name ?? string.Empty,
                    Motd = response.motd ?? string.Empty,
                };
                if (!string.IsNullOrEmpty(response.version)) result.Extras["version"] = response.version;
                return result;
            }
            catch (Exception exception)
            {
                return new ServerProbeResult { Error = "Invalid server-info response: " + exception.Message };
            }
        }
    }
}
