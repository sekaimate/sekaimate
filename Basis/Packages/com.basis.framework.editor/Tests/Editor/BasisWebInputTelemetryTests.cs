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
        StringAssert.Contains("basisInputTelemetry=1", source);
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
    public void BrowserTelemetryPublishesAReadOnlySnapshot()
    {
        string source = File.ReadAllText(BrowserPluginPath);

        StringAssert.Contains("window.BasisWebInputTelemetry", source);
        StringAssert.Contains("Object.freeze", source);
        StringAssert.Contains("UTF8ToString", source);
        StringAssert.DoesNotContain("SendMessage", source);
    }
}
