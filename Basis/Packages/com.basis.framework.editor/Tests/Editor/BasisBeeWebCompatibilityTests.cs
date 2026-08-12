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
    [TestCase("BasisMediaPlayer")]
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

        Assert.That(errors, Is.EqualTo(new[]
        {
            "Component: UnityEngine.VFX.VFXRenderer",
            "Component: BasisMediaPlayer",
            "Shader: Hidden/VoxelizeShader",
        }));
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
