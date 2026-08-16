#if UNITY_WEBGL && !UNITY_EDITOR
using System;
using System.Collections;
using System.Globalization;
using System.Runtime.InteropServices;
using Basis.Network.Core;
using Basis.Scripts.Networking;
using UnityEngine;
using UnityEngine.Networking;

namespace Basis.Integration.Sso
{
    internal static class BasisWebEnrollmentBootstrap
    {
        private const string EnrollmentParameter = "basisEnrollment";
        private const string ConfigParameter = "configUrl";
        private const string MeetingParameter = "basisMeeting";
        private const string MeetingUrlParameter = "meetingUrl";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Initialize()
        {
            if (!Uri.TryCreate(Application.absoluteURL, UriKind.Absolute, out Uri page)) return;
            string meetingUrl = Read(page.Query, MeetingUrlParameter);
            if (Read(page.Query, MeetingParameter) == "1" && IsAllowedConfigUrl(meetingUrl))
            {
                var meetingHost = new GameObject(nameof(BasisWebEnrollmentBootstrap));
                UnityEngine.Object.DontDestroyOnLoad(meetingHost);
                meetingHost.AddComponent<Runner>().StartMeetingDownload(meetingUrl);
                return;
            }
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
            internal void StartMeetingDownload(string url) => StartCoroutine(DownloadMeeting(url));

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

            private IEnumerator DownloadMeeting(string url)
            {
                using (UnityWebRequest manifestRequest = UnityWebRequest.Get(url))
                {
                    yield return manifestRequest.SendWebRequest();
                    if (manifestRequest.result != UnityWebRequest.Result.Success)
                    {
                        BasisDebug.LogError("[SSO] Web meeting manifest download failed: " + manifestRequest.error);
                        Destroy(gameObject);
                        yield break;
                    }

                    WebMeetingManifest manifest;
                    try { manifest = JsonUtility.FromJson<WebMeetingManifest>(manifestRequest.downloadHandler.text); }
                    catch { manifest = null; }
                    if (manifest == null || !IsAllowedConfigUrl(manifest.configUrl)
                        || string.IsNullOrWhiteSpace(manifest.websocketUri)
                        || string.IsNullOrWhiteSpace(manifest.userName))
                    {
                        BasisDebug.LogError("[SSO] Web meeting manifest is invalid.");
                        Destroy(gameObject);
                        yield break;
                    }

                    using (UnityWebRequest configRequest = UnityWebRequest.Get(manifest.configUrl))
                    {
                        yield return configRequest.SendWebRequest();
                        if (configRequest.result != UnityWebRequest.Result.Success)
                        {
                            BasisDebug.LogError("[SSO] Web meeting configuration download failed: " + configRequest.error);
                            Destroy(gameObject);
                            yield break;
                        }
                        if (!BasisSsoAuthController.ApplyRuntimeConfiguration(configRequest.downloadHandler.text, out string error))
                        {
                            BasisDebug.LogError("[SSO] Web meeting configuration was rejected: " + error);
                            Destroy(gameObject);
                            yield break;
                        }
                        BasisWebEnrollmentStoreConfig(configRequest.downloadHandler.text);
                    }

                    while (!BasisSsoAuthController.IsSignedIn) yield return null;
                    Uri websocketUri = new Uri(manifest.websocketUri);
                    ConnectionTarget target = new ConnectionTarget(BasisNetworkStackRegistry.WebSocketId, manifest.websocketUri);
                    target.Set(ConnectionTarget.Keys.Address, websocketUri.Host);
                    target.Set(ConnectionTarget.Keys.Port, websocketUri.Port.ToString(CultureInfo.InvariantCulture));
                    var entry = new ServerDirectoryEntry
                    {
                        Id = "__web_meeting__",
                        SourceId = SavedServersDirectorySource.Id,
                        DisplayName = "Web meeting",
                        Target = target,
                        WebSocketUri = manifest.websocketUri,
                        ServerInfoUri = BasisWebServerInfoClient.BuildServerInfoUri(websocketUri),
                        HasPassword = !string.IsNullOrEmpty(manifest.password),
                        Password = manifest.password,
                        CanEdit = false,
                        CanRemove = false,
                    };
                    if (!BasisConnectionService.RequestWebMeetingConnection(entry, manifest.userName))
                        BasisDebug.LogError("[SSO] Web meeting connection was already requested.");
                }
                Destroy(gameObject);
            }

            [Serializable]
            private sealed class WebMeetingManifest
            {
                public string configUrl;
                public string websocketUri;
                public string userName;
                public string password;
            }
        }
    }
}
#endif
