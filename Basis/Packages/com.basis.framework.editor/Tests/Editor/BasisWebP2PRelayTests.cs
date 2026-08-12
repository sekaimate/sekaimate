using System.IO;
using NUnit.Framework;

public class BasisWebP2PRelayTests
{
    [Test]
    public void WebBuildDisablesDirectPeerConnectionsAtCompileTime()
    {
        string source = File.ReadAllText("Packages/com.basis.framework/Networking/BasisNetworkPlatformCapabilities.cs");

        StringAssert.Contains("#if UNITY_WEBGL && !UNITY_EDITOR", source);
        StringAssert.Contains("public const bool SupportsDirectPeerConnections = false", source);
        StringAssert.Contains("public const bool SupportsDirectPeerConnections = true", source);
    }

    [Test]
    public void UnsupportedDirectRecipientsUseServerRelayChannels()
    {
        string p2pSource = File.ReadAllText("Packages/com.basis.framework/Networking/BasisP2PManager.cs");
        string sceneSource = File.ReadAllText("Packages/com.basis.framework/Networking/Handles/BasisNetworkGenericMessages.cs");
        string avatarSource = File.ReadAllText("Packages/com.basis.framework/Networking/BasisNetworkPlayer.cs");

        StringAssert.Contains("if (!BasisNetworkPlatformCapabilities.SupportsDirectPeerConnections)", p2pSource);
        StringAssert.Contains("BasisNetworkCommons.DirectSceneServerChannel", sceneSource);
        StringAssert.Contains("BasisNetworkCommons.DirectAvatarServerChannel", avatarSource);
    }

    [Test]
    public void UnsupportedPlatformDoesNotOfferDirectConnectionUi()
    {
        string playerMenuSource = File.ReadAllText("Packages/com.basis.framework/BasisUI/Menus/Main Menu Providers/IndividualPlayerProvider.cs");
        string settingsSource = File.ReadAllText("Packages/com.basis.framework/BasisUI/Menus/Main Menu Providers/SettingsProvider.cs");
        string incomingDialogSource = File.ReadAllText("Packages/com.basis.framework/Networking/BasisP2PIncomingDialog.cs");

        StringAssert.Contains("if (BasisNetworkPlatformCapabilities.SupportsDirectPeerConnections)", playerMenuSource);
        StringAssert.Contains("if (!BasisNetworkPlatformCapabilities.SupportsDirectPeerConnections)", settingsSource);
        StringAssert.Contains("if (!BasisNetworkPlatformCapabilities.SupportsDirectPeerConnections)", incomingDialogSource);
    }
}
