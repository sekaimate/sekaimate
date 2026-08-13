using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public class BasisBeeRuntimeCapabilityFixtureTests
{
    private const string TestAssetFolder = "Assets/BasisBeeRuntimeCapabilityFixtureTests";
    private GameObject root;

    [SetUp]
    public void SetUp()
    {
        AssetDatabase.DeleteAsset(TestAssetFolder);
        AssetDatabase.CreateFolder("Assets", "BasisBeeRuntimeCapabilityFixtureTests");
        root = new GameObject("FixtureRoot");
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(root);
        AssetDatabase.DeleteAsset(TestAssetFolder);
    }

    [TestCase(BasisBeeRuntimeCapabilityFormat.Avatar)]
    [TestCase(BasisBeeRuntimeCapabilityFormat.Prop)]
    [TestCase(BasisBeeRuntimeCapabilityFormat.World)]
    public void AttachCreatesRendererAnimationAndAudioThatCanRunInWebGl(
        BasisBeeRuntimeCapabilityFormat format)
    {
        GameObject marker = BasisBeeRuntimeCapabilityFixture.Attach(
            root,
            TestAssetFolder,
            format,
            Vector3.zero);

        Assert.That(marker.name, Is.EqualTo($"BasisRuntimeCapability-{format}"));

        Renderer renderer = marker.GetComponent<Renderer>();
        Assert.That(renderer, Is.Not.Null);
        Assert.That(renderer.sharedMaterial, Is.Not.Null);

        Animator animator = marker.GetComponent<Animator>();
        Assert.That(animator, Is.Not.Null);
        Assert.That(animator.runtimeAnimatorController, Is.Not.Null);
        Assert.That(animator.runtimeAnimatorController.animationClips, Has.Length.EqualTo(1));
        Assert.That(animator.runtimeAnimatorController.animationClips[0].length, Is.GreaterThanOrEqualTo(1f));

        animator.Rebind();
        animator.Update(0f);
        float initialX = marker.transform.localPosition.x;
        animator.Update(0.5f);
        Assert.That(marker.transform.localPosition.x, Is.Not.EqualTo(initialX).Within(0.01f));

        AudioSource audioSource = marker.GetComponent<AudioSource>();
        Assert.That(audioSource, Is.Not.Null);
        Assert.That(audioSource.clip, Is.Not.Null);
        Assert.That(audioSource.clip.length, Is.GreaterThanOrEqualTo(1f));
        Assert.That(audioSource.playOnAwake, Is.True);
        Assert.That(audioSource.loop, Is.True);
    }
}
