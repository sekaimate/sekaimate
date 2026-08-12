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
    }

    [Test]
    public void CameraSettingsUseIndexedDbPersistenceWithoutAddingImportOrExport()
    {
        string source = File.ReadAllText(CameraUiPath);

        StringAssert.Contains("File.WriteAllText(path, json);", source);
        StringAssert.Contains("string json = File.ReadAllText(path);", source);
        StringAssert.Contains("await BasisWebPersistence.FlushAsync();", source);
        StringAssert.Contains("await File.WriteAllTextAsync(path, json);", source);
        StringAssert.Contains("string json = await File.ReadAllTextAsync(path);", source);
        StringAssert.DoesNotContain("BasisWebFileDownload", source);
    }
}
