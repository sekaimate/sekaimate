using System.IO;
using NUnit.Framework;

public sealed class BasisWebLibraryE2EHarnessTests
{
    private const string HarnessPath = "Packages/com.basis.framework/BasisUI/Menus/Library/WebGL/BasisWebLibraryE2EHarness.cs";

    [Test]
    public void ImportsNetworkAndItemStoreRuntimeContracts()
    {
        string source = File.ReadAllText(HarnessPath);

        StringAssert.Contains("using Basis.Scripts.Networking;", source);
        StringAssert.Contains("using Basis.Scripts.UI.UI_Panels;", source);
        StringAssert.Contains("BasisNetworkConnection.LocalPlayerIsConnected", source);
        StringAssert.Contains("BasisDataStoreItemKeys.DisplayKeys()", source);
    }
}
