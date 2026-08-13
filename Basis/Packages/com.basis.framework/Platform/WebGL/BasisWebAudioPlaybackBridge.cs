#if UNITY_WEBGL && !UNITY_EDITOR
using System;
using System.Runtime.InteropServices;

public static class BasisWebAudioPlaybackBridge
{
    public static int CreateSink()
    {
        BasisWebAudioCaptureBridge.EnsureInitialized();
        return BasisWebAudioPlaybackCreateSink();
    }

    public static unsafe void Push(int sinkId, float[] samples, int sampleCount, float peak)
    {
        if (samples == null)
        {
            throw new ArgumentNullException(nameof(samples));
        }
        if (sampleCount < 0 || sampleCount > samples.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleCount));
        }

        fixed (float* samplePointer = samples)
        {
            BasisWebAudioPlaybackPush(sinkId, (IntPtr)samplePointer, sampleCount, peak);
        }
    }

    public static void RemoveSink(int sinkId)
    {
        BasisWebAudioPlaybackRemoveSink(sinkId);
    }

    [DllImport("__Internal")]
    private static extern int BasisWebAudioPlaybackCreateSink();

    [DllImport("__Internal")]
    private static extern void BasisWebAudioPlaybackPush(int sinkId, IntPtr samples, int sampleCount, float peak);

    [DllImport("__Internal")]
    private static extern void BasisWebAudioPlaybackRemoveSink(int sinkId);
}
#endif
