#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
using Basis.Streaming;

public static class BasisWebStreamingMetaBridge
{
    public static void Publish(BasisStreamingMetaServer.Snapshot snapshot)
    {
        BasisWebStreamingMetaPublish(
            snapshot.Fps,
            snapshot.Ccu,
            snapshot.PeerLimit,
            snapshot.RoundTripMs,
            snapshot.PingMs,
            snapshot.Connected ? 1 : 0);
    }

    public static void Clear()
    {
        BasisWebStreamingMetaClear();
    }

    [DllImport("__Internal")]
    private static extern void BasisWebStreamingMetaPublish(
        float fps,
        int ccu,
        int peerLimit,
        int roundTripMs,
        int pingMs,
        int connected);

    [DllImport("__Internal")]
    private static extern void BasisWebStreamingMetaClear();
}
#endif
