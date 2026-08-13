using System;
using OpenLipSync.Inference.OVRCompat;

public interface IBasisOpenLipSyncBackend : IDisposable
{
    string LastError { get; }
    int DebugMelFramesProduced { get; }
    int DebugInferenceRuns { get; }
    float DebugLastInferenceMax { get; }
    string DebugPipelineStatus { get; }
    string DebugInferenceDetail { get; }

    Result InitializeFromBytes(int sampleRate, byte[] modelBytes, string configJson);
    Result CreateContext(ref uint context);
    Result DestroyContext(uint context);
    Result SendSignal(uint context, Signals signal, int arg1);
    Result ProcessFrameFloat(uint context, ReadOnlySpan<float> audio, bool stereo, ref Frame frame);
}

public static class BasisOpenLipSyncBackendRegistry
{
    private static Func<IBasisOpenLipSyncBackend> _backendFactory;

    public static void Register(Func<IBasisOpenLipSyncBackend> factory)
    {
        _backendFactory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    public static IBasisOpenLipSyncBackend Create()
    {
        return _backendFactory?.Invoke();
    }
}
