using System.IO;
using NUnit.Framework;
using UnityEditor;

public class BasisWebCameraFileOutputTests
{
    private const string BrowserPluginPath = "Packages/com.basis.framework/Platform/WebGL/BasisWebFileDownload.jslib";
    private const string BridgePath = "Packages/com.basis.framework/Platform/WebGL/BasisWebFileDownload.cs";
    private const string CameraPath = "Packages/com.basis.framework/Camera/BasisHandHeldCamera.cs";
    private const string Camera360Path = "Packages/com.basis.framework/Camera/BasisHandHeldCamera360.cs";
    private const string CameraUiPath = "Packages/com.basis.framework/Camera/BasisHandHeldCameraUI.cs";

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
    public void FlatAndPanoramicPhotosDownloadInBrowserAndKeepNativeFileWrites()
    {
        string camera = File.ReadAllText(CameraPath);
        string camera360 = File.ReadAllText(Camera360Path);

        StringAssert.Contains("BasisWebFileDownload.Save(filename, imageData, contentType);", camera);
        StringAssert.Contains("await File.WriteAllBytesAsync(path, imageData);", camera);
        StringAssert.Contains("BasisWebFileDownload.Save(filename, imageData, contentType);", camera360);
        StringAssert.Contains("await File.WriteAllBytesAsync(path, imageData);", camera360);
        StringAssert.Contains(
            "#if UNITY_WEBGL && !UNITY_EDITOR\n            byte[] rgba = TonemapEquirectToRgba32",
            camera360);
        StringAssert.Contains("await Task.Run(() => TonemapEquirectToRgba32", camera360);
    }

    [Test]
    public void WebGlCaptureReadsRenderedPixelsWithoutRequiringAsyncGpuReadback()
    {
        string camera = File.ReadAllText(CameraPath);
        string camera360 = File.ReadAllText(Camera360Path);

        StringAssert.Contains("#if UNITY_WEBGL && !UNITY_EDITOR", camera);
        StringAssert.Contains("pooledScreenshot.ReadPixels", camera);
        StringAssert.Contains("#if UNITY_WEBGL && !UNITY_EDITOR", camera360);
        StringAssert.Contains("readbackTexture.ReadPixels", camera360);
        StringAssert.Contains("#else\n        AsyncGPUReadback.Request", camera);
        StringAssert.Contains("#else\n        AsyncGPUReadback.Request", camera360);
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
        StringAssert.Contains("window.basisCameraE2E", diagnostics);
    }

    [Test]
    public void CameraSettingsUseIndexedDbPersistenceWithoutAddingImportOrExport()
    {
        string source = File.ReadAllText(CameraUiPath);

        StringAssert.Contains("File.WriteAllText(path, json);", source);
        StringAssert.Contains("string json = File.ReadAllText(path);", source);
        StringAssert.Contains("await File.WriteAllTextAsync(path, json);", source);
        StringAssert.Contains("string json = await File.ReadAllTextAsync(path);", source);
        StringAssert.DoesNotContain("BasisWebFileDownload", source);
    }
}
