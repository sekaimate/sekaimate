#if UNITY_WEBGL && !UNITY_EDITOR
using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Networking;

namespace Basis.Integration.Sso
{
    internal static class BasisWebEnrollmentBootstrap
    {
        private const string EnrollmentParameter = "basisEnrollment";
        private const string ConfigParameter = "configUrl";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Initialize()
        {
            if (!Uri.TryCreate(Application.absoluteURL, UriKind.Absolute, out Uri page)) return;
            if (!string.Equals(Read(page.Query, EnrollmentParameter), "1", StringComparison.Ordinal))
            {
                string stored = ReadStoredConfiguration();
                if (!string.IsNullOrWhiteSpace(stored)) ApplyConfiguration(stored);
                return;
            }
            string configUrl = Read(page.Query, ConfigParameter);
            if (!IsAllowedConfigUrl(configUrl)) return;
            var host = new GameObject(nameof(BasisWebEnrollmentBootstrap));
            UnityEngine.Object.DontDestroyOnLoad(host);
            host.AddComponent<Runner>().StartDownload(configUrl);
        }

        private static void ApplyConfiguration(string json)
        {
            if (!BasisSsoAuthController.ApplyRuntimeConfiguration(json, out string error))
                BasisDebug.LogError("[SSO] Stored Web enrollment configuration was rejected: " + error);
            else
                BasisDebug.Log("[SSO] Restored Web enrollment configuration for this browser session.");
        }

        private static string ReadStoredConfiguration()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return BasisWebEnrollmentReadConfig();
#else
            return string.Empty;
#endif
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern string BasisWebEnrollmentReadConfig();

        [DllImport("__Internal")]
        private static extern void BasisWebEnrollmentStoreConfig(string json);
#endif

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
                string json = request.downloadHandler.text;
                BasisWebEnrollmentStoreConfig(json);
                ApplyConfiguration(json);
                Destroy(gameObject);
            }
        }
    }
}
#endif
