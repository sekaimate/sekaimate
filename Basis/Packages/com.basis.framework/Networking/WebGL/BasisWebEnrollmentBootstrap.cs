#if UNITY_WEBGL && !UNITY_EDITOR
using System;
using Basis.Integration.Sso;
using UnityEngine;
using UnityEngine.Networking;

namespace Basis.Scripts.Networking
{
    internal static class BasisWebEnrollmentBootstrap
    {
        private const string EnrollmentParameter = "basisEnrollment";
        private const string ConfigParameter = "configUrl";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Initialize()
        {
            if (!Uri.TryCreate(Application.absoluteURL, UriKind.Absolute, out Uri page)) return;
            if (!string.Equals(Read(page.Query, EnrollmentParameter), "1", StringComparison.Ordinal)) return;
            string configUrl = Read(page.Query, ConfigParameter);
            if (!IsAllowedConfigUrl(configUrl)) return;
            var host = new GameObject(nameof(BasisWebEnrollmentBootstrap));
            UnityEngine.Object.DontDestroyOnLoad(host);
            host.AddComponent<Runner>().StartDownload(configUrl);
        }

        private static string Read(string query, string key)
        {
            foreach (string part in query.TrimStart('?').Split('&'))
            {
                int separator = part.IndexOf('=');
                if (separator < 0 || !string.Equals(part[..separator], key, StringComparison.OrdinalIgnoreCase)) continue;
                try { return Uri.UnescapeDataString(part[(separator + 1)..]); }
                catch (UriFormatException) { return part[(separator + 1)..]; }
            }
            return string.Empty;
        }

        private static bool IsAllowedConfigUrl(string value)
        {
            return Uri.TryCreate(value, UriKind.Absolute, out Uri uri)
                && (uri.Scheme == Uri.UriSchemeHttps
                    || (uri.Scheme == Uri.UriSchemeHttp && BasisOidcConfig.IsLoopbackHost(uri.Host)));
        }

        private sealed class Runner : MonoBehaviour
        {
            internal void StartDownload(string url) => StartCoroutine(Download(url));

            private System.Collections.IEnumerator Download(string url)
            {
                using UnityWebRequest request = UnityWebRequest.Get(url);
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success)
                {
                    BasisDebug.LogError("[SSO] Web enrollment download failed: " + request.error);
                    Destroy(gameObject);
                    yield break;
                }
                if (!BasisSsoAuthController.ApplyRuntimeConfiguration(request.downloadHandler.text, out string error))
                    BasisDebug.LogError("[SSO] Web enrollment configuration was rejected: " + error);
                Destroy(gameObject);
            }
        }
    }
}
#endif
