using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;

public class BasisWebUiAssetCacheTests
{
    [TestCase("Packages/com.basis.framework/BasisUI/Addressables/AddressableAsset.cs")]
    [TestCase("Packages/com.basis.framework/BasisUI/Addressables/AddressableUIInstanceBase.cs")]
    [TestCase("Packages/com.basis.framework/BasisUI/Addressables/AddressableInstanceBase.cs")]
    public void UiAddressablesDoNotBlock(string sourcePath)
    {
        string source = File.ReadAllText(sourcePath);

        StringAssert.DoesNotContain("WaitForCompletion", source);
    }

    [Test]
    public void DeviceInitializationAwaitsUiAssetCache()
    {
        string source = File.ReadAllText("Packages/com.basis.framework/Device Management/BasisDeviceManagement.cs");

        StringAssert.Contains("await Basis.BasisUI.AddressableAssets.InitializeAsync()", source);
    }

    [Test]
    public void LibraryInitializationAwaitsUiAssetCacheBeforeMetadata()
    {
        string source = File.ReadAllText("Packages/com.basis.framework/BasisUI/Menus/Library/LibraryProvider.cs");
        int assetInitialization = source.IndexOf("await AddressableAssets.InitializeAsync()", System.StringComparison.Ordinal);
        int keyInitialization = source.IndexOf("await BasisDataStoreItemKeys.LoadKeys()", System.StringComparison.Ordinal);

        Assert.That(assetInitialization, Is.GreaterThanOrEqualTo(0));
        Assert.That(keyInitialization, Is.GreaterThan(assetInitialization));
    }

    [Test]
    public void UiAddressableGroupUsesRuntimePreloadLabel()
    {
        string source = File.ReadAllText("Assets/AddressableAssetsData/AssetGroups/Basis UI Assets.asset");

        StringAssert.Contains("m_SerializedLabels:\n    - basis-ui", source);
    }

    [Test]
    public void UiAssetCacheLoadsPrefabsAndSpritesByTheirRuntimeTypes()
    {
        string source = File.ReadAllText("Packages/com.basis.framework/BasisUI/Addressables/AddressableAsset.cs");

        StringAssert.Contains("LoadResourceLocationsAsync(UiLabel, typeof(GameObject))", source);
        StringAssert.Contains("LoadResourceLocationsAsync(UiLabel, typeof(Sprite))", source);
        StringAssert.DoesNotContain("LoadResourceLocationsAsync(UiLabel, typeof(UnityEngine.Object))", source);
    }

    [Test]
    public void UiAssetCacheReportsInitializationOnlyAfterAllAssetsLoad()
    {
        string source = File.ReadAllText("Packages/com.basis.framework/BasisUI/Addressables/AddressableAsset.cs");

        StringAssert.Contains("if (isInitialized)", source);
        StringAssert.Contains("isInitialized = true;", source);
        StringAssert.DoesNotContain("if (Prefabs.Count > 0 || SpriteAssets.Count > 0)", source);
    }

    [Test]
    public void WebEmbeddedItemSpritesAreExplicitlyPreloadedByAddress()
    {
        string source = File.ReadAllText("Packages/com.basis.framework/BasisUI/Addressables/AddressableAsset.cs");

        Assert.That(
            Regex.IsMatch(
                source,
                @"#if UNITY_WEBGL && !UNITY_EDITOR\s+await LoadSpriteAsync\(Sprites\.Camera\);\s+await LoadSpriteAsync\(Sprites\.Mirror\);\s+#endif"),
            Is.True);
    }

    [TestCase("Packages/com.basis.sdk/Textures/Runtime/microphone-solid.png")]
    [TestCase("Packages/com.basis.sdk/Textures/Runtime/microphone-mute-solid.png")]
    [TestCase("Packages/com.basis.sdk/Textures/Runtime/people-outline.png")]
    public void FoundationUiSpritesUseRuntimePreloadLabel(string address)
    {
        string source = File.ReadAllText("Assets/AddressableAssetsData/AssetGroups/Basis Foundation Assets.asset");
        int addressIndex = source.IndexOf($"m_Address: {address}", System.StringComparison.Ordinal);

        Assert.That(addressIndex, Is.GreaterThanOrEqualTo(0));
        int nextEntryIndex = source.IndexOf("  - m_GUID:", addressIndex, System.StringComparison.Ordinal);
        string entry = nextEntryIndex < 0
            ? source.Substring(addressIndex)
            : source.Substring(addressIndex, nextEntryIndex - addressIndex);
        StringAssert.Contains("- basis-ui", entry);
    }

    [TestCase("Packages/com.basis.sdk/Prefabs/UI/Loading Bar.prefab")]
    [TestCase("DesktopReticle")]
    [TestCase("Packages/com.basis.sdk/Prefabs/AvatarOrb.prefab")]
    [TestCase("Packages/com.basis.sdk/Prefabs/PropOrb.prefab")]
    [TestCase("Packages/com.basis.sdk/Prefabs/WorldOrb.prefab")]
    public void WebRuntimeUiPrefabsUseRuntimePreloadLabel(string address)
    {
        string uiAssets = File.ReadAllText("Assets/AddressableAssetsData/AssetGroups/Basis UI Assets.asset");
        string foundationAssets = File.ReadAllText("Assets/AddressableAssetsData/AssetGroups/Basis Foundation Assets.asset");
        string source = uiAssets + foundationAssets;
        int addressIndex = source.IndexOf($"m_Address: {address}", System.StringComparison.Ordinal);

        Assert.That(addressIndex, Is.GreaterThanOrEqualTo(0));
        int nextEntryIndex = source.IndexOf("  - m_GUID:", addressIndex, System.StringComparison.Ordinal);
        string entry = nextEntryIndex < 0
            ? source.Substring(addressIndex)
            : source.Substring(addressIndex, nextEntryIndex - addressIndex);
        StringAssert.Contains("- basis-ui", entry);
    }

    [Test]
    public void WebContentShareOrbsUsePreloadedPrefabsWhileNativeKeepsAddressablesInstances()
    {
        string source = File.ReadAllText("Packages/com.basis.framework/Networking/ContentShare/BasisContentShareManager.cs");

        StringAssert.Contains("#if UNITY_WEBGL && !UNITY_EDITOR", source);
        StringAssert.Contains("UnityEngine.Object.Instantiate(AddressableAssets.GetPrefab(orbKey)", source);
        StringAssert.Contains("UnityEngine.Object.Destroy(sphere.gameObject)", source);
        StringAssert.Contains("#else", source);
        StringAssert.Contains("Addressables.InstantiateAsync(orbKey", source);
        StringAssert.Contains("WaitForCompletion", source);
        StringAssert.Contains("Addressables.ReleaseInstance(sphere.gameObject)", source);
    }

    [TestCase("Packages/com.basis.framework/UI Panels/Base/BasisUIBase.cs")]
    [TestCase("Packages/com.basis.framework/Device Management/Devices/Desktop/BasisDesktopReticle.cs")]
    [TestCase("Packages/com.basis.framework/Players/Remote/BasisRemotePlayer.cs")]
    public void WebRuntimeUiInstantiationUsesPreloadedPrefabs(string sourcePath)
    {
        string source = File.ReadAllText(sourcePath);

        StringAssert.Contains("#if UNITY_WEBGL && !UNITY_EDITOR", source);
        StringAssert.Contains("AddressableAssets.GetPrefab", source);
        StringAssert.Contains("#else", source);
        StringAssert.Contains("WaitForCompletion", source);
    }

    [Test]
    public void WebRemoteNamePlateUsesRuntimePreloadLabel()
    {
        string source = File.ReadAllText("Assets/AddressableAssetsData/AssetGroups/Basis Foundation Assets.asset");
        const string address = "Assets/UI/Prefabs/NamePlate.prefab";
        int addressIndex = source.IndexOf($"m_Address: {address}", System.StringComparison.Ordinal);

        Assert.That(addressIndex, Is.GreaterThanOrEqualTo(0));
        int nextEntryIndex = source.IndexOf("  - m_GUID:", addressIndex, System.StringComparison.Ordinal);
        string entry = nextEntryIndex < 0
            ? source.Substring(addressIndex)
            : source.Substring(addressIndex, nextEntryIndex - addressIndex);
        StringAssert.Contains("- basis-ui", entry);
    }
}
