#if UNITY_WEBGL && !UNITY_EDITOR
using AOT;
using System;
using System.Runtime.InteropServices;
using UnityEngine;

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
    private delegate void DevicesChangedCallback(IntPtr devicesJson);

    private static readonly StateChangedCallback StateChanged = HandleStateChanged;
    private static readonly PcmCallback PcmReceived = HandlePcmReceived;
    private static readonly DevicesChangedCallback DevicesChanged = HandleDevicesChanged;
    private static readonly float[] Frame = new float[FrameSize];
    private static int frameOffset;
    private static bool initialized;

    public static event Action<BasisWebAudioCaptureState> CaptureStateChanged;
    public static event Action<float[]> PcmFrameReady;
    public static event Action<string[]> MicrophoneDevicesChanged;

    public static BasisWebAudioCaptureState State { get; private set; } = BasisWebAudioCaptureState.Idle;

    public static void EnsureInitialized()
    {
        if (initialized)
        {
            return;
        }

        BasisWebAudioInitialize(StateChanged, PcmReceived, DevicesChanged);
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

    public static void PlayMicrophoneToggleSound(bool muted, float volume)
    {
        EnsureInitialized();
        BasisWebAudioPlayMicrophoneToggleSound(muted ? 1 : 0, volume);
    }

    public static void SelectDevice(string deviceName)
    {
        EnsureInitialized();
        BasisWebAudioSetCaptureDevice(deviceName ?? string.Empty);
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

    [MonoPInvokeCallback(typeof(DevicesChangedCallback))]
    private static void HandleDevicesChanged(IntPtr devicesJson)
    {
        string json = Marshal.PtrToStringAnsi(devicesJson);
        DeviceList deviceList = JsonUtility.FromJson<DeviceList>(json);
        MicrophoneDevicesChanged?.Invoke(deviceList?.devices ?? Array.Empty<string>());
    }

    [Serializable]
    private sealed class DeviceList
    {
        public string[] devices;
    }

    [DllImport("__Internal")]
    private static extern void BasisWebAudioInitialize(StateChangedCallback onStateChanged, PcmCallback onPcm, DevicesChangedCallback onDevicesChanged);

    [DllImport("__Internal")]
    private static extern int BasisWebAudioCaptureRequestFromUserGesture();

    [DllImport("__Internal")]
    private static extern void BasisWebAudioCaptureStop();

    [DllImport("__Internal")]
    private static extern void BasisWebAudioSetCaptureDevice(string deviceName);

    [DllImport("__Internal")]
    private static extern void BasisWebAudioPlayMicrophoneToggleSound(int muted, float volume);
}
#endif
