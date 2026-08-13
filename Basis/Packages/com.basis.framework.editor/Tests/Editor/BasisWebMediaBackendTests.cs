using System.IO;
using NUnit.Framework;

public class BasisWebMediaBackendTests
{
    private const string PluginPath = "Packages/com.basis.mediaplayer/Runtime/Web/BasisWebMedia.jslib";
    private const string E2EFixturePath = "Packages/com.basis.mediaplayer/Runtime/Web/BasisWebMediaE2EFixture.cs";
    private const string SecurityPath = "Packages/com.basis.mediaplayer/Runtime/Core/BasisMediaPlayerSecurity.cs";
    private const string WebSecurityPath = "Packages/com.basis.mediaplayer/Runtime/Web/BasisWebMediaSecurityPolicy.cs";
    private const string PlayerPath = "Packages/com.basis.mediaplayer/Runtime/BasisMediaPlayer.cs";
    private const string SyntheticSourcePath = "Packages/com.basis.mediaplayer/Runtime/Sources/BasisSyntheticTestSource.cs";
    private const string PlayerLoopPath = "Packages/com.basis.mediaplayer/Runtime/Web/BasisWebMediaPlayerLoop.cs";
    private const string NativeMediaPath = "Packages/com.basis.mediaplayer/Runtime/Native/BasisNativeMedia.cs";
    private const string NativeSourcePath = "Packages/com.basis.mediaplayer/Runtime/Native/BasisNativeVideoSource.cs";

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
        string webSecurity = File.ReadAllText(WebSecurityPath);

        StringAssert.Contains("#if !UNITY_WEBGL || UNITY_EDITOR", security);
        StringAssert.Contains("Dns.GetHostAddressesAsync", security);
        StringAssert.DoesNotContain("System.Net.Dns", webSecurity);
        StringAssert.Contains("BasisMediaPlayerSecurity.IsUrlAllowed", webSecurity);
        StringAssert.Contains("BasisWebMediaPolicy.TryValidate", webSecurity);
    }

    [Test]
    public void NativeDnsResolutionFailureIsRejected()
    {
        string security = File.ReadAllText(SecurityPath);

        StringAssert.Contains("DNS resolution failed", security);
        StringAssert.DoesNotContain("catch { return null; }", security);
    }

    [Test]
    public void BrowserScreenshotUsesGpuReadbackAndDownload()
    {
        string source = File.ReadAllText(PlayerPath);

        StringAssert.Contains("BasisWebCameraGpuReadback.ReadInto", source);
        StringAssert.Contains("BasisWebFileDownload.Save", source);
        StringAssert.Contains("#else\n        AsyncGPUReadback.Request", source);
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
    }
}
