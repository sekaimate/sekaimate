using System.IO;
using NUnit.Framework;

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
}
