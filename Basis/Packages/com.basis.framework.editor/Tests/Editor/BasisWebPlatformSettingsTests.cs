using System;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public class BasisWebPlatformSettingsTests
{
    private static readonly string[] OpenVrWindowsLibraryPaths =
    {
        "Packages/com.valvesoftware.unity.openvr/Runtime/x86/openvr_api.lib",
        "Packages/com.valvesoftware.unity.openvr/Runtime/x86/XRSDKOpenVR.lib",
        "Packages/com.valvesoftware.unity.openvr/Runtime/x64/openvr_api.lib",
        "Packages/com.valvesoftware.unity.openvr/Runtime/x64/XRSDKOpenVR.lib",
    };

    [TestCaseSource(nameof(OpenVrWindowsLibraryPaths))]
    public void WindowsOpenVrLibrariesAreExcludedFromWebGl(string pluginPath)
    {
        PluginImporter importer = AssetImporter.GetAtPath(pluginPath) as PluginImporter;

        Assert.That(importer, Is.Not.Null, pluginPath);
        Assert.That(importer.GetCompatibleWithPlatform(BuildTarget.WebGL), Is.False, pluginPath);
    }

    [TestCase("Packages/com.steam.steamvr/SteamVR/SteamVR.asmdef")]
    [TestCase("Packages/com.steam.steamvr/SteamVR_Input/SteamVR_Actions.asmdef")]
    [TestCase("Packages/com.basis.openvr/BasisOpenVR.asmdef")]
    [TestCase("Packages/com.github.homuler.mediapipe/Runtime/Mediapipe.Runtime.asmdef")]
    [TestCase("Packages/com.basis.mediapipe/Runtime/Homuler/BasisMediaPipe.Homuler.asmdef")]
    public void UnsupportedNativeAssemblyExcludesWebGl(string assemblyDefinitionPath)
    {
        string json = File.ReadAllText(assemblyDefinitionPath);
        AssemblyDefinitionSettings settings = JsonUtility.FromJson<AssemblyDefinitionSettings>(json);

        Assert.That(settings.excludePlatforms, Does.Contain("WebGL"));
    }

    [TestCase("Packages/com.steam.steamaudio/Binaries/HTML5/libmysofa.a")]
    [TestCase("Packages/com.steam.steamaudio/Binaries/HTML5/libpffft.a")]
    [TestCase("Packages/com.steam.steamaudio/Binaries/HTML5/libz.a")]
    [TestCase("Packages/com.llealloo.audiolink/Runtime/Plugins/WebGL/WebALPeer.jslib")]
    public void NativeWebDependencyIsEnabledForWebGl(string pluginPath)
    {
        PluginImporter importer = AssetImporter.GetAtPath(pluginPath) as PluginImporter;

        Assert.That(importer, Is.Not.Null);
        Assert.That(importer.GetCompatibleWithPlatform(BuildTarget.WebGL), Is.True);
    }

    [Test]
    public void WebQualityLevelUsesWebRenderPipeline()
    {
        const string pipelinePath = "Assets/Basis/Settings/Quality Settiings/Modified - Web.asset";
        int webQualityLevel = Array.IndexOf(QualitySettings.names, "WEB");
        RenderPipelineAsset expectedPipeline = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>(pipelinePath);

        Assert.That(webQualityLevel, Is.GreaterThanOrEqualTo(0));
        Assert.That(expectedPipeline, Is.Not.Null);
        Assert.That(QualitySettings.GetRenderPipelineAssetAt(webQualityLevel), Is.SameAs(expectedPipeline));
    }

    [Test]
    public void QualityLevelsAreLimitedToTheirPlatforms()
    {
        int desktopQualityLevel = Array.IndexOf(QualitySettings.names, "DESKTOP");
        int webQualityLevel = Array.IndexOf(QualitySettings.names, "WEB");

        Assert.That(QualitySettings.IsPlatformIncluded("Standalone", desktopQualityLevel), Is.True);
        Assert.That(QualitySettings.IsPlatformIncluded("WebGL", desktopQualityLevel), Is.False);
        Assert.That(QualitySettings.IsPlatformIncluded("Standalone", webQualityLevel), Is.False);
        Assert.That(QualitySettings.IsPlatformIncluded("WebGL", webQualityLevel), Is.True);
    }

    [Test]
    public void GlobalGraphicsSettingsRemainDesktopDefaults()
    {
        const string pipelinePath = "Assets/Basis/Settings/Quality Settiings/Modified - Desktop.asset";
        RenderPipelineAsset expectedPipeline = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>(pipelinePath);
        SerializedObject graphicsSettings = new SerializedObject(GraphicsSettings.GetGraphicsSettings());

        Assert.That(GraphicsSettings.defaultRenderPipeline, Is.SameAs(expectedPipeline));
        Assert.That(graphicsSettings.FindProperty("m_BrgStripping").intValue, Is.EqualTo(2));
    }

    [TestCase("Universal Render Pipeline/Lit", false)]
    [TestCase("Jiggle/ProceduralPrimitiveURP", true)]
    [TestCase("Hidden/VoxelizeShader", true)]
    public void WebShaderStripperRemovesUnsupportedVariants(string shaderName, bool expected)
    {
        Assert.That(BasisWebShaderStripper.ShouldStrip(shaderName), Is.EqualTo(expected));
    }

    [Serializable]
    private sealed class AssemblyDefinitionSettings
    {
        public string[] excludePlatforms;
    }
}
