using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public class BasisBeeWebPlatformTests
{
    [Test]
    public void SdkBuildTargetsAppendWebGlWithoutChangingExistingTargets()
    {
        BuildTarget[] expectedTargets =
        {
            BuildTarget.StandaloneWindows64,
            BuildTarget.StandaloneOSX,
            BuildTarget.StandaloneLinux64,
            BuildTarget.Android,
            BuildTarget.iOS,
            BuildTarget.WebGL,
        };

        Assert.That(BasisSDKConstants.allowedTargets, Is.EqualTo(expectedTargets));
        Assert.That(BasisSDKConstants.targetDisplayNames[BuildTarget.WebGL], Is.EqualTo("Web"));
    }

    [TestCase(BuildTarget.StandaloneWindows64, BuildTargetGroup.Standalone)]
    [TestCase(BuildTarget.StandaloneOSX, BuildTargetGroup.Standalone)]
    [TestCase(BuildTarget.StandaloneLinux64, BuildTargetGroup.Standalone)]
    [TestCase(BuildTarget.Android, BuildTargetGroup.Android)]
    [TestCase(BuildTarget.iOS, BuildTargetGroup.iOS)]
    [TestCase(BuildTarget.WebGL, BuildTargetGroup.WebGL)]
    public void CheckTargetUsesTheTargetsOwnBuildTargetGroup(BuildTarget target, BuildTargetGroup expectedGroup)
    {
        BuildTargetGroup observedGroup = BuildTargetGroup.Unknown;
        BuildTarget observedTarget = default;

        bool supported = BasisBundleBuild.CheckTarget(target, (group, checkedTarget) =>
        {
            observedGroup = group;
            observedTarget = checkedTarget;
            return true;
        });

        Assert.That(supported, Is.True);
        Assert.That(observedGroup, Is.EqualTo(expectedGroup));
        Assert.That(observedTarget, Is.EqualTo(target));
    }

    [Test]
    public void WebGlRuntimeMatchesOnlyWebGlBundleSection()
    {
        Assert.That(BasisBundleConnector.PlatformMatch("WebGL", RuntimePlatform.WebGLPlayer), Is.True);
        Assert.That(BasisBundleConnector.PlatformMatch("StandaloneWindows64", RuntimePlatform.WebGLPlayer), Is.False);
        Assert.That(BasisBundleConnector.PlatformMatch("StandaloneOSX", RuntimePlatform.WebGLPlayer), Is.False);
        Assert.That(BasisBundleConnector.PlatformMatch("StandaloneLinux64", RuntimePlatform.WebGLPlayer), Is.False);
    }

    [TestCase("WindowsEditor", "StandaloneWindows64")]
    [TestCase("WindowsPlayer", "StandaloneWindows64")]
    [TestCase("WindowsServer", "StandaloneWindows64")]
    [TestCase("OSXEditor", "StandaloneOSX")]
    [TestCase("OSXPlayer", "StandaloneOSX")]
    [TestCase("LinuxEditor", "StandaloneLinux64")]
    [TestCase("LinuxPlayer", "StandaloneLinux64")]
    [TestCase("LinuxServer", "StandaloneLinux64")]
    [TestCase("Android", "Android")]
    [TestCase("IPhonePlayer", "iOS")]
    [TestCase("WebGLPlayer", "WebGL")]
    public void CachePlatformNormalizationUsesBundlePlatformNames(string runtimePlatform, string expectedBundlePlatform)
    {
        Assert.That(BasisIOManagement.NormalizeCachePlatformName(runtimePlatform), Is.EqualTo(expectedBundlePlatform));
    }

    [Test]
    public void WebGlBuildOptionsAlwaysUseLz4()
    {
        BuildAssetBundleOptions options = AssetBundleBuilder.ResolveBuildOptions(
            BuildTarget.WebGL,
            BuildAssetBundleOptions.None);

        Assert.That(options.HasFlag(BuildAssetBundleOptions.ChunkBasedCompression), Is.True);
        Assert.That(options.HasFlag(BuildAssetBundleOptions.UncompressedAssetBundle), Is.False);
    }

    [Test]
    public void WebGlBuildOptionsRejectUncompressedBundles()
    {
        Assert.Throws<InvalidOperationException>(() => AssetBundleBuilder.ResolveBuildOptions(
            BuildTarget.WebGL,
            BuildAssetBundleOptions.UncompressedAssetBundle));
    }

    [TestCase(BuildTarget.StandaloneWindows64)]
    [TestCase(BuildTarget.StandaloneOSX)]
    [TestCase(BuildTarget.StandaloneLinux64)]
    [TestCase(BuildTarget.Android)]
    [TestCase(BuildTarget.iOS)]
    public void NativeBuildOptionsRemainUnchanged(BuildTarget target)
    {
        BuildAssetBundleOptions configured = BuildAssetBundleOptions.DisableWriteTypeTree;

        Assert.That(AssetBundleBuilder.ResolveBuildOptions(target, configured), Is.EqualTo(configured));
    }
}
