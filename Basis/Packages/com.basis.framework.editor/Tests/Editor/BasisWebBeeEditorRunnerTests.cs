using NUnit.Framework;
using UnityEngine;

public sealed class BasisWebBeeEditorRunnerTests
{
    [Test]
    public void RejectsMissingConfiguration()
    {
        Assert.That(BasisWebBeeEditorRunner.TryValidate(null, out string error), Is.False);
        StringAssert.Contains(BasisWebBeeBuildConfiguration.AssetPath, error);
    }

    [Test]
    public void AcceptsCompleteConfiguration()
    {
        BasisWebBeeBuildConfiguration configuration = ScriptableObject.CreateInstance<BasisWebBeeBuildConfiguration>();
        configuration.AvatarOutputRoot = "avatar";
        configuration.AvatarPassword = "avatar-password";
        configuration.PropOutputRoot = "prop";
        configuration.PropPassword = "prop-password";
        configuration.WorldOutputRoot = "world";
        configuration.WorldPassword = "world-password";

        try
        {
            Assert.That(BasisWebBeeEditorRunner.TryValidate(configuration, out string error), Is.True, error);
        }
        finally
        {
            Object.DestroyImmediate(configuration);
        }
    }
}
