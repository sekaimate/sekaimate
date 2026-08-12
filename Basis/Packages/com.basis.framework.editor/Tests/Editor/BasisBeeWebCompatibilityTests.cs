using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;

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

        Assert.That(errors, Has.Exactly(1).Contains(shaderName));
    }

    [TestCase("UnityEngine.VFX.VisualEffect")]
    [TestCase("UnityEngine.VFX.VFXRenderer")]
    [TestCase("BasisMediaPlayer")]
    public void WebGlRejectsUnsupportedComponents(string componentTypeName)
    {
        string[] errors = BasisWebBeeCompatibilityValidator.FindUnsupportedContent(
            new[] { BuildTarget.WebGL },
            new[] { componentTypeName },
            new string[0]);

        Assert.That(errors, Has.Exactly(1).Contains(componentTypeName));
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
            new[] { "BasisMediaPlayer", "UnityEngine.VFX.VisualEffect" },
            new[] { "Jiggle/ProceduralPrimitiveURP", "Hidden/VoxelizeShader" });

        Assert.That(errors, Is.Empty);
    }

    [Test]
    public void WebGlReportsEveryUnsupportedContentCategory()
    {
        string[] errors = BasisWebBeeCompatibilityValidator.FindUnsupportedContent(
            new[] { BuildTarget.StandaloneWindows64, BuildTarget.WebGL },
            new[] { "BasisMediaPlayer", "UnityEngine.VFX.VFXRenderer" },
            new[] { "Hidden/VoxelizeShader" });

        Assert.That(errors, Has.Count.EqualTo(3));
        Assert.That(errors, Has.Exactly(1).Contains("BasisMediaPlayer"));
        Assert.That(errors, Has.Exactly(1).Contains("UnityEngine.VFX.VFXRenderer"));
        Assert.That(errors, Has.Exactly(1).Contains("Hidden/VoxelizeShader"));
    }
}
