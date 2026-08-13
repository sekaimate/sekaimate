using System.IO;
using NUnit.Framework;

public class BasisWebMediaBackendTests
{
    private const string PluginPath = "Packages/com.basis.mediaplayer/Runtime/Web/BasisWebMedia.jslib";
    private const string E2EFixturePath = "Packages/com.basis.mediaplayer/Runtime/Web/BasisWebMediaE2EFixture.cs";
    private const string SecurityPath = "Packages/com.basis.mediaplayer/Runtime/Core/BasisMediaPlayerSecurity.cs";
    private const string UrlSecurityPath = "Packages/com.basis.common/BasisUrlSecurity.cs";
    private const string WebSecurityPath = "Packages/com.basis.mediaplayer/Runtime/Web/BasisWebMediaSecurityPolicy.cs";
    private const string PlayerPath = "Packages/com.basis.mediaplayer/Runtime/BasisMediaPlayer.cs";
    private const string SyntheticSourcePath = "Packages/com.basis.mediaplayer/Runtime/Sources/BasisSyntheticTestSource.cs";
    private const string PlayerLoopPath = "Packages/com.basis.mediaplayer/Runtime/Web/BasisWebMediaPlayerLoop.cs";
    private const string WebSourcePath = "Packages/com.basis.mediaplayer/Runtime/Web/BasisWebMediaSource.cs";
    private const string NativeMediaPath = "Packages/com.basis.mediaplayer/Runtime/Native/BasisNativeMedia.cs";
    private const string NativeSourcePath = "Packages/com.basis.mediaplayer/Runtime/Native/BasisNativeVideoSource.cs";
    private const string MediaStatePath = "Packages/com.basis.mediaplayer/Runtime/Core/BasisMediaEngineState.cs";
    private const string ShimsPath = "Packages/com.basis.shim/Shims/BasisShims.cs";

    [Test]
    public void UploadsOnlyNewBrowserVideoFrames()
    {
        string source = File.ReadAllText(PluginPath);

        StringAssert.Contains("requestVideoFrameCallback", source);
        StringAssert.Contains("if (!player.framePending) return 0", source);
        StringAssert.Contains("texSubImage2D", source);
    }

    [Test]
    public void RestoresWebGlStateAfterVideoUpload()
    {
        string source = File.ReadAllText(PluginPath);

        StringAssert.Contains("var previousFlip", source);
        StringAssert.Contains("var previousTexture", source);
        StringAssert.Contains("finally", source);
        StringAssert.Contains("UNPACK_FLIP_Y_WEBGL, previousFlip", source);
    }

    [Test]
    public void PreservesMediaEndAndAutoplayErrorContracts()
    {
        string source = File.ReadAllText(PluginPath);

        StringAssert.Contains("player.video.loop = false", source);
        StringAssert.Contains("if (player.error !== 0) return 6", source);
        StringAssert.Contains("player.video.muted = mute !== 0", source);
    }

    [Test]
    public void RoutesWebMediaDirectlyWithoutFakeSpatialAudio()
    {
        string source = File.ReadAllText(PluginPath);

        StringAssert.DoesNotContain("createPanner", source);
        StringAssert.Contains("gain.connect(audioContext.destination)", source);
    }

    [Test]
    public void ExposesOptInDiagnosticsFromTheBrowserMediaBackend()
    {
        string source = File.ReadAllText(PluginPath);

        StringAssert.Contains("basisMediaE2E", source);
        StringAssert.Contains("window.__basisWebMediaE2E", source);
        StringAssert.Contains("video instanceof HTMLVideoElement", source);
        StringAssert.Contains("textureUploadCount", source);
        StringAssert.Contains("audioContext.state", source);
        StringAssert.Contains("canPlayType", source);
    }

    [Test]
    public void E2EFixtureLoadsMediaThroughBasisMediaPlayer()
    {
        string source = File.ReadAllText(E2EFixturePath);

        StringAssert.Contains("basisMediaE2EUrl", source);
        StringAssert.Contains("AddComponent<BasisMediaPlayer>()", source);
        StringAssert.Contains("player.LoadUrl(mediaUrl)", source);
        StringAssert.Contains("player.Pause()", source);
        StringAssert.Contains("player.Seek(TimeSpan.FromSeconds(0.25))", source);
        StringAssert.Contains("player.Play()", source);
        StringAssert.DoesNotContain("BasisWebMediaCreate", source);
    }

    [Test]
    public void BrowserMediaSecurityDoesNotCompileDnsResolution()
    {
        string security = File.ReadAllText(SecurityPath);
        string urlSecurity = File.ReadAllText(UrlSecurityPath);
        string webSecurity = File.ReadAllText(WebSecurityPath);

        StringAssert.Contains("#if !UNITY_WEBGL || UNITY_EDITOR", security);
        StringAssert.Contains("Dns.GetHostAddressesAsync", urlSecurity);
        StringAssert.DoesNotContain("System.Net.Dns", webSecurity);
        StringAssert.Contains("BasisMediaPlayerSecurity.IsUrlAllowed", webSecurity);
        StringAssert.Contains("BasisWebMediaPolicy.TryValidate", webSecurity);
    }

    [Test]
    public void NativeDnsResolutionFailureIsRejected()
    {
        string urlSecurity = File.ReadAllText(UrlSecurityPath);

        StringAssert.Contains("DNS lookup failed", urlSecurity);
        StringAssert.DoesNotContain("catch { return null; }", urlSecurity);
    }

    [Test]
    public void BrowserScreenshotUsesGpuReadbackAndDownload()
    {
        string source = File.ReadAllText(PlayerPath);

        StringAssert.Contains("BasisWebCameraGpuReadback.ReadInto", source);
        StringAssert.Contains("BasisWebFileDownload.Save", source);
        StringAssert.Contains("#else\n        if (!BasisMediaPlayerSecurity.TrySandboxLogPath", source);
        StringAssert.Contains("AsyncGPUReadback.Request", source);
        StringAssert.Contains("File.WriteAllBytes(fullPath, png)", source);
    }

    [Test]
    public void SyntheticSourceUsesPlayerLoopWithoutWebThreads()
    {
        string source = File.ReadAllText(SyntheticSourcePath);
        string playerLoop = File.ReadAllText(PlayerLoopPath);

        StringAssert.Contains("BasisWebMediaPlayerLoop.Register", source);
        StringAssert.Contains("BasisWebMediaPlayerLoop.Unregister", source);
        StringAssert.Contains("#else\n        thread = new Thread", source);
        StringAssert.Contains("PlayerLoop.SetPlayerLoop", playerLoop);
        StringAssert.DoesNotContain("System.Threading", playerLoop);
        StringAssert.Contains("System.Random webRandom", source);
        StringAssert.Contains("new System.Random(NoiseSeed)", source);
        StringAssert.DoesNotContain("new Random(NoiseSeed)", source);
    }

    [Test]
    public void NativeMediaInteropIsExcludedFromWebPlayers()
    {
        string interop = File.ReadAllText(NativeMediaPath);
        string source = File.ReadAllText(NativeSourcePath);
        string player = File.ReadAllText(PlayerPath);

        StringAssert.StartsWith("#if !UNITY_WEBGL || UNITY_EDITOR", interop);
        StringAssert.StartsWith("#if !UNITY_WEBGL || UNITY_EDITOR", source);
        StringAssert.Contains("#if !UNITY_WEBGL || UNITY_EDITOR\n    public BasisNativeVideoSource NativeEngine", player);
        StringAssert.Contains("public BasisPlatformMediaSource PlatformEngine => nativeEngine;", player);
    }

    [Test]
    public void MediaStateContractsCompileForNativeAndWebPlayers()
    {
        string contracts = File.ReadAllText(MediaStatePath);
        string nativeSource = File.ReadAllText(NativeSourcePath);

        StringAssert.Contains("public enum BasisMediaEngineState", contracts);
        StringAssert.Contains("public enum BasisVideoBufferMode", contracts);
        StringAssert.DoesNotContain("UNITY_WEBGL", contracts);
        StringAssert.DoesNotContain("public enum BasisMediaEngineState", nativeSource);
        StringAssert.DoesNotContain("public enum BasisVideoBufferMode", nativeSource);
    }

    [Test]
    public void BrowserSourceImplementsSharedTimelineContract()
    {
        string source = File.ReadAllText(WebSourcePath);

        StringAssert.Contains("public long DurationUs", source);
        StringAssert.Contains("public bool SeekUs(long positionUs)", source);
        StringAssert.Contains("BasisWebMediaSeek(handle", source);
        StringAssert.Contains("public string Transport", source);
        StringAssert.Contains("public void SetAudioLatencyUs(long latencyUs)", source);
    }

    [Test]
    public void BrowserDownloadsExcludeNativeDnsResolution()
    {
        string source = File.ReadAllText(ShimsPath);

        StringAssert.Contains("#if !UNITY_WEBGL || UNITY_EDITOR\n\t\t\tstring dnsReason", source);
    }
}
