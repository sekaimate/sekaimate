using NUnit.Framework;

public class BasisWebBeeArtifactValidatorTests
{
    [Test]
    public void AcceptsSingleWebGlSceneSectionWithExactFileLength()
    {
        BasisBundleConnector connector = CreateConnector();

        bool valid = BasisWebBeeArtifactValidator.TryValidate(connector, 64, 200, out string error);

        Assert.That(valid, Is.True, error);
    }

    [TestCase("StandaloneOSX", "Scene")]
    [TestCase("WebGL", "GameObject")]
    public void RejectsWrongPlatformOrAssetMode(string platform, string assetMode)
    {
        BasisBundleConnector connector = CreateConnector();
        connector.BasisBundleGenerated[0].Platform = platform;
        connector.BasisBundleGenerated[0].AssetMode = assetMode;

        bool valid = BasisWebBeeArtifactValidator.TryValidate(connector, 64, 200, out _);

        Assert.That(valid, Is.False);
    }

    [Test]
    public void RejectsFileLengthThatDoesNotMatchSectionLayout()
    {
        BasisBundleConnector connector = CreateConnector();

        bool valid = BasisWebBeeArtifactValidator.TryValidate(connector, 64, 201, out string error);

        Assert.That(valid, Is.False);
        StringAssert.Contains("file length", error);
    }

    [Test]
    public void RejectsEmptySection()
    {
        BasisBundleConnector connector = CreateConnector();
        connector.BasisBundleGenerated[0].EndByte = 0;

        bool valid = BasisWebBeeArtifactValidator.TryValidate(connector, 64, 72, out string error);

        Assert.That(valid, Is.False);
        StringAssert.Contains("length is invalid", error);
    }

    [Test]
    public void AcceptsZeroCrcValue()
    {
        BasisBundleConnector connector = CreateConnector();
        connector.BasisBundleGenerated[0].AssetBundleCRC = 0;

        bool valid = BasisWebBeeArtifactValidator.TryValidate(connector, 64, 200, out string error);

        Assert.That(valid, Is.True, error);
    }

    [Test]
    public void AcceptsSingleWebGlAvatarSectionWithExactFileLength()
    {
        BasisBundleConnector connector = CreateConnector("GameObject", "BasisAvatar");

        bool valid = BasisWebBeeArtifactValidator.TryValidateAvatar(connector, 64, 200, out string error);

        Assert.That(valid, Is.True, error);
    }

    [Test]
    public void AcceptsSingleWebGlPropSectionWithBasisPropMetadata()
    {
        BasisBundleConnector connector = CreateConnector("GameObject", "BasisProp");

        bool valid = BasisWebBeeArtifactValidator.TryValidateProp(connector, 64, 200, out string error);

        Assert.That(valid, Is.True, error);
    }

    [TestCase("Scene", "BasisAvatar")]
    [TestCase("GameObject", "BasisProp")]
    public void RejectsAvatarArtifactWithWrongModeOrContentType(string assetMode, string componentName)
    {
        BasisBundleConnector connector = CreateConnector(assetMode, componentName);

        bool valid = BasisWebBeeArtifactValidator.TryValidateAvatar(connector, 64, 200, out _);

        Assert.That(valid, Is.False);
    }

    [Test]
    public void RejectsPropSectionWithoutBasisPropMetadata()
    {
        BasisBundleConnector connector = CreateConnector("GameObject", "MeshRenderer");

        bool valid = BasisWebBeeArtifactValidator.TryValidateProp(connector, 64, 200, out string error);

        Assert.That(valid, Is.False);
        StringAssert.Contains("BasisProp", error);
    }

    [Test]
    public void RejectsSceneSectionWhenValidatingProp()
    {
        BasisBundleConnector connector = CreateConnector("Scene", "BasisProp");

        bool valid = BasisWebBeeArtifactValidator.TryValidateProp(connector, 64, 200, out string error);

        Assert.That(valid, Is.False);
        StringAssert.Contains("GameObject", error);
    }

    private static BasisBundleConnector CreateConnector(string assetMode = "Scene", string componentName = null)
    {
        return new BasisBundleConnector
        {
            BasisBundleGenerated = new[]
            {
                new BasisBundleGenerated(
                    "hash",
                    assetMode,
                    "asset-name",
                    42,
                    true,
                    "password",
                    "WebGL",
                    128),
            },
            MetaData = new BasisBundleConnector.BasisMetaData
            {
                ComponentNames = componentName == null
                    ? new BasisBundleConnector.BasisComponentName[0]
                    : new[]
                    {
                        new BasisBundleConnector.BasisComponentName
                        {
                            Name = componentName,
                            count = 1,
                        },
                    },
            },
        };
    }
}
