using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Basis.Scripts.Networking;
using UnityEngine;
using UnityEngine.Networking;

namespace Basis.Integration.Sso
{
    /// <summary>
    /// Receives one-time setup links issued by an SSO broker. No broker URL or provider
    /// configuration is compiled into the client: the link hands the running client a short-lived
    /// HTTPS configuration URL, which is applied only for the current process.
    /// </summary>
    internal sealed class BasisSsoConfigEnrollment : MonoBehaviour
    {
        private const int Port = 56831;
        private const string CallbackPath = "/basis-sso-config";
        private const string JoinCallbackPath = "/basis-join";
        private const int MaxConfigBytes = 256 * 1024;
        private static readonly ConcurrentQueue<string> PendingUrls = new ConcurrentQueue<string>();
        private static readonly ConcurrentQueue<string> PendingJoinLinks = new ConcurrentQueue<string>();
        private static readonly ConcurrentQueue<string> PendingJoinManifestUrls = new ConcurrentQueue<string>();
        private static readonly ConcurrentQueue<ConfiguredJoinRequest> PendingConfiguredJoinRequests = new ConcurrentQueue<ConfiguredJoinRequest>();
        private static HttpListener _listener;
        private static CancellationTokenSource _cancellation;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Initialize()
        {
            var host = new GameObject(nameof(BasisSsoConfigEnrollment));
            DontDestroyOnLoad(host);
            host.AddComponent<BasisSsoConfigEnrollment>();
            StartListener();
        }

        private static void StartListener()
        {
            try
            {
                _cancellation = new CancellationTokenSource();
                _listener = new HttpListener();
                _listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
                _listener.Start();
                _ = ListenAsync(_cancellation.Token);
            }
            catch (Exception e)
            {
                BasisDebug.LogWarning("[SSO] Setup-link listener could not start: " + e.Message);
                try { _listener?.Close(); } catch { }
                _listener = null;
            }
        }

        private static async Task ListenAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested && _listener != null)
            {
                try
                {
                    HttpListenerContext context = await _listener.GetContextAsync();
                    HandleCallback(context);
                }
                catch (ObjectDisposedException) { return; }
                catch (HttpListenerException) { return; }
                catch (Exception e) { BasisDebug.LogWarning("[SSO] Setup-link listener error: " + e.Message); }
            }
        }

        private static void HandleCallback(HttpListenerContext context)
        {
            try
            {
                HttpListenerRequest request = context.Request;
                HttpListenerResponse response = context.Response;
                response.Headers["Access-Control-Allow-Origin"] = "*";
                response.Headers["Cache-Control"] = "no-store";
                if (!string.Equals(request.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase))
                {
                    Respond(response, 404, "Not found.");
                    return;
                }
                if (request.Url?.AbsolutePath == JoinCallbackPath)
                {
                    string link = request.QueryString["link"];
                    string configUrl = request.QueryString["config"];
                    if (!string.IsNullOrWhiteSpace(configUrl))
                    {
                        if (!BasisDeepLinkProvider.TryParseBasisUrl(link, out _) || !IsSecureBrokerUrl(configUrl, out Uri configurationUri))
                        {
                            Respond(response, 400, "Invalid Basis meeting invitation.");
                            return;
                        }
                        PendingConfiguredJoinRequests.Enqueue(new ConfiguredJoinRequest(configurationUri.AbsoluteUri, link));
                        RespondJoinAccepted(response);
                        return;
                    }
                    if (BasisDeepLinkProvider.TryParseBasisUrl(link, out _))
                    {
                        PendingJoinLinks.Enqueue(link);
                        RespondJoinAccepted(response);
                        return;
                    }
                    string manifestUrl = request.QueryString["url"];
                    if (!IsSecureBrokerUrl(manifestUrl, out Uri brokerUri))
                    {
                        Respond(response, 400, "Invalid Basis meeting invitation.");
                        return;
                    }
                    PendingJoinManifestUrls.Enqueue(brokerUri.AbsoluteUri);
                    RespondJoinAccepted(response);
                    return;
                }
                if (request.Url?.AbsolutePath == CallbackPath)
                {
                    string url = request.QueryString["url"];
                    if (!IsSecureBrokerUrl(url, out Uri brokerUri))
                    {
                        Respond(response, 400, "Invalid secure Basis SSO setup URL.");
                        return;
                    }
                    PendingUrls.Enqueue(brokerUri.AbsoluteUri);
                    Respond(response, 200, "Basis received the SSO configuration. You can return to the app.");
                    return;
                }
                Respond(response, 404, "Not found.");
            }
            catch
            {
                try { context.Response.Abort(); } catch { }
            }
        }

        private static void Respond(HttpListenerResponse response, int status, string message)
        {
            byte[] body = Encoding.UTF8.GetBytes("<!doctype html><title>Basis SSO</title><p>" + WebUtility.HtmlEncode(message) + "</p>");
            response.StatusCode = status;
            response.ContentType = "text/html; charset=utf-8";
            response.ContentLength64 = body.Length;
            response.OutputStream.Write(body, 0, body.Length);
            response.OutputStream.Close();
        }

        private static void RespondJoinAccepted(HttpListenerResponse response)
        {
            const string body = "<!doctype html><title>Basis</title><p>Basis is opening the meeting…</p>"
                + "<script>if(window.parent!==window)window.parent.postMessage('basis-join-received','*');</script>";
            byte[] bytes = Encoding.UTF8.GetBytes(body);
            response.StatusCode = 200;
            response.ContentType = "text/html; charset=utf-8";
            response.ContentLength64 = bytes.Length;
            response.OutputStream.Write(bytes, 0, bytes.Length);
            response.OutputStream.Close();
        }

        private static bool IsSecureBrokerUrl(string url, out Uri brokerUri)
        {
            return Uri.TryCreate(url, UriKind.Absolute, out brokerUri)
                && brokerUri.Scheme == Uri.UriSchemeHttps
                && string.IsNullOrEmpty(brokerUri.UserInfo);
        }

        private static UnityWebRequest CreateBrokerGetRequest(string url)
        {
            var request = UnityWebRequest.Get(url);
            // Unity does not share the browser's certificate exception. A locally generated
            // certificate is therefore accepted only for HTTPS loopback configuration URLs.
            if (Uri.TryCreate(url, UriKind.Absolute, out Uri uri) && uri.Scheme == Uri.UriSchemeHttps
                && BasisOidcConfig.IsLoopbackHost(uri.Host))
                request.certificateHandler = new LoopbackCertificateHandler();
            return request;
        }

        private sealed class LoopbackCertificateHandler : CertificateHandler
        {
            protected override bool ValidateCertificate(byte[] certificateData) => certificateData != null && certificateData.Length > 0;
        }

        private void Update()
        {
            while (PendingUrls.TryDequeue(out string url)) StartCoroutine(DownloadAndApply(url));
            while (PendingJoinLinks.TryDequeue(out string link))
            {
                BasisDebug.Log("[SSO] Opening meeting invitation received from the local browser bridge.");
                if (!BasisDeepLinkProvider.TryActivateInvite(link))
                    BasisDebug.LogError("[SSO] Meeting invitation could not be opened.");
            }
            while (PendingConfiguredJoinRequests.TryDequeue(out ConfiguredJoinRequest request))
                StartCoroutine(DownloadConfigureAndJoin(request));
            while (PendingJoinManifestUrls.TryDequeue(out string url)) StartCoroutine(DownloadAndJoin(url));
        }

        private static System.Collections.IEnumerator DownloadAndApply(string url)
        {
            using (var request = CreateBrokerGetRequest(url))
            {
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success)
                {
                    BasisDebug.LogError("[SSO] Setup-link download failed: " + request.error);
                    yield break;
                }
                byte[] data = request.downloadHandler.data;
                if (data == null || data.Length > MaxConfigBytes)
                {
                    BasisDebug.LogError("[SSO] Setup-link configuration is missing or too large.");
                    yield break;
                }
                string json = Encoding.UTF8.GetString(data);
                if (!BasisSsoAuthController.ApplyRuntimeConfiguration(json, out string error))
                {
                    BasisDebug.LogError("[SSO] Setup-link configuration was rejected: " + error);
                    yield break;
                }
                BasisDebug.Log("[SSO] Broker-issued configuration applied for this session.");
            }
        }

        private static System.Collections.IEnumerator DownloadAndJoin(string manifestUrl)
        {
            using (var request = CreateBrokerGetRequest(manifestUrl))
            {
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success)
                {
                    BasisDebug.LogError("[SSO] Meeting invitation download failed: " + request.error);
                    yield break;
                }
                byte[] data = request.downloadHandler.data;
                if (data == null || data.Length > MaxConfigBytes)
                {
                    BasisDebug.LogError("[SSO] Meeting invitation is missing or too large.");
                    yield break;
                }
                JoinManifest? manifest;
                try { manifest = JsonUtility.FromJson<JoinManifest>(Encoding.UTF8.GetString(data)); }
                catch { manifest = null; }
                if (manifest?.connection == null || !IsValidMeetingHost(manifest.connection.host)
                    || manifest.connection.port < 1 || manifest.connection.port > ushort.MaxValue)
                {
                    BasisDebug.LogError("[SSO] Meeting invitation is invalid.");
                    yield break;
                }
                string link = BasisDeepLinkProvider.FormatDeepLink(
                    manifest.connection.host, (ushort)manifest.connection.port, manifest.connection.password);
                if (!BasisDeepLinkProvider.TryActivateInvite(link))
                    BasisDebug.LogError("[SSO] Meeting invitation could not be opened.");
            }
        }

        private static System.Collections.IEnumerator DownloadConfigureAndJoin(ConfiguredJoinRequest request)
        {
            using (var download = CreateBrokerGetRequest(request.ConfigurationUrl))
            {
                yield return download.SendWebRequest();
                if (download.result != UnityWebRequest.Result.Success)
                {
                    BasisDebug.LogError("[SSO] Meeting configuration download failed: " + download.error);
                    yield break;
                }
                if (!BasisSsoAuthController.ApplyRuntimeConfiguration(download.downloadHandler.text, out string error))
                {
                    BasisDebug.LogError("[SSO] Meeting configuration was rejected: " + error);
                    yield break;
                }
            }

            while (!BasisSsoAuthController.IsSignedIn) yield return null;
            BasisDebug.Log("[SSO] Opening meeting invitation with the organization configuration.");
            if (!BasisDeepLinkProvider.TryActivateInvite(request.Link))
                BasisDebug.LogError("[SSO] Meeting invitation could not be opened.");
        }

        private static bool IsValidMeetingHost(string host)
        {
            return !string.IsNullOrWhiteSpace(host)
                && host.Length <= 253
                && host.IndexOfAny(new[] { '/', '?', '#', '@', '\\', '\r', '\n' }) < 0;
        }

        private sealed class ConfiguredJoinRequest
        {
            public readonly string ConfigurationUrl;
            public readonly string Link;
            public ConfiguredJoinRequest(string configurationUrl, string link)
            {
                ConfigurationUrl = configurationUrl;
                Link = link;
            }
        }

        [Serializable]
        private sealed class JoinManifest
        {
            public JoinManifestConnection connection;
        }

        [Serializable]
        private sealed class JoinManifestConnection
        {
            public string host;
            public int port;
            public string password;
        }

        private void OnApplicationQuit()
        {
            _cancellation?.Cancel();
            try { _listener?.Close(); } catch { }
        }
    }
}
