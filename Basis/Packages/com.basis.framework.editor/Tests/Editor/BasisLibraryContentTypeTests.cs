using Basis.BasisUI;
using NUnit.Framework;

public class BasisLibraryContentTypeTests
{
    [Test]
    public void SceneAssetModeTakesPriorityOverNestedPropComponents()
    {
        BasisBundleConnector connector = Connector("Scene", "basisprop", "basisscene");

        BundledContentHolder.Mode mode = LibraryProvider.ResolveModeFromConnector(connector);

        Assert.That(mode, Is.EqualTo(BundledContentHolder.Mode.World));
    }

    [TestCase("basisavatar", BundledContentHolder.Mode.Avatar)]
    [TestCase("basisprop", BundledContentHolder.Mode.Prop)]
    public void GameObjectAssetModeUsesRootContentComponent(string componentName, BundledContentHolder.Mode expected)
    {
        BasisBundleConnector connector = Connector("GameObject", componentName);

        BundledContentHolder.Mode mode = LibraryProvider.ResolveModeFromConnector(connector);

        Assert.That(mode, Is.EqualTo(expected));
    }

    private static BasisBundleConnector Connector(string assetMode, params string[] componentNames)
    {
        BasisBundleConnector.BasisComponentName[] components = new BasisBundleConnector.BasisComponentName[componentNames.Length];
        for (int index = 0; index < componentNames.Length; index++)
        {
            components[index] = new BasisBundleConnector.BasisComponentName
            {
                Name = componentNames[index],
                count = 1
            };
        }

        return new BasisBundleConnector
        {
            BasisBundleGenerated = new[]
            {
                new BasisBundleGenerated { AssetMode = assetMode }
            },
            MetaData = new BasisBundleConnector.BasisMetaData
            {
                ComponentNames = components
            }
        };
    }
}
