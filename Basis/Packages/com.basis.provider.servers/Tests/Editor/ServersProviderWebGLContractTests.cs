using System.IO;
using NUnit.Framework;

public sealed class ServersProviderWebGLContractTests
{
    private const string ProviderPath = "Packages/com.basis.provider.servers/Runtime/ServersProvider.cs";
    private const string ProviderHarnessPath = "Packages/com.basis.provider.servers/Runtime/ServersProvider.WebE2E.cs";
    private const string HarnessPath = "Packages/com.basis.provider.servers/Runtime/WebGL/BasisWebServersUIE2EHarness.cs";
    private const string BridgePath = "Packages/com.basis.provider.servers/Runtime/WebGL/BasisWebServersUIE2E.jslib";

    [Test]
    public void WebGLServersPanelDoesNotOfferUnsupportedHostMode()
    {
        string source = File.ReadAllText(ProviderPath);

        StringAssert.Contains("#if !UNITY_WEBGL || UNITY_EDITOR", source);
        StringAssert.Contains("BuildHostSection", source);
        StringAssert.Contains("BuildAutoConnectSection", source);
    }

    [Test]
    public void DevelopmentHarnessOperatesTheProductionServersPanelControls()
    {
        string provider = File.ReadAllText(ProviderHarnessPath);
        string harness = File.ReadAllText(HarnessPath);

        StringAssert.Contains("DEVELOPMENT_BUILD", harness);
        StringAssert.Contains("BasisMainMenu.OpenWithProvider", harness);
        StringAssert.Contains("ServersProvider.ActiveInstance", harness);
        StringAssert.Contains("E2EClickAddServer", provider);
        StringAssert.Contains("E2EClickRefreshAll", provider);
        StringAssert.Contains("E2EClickConnect", provider);
        StringAssert.Contains("E2EClickEdit", provider);
        StringAssert.Contains("E2EClickRemove", provider);
        StringAssert.Contains("E2EConfirmRemove", provider);
        int entryStateStart = provider.IndexOf("private sealed class E2EEntryState", System.StringComparison.Ordinal);
        int stateStart = provider.IndexOf("private sealed class E2EState", entryStateStart, System.StringComparison.Ordinal);
        string entryState = provider.Substring(entryStateStart, stateStart - entryStateStart);
        StringAssert.Contains("public bool connectable;", entryState);
    }

    [Test]
    public void BrowserBridgeExposesStateAndCommandsWithoutOpeningItsOwnSocket()
    {
        string bridge = File.ReadAllText(BridgePath);

        StringAssert.Contains("basisServersUIE2E", bridge);
        StringAssert.Contains("Module.SendMessage", bridge);
        StringAssert.DoesNotContain("new WebSocket", bridge);
    }
}
