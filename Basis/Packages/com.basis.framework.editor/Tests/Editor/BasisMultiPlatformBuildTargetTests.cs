using NUnit.Framework;
using UnityEditor;

public sealed class BasisMultiPlatformBuildTargetTests
{
    private static readonly BuildTarget[] ExpectedTargets =
    {
        BuildTarget.StandaloneWindows64,
        BuildTarget.StandaloneOSX,
        BuildTarget.StandaloneLinux64,
        BuildTarget.Android,
        BuildTarget.iOS,
        BuildTarget.WebGL,
    };

    [Test]
    public void AllPlatformBuildTargetsContainEverySupportedPlatform()
    {
        Assert.That(BasisSDKConstants.GetAllPlatformBuildTargets(), Is.EqualTo(ExpectedTargets));
    }

    [Test]
    public void AssetBundleSettingsSelectEverySupportedPlatform()
    {
        BasisAssetBundleObject settings = AssetDatabase.LoadAssetAtPath<BasisAssetBundleObject>(
            BasisAssetBundleObject.AssetBundleObject);

        Assert.That(settings, Is.Not.Null);
        Assert.That(settings.selectedTargets, Is.EquivalentTo(ExpectedTargets));
    }
}
