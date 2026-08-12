using System;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public class BasisWebPlatformSettingsTests
{
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
    public void SteamAudioDependencyIsEnabledForWebGl(string pluginPath)
    {
        PluginImporter importer = AssetImporter.GetAtPath(pluginPath) as PluginImporter;

        Assert.That(importer, Is.Not.Null);
        Assert.That(importer.GetCompatibleWithPlatform(BuildTarget.WebGL), Is.True);
    }

    [TestCase("Universal Render Pipeline/Lit", true, true)]
    [TestCase("Universal Render Pipeline/Lit", false, false)]
    [TestCase("Jiggle/ProceduralPrimitiveURP", false, true)]
    public void WebShaderStripperRemovesUnsupportedVariants(string shaderName, bool usesDotsInstancing, bool expected)
    {
        Assert.That(BasisWebShaderStripper.ShouldStrip(shaderName, usesDotsInstancing), Is.EqualTo(expected));
    }

    [Serializable]
    private sealed class AssemblyDefinitionSettings
    {
        public string[] excludePlatforms;
    }
}
