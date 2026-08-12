using System;

namespace Basis.Network.Core
{
    public static class ClientConnectionTargetSelector
    {
        public static ConnectionTarget Select(
            ConnectionTarget nativeTarget,
            string webSocketUri,
            bool webGlPlayer)
        {
            if (!webGlPlayer)
            {
                return nativeTarget ?? throw new ArgumentNullException(nameof(nativeTarget));
            }
            if (string.IsNullOrWhiteSpace(webSocketUri))
            {
                throw new InvalidOperationException("The server directory entry does not provide a WebSocket URI.");
            }

            ConnectionTarget webSocketTarget = new ConnectionTarget(
                BasisNetworkStackRegistry.WebSocketId,
                webSocketUri);
            new WebSocketConnectionTargetParser().Parse(webSocketTarget);
            return webSocketTarget;
        }
    }
}
