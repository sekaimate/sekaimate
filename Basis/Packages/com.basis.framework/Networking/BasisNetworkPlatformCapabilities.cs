namespace Basis.Scripts.Networking
{
    public static class BasisNetworkPlatformCapabilities
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        public const bool SupportsDirectPeerConnections = false;
#else
        public const bool SupportsDirectPeerConnections = true;
#endif
    }
}
