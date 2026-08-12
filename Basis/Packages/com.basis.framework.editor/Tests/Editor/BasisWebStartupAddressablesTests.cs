using System.IO;
using NUnit.Framework;

public class BasisWebStartupAddressablesTests
{
    [TestCase("Packages/com.basis.framework/BasisUI/Localization/BasisLocalization.cs")]
    [TestCase("Packages/com.basis.framework/BasisUI/Localization/BasisTMPFontFallbacks.cs")]
    [TestCase("Packages/com.basis.framework/Avatar/BasisAvatarFactory.cs")]
    [TestCase("Packages/com.basis.framework/Players/Common/BasisPlayerFactory.cs")]
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
    }
}
