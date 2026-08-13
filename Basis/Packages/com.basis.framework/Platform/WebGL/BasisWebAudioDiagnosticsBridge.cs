#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;

public static class BasisWebAudioDiagnosticsBridge
{
    public static void MarkOpusEncoded(int encodedBytes)
    {
        BasisWebAudioDiagnosticsMarkOpusEncoded(encodedBytes);
    }

    public static void MarkNetworkSent(int encodedBytes)
    {
        BasisWebAudioDiagnosticsMarkNetworkSent(encodedBytes);
    }

    public static void MarkNetworkReceived(int encodedBytes)
    {
        BasisWebAudioDiagnosticsMarkNetworkReceived(encodedBytes);
    }

    public static void MarkOpusDecoded(int sampleCount)
    {
        BasisWebAudioDiagnosticsMarkOpusDecoded(sampleCount);
    }

    public static void MarkMuted(bool muted)
    {
        BasisWebAudioDiagnosticsMarkMuted(muted ? 1 : 0);
    }

    public static void MarkTalkMode(byte talkMode)
    {
        BasisWebAudioDiagnosticsMarkTalkMode(talkMode);
    }

    public static void MarkVisemeProcessed(bool isLocal, float peak)
    {
        BasisWebAudioDiagnosticsMarkVisemeProcessed(isLocal ? 1 : 0, peak);
    }

    [DllImport("__Internal")]
    private static extern void BasisWebAudioDiagnosticsMarkOpusEncoded(int encodedBytes);

    [DllImport("__Internal")]
    private static extern void BasisWebAudioDiagnosticsMarkNetworkSent(int encodedBytes);

    [DllImport("__Internal")]
    private static extern void BasisWebAudioDiagnosticsMarkNetworkReceived(int encodedBytes);

    [DllImport("__Internal")]
    private static extern void BasisWebAudioDiagnosticsMarkOpusDecoded(int sampleCount);

    [DllImport("__Internal")]
    private static extern void BasisWebAudioDiagnosticsMarkMuted(int muted);

    [DllImport("__Internal")]
    private static extern void BasisWebAudioDiagnosticsMarkTalkMode(int talkMode);

    [DllImport("__Internal")]
    private static extern void BasisWebAudioDiagnosticsMarkVisemeProcessed(int isLocal, float peak);
}
#endif
