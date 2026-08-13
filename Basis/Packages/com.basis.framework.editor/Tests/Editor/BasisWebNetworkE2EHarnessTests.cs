using System.IO;
using NUnit.Framework;

public sealed class BasisWebNetworkE2EHarnessTests
{
    [Test]
    public void RuntimeHarnessIsRestrictedToDevelopmentWebGlPlayers()
    {
        string source = File.ReadAllText("Packages/com.basis.framework/Networking/WebGL/BasisWebNetworkE2EHarness.cs");

        StringAssert.StartsWith("#if UNITY_WEBGL && !UNITY_EDITOR && DEVELOPMENT_BUILD", source);
        StringAssert.Contains("Application.absoluteURL", source);
        StringAssert.Contains("basisNetworkE2E", source);
    }

    [Test]
    public void RuntimeHarnessUsesProductionConnectionChatAndStatePaths()
    {
        string source = File.ReadAllText("Packages/com.basis.framework/Networking/WebGL/BasisWebNetworkE2EHarness.cs");

        StringAssert.Contains("BasisConnectionService.ConnectAsync", source);
        StringAssert.Contains("BasisNetworkHandleChat.SendChatMessage", source);
        StringAssert.Contains("BasisNetworkHandleChat.OnChatMessageReceived", source);
        StringAssert.Contains("BasisNetworkConnection.LocalPlayerIsConnected", source);
        StringAssert.Contains("BasisNetworkPlayers.RemotePlayers.Count", source);
        StringAssert.Contains("BasisNetworkManagement.Transmitter", source);
    }

    [Test]
    public void BrowserBridgeExposesOnlyHarnessEventsAndCommands()
    {
        string bridge = File.ReadAllText("Packages/com.basis.framework/Networking/WebGL/BasisWebNetworkE2E.jslib");

        StringAssert.Contains("BasisWebNetworkE2EReport", bridge);
        StringAssert.Contains("basisNetworkE2EEvents", bridge);
        StringAssert.Contains("basisNetworkE2ESendChat", bridge);
        StringAssert.Contains("basisNetworkE2EReconnect", bridge);
        StringAssert.DoesNotContain("new WebSocket", bridge);
    }
}
