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

    [DllImport("__Internal")]
    private static extern void BasisWebAudioDiagnosticsMarkOpusEncoded(int encodedBytes);

    [DllImport("__Internal")]
    private static extern void BasisWebAudioDiagnosticsMarkNetworkSent(int encodedBytes);

    [DllImport("__Internal")]
    private static extern void BasisWebAudioDiagnosticsMarkNetworkReceived(int encodedBytes);

    [DllImport("__Internal")]
    private static extern void BasisWebAudioDiagnosticsMarkOpusDecoded(int sampleCount);
}
#endif
