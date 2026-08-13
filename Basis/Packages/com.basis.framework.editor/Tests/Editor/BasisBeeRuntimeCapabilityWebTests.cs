using System.IO;
using NUnit.Framework;
using UnityEditor;

public class BasisBeeRuntimeCapabilityWebTests
{
    private const string ProbePath = "Packages/com.basis.framework/Platform/WebGL/BasisWebBeeRuntimeCapabilityProbe.cs";
    private const string PluginPath = "Packages/com.basis.framework/Platform/WebGL/BasisWebBeeRuntimeCapability.jslib";

    [Test]
    public void BrowserProbeIsDevelopmentWebGlAndUrlOptInOnly()
    {
        string source = File.ReadAllText(ProbePath);

        StringAssert.StartsWith("#if UNITY_WEBGL && !UNITY_EDITOR && DEVELOPMENT_BUILD", source);
        StringAssert.Contains("basisBeeRuntimeE2E", source);
        StringAssert.Contains("Application.absoluteURL", source);
        StringAssert.Contains("BasisRuntimeCapability-", source);
    }

    [Test]
    public void BrowserPluginPublishesFormatSpecificSnapshots()
    {
        PluginImporter importer = AssetImporter.GetAtPath(PluginPath) as PluginImporter;
        Assert.That(importer, Is.Not.Null);
        Assert.That(importer.GetCompatibleWithPlatform(BuildTarget.WebGL), Is.True);
        Assert.That(importer.GetCompatibleWithPlatform(BuildTarget.StandaloneOSX), Is.False);

        string source = File.ReadAllText(PluginPath);
        StringAssert.Contains("globalThis.BasisBeeRuntimeCapabilityDiagnostics", source);
        StringAssert.Contains("snapshots", source);
        StringAssert.Contains("format", source);
    }
}
