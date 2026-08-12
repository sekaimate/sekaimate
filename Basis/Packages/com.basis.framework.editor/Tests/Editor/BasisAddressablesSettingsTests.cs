using NUnit.Framework;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;

public class BasisAddressablesSettingsTests
{
    [Test]
    public void BuiltInDataUsesPlayerDataGroupSchema()
    {
        AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
        AddressableAssetGroup builtInData = settings.FindGroup("Built In Data");

        Assert.That(builtInData, Is.Not.Null);
        Assert.That(builtInData.GetSchema<PlayerDataGroupSchema>(), Is.Not.Null);
    }
}
