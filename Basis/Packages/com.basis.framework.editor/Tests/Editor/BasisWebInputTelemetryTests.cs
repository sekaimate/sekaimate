using System.IO;
using NUnit.Framework;
using UnityEditor;

public class BasisWebInputTelemetryTests
{
    private const string BrowserPluginPath = "Packages/com.basis.framework/Platform/WebGL/BasisWebInputTelemetry.jslib";
    private const string TelemetryPath = "Packages/com.basis.framework/Platform/WebGL/BasisWebInputTelemetry.cs";

    [Test]
    public void BrowserTelemetryPluginIsEnabledOnlyForWebGl()
    {
        PluginImporter importer = AssetImporter.GetAtPath(BrowserPluginPath) as PluginImporter;

        Assert.That(importer, Is.Not.Null);
        Assert.That(importer.GetCompatibleWithAnyPlatform(), Is.False);
        Assert.That(importer.GetCompatibleWithEditor(), Is.False);
        Assert.That(importer.GetCompatibleWithPlatform(BuildTarget.WebGL), Is.True);
    }

    [Test]
    public void TelemetryIsOptInAndWebPlayerOnly()
    {
        string source = File.ReadAllText(TelemetryPath);

        StringAssert.Contains("#if UNITY_WEBGL && !UNITY_EDITOR", source);
        StringAssert.Contains("Application.absoluteURL", source);
        StringAssert.Contains("basisInputE2E=1", source);
        StringAssert.Contains("BasisWebInputTelemetryPublish", source);
    }

    [Test]
    public void TelemetryObservesTheProductionInputConsumers()
    {
        string source = File.ReadAllText(TelemetryPath);

        StringAssert.Contains("BasisLocalInputActions", source);
        StringAssert.Contains("BasisLocalCharacterDriver", source);
        StringAssert.Contains("BasisDesktopEye", source);
        StringAssert.Contains("BasisCursorManagement.ActiveLockState", source);
        StringAssert.Contains("BasisOnScreenControls", source);
        StringAssert.DoesNotContain("InputSystem.Queue", source);
        StringAssert.DoesNotContain("SetMovementVector", source);
        StringAssert.DoesNotContain("SetLookRotationVector", source);
    }

    [Test]
    public void SnapshotKeepsThePlaywrightInputContractStable()
    {
        string source = File.ReadAllText(TelemetryPath);

        StringAssert.Contains("schemaVersion", source);
        StringAssert.Contains("pointerLocked", source);
        StringAssert.Contains("moveAction", source);
        StringAssert.Contains("moveDevice", source);
        StringAssert.Contains("movement", source);
        StringAssert.Contains("playerPosition", source);
        StringAssert.Contains("lookAction", source);
        StringAssert.Contains("lookDevice", source);
        StringAssert.Contains("lookVector", source);
        StringAssert.Contains("lookYaw", source);
        StringAssert.Contains("lookPitch", source);
        StringAssert.Contains("activeTouches", source);
        StringAssert.Contains("onScreenControls", source);
    }

    [Test]
    public void BrowserTelemetryPublishesAReadOnlySnapshot()
    {
        string source = File.ReadAllText(BrowserPluginPath);

        StringAssert.Contains("window.basisInputE2E", source);
        StringAssert.Contains("Object.freeze", source);
        StringAssert.Contains("UTF8ToString", source);
        StringAssert.DoesNotContain("SendMessage", source);
    }
}
