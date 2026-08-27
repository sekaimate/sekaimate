using System.IO;
using System.Reflection;
using Basis.BasisUI;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;

public class BasisAddressablesSettingsTests
{
    private const string UiLabel = "basis-ui";

    [Test]
    public void BuiltInDataUsesPlayerDataGroupSchema()
    {
        AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
        AddressableAssetGroup builtInData = settings.FindGroup("Built In Data");

        Assert.That(builtInData, Is.Not.Null);
        Assert.That(builtInData.GetSchema<PlayerDataGroupSchema>(), Is.Not.Null);
    }

    [Test]
    public void ValveOpenXrUtilsReadmeHasItsAssetAndMetadata()
    {
        const string readmePath = "Packages/com.valvesoftware.openxr.utils/README.md";

        Assert.That(File.Exists(readmePath), Is.True);
        Assert.That(File.Exists($"{readmePath}.meta"), Is.True);
    }

    [Test]
    public void EveryUiSpriteIsIncludedInThePreloadLabel()
    {
        AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
        FieldInfo[] spriteFields = typeof(AddressableAssets.Sprites).GetFields(BindingFlags.Public | BindingFlags.Static);

        foreach (FieldInfo spriteField in spriteFields)
        {
            string address = (string)spriteField.GetValue(null);
            string guid = AssetDatabase.AssetPathToGUID(address);
            AddressableAssetEntry entry = settings.FindAssetEntry(guid);

            Assert.That(entry, Is.Not.Null, $"{spriteField.Name} is not addressable: {address}");
            CollectionAssert.Contains(entry.labels, UiLabel, $"{spriteField.Name} is not preloaded: {address}");
        }
    }
}
