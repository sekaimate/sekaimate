#if UNITY_WEBGL && !UNITY_EDITOR && DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Runtime.InteropServices;
using Basis.Scripts.BasisSdk;
using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Networking;
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
            ResolveOwner(gameObject, out string ownerKind, out int ownerPlayerId);
            BasisWebBeeRuntimeCapabilityPublish(JsonUtility.ToJson(new CapabilitySnapshot
            {
                format = format,
                instanceId = EntityId.ToULong(gameObject.GetEntityId()),
                ownerKind = ownerKind,
                ownerPlayerId = ownerPlayerId,
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

    private static void ResolveOwner(GameObject marker, out string ownerKind, out int ownerPlayerId)
    {
        BasisAvatar avatar = marker.GetComponentInParent<BasisAvatar>(true);
        if (avatar == null)
        {
            ownerKind = "Content";
            ownerPlayerId = -1;
            return;
        }

        if (BasisLocalPlayer.Instance != null && BasisLocalPlayer.Instance.BasisAvatar == avatar)
        {
            ownerKind = "LocalAvatar";
            ownerPlayerId = BasisNetworkConnection.LocalPlayerPeer?.RemoteId ?? -1;
            return;
        }

        foreach (var remotePlayer in BasisNetworkPlayers.RemotePlayers)
        {
            if (remotePlayer.Value != null && remotePlayer.Value.BasisAvatar == avatar)
            {
                ownerKind = "RemoteAvatar";
                ownerPlayerId = remotePlayer.Key;
                return;
            }
        }

        ownerKind = "Avatar";
        ownerPlayerId = -1;
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
        public string ownerKind;
        public int ownerPlayerId;
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
