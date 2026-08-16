using System;
using System.Threading;
using System.Threading.Tasks;

namespace Basis.Integration.Sso
{
    /// <summary>
    /// The SSO "brain": loads config, drives silent-renew / interactive sign-in / sign-out, applies
    /// the org access rules, and binds identity + display name. UI (the login screen, the sign-out
    /// button) and the launch gate call into this and observe <see cref="StateChanged"/>; all the
    /// orchestration lives here so those surfaces stay thin.
    /// </summary>
    public static class BasisSsoAuthController
    {
        private static BasisOidcConfig _config;
        private static BasisOidcConfig _activeProviderConfig;
        private static BasisOidcLoginService _service;
        private static bool _runtimeConfigurationActive;

        public static BasisSsoSession Current { get; private set; }
        public static bool IsSignedIn => Current != null;
        public static string SelectedProviderId { get; private set; }
        public static bool HasProviderChoice => _config?.Providers != null && _config.Providers.Count > 1;
        public static System.Collections.Generic.IReadOnlyList<BasisOidcConfig.ProviderConfig> Providers => _config?.Providers;

        /// <summary>True once config has loaded and validated. When false, SSO cannot proceed.</summary>
        public static bool IsConfigured => _config != null;

        public static bool HasPendingBrowserCallback
        {
            get
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                return BasisWebOidcBridge.HasPendingCallback;
#else
                return false;
#endif
            }
        }

        public static string ActiveDisplayName =>
            IsSignedIn ? BasisSsoIdentityBinding.ResolveDisplayNameFromClaims(_activeProviderConfig ?? _config, Current) : null;

        /// <summary>Raised after any sign-in/sign-out/renew so the gate and settings UI can refresh.</summary>
        public static event Action StateChanged;
        /// <summary>Raised after a broker-issued configuration has been accepted for this process.</summary>
        public static event Action RuntimeConfigurationApplied;

        /// <summary>Loads and validates config. Safe to call repeatedly; returns cached result.</summary>
        public static bool EnsureConfigLoaded()
        {
            if (_config != null) return true;
            _config = BasisOidcConfig.Load();
            if (_config == null) return false;
            ConfigureLoadedConfig();
            return true;
        }

        /// <summary>Loads streaming assets asynchronously when WebGL exposes them as browser URLs.</summary>
        public static async Task<bool> EnsureConfigLoadedAsync(CancellationToken cancellationToken = default)
        {
            if (_config != null) return true;
            _config = await BasisOidcConfig.LoadAsync(cancellationToken);
            if (_config == null) return false;
            ConfigureLoadedConfig();
            return true;
        }

        private static void ConfigureLoadedConfig()
        {
            SelectedProviderId = !string.IsNullOrEmpty(_config.DefaultProviderId)
                ? _config.DefaultProviderId
                : (_config.Providers != null && _config.Providers.Count > 0 ? _config.Providers[0].Id : null);
            ConfigureService(SelectedProviderId);
        }

        /// <summary>
        /// Activates a broker-issued configuration for this process only. It deliberately does
        /// not write <c>basis-sso.json</c> to persistent storage: setup links are ephemeral.
        /// </summary>
        public static bool ApplyRuntimeConfiguration(string json, out string error)
        {
            if (!BasisOidcConfig.TryParse(json, out BasisOidcConfig config, out error)) return false;
            BasisOidcConfig.ProviderConfig nextProvider = config.FindProvider(
                !string.IsNullOrEmpty(config.DefaultProviderId) ? config.DefaultProviderId : Current?.ProviderId);
            bool keepSession = Current != null && nextProvider != null && _activeProviderConfig != null
                && string.Equals(Current.ProviderId, nextProvider.Id, StringComparison.OrdinalIgnoreCase)
                && string.Equals(_activeProviderConfig.Issuer, nextProvider.Issuer, StringComparison.Ordinal)
                && string.Equals(_activeProviderConfig.ClientId, nextProvider.ClientId, StringComparison.Ordinal);
            if (Current != null && !keepSession)
            {
                Current = null;
                BasisSsoSessionStore.Clear();
                BasisSsoIdentityBinding.Unbind();
            }
            _config = config;
            _runtimeConfigurationActive = true;
            SelectedProviderId = !string.IsNullOrEmpty(config.DefaultProviderId)
                ? config.DefaultProviderId
                : (config.Providers != null && config.Providers.Count > 0 ? config.Providers[0].Id : null);
            ConfigureService(SelectedProviderId);
            if (keepSession) BasisSsoIdentityBinding.Bind(_activeProviderConfig ?? _config, Current);
            StateChanged?.Invoke();
            RuntimeConfigurationApplied?.Invoke();
            return true;
        }

        /// <summary>Selects the provider for the next interactive sign-in. A stored session always selects its own provider.</summary>
        public static bool SelectProvider(string providerId)
        {
            if (!EnsureConfigLoaded()) return false;
            if (_config.Providers == null || _config.Providers.Count == 0) return string.IsNullOrEmpty(providerId);
            if (_config.FindProvider(providerId) == null) return false;
            SelectedProviderId = providerId;
            ConfigureService(providerId);
            return true;
        }

        /// <summary>
        /// Startup path: reuse a stored session (silent renew / offline). Does not launch the
        /// browser. Returns the outcome so the gate can decide whether to show the login screen.
        /// </summary>
        public static async Task<SsoAuthResult> InitializeAsync(CancellationToken ct = default)
        {
            if (!EnsureConfigLoaded())
                return SsoAuthResult.Fail("SSO is not configured.");

            BasisSsoSession stored = BasisSsoSessionStore.Load();
            if (stored == null) return SsoAuthResult.Fail("No stored session.");

            if (_config.Providers != null && _config.Providers.Count > 0)
            {
                if (!SelectProvider(stored.ProviderId))
                    return SsoAuthResult.Fail("Stored session belongs to a provider no longer configured.");
            }

            SsoAuthResult result = await _service.TrySilentAsync(stored, ct);
            return await FinalizeAsync(result, persist: true);
        }

        /// <summary>Interactive browser sign-in.</summary>
        public static async Task<SsoAuthResult> SignInAsync(CancellationToken ct = default, string prompt = null)
        {
            if (!EnsureConfigLoaded())
                return SsoAuthResult.Fail("SSO is not configured.");

            SsoAuthResult result = await _service.SignInInteractiveAsync(ct, prompt);
            return await FinalizeAsync(result, persist: true);
        }

        /// <summary>Sign out: drop the local session and identity binding. UI returns to the login gate.</summary>
        public static void SignOut()
        {
            // Remember the (possibly edited) display name for this account before tearing down.
            BasisSsoIdentityBinding.CaptureGlobalNameForActive();
            Current = null;
            BasisSsoSessionStore.Clear();
            BasisSsoIdentityBinding.Unbind();
            StateChanged?.Invoke();
        }

        public static async Task<string> GetEndSessionEndpointAsync(CancellationToken ct = default)
        {
            if (!EnsureConfigLoaded() || _service == null) return null;
            return await _service.GetEndSessionEndpointAsync(ct);
        }

        internal static bool TryGetAdmissionRequest(out string endpoint, out string idToken, out string serverPublicKey,
            out bool allowUntrustedLoopbackCertificate)
        {
            // The admission transport key can be rotated independently of an existing OIDC
            // session. Reload it here so an Editor session with domain reload disabled cannot
            // keep encrypting tickets to a stale server key.
            BasisOidcConfig freshConfig = _runtimeConfigurationActive ? null : BasisOidcConfig.Load();
            if (_config != null && freshConfig?.ServerTransport != null
                && !string.IsNullOrWhiteSpace(freshConfig.ServerTransport.AdmissionEndpoint)
                && !string.IsNullOrWhiteSpace(freshConfig.ServerTransport.ServerPublicKey))
            {
                _config.ServerTransport = freshConfig.ServerTransport;
            }
            endpoint = _config?.ServerTransport?.AdmissionEndpoint;
            idToken = Current?.IdToken;
            serverPublicKey = _config?.ServerTransport?.ServerPublicKey;
            allowUntrustedLoopbackCertificate = _config?.ServerTransport?.AllowUntrustedLoopbackCertificate == true;
            return IsSignedIn && !string.IsNullOrWhiteSpace(endpoint) && !string.IsNullOrWhiteSpace(idToken)
                && !string.IsNullOrWhiteSpace(serverPublicKey);
        }

        // ── shared post-processing ─────────────────────────────────────────

        private static Task<SsoAuthResult> FinalizeAsync(SsoAuthResult result, bool persist)
        {
            if (result == null || !result.Success || result.Session == null)
                return Task.FromResult(result ?? SsoAuthResult.Fail("Unknown sign-in failure."));

            SsoAccessDecision decision = BasisSsoAccessControl.Evaluate(_activeProviderConfig ?? _config, result.Session);
            if (!decision.Allowed)
            {
                // Never persist or activate a denied session.
                BasisSsoSessionStore.Clear();
                BasisSsoIdentityBinding.Unbind();
                Current = null;
                StateChanged?.Invoke();
                return Task.FromResult(SsoAuthResult.Deny(decision.Reason));
            }

            result.Session.ProviderId = SelectedProviderId ?? string.Empty;
            Current = result.Session;
            if (persist) BasisSsoSessionStore.Save(result.Session);
            BasisSsoIdentityBinding.Bind(_activeProviderConfig ?? _config, result.Session);
            StateChanged?.Invoke();
            return Task.FromResult(SsoAuthResult.Ok(result.Session));
        }

        private static void ConfigureService(string providerId)
        {
            _activeProviderConfig = _config.ForProvider(providerId) ?? _config;
            _service = new BasisOidcLoginService(_activeProviderConfig);
        }
    }
}
