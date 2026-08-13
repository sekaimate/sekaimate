using System.IO;
using NUnit.Framework;

public class BasisWebSettingsE2EHarnessTests
{
    private const string HarnessPath = "Packages/com.basis.framework/Platform/WebGL/BasisWebSettingsE2EHarness.cs";

    [Test]
    public void ImportsPermissionAndSettingsRuntimeContracts()
    {
        string source = File.ReadAllText(HarnessPath);

        StringAssert.Contains("using BasisPermissions;", source);
        StringAssert.Contains("using Basis.Scripts.Settings;", source);
    }

    [Test]
    public void YieldsOnlyAfterTheProtectedTabClickCompletes()
    {
        string source = File.ReadAllText(HarnessPath);
        int clickIndex = source.IndexOf("settingsTabs.SelectionButtons[tab.index].OnClicked?.Invoke();", System.StringComparison.Ordinal);
        int catchIndex = source.IndexOf("catch (Exception exception)", clickIndex, System.StringComparison.Ordinal);
        int yieldIndex = source.IndexOf("yield return null;", clickIndex, System.StringComparison.Ordinal);

        Assert.That(clickIndex, Is.GreaterThanOrEqualTo(0));
        Assert.That(catchIndex, Is.GreaterThan(clickIndex));
        Assert.That(yieldIndex, Is.GreaterThan(catchIndex));
    }
}
