using System.IO;
using NUnit.Framework;
using UnityEditor;

public class BasisWebAudioBridgeTests
{
    private const string BrowserPluginPath = "Packages/com.basis.framework/Platform/WebGL/BasisWebAudio.jslib";
    private const string CaptureBridgePath = "Packages/com.basis.framework/Platform/WebGL/BasisWebAudioCaptureBridge.cs";
    private const string PlaybackBridgePath = "Packages/com.basis.framework/Platform/WebGL/BasisWebAudioPlaybackBridge.cs";
    private const string UiSoundBridgePath = "Packages/com.basis.framework/Platform/WebGL/BasisWebAudioUiSoundBridge.cs";
    private const string InputPath = "Packages/com.basis.framework/Device Management/Devices/Base/BasisInput.cs";
    private const string DiagnosticsBridgePath = "Packages/com.basis.framework/Platform/WebGL/BasisWebAudioDiagnosticsBridge.cs";
    private const string MicrophoneDriverPath = "Packages/com.basis.framework/Drivers/Local/BasisLocalMicrophoneDriver.cs";
    private const string SettingsProviderPath = "Packages/com.basis.framework/BasisUI/Menus/Main Menu Providers/SettingsProvider.cs";
    private const string MicrophoneIconDriverPath = "Packages/com.basis.framework/Drivers/Local/BasisLocalMicrophoneIconDriver.cs";
    private const string AudioTransmissionPath = "Packages/com.basis.framework/Networking/Transmitters/BasisAudioTransmission.cs";
    private const string AudioReceiverPath = "Packages/com.basis.framework/Networking/Recievers/BasisAudioReceiver.cs";
    private const string RemoteAudioDriverPath = "Packages/com.basis.framework/Drivers/Remote/BasisRemoteAudioDriver.cs";
    private const string VisemeDriverPath = "Packages/com.basis.framework/Drivers/Common/BasisAudioAndVisemeDriver.cs";
    private const string NetworkHarnessPath = "Packages/com.basis.framework/Networking/WebGL/BasisWebNetworkE2EHarness.cs";
    private const string NetworkHarnessPluginPath = "Packages/com.basis.framework/Networking/WebGL/BasisWebNetworkE2E.jslib";

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
        StringAssert.DoesNotContain("navigator.userActivation.isActive", source);
        StringAssert.Contains("visibilitychange", source);
        StringAssert.Contains("document.hidden", source);
        StringAssert.DoesNotContain("BasisWebAudio.context.suspend()", source);
        StringAssert.DoesNotContain("track.enabled = false", source);
        StringAssert.Contains("new window.AudioContext", source);
        StringAssert.Contains("sampleRate: BasisWebAudio.sampleRate", source);
    }

    [Test]
    public void CaptureRequestsPermissionWithoutTransientActivationDependency()
    {
        string source = File.ReadAllText(BrowserPluginPath);
        int acquireIndex = source.IndexOf("acquireStream: async function()");
        int permissionIndex = source.IndexOf("navigator.mediaDevices.getUserMedia", acquireIndex);
        int requestIndex = source.IndexOf("requestCapture: async function()");
        int acquireCallIndex = source.IndexOf("BasisWebAudio.acquireStream()", requestIndex);
        int initializationIndex = source.IndexOf("await BasisWebAudio.ensureInitialized();", acquireCallIndex);
        int initializationStartIndex = source.IndexOf("ensureInitialized: function()");
        int initializationResumeIndex = source.IndexOf("await BasisWebAudio.context.resume();", initializationStartIndex);
        int processorIndex = source.IndexOf("createScriptProcessor", initializationResumeIndex);
        int gestureIndex = source.IndexOf("resumeFromGesture: function()");
        int gestureResumeIndex = source.IndexOf("context.resume()", gestureIndex);
        int gestureInitializationIndex = source.IndexOf("BasisWebAudio.ensureInitialized()", gestureResumeIndex);
        int awaitResumeIndex = source.IndexOf("await resumePromise;", initializationIndex);

        Assert.That(acquireIndex, Is.GreaterThanOrEqualTo(0));
        Assert.That(permissionIndex, Is.GreaterThan(acquireIndex));
        Assert.That(requestIndex, Is.GreaterThan(permissionIndex));
        Assert.That(acquireCallIndex, Is.GreaterThan(requestIndex));
        Assert.That(initializationIndex, Is.GreaterThan(acquireCallIndex));
        Assert.That(initializationResumeIndex, Is.GreaterThan(initializationStartIndex));
        Assert.That(processorIndex, Is.GreaterThan(initializationResumeIndex));
        Assert.That(gestureIndex, Is.GreaterThanOrEqualTo(0));
        Assert.That(gestureResumeIndex, Is.GreaterThan(gestureIndex));
        Assert.That(gestureInitializationIndex, Is.GreaterThan(gestureResumeIndex));
        StringAssert.Contains("await BasisWebAudio.context.resume();", source.Substring(initializationIndex));
        Assert.That(awaitResumeIndex, Is.EqualTo(-1));
        StringAssert.Contains("BasisWebAudio.notifyState(BasisWebAudio.State.AwaitingUserGesture);", source);
    }

    [Test]
    public void MicrophoneToggleSoundUsesWebAudioInsteadOfUnityAudioSourceOnWebGl()
    {
        string pluginSource = File.ReadAllText(BrowserPluginPath);
        string bridgeSource = File.ReadAllText(CaptureBridgePath);
        string iconDriverSource = File.ReadAllText(MicrophoneIconDriverPath);

        StringAssert.Contains("BasisWebAudioPlayMicrophoneToggleSound", pluginSource);
        StringAssert.Contains("BasisWebAudioFeedback.play", pluginSource);
        StringAssert.Contains("BasisWebAudioPlayMicrophoneToggleSound__deps: ['$BasisWebAudioFeedback']", pluginSource);
        StringAssert.Contains("BasisWebAudioPlayMicrophoneToggleSound", bridgeSource);
        StringAssert.Contains("BasisWebAudioCaptureBridge.PlayMicrophoneToggleSound", iconDriverSource);
        StringAssert.Contains("#if UNITY_WEBGL && !UNITY_EDITOR", iconDriverSource);
    }

    [Test]
    public void UiSoundsBypassUnityAudioClipsOnWebGl()
    {
        string pluginSource = File.ReadAllText(BrowserPluginPath);
        string bridgeSource = File.ReadAllText(UiSoundBridgePath);
        string inputSource = File.ReadAllText(InputPath);

        StringAssert.Contains("BasisWebAudioPlayUiSound", pluginSource);
        StringAssert.Contains("$BasisWebAudioFeedback", pluginSource);
        StringAssert.Contains("BasisWebAudioPlayUiSound", bridgeSource);
        StringAssert.Contains("BasisWebAudioUiSoundBridge.Play", inputSource);
        StringAssert.Contains("#if UNITY_WEBGL && !UNITY_EDITOR", inputSource);
        StringAssert.Contains("AudioSource.PlayClipAtPoint", inputSource);
    }

    [Test]
    public void PlaybackUsesOneExplicit48KhzVoiceContext()
    {
        string pluginSource = File.ReadAllText(BrowserPluginPath);

        StringAssert.Contains("new window.AudioContext", pluginSource);
        StringAssert.Contains("sampleRate: BasisWebAudio.sampleRate", pluginSource);
        StringAssert.Contains("latencyHint: 'interactive'", pluginSource);
        StringAssert.Contains("createScriptProcessor(2048, 1, 1)", pluginSource);
        StringAssert.Contains("BasisWebAudio.playbackSources", pluginSource);
        StringAssert.DoesNotContain("audioWorklet.addModule", pluginSource);
        StringAssert.Contains("BasisWebAudioPlaybackPush", pluginSource);
        StringAssert.Contains("BasisWebAudioPlaybackRemoveSink", pluginSource);
    }

    [Test]
    public void BrowserPacketsStartPlaybackBeforeDecode()
    {
        string source = File.ReadAllText(AudioReceiverPath);
        int insertIndex = source.IndexOf("VoiceBuffer.InsertEncoded", System.StringComparison.Ordinal);
        int startIndex = source.IndexOf("StartAudio(BasisTransmissionResults.ConvertedVoiceDistance)", System.StringComparison.Ordinal);
        int decodeGateIndex = source.IndexOf("if (!HasAudioSource)\n            {\n                return;", System.StringComparison.Ordinal);

        Assert.That(insertIndex, Is.GreaterThanOrEqualTo(0));
        Assert.That(startIndex, Is.GreaterThan(insertIndex));
        Assert.That(decodeGateIndex, Is.GreaterThan(startIndex));
    }

    [Test]
    public void BrowserMicrophoneDevicesPopulateTheSettingsList()
    {
        string pluginSource = File.ReadAllText(BrowserPluginPath);
        string bridgeSource = File.ReadAllText(CaptureBridgePath);
        string microphoneSource = File.ReadAllText(MicrophoneDriverPath);

        StringAssert.Contains("navigator.mediaDevices.enumerateDevices()", pluginSource);
        StringAssert.Contains("stringToNewUTF8(payload)", pluginSource);
        StringAssert.Contains("device.kind === 'audioinput'", pluginSource);
        StringAssert.Contains("devicechange", pluginSource);
        StringAssert.Contains("BasisWebAudioSetCaptureDevice", pluginSource);
        StringAssert.Contains("MicrophoneDevicesChanged", bridgeSource);
        StringAssert.Contains("SMDMicrophone.SetDeviceList", microphoneSource);
    }

    [Test]
    public void OpeningMicrophoneSettingsRequestsPermissionBeforeListingDevices()
    {
        string pluginSource = File.ReadAllText(BrowserPluginPath);
        string bridgeSource = File.ReadAllText(CaptureBridgePath);
        string settingsSource = File.ReadAllText(SettingsProviderPath);

        StringAssert.Contains("requestDevicePermission: function()", pluginSource);
        StringAssert.Contains("BasisWebAudioRequestDevicePermission", pluginSource);
        StringAssert.Contains("BasisWebAudioRequestDevicePermission", bridgeSource);
        StringAssert.Contains("BasisWebAudioCaptureBridge.RequestDevicePermission();", settingsSource);
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
        StringAssert.Contains("capturePeak", pluginSource);
        StringAssert.Contains("activeDeviceName", pluginSource);
        StringAssert.DoesNotContain("basis-voice-diagnostics", pluginSource);
        StringAssert.Contains("selectInputDeviceForE2E", pluginSource);
        StringAssert.Contains("opusEncodedPackets", pluginSource);
        StringAssert.Contains("networkPacketsSent", pluginSource);
        StringAssert.Contains("networkPacketsReceived", pluginSource);
        StringAssert.Contains("opusDecodedFrames", pluginSource);
        StringAssert.Contains("playbackFramesPushed", pluginSource);
        StringAssert.Contains("playbackNonSilentFramesPushed", pluginSource);
        StringAssert.Contains("playbackPeak", pluginSource);
        StringAssert.Contains("verifySender", pluginSource);
        StringAssert.Contains("verifyReceiver", pluginSource);
        StringAssert.Contains("#if UNITY_WEBGL && !UNITY_EDITOR", bridgeSource);
        StringAssert.Contains("BasisWebAudioDiagnosticsBridge.MarkOpusEncoded", transmissionSource);
        StringAssert.Contains("BasisWebAudioDiagnosticsBridge.MarkNetworkSent", transmissionSource);
        StringAssert.Contains("BasisWebAudioDiagnosticsBridge.MarkNetworkReceived", receiverSource);
        StringAssert.Contains("BasisWebAudioDiagnosticsBridge.MarkOpusDecoded", receiverSource);
    }

    [Test]
    public void BrowserDiagnosticsMeasureMuteTalkModeAndLipSync()
    {
        string pluginSource = File.ReadAllText(BrowserPluginPath);
        string visemeSource = File.ReadAllText(VisemeDriverPath);
        string harnessSource = File.ReadAllText(NetworkHarnessPath);
        string harnessPluginSource = File.ReadAllText(NetworkHarnessPluginPath);

        StringAssert.Contains("muted", pluginSource);
        StringAssert.Contains("talkMode", pluginSource);
        StringAssert.Contains("localVisemeFrames", pluginSource);
        StringAssert.Contains("remoteVisemeFrames", pluginSource);
        StringAssert.Contains("remoteMuted", pluginSource);
        StringAssert.Contains("remoteTalkMode", pluginSource);
        StringAssert.Contains("MarkVisemeProcessed", visemeSource);
        StringAssert.Contains("BasisLocalMicrophoneDriver.ToggleIsPaused", harnessSource);
        StringAssert.Contains("BasisTalkModeManager.SetMode", harnessSource);
        StringAssert.Contains("basisNetworkE2ESetMuted", harnessPluginSource);
        StringAssert.Contains("basisNetworkE2ESetTalkMode", harnessPluginSource);
    }
}
