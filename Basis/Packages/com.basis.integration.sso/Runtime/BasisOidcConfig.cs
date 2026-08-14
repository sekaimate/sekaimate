using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

namespace Basis.Integration.Sso
{
    /// <summary>
    /// Runtime-loaded OIDC connection settings for the SSO login gate. Shipped with the
    /// build as a JSON file so an org admin can point the client at their IdP (Okta or any
    /// generic OIDC issuer) without a rebuild. See docs/sso-spec.md §5 for the schema.
    ///
    /// Load precedence (later wins): the copy under <see cref="Application.streamingAssetsPath"/>
    /// bundled with the build is the default; a copy under
    /// <see cref="Application.persistentDataPath"/> overrides it for per-machine tweaks.
    /// </summary>
    [Serializable]
    public sealed class BasisOidcConfig
    {
        public const string FileName = "basis-sso.json";

        /// <summary>
        /// The supported identity providers.  A deployment normally uses this field rather than
        /// the legacy top-level provider fields below.  Keeping the latter for one release makes
        /// existing single-provider files continue to work while administrators migrate.
        /// </summary>
        [JsonProperty("providers")] public List<ProviderConfig> Providers = new List<ProviderConfig>();
        [JsonProperty("defaultProviderId")] public string DefaultProviderId = string.Empty;
        /// <summary>Public key pinned for the SSO server transport. It is never a private OAuth secret.</summary>
        [JsonProperty("serverTransport")] public ServerTransportConfig ServerTransport = new ServerTransportConfig();

        [JsonProperty("issuer")] public string Issuer = string.Empty;
        [JsonProperty("clientId")] public string ClientId = string.Empty;
        /// <summary>
        /// Optional credential required by some native OAuth client registrations. This value is
        /// used only in the HTTPS token exchange with the identity provider; it is never sent to
        /// the Basis server or SSO broker. Native-client secrets are not confidential secrets.
        /// </summary>
        [JsonProperty("clientSecret")] public string ClientSecret = string.Empty;

        [JsonProperty("scopes")] public List<string> Scopes = new List<string> { "openid", "profile", "email" };

        /// <summary>
        /// Extra query params appended to the authorization request, for provider-specific needs.
        /// Google requires <c>access_type=offline</c> (and typically <c>prompt=consent</c>) to return a
        /// refresh token. An explicit account-switch prompt overrides any <c>prompt</c> set here.
        /// </summary>
        [JsonProperty("extraAuthParams")] public Dictionary<string, string> ExtraAuthParams = new Dictionary<string, string>();

        [JsonProperty("redirect")] public RedirectConfig Redirect = new RedirectConfig();
        [JsonProperty("displayNameClaims")] public List<string> DisplayNameClaims = new List<string> { "name", "preferred_username", "email" };
        [JsonProperty("access")] public AccessConfig Access = new AccessConfig();
        [JsonProperty("enforcement")] public EnforcementConfig Enforcement = new EnforcementConfig();

        [Serializable]
        public sealed class ProviderConfig
        {
            [JsonProperty("id")] public string Id = string.Empty;
            [JsonProperty("label")] public string Label = string.Empty;
            [JsonProperty("issuer")] public string Issuer = string.Empty;
            [JsonProperty("clientId")] public string ClientId = string.Empty;
            [JsonProperty("clientSecret")] public string ClientSecret = string.Empty;
            [JsonProperty("scopes")] public List<string> Scopes = new List<string> { "openid", "profile", "email" };
            [JsonProperty("extraAuthParams")] public Dictionary<string, string> ExtraAuthParams = new Dictionary<string, string>();
            [JsonProperty("displayNameClaims")] public List<string> DisplayNameClaims = new List<string> { "name", "preferred_username", "email" };
            [JsonProperty("access")] public AccessConfig Access = new AccessConfig();
        }

        [Serializable]
        public sealed class ServerTransportConfig
        {
            [JsonProperty("serverPublicKey")] public string ServerPublicKey = string.Empty;
            [JsonProperty("admissionEndpoint")] public string AdmissionEndpoint = string.Empty;
            /// <summary>
            /// Development-only escape hatch for a broker running on the same machine with a
            /// locally generated certificate. Validation only permits this for loopback HTTPS
            /// endpoints, so a configuration can never opt out of certificate verification for
            /// a remote broker.
            /// </summary>
            [JsonProperty("allowUntrustedLoopbackCertificate")] public bool AllowUntrustedLoopbackCertificate;
        }

        [Serializable]
        public sealed class RedirectConfig
        {
            /// <summary>Only "loopback" is supported this round (native app on the loopback interface).</summary>
            [JsonProperty("mode")] public string Mode = "loopback";
            /// <summary>Loopback host: "127.0.0.1" (default) or "localhost". Must match what the IdP allows.</summary>
            [JsonProperty("host")] public string Host = "127.0.0.1";
            /// <summary>Fixed loopback port, or 0 to pick a free ephemeral port each run (works with IdPs
            /// that allow dynamic loopback ports, e.g. Google Desktop app). Pin it if the IdP requires an
            /// exact registered redirect URI.</summary>
            [JsonProperty("port")] public int Port = 0;
            [JsonProperty("path")] public string Path = "/callback";
        }

        [Serializable]
        public sealed class AccessConfig
        {
            /// <summary>Any one match admits the user. Empty = no group restriction.</summary>
            [JsonProperty("allowedGroups")] public List<string> AllowedGroups = new List<string>();

            /// <summary>Every listed claim must match one of its allowed values. Empty = no restriction.</summary>
            [JsonProperty("allowedClaims")] public List<ClaimRule> AllowedClaims = new List<ClaimRule>();
        }

        [Serializable]
        public sealed class ClaimRule
        {
            [JsonProperty("claim")] public string Claim = string.Empty;
            [JsonProperty("values")] public List<string> Values = new List<string>();
        }

        [Serializable]
        public sealed class EnforcementConfig
        {
            /// <summary>Allow launching with a still-valid cached session when the IdP is unreachable.</summary>
            [JsonProperty("allowOfflineWithinTokenValidity")] public bool AllowOfflineWithinTokenValidity = true;
        }

        public bool HasGroupRestriction => Access?.AllowedGroups != null && Access.AllowedGroups.Count > 0;
        public bool HasClaimRestriction => Access?.AllowedClaims != null && Access.AllowedClaims.Count > 0;

        /// <summary>Structural validation of required fields. Does not contact the IdP.</summary>
        public bool TryValidate(out string error)
        {
            if (Providers != null && Providers.Count > 0)
            {
                var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (ProviderConfig provider in Providers)
                {
                    if (provider == null || string.IsNullOrWhiteSpace(provider.Id))
                    {
                        error = "OIDC config: every provider needs a non-empty 'id'.";
                        return false;
                    }
                    if (!ids.Add(provider.Id))
                    {
                        error = $"OIDC config: duplicate provider id '{provider.Id}'.";
                        return false;
                    }
                    if (!TryValidateProvider(provider.Issuer, provider.ClientId, provider.Scopes, out error))
                    {
                        error = $"OIDC config provider '{provider.Id}': {error}";
                        return false;
                    }
                }
                if (!string.IsNullOrWhiteSpace(DefaultProviderId) && FindProvider(DefaultProviderId) == null)
                {
                    error = $"OIDC config: defaultProviderId '{DefaultProviderId}' is not in providers.";
                    return false;
                }
                if (!ValidateRedirect(out error)) return false;
                if (ServerTransport == null || string.IsNullOrWhiteSpace(ServerTransport.ServerPublicKey))
                {
                    error = "OIDC config: serverTransport.serverPublicKey is required when SSO providers are configured.";
                    return false;
                }
                if (!Uri.TryCreate(ServerTransport.AdmissionEndpoint, UriKind.Absolute, out Uri admissionUri)
                    || admissionUri.Scheme != Uri.UriSchemeHttps)
                {
                    error = "OIDC config: serverTransport.admissionEndpoint must be an absolute HTTPS URL.";
                    return false;
                }
                if (ServerTransport.AllowUntrustedLoopbackCertificate && !IsLoopbackHost(admissionUri.Host))
                {
                    error = "OIDC config: serverTransport.allowUntrustedLoopbackCertificate is allowed only for localhost or a loopback IP.";
                    return false;
                }
                error = null;
                return true;
            }

            if (!TryValidateProvider(Issuer, ClientId, Scopes, out error)) return false;
            return ValidateRedirect(out error);
        }

        private bool ValidateRedirect(out string error)
        {
            if (Redirect == null)
            {
                error = "OIDC config: redirect is required.";
                return false;
            }
            if (string.Equals(Redirect.Mode, "loopback", StringComparison.OrdinalIgnoreCase))
            {
                error = null;
                return true;
            }
            if (string.Equals(Redirect.Mode, "browser", StringComparison.OrdinalIgnoreCase)
                && IsSafeBrowserPath(Redirect.Path))
            {
                error = null;
                return true;
            }
            error = "OIDC config: redirect.mode must be 'loopback' or 'browser'.";
            return false;
        }

        private static bool IsSafeBrowserPath(string path)
        {
            return !string.IsNullOrWhiteSpace(path)
                && path.StartsWith("/", StringComparison.Ordinal)
                && !path.Contains("..", StringComparison.Ordinal)
                && !path.Contains("#", StringComparison.Ordinal);
        }

        internal static bool IsLoopbackHost(string host)
        {
            if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)) return true;
            return System.Net.IPAddress.TryParse(host, out System.Net.IPAddress address)
                && System.Net.IPAddress.IsLoopback(address);
        }

        private static bool TryValidateProvider(string issuer, string clientId, List<string> scopes, out string error)
        {
            if (string.IsNullOrWhiteSpace(issuer)) { error = "'issuer' is required."; return false; }
            if (!Uri.TryCreate(issuer, UriKind.Absolute, out Uri issuerUri) || issuerUri.Scheme != Uri.UriSchemeHttps)
            {
                error = $"'issuer' must be an absolute HTTPS URL (got '{issuer}').";
                return false;
            }
            if (string.IsNullOrWhiteSpace(clientId)) { error = "'clientId' is required."; return false; }
            if (scopes == null || scopes.Count == 0 || !scopes.Contains("openid"))
            {
                error = "'scopes' must include 'openid'.";
                return false;
            }
            error = null;
            return true;
        }

        public ProviderConfig FindProvider(string id)
        {
            if (Providers == null) return null;
            return Providers.Find(p => p != null && string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Creates the single-provider view consumed by the OIDC protocol implementation.</summary>
        public BasisOidcConfig ForProvider(string id)
        {
            ProviderConfig provider = FindProvider(id);
            if (provider == null) return null;
            return new BasisOidcConfig
            {
                Issuer = provider.Issuer,
                ClientId = provider.ClientId,
                ClientSecret = provider.ClientSecret,
                Scopes = provider.Scopes,
                ExtraAuthParams = provider.ExtraAuthParams,
                Redirect = Redirect,
                DisplayNameClaims = provider.DisplayNameClaims,
                Access = provider.Access,
                Enforcement = Enforcement,
                ServerTransport = ServerTransport,
                DefaultProviderId = provider.Id,
            };
        }

        // ── Loading ──────────────────────────────────────────────────────────

        public static string StreamingPath => Path.Combine(Application.streamingAssetsPath, FileName);
        public static string OverridePath => Path.Combine(Application.persistentDataPath, FileName);

        /// <summary>
        /// Loads and validates the config. Returns null (and logs) if neither location has a
        /// readable, valid file. The persistentData copy overrides the streamingAssets copy.
        /// </summary>
        public static BasisOidcConfig Load()
        {
            BasisOidcConfig config = ReadFrom(OverridePath) ?? ReadFrom(StreamingPath);
            return ValidateLoaded(config);
        }

        /// <summary>Loads the WebGL streaming asset through the browser URL.</summary>
        public static async Task<BasisOidcConfig> LoadAsync(CancellationToken cancellationToken = default)
        {
            BasisOidcConfig config = ReadFrom(OverridePath);
#if UNITY_WEBGL && !UNITY_EDITOR
            if (config == null) config = await ReadFromStreamingUrlAsync(StreamingPath, cancellationToken);
#else
            if (config == null) config = ReadFrom(StreamingPath);
#endif
            return ValidateLoaded(config);
        }

        private static BasisOidcConfig ValidateLoaded(BasisOidcConfig config)
        {
            if (config == null)
            {
                BasisDebug.LogError(
                    $"[SSO] No OIDC config found. Provide '{FileName}' at '{StreamingPath}' " +
                    $"or '{OverridePath}'.");
                return null;
            }
            if (string.IsNullOrWhiteSpace(config.Issuer)
                && (config.Providers == null || config.Providers.Count == 0))
                return null;
            if (!config.TryValidate(out string error))
            {
                BasisDebug.LogError($"[SSO] {error}");
                return null;
            }
            return config;
        }

        /// <summary>Parses an in-memory setup payload without writing it to disk.</summary>
        public static bool TryParse(string json, out BasisOidcConfig config, out string error)
        {
            config = null;
            try
            {
                config = JsonConvert.DeserializeObject<BasisOidcConfig>(json);
                if (config == null)
                {
                    error = "OIDC config is empty.";
                    return false;
                }
                if (!config.TryValidate(out error))
                {
                    config = null;
                    return false;
                }
                return true;
            }
            catch (Exception e)
            {
                error = "Invalid OIDC config: " + e.Message;
                return false;
            }
        }

        private static BasisOidcConfig ReadFrom(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                string json = File.ReadAllText(path);
                BasisOidcConfig config = JsonConvert.DeserializeObject<BasisOidcConfig>(json);
                if (config != null)
                    BasisDebug.Log($"[SSO] Loaded OIDC config from '{path}'.");
                return config;
            }
            catch (Exception e)
            {
                BasisDebug.LogWarning($"[SSO] Failed to read OIDC config from '{path}': {e.Message}");
                return null;
            }
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        private static async Task<BasisOidcConfig> ReadFromStreamingUrlAsync(string url, CancellationToken cancellationToken)
        {
            using UnityWebRequest request = UnityWebRequest.Get(url);
            using CancellationTokenRegistration cancellation = cancellationToken.Register(request.Abort);
            await request.SendWebRequest();
            cancellationToken.ThrowIfCancellationRequested();

            if (request.result != UnityWebRequest.Result.Success)
            {
                if (request.responseCode != 404)
                    BasisDebug.LogWarning($"[SSO] Failed to read streaming config '{url}': {request.error}");
                return null;
            }

            try
            {
                BasisOidcConfig config = JsonConvert.DeserializeObject<BasisOidcConfig>(request.downloadHandler.text);
                if (config != null) BasisDebug.Log($"[SSO] Loaded OIDC config from '{url}'.");
                return config;
            }
            catch (Exception e)
            {
                BasisDebug.LogWarning($"[SSO] Failed to parse streaming config '{url}': {e.Message}");
                return null;
            }
        }
#endif
    }
}
