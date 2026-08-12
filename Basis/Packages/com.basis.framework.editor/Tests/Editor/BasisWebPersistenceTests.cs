using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;

public class BasisWebPersistenceTests
{
    [Test]
    public void BrowserBridgePopulatesAndFlushesIndexedDbFileSystem()
    {
        string plugin = File.ReadAllText("Packages/com.basis.sdk/Plugins/WebGL/BasisWebPersistence.jslib");
        string bridge = File.ReadAllText("Packages/com.basis.sdk/Scripts/Platform/BasisWebPersistence.cs");

        StringAssert.Contains("FS.syncfs(operation.populate", plugin);
        StringAssert.Contains("public static Task EnsureInitializedAsync()", bridge);
        StringAssert.Contains("public static async Task FlushAsync()", bridge);
        StringAssert.Contains("BeginSync(populate: true)", bridge);
        StringAssert.Contains("BeginSync(populate: false)", bridge);
        StringAssert.Contains("[DllImport(\"__Internal\")]", bridge);
    }

    [Test]
    public void WebBootAwaitsPersistentDataBeforeAddressables()
    {
        string source = File.ReadAllText(
            "Packages/com.basis.framework/Device Management/Boot Sequence/BasisBootSequence.cs");

        int persistence = source.IndexOf("await BasisWebPersistence.EnsureInitializedAsync()");
        int addressables = source.IndexOf("Addressables.InitializeAsync(false)");

        Assert.That(persistence, Is.GreaterThanOrEqualTo(0));
        Assert.That(addressables, Is.GreaterThan(persistence));
    }

    [Test]
    public void WebBeeFilesFlushAfterMaterialWrites()
    {
        string io = File.ReadAllText("Packages/com.basis.bundlemanagement/BasisIOManagement.cs");
        string metadata = File.ReadAllText("Packages/com.basis.bundlemanagement/BasisLoadhandler.cs");

        StringAssert.Contains(
            "File.Move(tempPath, path);\n#if UNITY_WEBGL && !UNITY_EDITOR\n            await BasisWebPersistence.FlushAsync();",
            io);
        StringAssert.Contains(
            "File.WriteAllBytes(filePath, serializedData);\n            await BasisWebPersistence.FlushAsync();",
            metadata);
        StringAssert.Contains("await File.WriteAllBytesAsync(filePath, serializedData);", metadata);
    }

    [Test]
    public void WebKeyStoresWriteFinalFilesSynchronouslyBeforeFlush()
    {
        string itemKeys = File.ReadAllText(
            "Packages/com.basis.framework/UI Panels/BasisDataStoreItemKeys.cs");
        string avatarKeys = File.ReadAllText(
            "Packages/com.basis.framework/UI Panels/BasisDataStoreAvatarKeys.cs");

        const string webWritePattern =
            @"#if UNITY_WEBGL && !UNITY_EDITOR\s+(?:try\s+\{\s+)?File\.WriteAllBytes\(FilePath, byteData\);\s+await BasisWebPersistence\.FlushAsync\(\);";

        Assert.That(Regex.IsMatch(itemKeys, webWritePattern), Is.True);
        Assert.That(Regex.IsMatch(avatarKeys, webWritePattern), Is.True);
    }

    [Test]
    public void WebKeyStoresReadFilesSynchronously()
    {
        string itemKeys = File.ReadAllText(
            "Packages/com.basis.framework/UI Panels/BasisDataStoreItemKeys.cs");
        string avatarKeys = File.ReadAllText(
            "Packages/com.basis.framework/UI Panels/BasisDataStoreAvatarKeys.cs");

        StringAssert.Contains(
            "#if UNITY_WEBGL && !UNITY_EDITOR\n                byteData = File.ReadAllBytes(FilePath);",
            itemKeys);
        StringAssert.Contains(
            "#if UNITY_WEBGL && !UNITY_EDITOR\n                byte[] byteData = File.ReadAllBytes(FilePath);",
            avatarKeys);
    }

    [Test]
    public void NativeItemKeyStoreRetainsAtomicAsyncWrite()
    {
        string itemKeys = File.ReadAllText(
            "Packages/com.basis.framework/UI Panels/BasisDataStoreItemKeys.cs");

        StringAssert.Contains("byteData = await File.ReadAllBytesAsync(FilePath);", itemKeys);
        StringAssert.Contains("await File.WriteAllBytesAsync(tempPath, byteData);", itemKeys);
        StringAssert.Contains("File.Replace(tempPath, FilePath, null);", itemKeys);
        StringAssert.Contains("File.Move(tempPath, FilePath);", itemKeys);
    }

    [Test]
    public void WebItemKeyStoreDoesNotEnterNativeSwapBranch()
    {
        string itemKeys = File.ReadAllText(
            "Packages/com.basis.framework/UI Panels/BasisDataStoreItemKeys.cs");

        int webWrite = itemKeys.IndexOf("File.WriteAllBytes(FilePath, byteData);");
        int nativeBranch = itemKeys.IndexOf("#else", webWrite);
        int replace = itemKeys.IndexOf("File.Replace(tempPath, FilePath, null);", nativeBranch);
        int move = itemKeys.IndexOf("File.Move(tempPath, FilePath);", nativeBranch);
        int branchEnd = itemKeys.IndexOf("#endif", nativeBranch);

        Assert.That(webWrite, Is.GreaterThanOrEqualTo(0));
        Assert.That(nativeBranch, Is.GreaterThan(webWrite));
        Assert.That(replace, Is.GreaterThan(nativeBranch));
        Assert.That(move, Is.GreaterThan(replace));
        Assert.That(branchEnd, Is.GreaterThan(move));
    }

    [Test]
    public void NativeAvatarKeyStoreRetainsAsyncWrite()
    {
        string avatarKeys = File.ReadAllText(
            "Packages/com.basis.framework/UI Panels/BasisDataStoreAvatarKeys.cs");

        StringAssert.Contains("byte[] byteData = await File.ReadAllBytesAsync(FilePath);", avatarKeys);
        StringAssert.Contains("await File.WriteAllBytesAsync(FilePath, byteData);", avatarKeys);
    }

    [Test]
    public void WebPreloadContentUsesSynchronousPersistentStorage()
    {
        string source = File.ReadAllText(
            "Packages/com.basis.framework/Resource Management/BasisPreloadContentStore.cs");

        StringAssert.Contains(
            "#if UNITY_WEBGL && !UNITY_EDITOR\n            text = File.ReadAllText(FilePath);\n#else\n            text = await File.ReadAllTextAsync(FilePath);\n#endif",
            source);
        Assert.That(
            Regex.IsMatch(
                source,
                @"#if UNITY_WEBGL && !UNITY_EDITOR\s+try\s+\{\s+File\.WriteAllText\(FilePath, doc\.ToString\(\)\);\s+await BasisWebPersistence\.FlushAsync\(\);"),
            Is.True);
    }

    [Test]
    public void WebActionBindingsUseSynchronousIoAndFlushWrites()
    {
        string source = File.ReadAllText(
            "Packages/com.basis.framework/Device Management/Devices/Base/BasisActionDriver.cs");

        StringAssert.Contains(
            "string json = File.ReadAllText(SavePath);\n#else\n            string json = await File.ReadAllTextAsync(SavePath);",
            source);
        StringAssert.Contains(
            "File.WriteAllText(SavePath, json);\n            await BasisWebPersistence.FlushAsync();\n#else\n            await File.WriteAllTextAsync(SavePath, json);",
            source);
    }

    [Test]
    public void NativePreloadContentKeepsAsynchronousAtomicWrite()
    {
        string source = File.ReadAllText(
            "Packages/com.basis.framework/Resource Management/BasisPreloadContentStore.cs");

        StringAssert.Contains("await File.WriteAllTextAsync(tempPath, doc.ToString());", source);
        StringAssert.Contains("File.Replace(tempPath, FilePath, null);", source);
        StringAssert.Contains("File.Move(tempPath, FilePath);", source);
    }

    [Test]
    public void WebBuildEnablesAutomaticPersistentDataSync()
    {
        const string html = "<script>\nvar config = {\n  dataUrl: buildUrl + '/build.data',\n};\n</script>";

        string configured = BasisWebBuildConfiguration.AddAutomaticPersistentDataSync(html);

        StringAssert.Contains("autoSyncPersistentDataPath: true,", configured);
        Assert.That(
            BasisWebBuildConfiguration.AddAutomaticPersistentDataSync(configured),
            Is.EqualTo(configured));
    }

    [Test]
    public void NativeBuildDoesNotModifyGeneratedIndex()
    {
        const string html = "<script>\nvar config = {\n  dataUrl: buildUrl + '/build.data',\n};\n</script>";

        string configured = BasisWebBuildConfiguration.ConfigureGeneratedIndex(BuildTarget.StandaloneOSX, html);

        Assert.That(configured, Is.EqualTo(html));
    }

    [Test]
    public void ExistingDisabledAutomaticSyncIsEnabled()
    {
        const string html = "<script>\nvar config = {\n  autoSyncPersistentDataPath: false,\n};\n</script>";

        string configured = BasisWebBuildConfiguration.AddAutomaticPersistentDataSync(html);

        StringAssert.Contains("autoSyncPersistentDataPath: true,", configured);
        StringAssert.DoesNotContain("autoSyncPersistentDataPath: false", configured);
    }
}
