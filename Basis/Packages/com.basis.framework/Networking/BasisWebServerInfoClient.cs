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
        [Serializable]
        private sealed class Response
        {
            public ushort online;
            public ushort max;
            public ushort protocolVersion;
            public string name;
            public string motd;
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
                return new ServerProbeResult
                {
                    Reachable = true,
                    Online = response.online,
                    Max = response.max,
                    ProtocolVersion = response.protocolVersion,
                    Name = response.name ?? string.Empty,
                    Motd = response.motd ?? string.Empty,
                };
            }
            catch (Exception exception)
            {
                return new ServerProbeResult { Error = "Invalid server-info response: " + exception.Message };
            }
        }
    }
}
