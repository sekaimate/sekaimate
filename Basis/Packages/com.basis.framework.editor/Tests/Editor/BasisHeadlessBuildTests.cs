using NUnit.Framework;
using UnityEditor;

public class BasisHeadlessBuildTests
{
    [Test]
    public void CreateWebBuildPlayerOptionsUsesWebTargetAndReleaseBuild()
    {
        const string buildPath = "Build/Web";

        BuildPlayerOptions options = BasisHeadlessBuild.CreateWebBuildPlayerOptions(buildPath);

        Assert.That(options.locationPathName, Is.EqualTo(buildPath));
        Assert.That(options.target, Is.EqualTo(BuildTarget.WebGL));
        Assert.That(options.targetGroup, Is.EqualTo(BuildTargetGroup.WebGL));
        Assert.That(options.subtarget, Is.Zero);
        Assert.That(options.options, Is.EqualTo(BuildOptions.None));
    }

    [Test]
    public void CreateWebBuildPlayerOptionsUsesEnabledScenes()
    {
        string[] expectedScenes = { "Assets/Enabled.unity" };
        EditorBuildSettingsScene[] originalScenes = EditorBuildSettings.scenes;
        EditorBuildSettings.scenes = new[]
        {
            new EditorBuildSettingsScene(expectedScenes[0], true),
            new EditorBuildSettingsScene("Assets/Disabled.unity", false)
        };

        try
        {
            BuildPlayerOptions options = BasisHeadlessBuild.CreateWebBuildPlayerOptions("Build/Web");

            Assert.That(options.scenes, Is.EqualTo(expectedScenes));
        }
        finally
        {
            EditorBuildSettings.scenes = originalScenes;
        }
    }

    [Test]
    public void CreateWebBuildPlayerOptionsEnablesDevelopmentCodeForE2E()
    {
        BuildPlayerOptions options = BasisHeadlessBuild.CreateWebBuildPlayerOptions("Build/WebE2E", true);

        Assert.That(options.options, Is.EqualTo(BuildOptions.Development));
    }

    [Test]
    public void FindMissingWebBuildArtifactsAcceptsCompleteCompressedBuild()
    {
        string[] paths =
        {
            "index.html",
            "TemplateData/style.css",
            "Build/Basis.loader.js",
            "Build/Basis.framework.js.gz",
            "Build/Basis.wasm.gz",
            "Build/Basis.data.gz",
            "StreamingAssets/aa/settings.json",
            "StreamingAssets/aa/catalog.bin",
            "StreamingAssets/aa/WebGL/content.bundle"
        };

        Assert.That(BasisHeadlessBuild.FindMissingWebBuildArtifacts(paths), Is.Empty);
    }

    [Test]
    public void FindMissingWebBuildArtifactsReportsEveryMissingArtifact()
    {
        Assert.That(BasisHeadlessBuild.FindMissingWebBuildArtifacts(new[] { "index.html" }), Is.EquivalentTo(new[]
        {
            "TemplateData",
            "loader.js",
            "framework.js",
            "wasm",
            "data",
            "Addressables settings",
            "Addressables catalog",
            "Addressables bundle"
        }));
    }
}
