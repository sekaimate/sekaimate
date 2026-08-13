using System.IO;
using Basis.Scripts.BasisSdk;
using NUnit.Framework;
using UnityEngine;

public class BasisWebLipSyncTests
{
    private const string AudioAndVisemeDriverPath = "Packages/com.basis.framework/Drivers/Common/BasisAudioAndVisemeDriver.cs";
    private const string WebVolumeDriverPath = "Packages/com.basis.framework/Drivers/Common/BasisWebVolumeVisemeDriver.cs";
    private const string MicrophoneDriverPath = "Packages/com.basis.framework/Drivers/Local/BasisLocalMicrophoneDriver.cs";

    [Test]
    public void SilentPcmClosesMouth()
    {
        float level = BasisWebVolumeVisemeDriver.MeasureNormalizedLevel(new float[960], 960);

        Assert.That(level, Is.Zero);
        Assert.That(BasisWebVolumeVisemeDriver.MeasureNormalizedLevel(new float[0], 1), Is.Zero);
    }

    [Test]
    public void LouderPcmProducesHigherMouthWeight()
    {
        float quiet = BasisWebVolumeVisemeDriver.MeasureNormalizedLevel(CreateConstantFrame(0.02f), 960);
        float loud = BasisWebVolumeVisemeDriver.MeasureNormalizedLevel(CreateConstantFrame(0.2f), 960);

        Assert.That(quiet, Is.GreaterThan(0f));
        Assert.That(loud, Is.GreaterThan(quiet));
        Assert.That(loud, Is.EqualTo(1f));
    }

    [Test]
    public void EnvelopeUsesAttackAndReleaseRates()
    {
        float attack = BasisWebVolumeVisemeDriver.UpdateEnvelope(0f, 1f, 0.02f);
        float release = BasisWebVolumeVisemeDriver.UpdateEnvelope(1f, 0f, 0.02f);

        Assert.That(attack, Is.GreaterThan(1f - release));
        Assert.That(attack, Is.InRange(0f, 1f));
        Assert.That(release, Is.InRange(0f, 1f));
    }

    [Test]
    public void PcmLevelDrivesAvatarMouthBlendShape()
    {
        GameObject avatarObject = new GameObject("Web volume viseme test avatar");
        Mesh mesh = new Mesh();
        try
        {
            mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
            mesh.triangles = new[] { 0, 1, 2 };
            Vector3[] deltas = { Vector3.zero, Vector3.zero, Vector3.zero };
            mesh.AddBlendShapeFrame("aa", 100f, deltas, deltas, deltas);

            SkinnedMeshRenderer renderer = avatarObject.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = mesh;
            BasisAvatar avatar = avatarObject.AddComponent<BasisAvatar>();
            avatar.FaceVisemeMesh = renderer;
            avatar.FaceVisemeMovement = new[] { -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, 0, -1, -1, -1, -1 };

            BasisWebVolumeVisemeDriver driver = new BasisWebVolumeVisemeDriver();
            Assert.That(driver.Initialize(avatar), Is.True);

            driver.ProcessAudioSamples(CreateConstantFrame(0.2f), 960);
            driver.Simulate(0.1f);
            driver.Apply();

            Assert.That(renderer.GetBlendShapeWeight(0), Is.GreaterThan(0f));

            driver.Dispose();
            Assert.That(renderer.GetBlendShapeWeight(0), Is.Zero);
        }
        finally
        {
            Object.DestroyImmediate(avatarObject);
            Object.DestroyImmediate(mesh);
        }
    }

    [Test]
    public void BrowserPcmDrivesWebVolumeVisemes()
    {
        string microphoneSource = File.ReadAllText(MicrophoneDriverPath);
        string visemeSource = File.ReadAllText(AudioAndVisemeDriverPath);
        string webDriverSource = File.ReadAllText(WebVolumeDriverPath);

        StringAssert.Contains("BasisWebAudioCaptureBridge.PcmFrameReady", microphoneSource);
        StringAssert.Contains("OnHasAudio?.Invoke()", microphoneSource);
        StringAssert.Contains("#if UNITY_WEBGL && !UNITY_EDITOR", visemeSource);
        StringAssert.Contains("BasisWebVolumeVisemeDriver", visemeSource);
        StringAssert.Contains("SetBlendShapeWeight", webDriverSource);
    }

    [Test]
    public void RemoteRangeExitClosesWebVolumeVisemeBeforeRemoval()
    {
        string remoteSource = File.ReadAllText("Packages/com.basis.framework/Drivers/Remote/BasisRemoteAudioDriver.cs");

        int closeIndex = remoteSource.IndexOf("driver.CloseWebVolumeViseme()", System.StringComparison.Ordinal);
        int removalIndex = remoteSource.IndexOf("RemoveFromActive(driver)", closeIndex, System.StringComparison.Ordinal);

        Assert.That(closeIndex, Is.GreaterThanOrEqualTo(0));
        Assert.That(removalIndex, Is.GreaterThan(closeIndex));
    }

    [Test]
    public void AvatarReinitializationRebindsFaceRendererLifecycle()
    {
        string source = File.ReadAllText(AudioAndVisemeDriverPath);

        StringAssert.Contains("UnbindFaceRenderer();", source);
        StringAssert.Contains("HashInstanceID = Player.FaceRenderer.GetEntityId();", source);
        StringAssert.Contains("HashInstanceID = EntityId.None;", source);
    }

    [Test]
    public void NativeOpenLipSyncPathRemainsAvailable()
    {
        string source = File.ReadAllText(AudioAndVisemeDriverPath);

        StringAssert.Contains("TryAcquireOpenLipSyncContext()", source);
        StringAssert.Contains("openLipSyncContext.ProcessAudioSamples", source);
        StringAssert.Contains("openLipSyncContext.Apply()", source);
    }

    private static float[] CreateConstantFrame(float amplitude)
    {
        float[] samples = new float[960];
        for (int index = 0; index < samples.Length; index++)
        {
            samples[index] = amplitude;
        }

        return samples;
    }
}
