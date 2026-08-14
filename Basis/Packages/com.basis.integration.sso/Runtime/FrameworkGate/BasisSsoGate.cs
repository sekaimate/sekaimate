using System;
using System.Collections;
using System.Threading;
using Basis.BasisUI;
using Basis.Integration.Sso;
using Basis.Scripts.Device_Management;
using Basis.Scripts.Networking;
using UnityEngine;
using UnityEngine.Networking;

namespace Basis.Integration.Sso.FrameworkGate
{
    /// <summary>
    /// The launch gate. When an OIDC config is present it blocks the normal flow (auto-connect and
    /// manual connects) until the user has signed in via <see cref="BasisSsoAuthController"/>. With
    /// no config it disables itself so non-SSO / dev builds are unaffected. Login is presented
    /// through the existing <see cref="BasisMainMenu"/> dialogue system so it renders and takes
    /// input on both desktop and PCVR. When a server requires SSO, the gate also supplies its
    /// encrypted, DID-bound admission envelope before the normal DID challenge.
    /// </summary>
    public static class BasisSsoGate
    {
        internal const string BlockedReason = "Please sign in before connecting.";
        private static bool _hooksInstalled;
        private static bool _runnerStarted;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            BasisSsoAuthController.RuntimeConfigurationApplied -= ActivateRuntimeConfiguration;
            BasisSsoAuthController.RuntimeConfigurationApplied += ActivateRuntimeConfiguration;
#if UNITY_WEBGL && !UNITY_EDITOR
            InstallConnectionHooks();
            GameObject loader = new GameObject("BasisSsoConfigLoader");
            UnityEngine.Object.DontDestroyOnLoad(loader);
            loader.AddComponent<BasisSsoConfigLoader>();
#else
            if (!BasisSsoAuthController.EnsureConfigLoaded())
            {
                BasisDebug.Log("[SSO] No OIDC config found; launch gate disabled.");
                return;
            }

            ActivateConfiguredGate();
#endif
        }

        /// <summary>Enables the gate after a broker-issued runtime configuration arrives.</summary>
        private static void ActivateRuntimeConfiguration()
        {
            if (!BasisSsoAuthController.IsConfigured) return;
            ActivateConfiguredGate();
        }

        private static void ActivateConfiguredGate()
        {
            InstallConnectionHooks();
            if (_runnerStarted) return;
            _runnerStarted = true;
            BasisSsoAccountTab.Register();
            BasisSsoGateRunner.Begin();
        }

        private static void InstallConnectionHooks()
        {
            if (_hooksInstalled) return;
            _hooksInstalled = true;
            // Block startup auto-connect and every manual/CLI connect until sign-in completes.
            BasisConnectionService.AutoConnectAttempted = true;
            BasisConnectionService.ConnectionBlockedReason = () =>
                !BasisSsoAuthController.IsConfigured
                    ? "SSO configuration is loading."
                    : (!BasisSsoAuthController.IsSignedIn ? BlockedReason : null);
            BasisConnectionService.ConnectionAuthenticationPayloadProvider = password =>
                BasisSsoAdmissionService.CreateConnectionPayloadAsync(password);
        }

        private static void DisableConnectionHooks()
        {
            if (!_hooksInstalled) return;
            _hooksInstalled = false;
            BasisConnectionService.AutoConnectAttempted = false;
            BasisConnectionService.ConnectionBlockedReason = null;
            BasisConnectionService.ConnectionAuthenticationPayloadProvider = null;
        }

        /// <summary>Called once sign-in succeeds: let connections through again.</summary>
        internal static void OnSignedIn()
        {
            // Allow the Servers panel's auto-connect to run on next open now that we're signed in.
            BasisConnectionService.AutoConnectAttempted = false;
        }

        /// <summary>Called on sign-out: re-arm the block so nothing connects until sign-in again.</summary>
        internal static void OnSignedOut()
        {
            BasisConnectionService.AutoConnectAttempted = true;
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        private sealed class BasisSsoConfigLoader : MonoBehaviour
        {
            private void Start()
            {
                StartCoroutine(LoadConfigCoroutine());
            }

            private IEnumerator LoadConfigCoroutine()
            {
                using UnityWebRequest request = UnityWebRequest.Get(BasisOidcConfig.StreamingPath);
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success)
                {
                    if (request.responseCode != 404)
                        BasisDebug.LogWarning($"[SSO] Failed to read streaming config '{BasisOidcConfig.StreamingPath}': {request.error}");
                    Destroy(gameObject);
                    DisableConnectionHooks();
                    yield break;
                }

                if (!BasisSsoAuthController.ApplyRuntimeConfiguration(request.downloadHandler.text, out string error))
                {
                    BasisDebug.LogError($"[SSO] Failed to load streaming config: {error}");
                    DisableConnectionHooks();
                }
                Destroy(gameObject);
            }
        }
#endif
    }

    /// <summary>
    /// Persistent driver for the gate: runs silent renew, then presents the login dialogs, and
    /// re-engages whenever the user signs out. Kept as a MonoBehaviour so it has a Unity context to
    /// await on and can wait for the menu system to become available at startup.
    /// </summary>
    public sealed class BasisSsoGateRunner : MonoBehaviour
    {
        private static BasisSsoGateRunner _instance;

        private CancellationTokenSource _cts;
        private bool _flowActive;

        internal static void Begin()
        {
            if (_instance != null) return;
            GameObject go = new GameObject("BasisSsoGate");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<BasisSsoGateRunner>();
        }

        /// <summary>Starts an account-tab initiated sign-in with the selected provider.</summary>
        internal static void RequestInteractiveSignIn(string providerId)
        {
            Begin();
            _instance?.BeginInteractiveSignInFromAccount(providerId);
        }

        private void Start()
        {
            BasisSsoAuthController.StateChanged += OnStateChanged;
#if UNITY_WEBGL && !UNITY_EDITOR
            StartCoroutine(StartFlowWhenDeviceReady());
#else
            StartFlow();
#endif
        }

        private void OnDestroy()
        {
            BasisSsoAuthController.StateChanged -= OnStateChanged;
            _cts?.Cancel();
            _cts?.Dispose();
        }

        private void OnStateChanged()
        {
            // If the user signed out (and SSO is still required), gate again.
            if (BasisSsoAuthController.IsConfigured && !BasisSsoAuthController.IsSignedIn && !_flowActive)
            {
                BasisSsoGate.OnSignedOut();
                StartFlow();
            }
        }

        private void StartFlow()
        {
            if (_flowActive) return;
            _flowActive = true;
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            RunGateAsync();
        }

        private void BeginInteractiveSignInFromAccount(string providerId)
        {
            if (!string.IsNullOrWhiteSpace(providerId) && !BasisSsoAuthController.SelectProvider(providerId))
            {
                ShowDialog("Sign-in error", "The selected identity provider is no longer configured.", "OK", _ => { });
                return;
            }

            _cts?.Cancel();
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            _flowActive = true;
            BeginInteractiveSignIn();
        }

        private async void RunGateAsync()
        {
            try
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                if (BasisSsoAuthController.HasPendingBrowserCallback)
                {
                    BeginInteractiveSignIn();
                    return;
                }
#endif
                // 1) Try to reuse a stored session (silent renew / offline).
                SsoAuthResult silent = await BasisSsoAuthController.InitializeAsync(_cts.Token);
                if (silent.Success)
                {
                    Succeed(false);
                    return;
                }

                // 2) Need interactive sign-in — present the prompt through the menu.
                StartCoroutine(EnsureMenuReadyThen(PresentSignInPrompt));
            }
            catch (OperationCanceledException) { /* re-gate cancelled */ }
            catch (Exception e)
            {
                BasisDebug.LogError($"[SSO] Gate failure: {e}");
                StartCoroutine(EnsureMenuReadyThen(() => ShowDialog("Sign-in error", e.Message, "Retry", "Quit", ok =>
                {
                    if (ok) StartFlow(); else Quit();
                })));
            }
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        private IEnumerator StartFlowWhenDeviceReady()
        {
            while (!BasisDeviceManagement.OnInitializationComplete)
            {
                yield return null;
            }

            StartFlow();
        }
#endif

        // ── Dialog steps ───────────────────────────────────────────────────

        private void PresentSignInPrompt()
        {
            if (BasisSsoAuthController.HasProviderChoice)
            {
                var providers = BasisSsoAuthController.Providers;
                // The existing dialogue component has two actions. The supported deployment
                // profile deliberately exposes Google and Okta; reject a larger list instead of
                // silently choosing an identity provider for the user.
                if (providers.Count == 2)
                {
                    ShowDialog(
                        "Choose sign-in provider",
                        "Choose the organization account you use for BasisVR.",
                        ProviderLabel(providers[0]), ProviderLabel(providers[1]), chooseFirst =>
                        {
                            BasisSsoAuthController.SelectProvider(providers[chooseFirst ? 0 : 1].Id);
                            BeginInteractiveSignIn();
                        });
                    return;
                }

                ShowDialog("SSO configuration error",
                    "This client UI currently supports exactly two sign-in providers.",
                    "Quit", _ => Quit());
                return;
            }

            ShowDialog(
                "Sign in required",
                "This BasisVR client requires you to sign in with your organization account to continue.",
                "Sign in",
                "Quit",
                accepted =>
                {
                    if (accepted) BeginInteractiveSignIn();
                    else Quit();
                });
        }

        private static string ProviderLabel(BasisOidcConfig.ProviderConfig provider)
        {
            if (string.Equals(provider?.Id, "google", StringComparison.OrdinalIgnoreCase)
                && string.Equals(provider.Label, "Google Workspace", StringComparison.OrdinalIgnoreCase))
                return "Google organization account";
            return !string.IsNullOrWhiteSpace(provider?.Label) ? provider.Label : provider?.Id ?? "Sign in";
        }

        private async void BeginInteractiveSignIn()
        {
            ShowDialog(
                "Signing in…",
                "Complete the sign-in in your web browser, then return here.",
                "Cancel",
                _ => _cts?.Cancel());

            SsoAuthResult result;
            try
            {
                result = await BasisSsoAuthController.SignInAsync(_cts.Token);
            }
            catch (OperationCanceledException)
            {
                ResetCancellation();
                PresentSignInPrompt();
                return;
            }

            ReleaseDialogue();

            if (result.Success)
            {
                Succeed(true);
            }
            else if (result.AccessDenied)
            {
                ShowDialog("Access denied", result.Error ?? "You are not permitted to use this client.",
                    "Try another account", "Quit", tryAgain =>
                    {
                        if (tryAgain) { ResetCancellation(); BeginInteractiveSignIn(); }
                        else Quit();
                    });
            }
            else if (result.Cancelled)
            {
                ResetCancellation();
                PresentSignInPrompt();
            }
            else
            {
                ShowDialog("Sign-in failed", result.Error ?? "Unknown error.", "Retry", "Quit", retry =>
                {
                    if (retry) { ResetCancellation(); BeginInteractiveSignIn(); }
                    else Quit();
                });
            }
        }

        private void Succeed(bool showConfirmation)
        {
            ReleaseDialogue();
            BasisSsoGate.OnSignedIn();
            _flowActive = false;
            BasisDebug.Log($"[SSO] Signed in as '{BasisSsoAuthController.ActiveDisplayName}'.");
            if (showConfirmation)
            {
                ShowDialog(
                    "Signed in",
                    $"Signed in as {BasisSsoAuthController.ActiveDisplayName}.",
                    "Continue",
                    _ => ReleaseDialogue());
            }
        }

        // ── Helpers ────────────────────────────────────────────────────────

        private void ResetCancellation()
        {
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
        }

        private IEnumerator EnsureMenuReadyCoroutine(Action done)
        {
            while (!BasisDeviceManagement.OnInitializationComplete)
            {
                yield return null;
            }

            float deadline = Time.realtimeSinceStartup + 20f;
            while (Time.realtimeSinceStartup < deadline)
            {
                bool ready = false;
                try
                {
                    if (!BasisMainMenu.Instance) BasisMainMenu.Open();
                    ready = BasisMainMenu.Instance;
                }
                catch (Exception e)
                {
                    BasisDebug.LogWarning($"[SSO] Menu not ready yet: {e.Message}");
                }
                if (ready) break;
                yield return null;
            }
            done?.Invoke();
        }

        private IEnumerator EnsureMenuReadyThen(Action action)
        {
            yield return EnsureMenuReadyCoroutine(null);
            action?.Invoke();
        }

        private static void ShowDialog(string title, string desc, string accept, string deny, Action<bool> cb)
        {
            if (!EnsureMenuInstance()) return;
            ReleaseDialogue();
            BasisMainMenu.Instance.OpenDialogue(title, desc, accept, deny, cb);
        }

        private static void ShowDialog(string title, string desc, string accept, Action<bool> cb)
        {
            if (!EnsureMenuInstance()) return;
            ReleaseDialogue();
            BasisMainMenu.Instance.OpenDialogue(title, desc, accept, cb);
        }

        private static bool EnsureMenuInstance()
        {
            try
            {
                if (!BasisMainMenu.Instance) BasisMainMenu.Open();
            }
            catch (Exception e)
            {
                BasisDebug.LogError($"[SSO] Could not open the menu to show the sign-in dialog: {e.Message}");
            }
            return BasisMainMenu.Instance;
        }

        private static void ReleaseDialogue()
        {
            if (BasisMainMenu.Instance && BasisMainMenu.Instance.Dialogue)
                BasisMainMenu.Instance.Dialogue.ReleaseInstance();
        }

        private void Quit()
        {
            BasisDebug.Log("[SSO] User chose to quit at the sign-in gate.");
            Application.Quit();
            // In the editor Application.Quit is a no-op; leave the gate armed.
        }
    }
}
