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

    private static BasisBundleConnector CreateConnector()
    {
        return new BasisBundleConnector
        {
            BasisBundleGenerated = new[]
            {
                new BasisBundleGenerated(
                    "hash",
                    "Scene",
                    "scene-name",
                    42,
                    true,
                    "password",
                    "WebGL",
                    128),
            },
        };
    }
}
