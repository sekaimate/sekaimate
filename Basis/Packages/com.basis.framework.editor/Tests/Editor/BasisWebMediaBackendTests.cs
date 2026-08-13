using System.IO;
using NUnit.Framework;

public class BasisWebMediaBackendTests
{
    private const string PluginPath = "Packages/com.basis.mediaplayer/Runtime/Web/BasisWebMedia.jslib";
    private const string E2EFixturePath = "Packages/com.basis.mediaplayer/Runtime/Web/BasisWebMediaE2EFixture.cs";

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
}
