using System.IO;
using NUnit.Framework;

public sealed class BasisWebNetworkE2EHarnessTests
{
    [Test]
    public void WebClientConfiguresNetworkListenerBeforeStartingTransport()
    {
        string source = File.ReadAllText("Packages/com.basis.framework/Networking/BasisNetworkConnection.cs");

        StringAssert.Contains("authenticationPayload, serverConfig,\n                        ConfigureNetworkListener);", source);
        StringAssert.DoesNotContain("NetworkClient.listener.NetworkReceiveEvent +=", source);
    }

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
        StringAssert.Contains("using Basis.Scripts.Networking.NetworkedAvatar;", source);
        StringAssert.Contains("BasisNetworkManagement.Transmitter", source);
    }

    [Test]
    public void RuntimeHarnessUsesProductionContentShareChannelsAndLifecycleEvents()
    {
        string source = File.ReadAllText("Packages/com.basis.framework/Networking/WebGL/BasisWebNetworkE2EHarness.cs");

        StringAssert.Contains("BasisNetworkCommons.ContentShareChannel", source);
        StringAssert.Contains("BasisContentShareManager.RequestRemoveSphere", source);
        StringAssert.Contains("BasisContentShareManager.OnSphereCreated", source);
        StringAssert.Contains("BasisContentShareManager.OnSphereRemoved", source);
        StringAssert.Contains("ContentShareType.Avatar", source);
        StringAssert.Contains("ContentShareType.Prop", source);
        StringAssert.Contains("ContentShareType.World", source);
        StringAssert.Contains("ContentShareType.Server", source);
    }

    [Test]
    public void RuntimeHarnessLoadsReceivedBeeContentThroughProductionLoaders()
    {
        string source = File.ReadAllText("Packages/com.basis.framework/Networking/WebGL/BasisWebNetworkE2EHarness.cs");

        StringAssert.Contains("BasisLoadHandler.LoadGameObjectBundle", source);
        StringAssert.Contains("BasisSceneLoad.LoadSceneAssetBundle", source);
        StringAssert.Contains("BasisContentShareManager.TryGetSphere", source);
        StringAssert.Contains("content-load-complete", source);
        StringAssert.Contains("content-load-failed", source);
    }

    [Test]
    public void RuntimeHarnessChangesTheProductionLocalAvatarForNetworkReplication()
    {
        string source = File.ReadAllText("Packages/com.basis.framework/Networking/WebGL/BasisWebNetworkE2EHarness.cs");
        string bridge = File.ReadAllText("Packages/com.basis.framework/Networking/WebGL/BasisWebNetworkE2E.jslib");

        StringAssert.Contains("BasisLocalPlayer.Instance.CreateAvatar", source);
        StringAssert.Contains("BasisPlayer.LoadModeNetworkDownloadable", source);
        StringAssert.Contains("avatar-load-complete", source);
        StringAssert.Contains("basisNetworkE2ESetAvatar", bridge);
    }

    [Test]
    public void RuntimeHarnessReportsObservableJsonWithoutOwningAWebSocket()
    {
        string source = File.ReadAllText("Packages/com.basis.framework/Networking/WebGL/BasisWebNetworkE2EHarness.cs");
        string bridge = File.ReadAllText("Packages/com.basis.framework/Networking/WebGL/BasisWebNetworkE2E.jslib");

        StringAssert.Contains("JsonUtility.ToJson", source);
        StringAssert.Contains("[BasisWebNetworkE2E]", source);
        StringAssert.DoesNotContain("new WebSocket", source);
        StringAssert.Contains("basisNetworkE2ESendChat", bridge);
        StringAssert.Contains("basisNetworkE2EReconnect", bridge);
        StringAssert.Contains("basisNetworkE2EShareContent", bridge);
        StringAssert.Contains("basisNetworkE2ERemoveContent", bridge);
        StringAssert.Contains("basisNetworkE2ELoadContent", bridge);
        StringAssert.DoesNotContain("new WebSocket", bridge);
    }

    [Test]
    public void RuntimeHarnessCanCloseMenusBeforeVisualAssertions()
    {
        string source = File.ReadAllText("Packages/com.basis.framework/Networking/WebGL/BasisWebNetworkE2EHarness.cs");
        string bridge = File.ReadAllText("Packages/com.basis.framework/Networking/WebGL/BasisWebNetworkE2E.jslib");

        StringAssert.Contains("BasisUIManagement.CloseAllMenus", source);
        StringAssert.Contains("BasisMainMenu.Close", source);
        StringAssert.Contains("menus-closed", source);
        StringAssert.Contains("basisNetworkE2ECloseMenus", bridge);
    }

    [Test]
    public void RuntimeHarnessDrivesProductionPlayerPanelsAndReportsTheirState()
    {
        string source = File.ReadAllText("Packages/com.basis.framework/Networking/WebGL/BasisWebNetworkE2EHarness.cs");
        string bridge = File.ReadAllText("Packages/com.basis.framework/Networking/WebGL/BasisWebNetworkE2E.jslib");

        StringAssert.Contains("BasisMainMenu.OpenWithProvider(UserListProvider.StaticTitle)", source);
        StringAssert.Contains("PanelTextField", source);
        StringAssert.Contains("PanelDropdown", source);
        StringAssert.Contains("PanelButton", source);
        StringAssert.Contains("PanelSlider", source);
        StringAssert.Contains("dialogue.AcceptButton.OnClick", source);
        StringAssert.Contains("BasisPlayerSettingsManager.RequestPlayerSettings", source);
        StringAssert.Contains("IndividualPlayerActionPermissions.CanUse", source);
        StringAssert.Contains("basisNetworkE2EOpenPlayerList", bridge);
        StringAssert.Contains("basisNetworkE2EPlayerUiAction", bridge);
        StringAssert.Contains("basisNetworkE2EConfirmDialogue", bridge);
    }
}
