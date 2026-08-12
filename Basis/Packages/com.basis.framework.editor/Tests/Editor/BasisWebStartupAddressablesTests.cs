using System.IO;
using NUnit.Framework;

public class BasisWebStartupAddressablesTests
{
    [TestCase("Packages/com.basis.framework/BasisUI/Localization/BasisLocalization.cs")]
    [TestCase("Packages/com.basis.framework/BasisUI/Localization/BasisTMPFontFallbacks.cs")]
    [TestCase("Packages/com.basis.framework/Avatar/BasisAvatarFactory.cs")]
    [TestCase("Packages/com.basis.framework/Players/Common/BasisPlayerFactory.cs")]
    [TestCase("Packages/com.basis.framework/Drivers/Local/BasisLocalAvatarDriver.cs")]
    [TestCase("Packages/com.basis.openlipsync/Runtime/BasisOpenLipSyncDriver.cs")]
    [TestCase("Packages/com.basis.framework/UI/NamePlate/BasisRemoteNamePlateDriver.cs")]
    [TestCase("Packages/com.basis.framework/UI/BasisUIRaycast.cs")]
    [TestCase("Packages/com.basis.framework/Interactions/BasisPlayerInteract.cs")]
    [TestCase("Packages/com.basis.framework/BasisUI/Menus/Library/EmbeddedItems.cs")]
    [TestCase("Packages/com.basis.sdk/UiStyling/Runtime/Components/UiStyleSettings.cs")]
    public void StartupAddressablesDoNotBlock(string sourcePath)
    {
        string source = File.ReadAllText(sourcePath);

        StringAssert.DoesNotContain("WaitForCompletion", source);
    }

    [Test]
    public void DeviceInitializationAwaitsStartupAssets()
    {
        string source = File.ReadAllText("Packages/com.basis.framework/Device Management/BasisDeviceManagement.cs");

        StringAssert.Contains("await Basis.BasisUI.BasisLocalization.InitializeAsync()", source);
        StringAssert.Contains("await Basis.BasisUI.BasisTMPFontFallbacks.InitializeAsync()", source);
        StringAssert.Contains("await BasisAvatarFactory.InitializeAsync()", source);
        StringAssert.Contains("await BasisPlayerFactory.InitializeAsync()", source);
        StringAssert.Contains("await BasisRemoteNamePlateDriver.InitializeAsync()", source);
        StringAssert.Contains("await Basis.Scripts.UI.BasisUIRaycast.InitializeAssetsAsync()", source);
    }

    [Test]
    public void WebMicrophoneAvoidsUnsupportedTypeInitializers()
    {
        string source = File.ReadAllText("Packages/com.basis.framework/Drivers/Local/BasisLocalMicrophoneDriver.cs");

        StringAssert.DoesNotContain("processingEvent = new AutoResetEvent", source);
        StringAssert.DoesNotContain("Denoiser = new RNNoise.NET.Denoiser", source);
        StringAssert.Contains("#if UNITY_WEBGL && !UNITY_EDITOR", source);
    }

    [Test]
    public void WebFontFallbacksDoNotProbeOperatingSystemFonts()
    {
        string source = File.ReadAllText("Packages/com.basis.framework/BasisUI/Localization/BasisTMPFontFallbacks.cs");

        StringAssert.Contains("if (group.Label != JaJpLabel)", source);
    }

    [Test]
    public void WebDiscCacheDoesNotScheduleThreadPoolWork()
    {
        string source = File.ReadAllText("Packages/com.basis.bundlemanagement/BasisLoadhandler.cs");

        StringAssert.Contains("#if UNITY_WEBGL && !UNITY_EDITOR", source);
        StringAssert.Contains("return Task.FromResult((false, new BasisBEEExtensionMeta()))", source);
    }

    [Test]
    public void WebBootDoesNotInitializeUnityAnalytics()
    {
        string source = File.ReadAllText("Packages/com.basis.framework/Device Management/Boot Sequence/BasisBootSequence.cs");

        StringAssert.Contains("#if !UNITY_WEBGL || UNITY_EDITOR", source);
        StringAssert.Contains("await UnityServices.InitializeAsync()", source);
    }

    [Test]
    public void ItemKeysAwaitEmbeddedCatalogInitialization()
    {
        string source = File.ReadAllText("Packages/com.basis.framework/UI Panels/BasisDataStoreItemKeys.cs");

        StringAssert.Contains("await BasisUI.EmbeddedItems.InitializeAsync()", source);
    }

    [Test]
    public void WebUiStyleInitializationAwaitsAddressables()
    {
        string source = File.ReadAllText("Packages/com.basis.framework/BasisUI/Menus/Main Menu Providers/SettingsProviderParts/SettingsProviderUIStyle.cs");

        StringAssert.Contains("await UiStyleSettings.InitializeAsync()", source);
    }

    [Test]
    public void WebInputBindingsAvoidAsyncFileOperations()
    {
        string source = File.ReadAllText("Packages/com.basis.framework/Device Management/Devices/Base/BasisActionDriver.cs");

        StringAssert.Contains("#if UNITY_WEBGL && !UNITY_EDITOR", source);
        StringAssert.Contains("string json = File.ReadAllText(SavePath)", source);
        StringAssert.Contains("File.WriteAllText(SavePath, json)", source);
    }
}
