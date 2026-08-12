using NUnit.Framework;
using UnityEditor;

public class BasisHeadlessBuildTests
{
    [Test]
    public void CreateWebBuildPlayerOptionsUsesWebTargetAndDevelopmentBuild()
    {
        const string buildPath = "Build/Web";

        BuildPlayerOptions options = BasisHeadlessBuild.CreateWebBuildPlayerOptions(buildPath);

        Assert.That(options.locationPathName, Is.EqualTo(buildPath));
        Assert.That(options.target, Is.EqualTo(BuildTarget.WebGL));
        Assert.That(options.targetGroup, Is.EqualTo(BuildTargetGroup.WebGL));
        Assert.That(options.subtarget, Is.Zero);
        Assert.That(options.options, Is.EqualTo(BuildOptions.Development));
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
}
