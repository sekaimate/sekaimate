using System;
using System.IO;
using NUnit.Framework;
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
