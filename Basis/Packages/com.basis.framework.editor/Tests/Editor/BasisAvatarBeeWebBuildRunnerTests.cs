using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public class BasisAvatarBeeWebBuildRunnerTests
{
    private const string SourceAvatarPath = "Packages/com.unity.3rdpersondemo/HumanoidMidAir.fbx";

    [Test]
    public void VerificationSourceProvidesHumanoidAnimator()
    {
        GameObject sourceAvatar = AssetDatabase.LoadAssetAtPath<GameObject>(SourceAvatarPath);

        Assert.That(sourceAvatar, Is.Not.Null);
        Animator animator = sourceAvatar.GetComponentInChildren<Animator>(true);
        Assert.That(animator, Is.Not.Null);
        Assert.That(animator.avatar, Is.Not.Null);
        Assert.That(animator.isHuman, Is.True);
    }
}
