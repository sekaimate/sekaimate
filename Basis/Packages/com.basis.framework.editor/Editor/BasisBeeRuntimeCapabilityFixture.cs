using System;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public enum BasisBeeRuntimeCapabilityFormat
{
    Avatar,
    Prop,
    World
}

public static class BasisBeeRuntimeCapabilityFixture
{
    public const string MarkerPrefix = "BasisRuntimeCapability-";
    public static GameObject Attach(
        GameObject parent,
        string assetFolder,
        BasisBeeRuntimeCapabilityFormat format,
        Vector3 localPosition)
    {
        if (parent == null)
        {
            throw new ArgumentNullException(nameof(parent));
        }

        if (!AssetDatabase.IsValidFolder(assetFolder))
        {
            throw new ArgumentException($"Asset folder does not exist: {assetFolder}", nameof(assetFolder));
        }

        string formatName = format.ToString();
        GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
        marker.name = MarkerPrefix + formatName;
        marker.transform.SetParent(parent.transform, false);
        marker.transform.localPosition = localPosition;
        marker.transform.localScale = Vector3.one * 0.35f;

        Collider collider = marker.GetComponent<Collider>();
        UnityEngine.Object.DestroyImmediate(collider);

        Renderer renderer = marker.GetComponent<Renderer>();
        renderer.sharedMaterial = CreateMaterial(assetFolder, formatName);

        Animator animator = marker.AddComponent<Animator>();
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        animator.runtimeAnimatorController = CreateAnimatorController(assetFolder, formatName, localPosition.x);

        AudioSource audioSource = marker.AddComponent<AudioSource>();
        audioSource.loop = true;
        audioSource.playOnAwake = true;
        audioSource.spatialBlend = 0f;
        audioSource.volume = 0f;
        marker.AddComponent<BasisWebRuntimePcmAudioSource>();

        return marker;
    }

    private static Material CreateMaterial(string assetFolder, string formatName)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
        {
            throw new InvalidOperationException("The Universal Render Pipeline/Lit shader is required for BEE capability fixtures.");
        }

        var material = new Material(shader)
        {
            name = $"BasisRuntimeCapability-{formatName}-Material",
            color = formatName switch
            {
                nameof(BasisBeeRuntimeCapabilityFormat.Avatar) => new Color(1f, 0.1f, 0.7f, 1f),
                nameof(BasisBeeRuntimeCapabilityFormat.Prop) => new Color(0.1f, 1f, 0.7f, 1f),
                nameof(BasisBeeRuntimeCapabilityFormat.World) => new Color(0.1f, 0.7f, 1f, 1f),
                _ => throw new ArgumentOutOfRangeException(nameof(formatName), formatName, null)
            }
        };
        string materialPath = $"{assetFolder}/{material.name}.mat";
        AssetDatabase.CreateAsset(material, materialPath);
        return AssetDatabase.LoadAssetAtPath<Material>(materialPath);
    }

    private static RuntimeAnimatorController CreateAnimatorController(
        string assetFolder,
        string formatName,
        float initialX)
    {
        string clipPath = $"{assetFolder}/BasisRuntimeCapability-{formatName}-Animation.anim";
        var clip = new AnimationClip
        {
            name = $"BasisRuntimeCapability-{formatName}-Animation",
            wrapMode = WrapMode.Loop
        };
        clip.SetCurve(
            string.Empty,
            typeof(Transform),
            "m_LocalPosition.x",
            new AnimationCurve(
                new Keyframe(0f, initialX - 0.45f),
                new Keyframe(0.5f, initialX + 0.45f),
                new Keyframe(1f, initialX - 0.45f)));
        AssetDatabase.CreateAsset(clip, clipPath);

        string controllerPath = $"{assetFolder}/BasisRuntimeCapability-{formatName}.controller";
        AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
        AnimatorState state = controller.layers[0].stateMachine.AddState("RuntimeCapability");
        state.motion = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
        controller.layers[0].stateMachine.defaultState = state;
        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        return controller;
    }

}
