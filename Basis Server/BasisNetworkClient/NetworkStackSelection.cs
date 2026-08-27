using Basis.Network.Core;

namespace Basis.Network.WebSocketClient
{
    public static class NetworkStackSelection
    {
        public static string ResolveClientStackId(string configuredStackId)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return ResolveClientStackId(configuredStackId, true);
#else
            return ResolveClientStackId(configuredStackId, false);
#endif
        }

        public static string ResolveClientStackId(string configuredStackId, bool webGlPlayer)
        {
            if (!string.IsNullOrEmpty(configuredStackId))
            {
                return configuredStackId;
            }
            return webGlPlayer
                ? BasisNetworkStackRegistry.WebSocketId
                : BasisNetworkStackRegistry.DefaultId;
        }
    }
}
