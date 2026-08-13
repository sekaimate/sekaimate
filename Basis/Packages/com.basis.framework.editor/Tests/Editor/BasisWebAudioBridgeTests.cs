using System.IO;
using NUnit.Framework;
using UnityEditor;

public class BasisWebAudioBridgeTests
{
    private const string BrowserPluginPath = "Packages/com.basis.framework/Platform/WebGL/BasisWebAudio.jslib";
    private const string CaptureBridgePath = "Packages/com.basis.framework/Platform/WebGL/BasisWebAudioCaptureBridge.cs";
    private const string PlaybackBridgePath = "Packages/com.basis.framework/Platform/WebGL/BasisWebAudioPlaybackBridge.cs";
    private const string DiagnosticsBridgePath = "Packages/com.basis.framework/Platform/WebGL/BasisWebAudioDiagnosticsBridge.cs";
    private const string MicrophoneDriverPath = "Packages/com.basis.framework/Drivers/Local/BasisLocalMicrophoneDriver.cs";
    private const string AudioTransmissionPath = "Packages/com.basis.framework/Networking/Transmitters/BasisAudioTransmission.cs";
    private const string AudioReceiverPath = "Packages/com.basis.framework/Networking/Recievers/BasisAudioReceiver.cs";
    private const string RemoteAudioDriverPath = "Packages/com.basis.framework/Drivers/Remote/BasisRemoteAudioDriver.cs";

    [Test]
    public void BrowserAudioPluginIsEnabledOnlyForWebGl()
    {
        PluginImporter importer = AssetImporter.GetAtPath(BrowserPluginPath) as PluginImporter;

        Assert.That(importer, Is.Not.Null);
        Assert.That(importer.GetCompatibleWithAnyPlatform(), Is.False);
        Assert.That(importer.GetCompatibleWithEditor(), Is.False);
        Assert.That(importer.GetCompatibleWithPlatform(BuildTarget.WebGL), Is.True);
    }

    [Test]
    public void CaptureUsesExplicitBrowserAudioContract()
    {
        string source = File.ReadAllText(BrowserPluginPath);

        StringAssert.Contains("navigator.mediaDevices.getUserMedia", source);
        StringAssert.Contains("sampleRate: 48000", source);
        StringAssert.Contains("channelCount: 1", source);
        StringAssert.Contains("frameSize: 960", source);
        StringAssert.Contains("NotAllowedError", source);
        StringAssert.Contains("navigator.userActivation.isActive", source);
        StringAssert.Contains("visibilitychange", source);
        StringAssert.Contains("document.hidden", source);
    }

    [Test]
    public void PlaybackUsesAudioWorkletPcmSink()
    {
        string source = File.ReadAllText(BrowserPluginPath);

        StringAssert.Contains("registerProcessor('basis-capture-processor'", source);
        StringAssert.Contains("registerProcessor('basis-playback-processor'", source);
        StringAssert.Contains("audioWorklet.addModule", source);
        StringAssert.Contains("BasisWebAudioPlaybackPush", source);
        StringAssert.Contains("BasisWebAudioPlaybackRemoveSink", source);
    }

    [Test]
    public void BrowserInteropExistsOnlyInWebGlPlayer()
    {
        string captureSource = File.ReadAllText(CaptureBridgePath);
        string playbackSource = File.ReadAllText(PlaybackBridgePath);

        StringAssert.Contains("#if UNITY_WEBGL && !UNITY_EDITOR", captureSource);
        StringAssert.Contains("DllImport(\"__Internal\")", captureSource);
        StringAssert.Contains("#if UNITY_WEBGL && !UNITY_EDITOR", playbackSource);
        StringAssert.Contains("DllImport(\"__Internal\")", playbackSource);
    }

    [Test]
    public void WebAudioBranchesDoNotReplaceNativeAudioPaths()
    {
        string microphoneSource = File.ReadAllText(MicrophoneDriverPath);
        string remoteSource = File.ReadAllText(RemoteAudioDriverPath);

        StringAssert.Contains("Microphone.Start", microphoneSource);
        StringAssert.Contains("Microphone.GetPosition", microphoneSource);
        StringAssert.Contains("OnAudioFilterRead", remoteSource);
        StringAssert.Contains("BasisWebAudioCaptureBridge", microphoneSource);
        StringAssert.Contains("BasisWebAudioPlaybackBridge", remoteSource);
    }

    [Test]
    public void BrowserDiagnosticsMeasureTheCanonicalVoicePipeline()
    {
        string pluginSource = File.ReadAllText(BrowserPluginPath);
        string bridgeSource = File.ReadAllText(DiagnosticsBridgePath);
        string transmissionSource = File.ReadAllText(AudioTransmissionPath);
        string receiverSource = File.ReadAllText(AudioReceiverPath);

        StringAssert.Contains("globalThis.BasisWebAudioDiagnostics", pluginSource);
        StringAssert.Contains("capturePcmFrames", pluginSource);
        StringAssert.Contains("opusEncodedPackets", pluginSource);
        StringAssert.Contains("networkPacketsSent", pluginSource);
        StringAssert.Contains("networkPacketsReceived", pluginSource);
        StringAssert.Contains("opusDecodedFrames", pluginSource);
        StringAssert.Contains("playbackFramesPushed", pluginSource);
        StringAssert.Contains("#if UNITY_WEBGL && !UNITY_EDITOR", bridgeSource);
        StringAssert.Contains("BasisWebAudioDiagnosticsBridge.MarkOpusEncoded", transmissionSource);
        StringAssert.Contains("BasisWebAudioDiagnosticsBridge.MarkNetworkSent", transmissionSource);
        StringAssert.Contains("BasisWebAudioDiagnosticsBridge.MarkNetworkReceived", receiverSource);
        StringAssert.Contains("BasisWebAudioDiagnosticsBridge.MarkOpusDecoded", receiverSource);
    }
}
