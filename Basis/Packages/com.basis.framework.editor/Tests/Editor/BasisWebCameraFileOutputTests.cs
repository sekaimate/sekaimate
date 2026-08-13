using System.IO;
using NUnit.Framework;
using UnityEditor;

public class BasisWebCameraFileOutputTests
{
    private const string BrowserPluginPath = "Packages/com.basis.framework/Platform/WebGL/BasisWebFileDownload.jslib";
    private const string BridgePath = "Packages/com.basis.framework/Platform/WebGL/BasisWebFileDownload.cs";

    [Test]
    public void BrowserDownloadPluginIsEnabledOnlyForWebGl()
    {
        PluginImporter importer = AssetImporter.GetAtPath(BrowserPluginPath) as PluginImporter;

        Assert.That(importer, Is.Not.Null);
        Assert.That(importer.GetCompatibleWithAnyPlatform(), Is.False);
        Assert.That(importer.GetCompatibleWithEditor(), Is.False);
        Assert.That(importer.GetCompatibleWithPlatform(BuildTarget.WebGL), Is.True);
    }

    [Test]
    public void BrowserDownloadCopiesBytesIntoBlobAndRevokesObjectUrl()
    {
        string source = File.ReadAllText(BrowserPluginPath);

        StringAssert.Contains("HEAPU8.slice", source);
        StringAssert.Contains("new Blob", source);
        StringAssert.Contains("anchor.download", source);
        StringAssert.Contains("anchor.click()", source);
        StringAssert.Contains("URL.revokeObjectURL", source);
    }

    [Test]
    public void BrowserBridgeIsCompiledOnlyForWebGlPlayer()
    {
        string source = File.ReadAllText(BridgePath);

        StringAssert.Contains("#if UNITY_WEBGL && !UNITY_EDITOR", source);
        StringAssert.Contains("[DllImport(\"__Internal\")]", source);
        StringAssert.Contains("BasisWebDownloadFile", source);
    }

    [Test]
    public void CameraBrowserE2EProbeIsDevelopmentBuildAndUrlOptInOnly()
    {
        const string probePath = "Packages/com.basis.framework/Platform/WebGL/BasisWebCameraE2EProbe.cs";
        const string diagnosticsPath = "Packages/com.basis.framework/Platform/WebGL/BasisWebCameraE2E.jslib";
        string probe = File.ReadAllText(probePath);
        string diagnostics = File.ReadAllText(diagnosticsPath);

        StringAssert.Contains("UNITY_WEBGL && !UNITY_EDITOR && DEVELOPMENT_BUILD", probe);
        StringAssert.Contains("basisCameraE2E", probe);
        StringAssert.Contains("CaptureFlat", probe);
        StringAssert.Contains("CapturePanorama", probe);
        StringAssert.Contains("BasisWebFileDownload.Save", probe);
        StringAssert.Contains("var equirectDescriptor = new RenderTextureDescriptor", probe);
        StringAssert.Contains("new RenderTexture(equirectDescriptor)", probe);
        StringAssert.Contains("window.basisCameraE2E", diagnostics);
    }

}
