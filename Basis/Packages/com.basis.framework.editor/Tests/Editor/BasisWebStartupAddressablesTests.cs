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
}
