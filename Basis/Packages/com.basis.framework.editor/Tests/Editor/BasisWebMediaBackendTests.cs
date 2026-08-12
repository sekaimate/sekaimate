using System.IO;
using NUnit.Framework;

public class BasisWebMediaBackendTests
{
    private const string PluginPath = "Packages/com.basis.mediaplayer/Runtime/Web/BasisWebMedia.jslib";

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
    public void RoutesWebMediaThroughSpatialAudioGraph()
    {
        string source = File.ReadAllText(PluginPath);

        StringAssert.Contains("createPanner", source);
        StringAssert.Contains("spatialGain.connect(panner)", source);
        StringAssert.Contains("listener.positionX.value", source);
    }
}
