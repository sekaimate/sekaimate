using System.IO;
using NUnit.Framework;

public class BasisWebAvatarAddressablesTests
{
    [Test]
    public void WebSeatAwaitsHighlightMaterialWhileNativeKeepsSynchronousLoad()
    {
        string source = File.ReadAllText("Packages/com.basis.framework/Interactions/BasisSeat.cs");

        StringAssert.Contains("#if UNITY_WEBGL && !UNITY_EDITOR", source);
        StringAssert.Contains("_colliderHighlightMat = await op.Task", source);
        StringAssert.Contains("#else", source);
        StringAssert.Contains("_colliderHighlightMat = op.WaitForCompletion()", source);
    }

    [Test]
    public void WebFaceTrackingAwaitsDefaultDefinitionsWhileNativeKeepsSynchronousLoad()
    {
        string source = File.ReadAllText("Packages/dev.hai-vr.basis.comms/Scripts/Systems/Runtime/Components/FaceTracking/AutomaticFaceTracking.cs");

        StringAssert.Contains("#if UNITY_WEBGL && !UNITY_EDITOR", source);
        StringAssert.Contains("await LoadDefaultDefinitionFilesAsync()", source);
        StringAssert.Contains("await Addressables.LoadAssetAsync<BlendshapeActuationDefinitionFile>", source);
        StringAssert.Contains("#else", source);
        StringAssert.Contains("WaitForCompletion", source);
    }

    [Test]
    public void WebVisualStateUsesPreloadedCircleWhileNativeKeepsSynchronousLoad()
    {
        string source = File.ReadAllText("Packages/com.basis.framework/Settings/BasisVisualStateModule.cs");

        StringAssert.Contains("#if UNITY_WEBGL && !UNITY_EDITOR", source);
        StringAssert.Contains("AddressableAssets.GetPrefab(AdaptiveCircleId)", source);
        StringAssert.Contains("#else", source);
        StringAssert.Contains("WaitForCompletion", source);
    }

    [Test]
    public void WebVisualStateCircleUsesRuntimePreloadLabel()
    {
        string source = File.ReadAllText("Assets/AddressableAssetsData/AssetGroups/Basis Foundation Assets.asset");
        const string address = "Adaptive Circle Display.prefab";
        int addressIndex = source.IndexOf($"m_Address: {address}", System.StringComparison.Ordinal);

        Assert.That(addressIndex, Is.GreaterThanOrEqualTo(0));
        int nextEntryIndex = source.IndexOf("  - m_GUID:", addressIndex, System.StringComparison.Ordinal);
        string entry = nextEntryIndex < 0
            ? source.Substring(addressIndex)
            : source.Substring(addressIndex, nextEntryIndex - addressIndex);
        StringAssert.Contains("- basis-ui", entry);
    }
}
