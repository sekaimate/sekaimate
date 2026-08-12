using System.IO;
using NUnit.Framework;

public class BasisWebSecondaryAddressablesTests
{
    [Test]
    public void WebVirtualKeyboardLoadsItsLayoutAsynchronously()
    {
        string keyboardSource = File.ReadAllText(
            "Packages/com.basis.framework/BasisUI/BasisMenuVirtualKeyboardPanel.cs");
        string inputSource = File.ReadAllText(
            "Packages/com.basis.framework/UI/BasisInputModuleHandler.cs");

        StringAssert.Contains("#if UNITY_WEBGL && !UNITY_EDITOR", keyboardSource);
        StringAssert.Contains("LoadDefaultLayoutAsync", keyboardSource);
        StringAssert.Contains("await handle.Task", keyboardSource);
        StringAssert.Contains("#else", keyboardSource);
        StringAssert.Contains("WaitForCompletion", keyboardSource);
        StringAssert.Contains("BasisMenuVirtualKeyboardPanel.CreateNewAsync", inputSource);
    }

    [Test]
    public void WebTrustedUrlsAreInitializedBeforeDeviceStartupCompletes()
    {
        string trustedUrlsSource = File.ReadAllText(
            "Packages/com.basis.framework/BasisUI/BasisTrustedUrls.cs");
        string deviceSource = File.ReadAllText(
            "Packages/com.basis.framework/Device Management/BasisDeviceManagement.cs");

        StringAssert.Contains("public static async Task InitializeAsync()", trustedUrlsSource);
        StringAssert.Contains("await handle.Task", trustedUrlsSource);
        StringAssert.Contains("#else", trustedUrlsSource);
        StringAssert.Contains("WaitForCompletion", trustedUrlsSource);
        StringAssert.Contains("await Basis.BasisUI.BasisTrustedUrls.InitializeAsync()", deviceSource);
    }

    [Test]
    public void WebThirdPartyLicensesLoadAsynchronously()
    {
        string source = File.ReadAllText(
            "Packages/dev.hai-vr.hvr.license-review/Scripts/Runtime/SettingsProviderThirdPartyLicenses.cs");

        StringAssert.Contains("#if UNITY_WEBGL && !UNITY_EDITOR", source);
        StringAssert.Contains("await handle.Task", source);
        StringAssert.Contains("#else", source);
        StringAssert.Contains("WaitForCompletion", source);
    }
}
