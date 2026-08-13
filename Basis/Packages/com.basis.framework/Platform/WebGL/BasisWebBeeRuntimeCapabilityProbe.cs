#if UNITY_WEBGL && !UNITY_EDITOR && DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Runtime.InteropServices;
using UnityEngine;

public sealed class BasisWebBeeRuntimeCapabilityProbe : MonoBehaviour
{
    private const string QueryKey = "basisBeeRuntimeE2E";
    private const string MarkerPrefix = "BasisRuntimeCapability-";
    private static readonly WaitForSecondsRealtime SampleInterval = new WaitForSecondsRealtime(0.25f);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Run()
    {
        if (ReadQueryValue(Application.absoluteURL, QueryKey) != "1")
        {
            return;
        }

        var probe = new GameObject(nameof(BasisWebBeeRuntimeCapabilityProbe));
        DontDestroyOnLoad(probe);
        probe.AddComponent<BasisWebBeeRuntimeCapabilityProbe>();
    }

    private IEnumerator Start()
    {
        while (true)
        {
            PublishActiveFixtures();
            yield return SampleInterval;
        }
    }

    private static void PublishActiveFixtures()
    {
        GameObject[] gameObjects = FindObjectsByType<GameObject>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        foreach (GameObject gameObject in gameObjects)
        {
            if (!gameObject.name.StartsWith(MarkerPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            Renderer renderer = gameObject.GetComponent<Renderer>();
            Animator animator = gameObject.GetComponent<Animator>();
            AudioSource audioSource = gameObject.GetComponent<AudioSource>();
            if (renderer == null || animator == null || audioSource == null ||
                animator.runtimeAnimatorController == null || audioSource.clip == null)
            {
                continue;
            }

            AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
            string format = gameObject.name.Substring(MarkerPrefix.Length).Replace("(Clone)", string.Empty).Trim();
            BasisWebBeeRuntimeCapabilityPublish(JsonUtility.ToJson(new CapabilitySnapshot
            {
                format = format,
                instanceId = EntityId.ToULong(gameObject.GetEntityId()),
                rendererVisible = renderer.isVisible,
                rendererCenterX = renderer.bounds.center.x,
                animationNormalizedTime = state.normalizedTime,
                animationClipLength = animator.runtimeAnimatorController.animationClips[0].length,
                audioIsPlaying = audioSource.isPlaying,
                audioTime = audioSource.time,
                audioClipLength = audioSource.clip.length,
                observedAt = Time.realtimeSinceStartup
            }));
        }
    }

    private static string ReadQueryValue(string absoluteUrl, string key)
    {
        if (!Uri.TryCreate(absoluteUrl, UriKind.Absolute, out Uri uri))
        {
            return string.Empty;
        }

        foreach (string field in uri.Query.TrimStart('?').Split('&'))
        {
            string[] pair = field.Split(new[] { '=' }, 2);
            if (Uri.UnescapeDataString(pair[0]) == key)
            {
                return pair.Length == 2 ? Uri.UnescapeDataString(pair[1]) : string.Empty;
            }
        }

        return string.Empty;
    }

    [DllImport("__Internal")]
    private static extern void BasisWebBeeRuntimeCapabilityPublish(string snapshotJson);

    [Serializable]
    private sealed class CapabilitySnapshot
    {
        public string format;
        public ulong instanceId;
        public bool rendererVisible;
        public float rendererCenterX;
        public float animationNormalizedTime;
        public float animationClipLength;
        public bool audioIsPlaying;
        public float audioTime;
        public float audioClipLength;
        public float observedAt;
    }
}
#endif
