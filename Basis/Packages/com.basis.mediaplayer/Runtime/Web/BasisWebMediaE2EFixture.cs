#if UNITY_WEBGL && !UNITY_EDITOR && DEVELOPMENT_BUILD
using System;
using UnityEngine;

public sealed class BasisWebMediaE2EFixture : MonoBehaviour
{
    private const string EnabledParameter = "basisMediaE2E";
    private const string MediaUrlParameter = "basisMediaE2EUrl";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Initialize()
    {
        if (!TryGetQueryValue(Application.absoluteURL, EnabledParameter, out string enabled) || enabled != "1") return;
        if (!TryGetQueryValue(Application.absoluteURL, MediaUrlParameter, out string mediaUrl)) return;

        GameObject fixture = new GameObject("BasisWebMediaE2E");
        DontDestroyOnLoad(fixture);
        fixture.AddComponent<BasisWebMediaE2EFixture>();
        BasisMediaPlayer player = fixture.AddComponent<BasisMediaPlayer>();
        player.AutoPlayOnSourceAssigned = true;
        player.Volume = 0.25f;
        player.Mute = false;
        player.LoadUrl(mediaUrl);
    }

    private static bool TryGetQueryValue(string absoluteUrl, string name, out string value)
    {
        value = null;
        if (!Uri.TryCreate(absoluteUrl, UriKind.Absolute, out Uri uri)) return false;

        string query = uri.Query.TrimStart('?');
        foreach (string field in query.Split('&'))
        {
            string[] parts = field.Split(new[] { '=' }, 2);
            if (!string.Equals(Uri.UnescapeDataString(parts[0]), name, StringComparison.Ordinal)) continue;
            value = parts.Length == 2 ? Uri.UnescapeDataString(parts[1].Replace("+", " ")) : string.Empty;
            return true;
        }
        return false;
    }
}
#endif
