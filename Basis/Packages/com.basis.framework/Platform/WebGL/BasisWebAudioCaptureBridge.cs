#if UNITY_WEBGL && !UNITY_EDITOR
using AOT;
using System;
using System.Runtime.InteropServices;

public enum BasisWebAudioCaptureState
{
    Idle = 0,
    AwaitingUserGesture = 1,
    RequestingPermission = 2,
    Running = 3,
    PermissionDenied = 4,
    Unavailable = 5,
    Suspended = 6,
}

public static class BasisWebAudioCaptureBridge
{
    public const int SampleRate = 48000;
    public const int Channels = 1;
    public const int FrameSize = 960;

    private delegate void StateChangedCallback(int state);
    private delegate void PcmCallback(IntPtr samples, int sampleCount);

    private static readonly StateChangedCallback StateChanged = HandleStateChanged;
    private static readonly PcmCallback PcmReceived = HandlePcmReceived;
    private static readonly float[] Frame = new float[FrameSize];
    private static int frameOffset;
    private static bool initialized;

    public static event Action<BasisWebAudioCaptureState> CaptureStateChanged;
    public static event Action<float[]> PcmFrameReady;

    public static BasisWebAudioCaptureState State { get; private set; } = BasisWebAudioCaptureState.Idle;

    public static void EnsureInitialized()
    {
        if (initialized)
        {
            return;
        }

        BasisWebAudioInitialize(StateChanged, PcmReceived);
        initialized = true;
    }

    public static bool RequestFromUserGesture()
    {
        EnsureInitialized();
        return BasisWebAudioCaptureRequestFromUserGesture() == 1;
    }

    public static void Stop()
    {
        if (!initialized)
        {
            return;
        }

        BasisWebAudioCaptureStop();
        frameOffset = 0;
    }

    [MonoPInvokeCallback(typeof(StateChangedCallback))]
    private static void HandleStateChanged(int state)
    {
        State = (BasisWebAudioCaptureState)state;
        CaptureStateChanged?.Invoke(State);
    }

    [MonoPInvokeCallback(typeof(PcmCallback))]
    private static void HandlePcmReceived(IntPtr samples, int sampleCount)
    {
        int sourceOffset = 0;
        while (sourceOffset < sampleCount)
        {
            int copyCount = Math.Min(FrameSize - frameOffset, sampleCount - sourceOffset);
            Marshal.Copy(IntPtr.Add(samples, sourceOffset * sizeof(float)), Frame, frameOffset, copyCount);
            frameOffset += copyCount;
            sourceOffset += copyCount;

            if (frameOffset == FrameSize)
            {
                PcmFrameReady?.Invoke(Frame);
                frameOffset = 0;
            }
        }
    }

    [DllImport("__Internal")]
    private static extern void BasisWebAudioInitialize(StateChangedCallback onStateChanged, PcmCallback onPcm);

    [DllImport("__Internal")]
    private static extern int BasisWebAudioCaptureRequestFromUserGesture();

    [DllImport("__Internal")]
    private static extern void BasisWebAudioCaptureStop();
}
#endif
