using System.IO;
using NUnit.Framework;
using UnityEditor;

public class BasisWebPointerLockTests
{
    private const string BrowserPluginPath = "Packages/com.basis.framework/Platform/WebGL/BasisWebPointerLock.jslib";
    private const string CursorManagementPath = "Packages/com.basis.framework/Device Management/Devices/Base/BasisCursorManagement.cs";
    private const string BridgePath = "Packages/com.basis.framework/Platform/WebGL/BasisWebPointerLockBridge.cs";

    [Test]
    public void PointerLockBrowserPluginIsEnabledOnlyForWebGl()
    {
        PluginImporter importer = AssetImporter.GetAtPath(BrowserPluginPath) as PluginImporter;

        Assert.That(importer, Is.Not.Null);
        Assert.That(importer.GetCompatibleWithAnyPlatform(), Is.False);
        Assert.That(importer.GetCompatibleWithEditor(), Is.False);
        Assert.That(importer.GetCompatibleWithPlatform(BuildTarget.WebGL), Is.True);
    }

    [Test]
    public void BrowserPluginReportsEveryExternalUnlockPath()
    {
        string source = File.ReadAllText(BrowserPluginPath);

        StringAssert.Contains("pointerlockchange", source);
        StringAssert.Contains("pointerlockerror", source);
        StringAssert.Contains("visibilitychange", source);
        StringAssert.Contains("blur", source);
        StringAssert.Contains("document.pointerLockElement", source);
    }

    [Test]
    public void LockRequestApiRequiresUserGestureAtTheCallSite()
    {
        string source = File.ReadAllText(CursorManagementPath);

        StringAssert.Contains("RequestPointerLockFromUserGesture", source);
    }

    [Test]
    public void BrowserInteropExistsOnlyInWebGlPlayer()
    {
        string source = File.ReadAllText(BridgePath);

        StringAssert.Contains("#if UNITY_WEBGL && !UNITY_EDITOR", source);
        StringAssert.Contains("DllImport(\"__Internal\")", source);
        StringAssert.DoesNotContain("Cursor.lockState", source);
    }
}
