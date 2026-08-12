using System.IO;
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
    public void UiAddressableGroupUsesRuntimePreloadLabel()
    {
        string source = File.ReadAllText("Assets/AddressableAssetsData/AssetGroups/Basis UI Assets.asset");

        StringAssert.Contains("m_SerializedLabels:\n    - basis-ui", source);
    }
}
