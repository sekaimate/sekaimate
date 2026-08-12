using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Basis.Network.Core;
using BasisNetworkClient;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace Basis.Integration.Sso
{
    /// <summary>Exchanges a locally validated OIDC session for a DID-bound, short-lived admission ticket over HTTPS.</summary>
    /// <summary>Builds the encrypted server-admission envelope for the framework integration assembly.</summary>
    public static class BasisSsoAdmissionService
    {
        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };

        public static async Task<byte[]> CreateConnectionPayloadAsync(string password, CancellationToken ct = default)
        {
            if (!BasisSsoAuthController.TryGetAdmissionRequest(out string endpoint, out string idToken, out string serverPublicKey,
                out bool allowUntrustedLoopbackCertificate)) return null;
            string did = BasisDIDAuthIdentityClient.GetOrSaveDID();
            if (string.IsNullOrEmpty(did)) return null;
            var body = new JObject { ["idToken"] = idToken, ["did"] = did };
            if (allowUntrustedLoopbackCertificate)
            {
                return await PostToTrustedLoopbackBroker(endpoint, body.ToString(Newtonsoft.Json.Formatting.None), password, serverPublicKey, ct);
            }
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = new StringContent(body.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json")
            };
            using HttpResponseMessage response = await Http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) return null;
            JObject json = JObject.Parse(await response.Content.ReadAsStringAsync());
            string ticket = (string)json["ticket"];
            if (string.IsNullOrWhiteSpace(ticket)) return null;
            byte[] payload = SsoConnectionAuthPayload.EncodeEncrypted(password, ticket, serverPublicKey);
            if (payload == null)
            {
                BasisDebug.LogError("[SSO] Could not encrypt the server admission payload. Check the pinned server public key.");
                return null;
            }
            BasisDebug.Log($"[SSO] Prepared encrypted admission payload ({payload.Length} bytes).", BasisDebug.LogTag.System);
            return payload;
        }

        private static async Task<byte[]> PostToTrustedLoopbackBroker(string endpoint, string json, string password,
            string serverPublicKey, CancellationToken ct)
        {
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out Uri uri) || !BasisOidcConfig.IsLoopbackHost(uri.Host))
            {
                BasisDebug.LogError("[SSO] Refusing untrusted certificate setting for a non-loopback admission endpoint.");
                return null;
            }

            using var request = new UnityWebRequest(endpoint, UnityWebRequest.kHttpVerbPOST)
            {
                uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json)),
                downloadHandler = new DownloadHandlerBuffer(),
                certificateHandler = new LoopbackCertificateHandler(),
            };
            request.SetRequestHeader("Content-Type", "application/json");
            var operation = request.SendWebRequest();
            while (!operation.isDone)
            {
                ct.ThrowIfCancellationRequested();
                await Task.Yield();
            }
            if (request.result != UnityWebRequest.Result.Success)
            {
                BasisDebug.LogError("[SSO] Local broker admission request failed: " + request.error);
                return null;
            }
            try
            {
                JObject response = JObject.Parse(request.downloadHandler.text);
                string ticket = (string)response["ticket"];
                if (string.IsNullOrWhiteSpace(ticket)) return null;
                return SsoConnectionAuthPayload.EncodeEncrypted(password, ticket, serverPublicKey);
            }
            catch (Exception e)
            {
                BasisDebug.LogError("[SSO] Local broker admission response was invalid: " + e.Message);
                return null;
            }
        }

        // This handler is reachable only after both JSON validation and a second URI check
        // above established that the endpoint is loopback. It is never used for a remote host.
        private sealed class LoopbackCertificateHandler : CertificateHandler
        {
            protected override bool ValidateCertificate(byte[] certificateData) => certificateData != null && certificateData.Length > 0;
        }
    }
}
