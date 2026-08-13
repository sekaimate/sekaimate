using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;

public class BasisWebPersistenceTests
{
    private const string E2EProbePath =
        "Packages/com.basis.framework/Platform/WebGL/BasisWebPersistenceE2EProbe.cs";
    private const string E2EPluginPath =
        "Packages/com.basis.framework/Platform/WebGL/BasisWebPersistenceE2E.jslib";
    private const string E2ESpecPath =
        "Tests/WebPersistenceE2E/tests/persistence.spec.ts";

    [Test]
    public void WebPersistenceUsesUnityAutomaticSyncWithoutManualBridge()
    {
        Assert.That(File.Exists("Packages/com.basis.sdk/Plugins/WebGL/BasisWebPersistence.jslib"), Is.False);
        Assert.That(File.Exists("Packages/com.basis.sdk/Scripts/Platform/BasisWebPersistence.cs"), Is.False);
    }

    [Test]
    public void WebBeeFilesFlushAfterMaterialWrites()
    {
        string io = File.ReadAllText("Packages/com.basis.bundlemanagement/BasisIOManagement.cs");
        string metadata = File.ReadAllText("Packages/com.basis.bundlemanagement/BasisLoadhandler.cs");

        StringAssert.Contains(
            "File.Move(tempPath, path);",
            io);
        StringAssert.Contains(
            "File.WriteAllBytes(filePath, serializedData);",
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
            @"#if UNITY_WEBGL && !UNITY_EDITOR\s+(?:try\s+\{\s+)?File\.WriteAllBytes\(FilePath, byteData\);";

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
                @"#if UNITY_WEBGL && !UNITY_EDITOR\s+try\s+\{\s+File\.WriteAllText\(FilePath, doc\.ToString\(\)\);"),
            Is.True);
    }

    [Test]
    public void WebActionBindingsUseSynchronousIo()
    {
        string source = File.ReadAllText(
            "Packages/com.basis.framework/Device Management/Devices/Base/BasisActionDriver.cs");

        StringAssert.Contains(
            "string json = File.ReadAllText(SavePath);\n#else\n            string json = await File.ReadAllTextAsync(SavePath);",
            source);
        StringAssert.Contains(
            "File.WriteAllText(SavePath, json);\n#else\n            await File.WriteAllTextAsync(SavePath, json);",
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
    public void WebBuildUsesStableStandardDpiRendering()
    {
        const string html = "<script>\nvar config = {\n  dataUrl: buildUrl + '/build.data',\n};\n</script>";

        string configured = BasisWebBuildConfiguration.ConfigureGeneratedIndex(BuildTarget.WebGL, html);

        StringAssert.Contains("devicePixelRatio: 1,", configured);
        Assert.That(
            BasisWebBuildConfiguration.ConfigureGeneratedIndex(BuildTarget.WebGL, configured),
            Is.EqualTo(configured));
    }

    [Test]
    public void ExistingDevicePixelRatioIsSetToStandardDpi()
    {
        const string html = "<script>\nvar config = {\n  devicePixelRatio: 2,\n};\n</script>";

        string configured = BasisWebBuildConfiguration.UseStandardDpi(html);

        StringAssert.Contains("devicePixelRatio: 1,", configured);
        StringAssert.DoesNotContain("devicePixelRatio: 2", configured);
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

    [Test]
    public void DevelopmentWebBuildExposesExplicitPersistenceReloadProbe()
    {
        string probe = File.ReadAllText(E2EProbePath);
        string plugin = File.ReadAllText(E2EPluginPath);

        StringAssert.StartsWith("#if UNITY_WEBGL && !UNITY_EDITOR && DEVELOPMENT_BUILD", probe);
        StringAssert.Contains("basisPersistenceE2E", probe);
        StringAssert.Contains("case \"seed\"", probe);
        StringAssert.Contains("case \"verify\"", probe);
        StringAssert.Contains("window.basisPersistenceE2E", plugin);
        StringAssert.DoesNotContain("location.reload", plugin);
    }

    [Test]
    public void PersistenceReloadProbeCoversUserOwnedWebState()
    {
        string probe = File.ReadAllText(E2EProbePath);

        StringAssert.Contains("BasisDataStoreAvatarKeys.AddNewKey", probe);
        StringAssert.Contains("BasisDataStoreItemKeys.AddNewKey", probe);
        StringAssert.Contains("BundledContentHolder.Mode.Prop", probe);
        StringAssert.Contains("BundledContentHolder.Mode.World", probe);
        StringAssert.Contains("BasisActionDriver.SaveFromDriver", probe);
        StringAssert.Contains("WaitForWebDeviceMode", probe);
        StringAssert.Contains("BasisSettingsSystem.SaveString", probe);
        StringAssert.Contains("SavedServerStore.Save", probe);
        StringAssert.Contains("BasisTrustedUrls.Add", probe);
    }

    [Test]
    public void PersistenceReloadProbeVerifiesThroughProductionReaders()
    {
        string probe = File.ReadAllText(E2EProbePath);

        StringAssert.Contains("BasisDataStoreAvatarKeys.LoadKeys", probe);
        StringAssert.Contains("BasisDataStoreItemKeys.LoadKeys", probe);
        StringAssert.Contains("BasisActionDriver.LoadApplyToDriverAsync", probe);
        StringAssert.Contains("BasisSettingsSystem.LoadString", probe);
        StringAssert.Contains("SavedServerStore.Load", probe);
        StringAssert.Contains("BasisTrustedUrls.GetUserAdded", probe);
        StringAssert.Contains("avatar =", probe);
        StringAssert.Contains("prop =", probe);
        StringAssert.Contains("world =", probe);
        StringAssert.Contains("binding =", probe);
        StringAssert.Contains("settings =", probe);
        StringAssert.Contains("savedServers =", probe);
        StringAssert.Contains("trustedUrls =", probe);
    }

    [Test]
    public void PlaywrightPersistenceSpecWaitsForIndexedDbThenReloadsIntoVerifyPhase()
    {
        string spec = File.ReadAllText(E2ESpecPath);

        StringAssert.Contains("basisPersistenceE2E', 'seed'", spec);
        StringAssert.Contains("indexedDB.databases()", spec);
        StringAssert.Contains("KeyStore.json", spec);
        StringAssert.Contains("SavedServers.BAS", spec);
        StringAssert.Contains("trustedUrls.json", spec);
        StringAssert.Contains("basisPersistenceE2E', 'verify'", spec);
        StringAssert.Contains("page.reload()", spec);
        StringAssert.DoesNotContain("waitForTimeout", spec);
    }

    [Test]
    public void PersistenceReloadProbeBrowserPluginIsWebGlOnly()
    {
        PluginImporter importer = AssetImporter.GetAtPath(E2EPluginPath) as PluginImporter;

        Assert.That(importer, Is.Not.Null);
        Assert.That(importer.GetCompatibleWithAnyPlatform(), Is.False);
        Assert.That(importer.GetCompatibleWithEditor(), Is.False);
        Assert.That(importer.GetCompatibleWithPlatform(BuildTarget.WebGL), Is.True);
    }
}
