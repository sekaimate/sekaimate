using Basis.Scripts.BasisSdk;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public class BasisBeeWebCompatibilityTests
{
    [TestCase("Jiggle/ProceduralPrimitiveURP")]
    [TestCase("Hidden/VoxelizeShader")]
    public void WebGlRejectsUnsupportedShaders(string shaderName)
    {
        string[] errors = BasisWebBeeCompatibilityValidator.FindUnsupportedContent(
            new[] { BuildTarget.WebGL },
            new string[0],
            new[] { shaderName });

        Assert.That(errors, Is.EqualTo(new[] { $"Shader: {shaderName}" }));
    }

    [TestCase("UnityEngine.VFX.VisualEffect")]
    [TestCase("UnityEngine.VFX.VFXRenderer")]
    public void WebGlRejectsUnsupportedComponents(string componentTypeName)
    {
        string[] errors = BasisWebBeeCompatibilityValidator.FindUnsupportedContent(
            new[] { BuildTarget.WebGL },
            new[] { componentTypeName },
            new string[0]);

        Assert.That(errors, Is.EqualTo(new[] { $"Component: {componentTypeName}" }));
    }

    [TestCase(BuildTarget.StandaloneWindows64)]
    [TestCase(BuildTarget.StandaloneOSX)]
    [TestCase(BuildTarget.StandaloneLinux64)]
    [TestCase(BuildTarget.Android)]
    [TestCase(BuildTarget.iOS)]
    public void NativeTargetsDoNotApplyWebCompatibilityRules(BuildTarget target)
    {
        string[] errors = BasisWebBeeCompatibilityValidator.FindUnsupportedContent(
            new[] { target },
            new[] { "UnityEngine.VFX.VisualEffect" },
            new[] { "Jiggle/ProceduralPrimitiveURP", "Hidden/VoxelizeShader" });

        Assert.That(errors, Is.Empty);
    }

    [Test]
    public void WebGlReportsEveryUnsupportedContentCategory()
    {
        string[] errors = BasisWebBeeCompatibilityValidator.FindUnsupportedContent(
            new[] { BuildTarget.StandaloneWindows64, BuildTarget.WebGL },
            new[] { "UnityEngine.VFX.VFXRenderer" },
            new[] { "Hidden/VoxelizeShader" });

        Assert.That(errors, Is.EqualTo(new[]
        {
            "Component: UnityEngine.VFX.VFXRenderer",
            "Shader: Hidden/VoxelizeShader",
        }));
    }

    [Test]
    public void WebGlAcceptsBasisMediaPlayer()
    {
        string[] errors = BasisWebBeeCompatibilityValidator.FindUnsupportedContent(
            new[] { BuildTarget.WebGL },
            new[] { "BasisMediaPlayer" },
            new string[0]);

        Assert.That(errors, Is.Empty);
    }

    [TestCase("https://media.example/video.mp4", false, true)]
    [TestCase("https://media.example/video.mp4", true, true)]
    [TestCase("http://media.example/video.mp4", false, true)]
    [TestCase("http://media.example/video.mp4", true, false)]
    [TestCase("rtsp://media.example/live", false, false)]
    public void WebMediaUrlPolicyEnforcesBrowserTransport(
        string mediaUrl,
        bool pageUsesHttps,
        bool expected)
    {
        bool allowed = BasisWebMediaPolicy.TryValidate(mediaUrl, null, pageUsesHttps, false, out _);

        Assert.That(allowed, Is.EqualTo(expected));
    }

    [Test]
    public void WebMediaUrlPolicyRejectsSeparateAudioStream()
    {
        bool allowed = BasisWebMediaPolicy.TryValidate(
            "https://media.example/video.mp4",
            "https://media.example/audio.m4a",
            true,
            false,
            out string reason);

        Assert.That(allowed, Is.False);
        StringAssert.Contains("Separate audio", reason);
    }

    [Test]
    public void WebMediaUrlPolicyRejectsCustomHeaders()
    {
        bool allowed = BasisWebMediaPolicy.TryValidate(
            "https://media.example/video.mp4",
            null,
            true,
            true,
            out string reason);

        Assert.That(allowed, Is.False);
        StringAssert.Contains("Custom HTTP headers", reason);
    }

    [Test]
    public void WebMediaRejectsAudioMixerRouting()
    {
        Assert.That(BasisWebMediaPolicy.TryValidateAudioOutput(false, false, false, out _), Is.True);
        Assert.That(BasisWebMediaPolicy.TryValidateAudioOutput(true, false, false, out string reason), Is.False);
        StringAssert.Contains("AudioMixer", reason);
    }

    [Test]
    public void WebMediaRejectsSpatialAudio()
    {
        Assert.That(BasisWebMediaPolicy.TryValidateAudioOutput(false, true, false, out string reason), Is.False);
        StringAssert.Contains("Spatial audio", reason);
    }

    [Test]
    public void WebMediaRejectsMultipleAudioOutputs()
    {
        Assert.That(BasisWebMediaPolicy.TryValidateAudioOutput(false, false, true, out string reason), Is.False);
        StringAssert.Contains("Multiple audio outputs", reason);
    }

    [Test]
    public void HierarchyValidationReturnsExplicitWebGlBuildError()
    {
        GameObject root = new GameObject("Web BEE test avatar");
        Material material = null;

        try
        {
            BasisAvatar avatar = root.AddComponent<BasisAvatar>();
            MeshRenderer renderer = new GameObject("Unsupported renderer").AddComponent<MeshRenderer>();
            renderer.transform.SetParent(root.transform);
            Shader shader = Shader.Find("Hidden/VoxelizeShader");
            Assert.That(shader, Is.Not.Null);
            material = new Material(shader);
            renderer.sharedMaterial = material;

            bool isCompatible = BasisWebBeeCompatibilityValidator.TryValidate(
                avatar,
                new[] { BuildTarget.WebGL },
                out string error);

            Assert.That(isCompatible, Is.False);
            StringAssert.Contains("WebGL BEE build failed", error);
            StringAssert.Contains("Shader: Hidden/VoxelizeShader", error);
        }
        finally
        {
            if (material != null)
            {
                Object.DestroyImmediate(material);
            }
            Object.DestroyImmediate(root);
        }
    }
}
