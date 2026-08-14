using System;
using System.Collections;
using Basis.BasisUI;
using Basis.Integration.Sso;
using UnityEngine;

namespace Basis.Integration.Sso.FrameworkGate
{
    /// <summary>
    /// Adds an "Account" tab to Settings showing the signed-in identity with Sign out / Switch
    /// account actions. Registered through <see cref="SettingsProvider.ExternalTabs"/> so no edit to
    /// the framework's SettingsProvider is required. Sign-out defers re-login to <see cref="BasisSsoGate"/>
    /// (which re-arms the connection block and re-presents the prompt), avoiding a second parallel flow.
    /// </summary>
    public static class BasisSsoAccountTab
    {
        private static bool _registered;

        internal static void Register()
        {
            if (_registered) return;
            _registered = true;
            // TabName is treated as a localization key that falls back to itself, so a plain
            // string shows as-is when no localization entry exists.
            SettingsProvider.ExternalTabs.Add(("Account", BuildTab));
        }

        private static PanelTabPage BuildTab(PanelTabGroup tabGroup)
        {
            PanelTabPage tab = PanelTabPage.CreateVertical(tabGroup.Descriptor.ContentParent);
            PanelElementDescriptor descriptor = tab.Descriptor;
            descriptor.SetIcon(AddressableAssets.Sprites.Settings);
            descriptor.SetTitle("Account");

            RectTransform container = descriptor.ContentParent;

            BasisSsoAccountTabLiveView liveView = tab.gameObject.AddComponent<BasisSsoAccountTabLiveView>();
            liveView.Initialize(container, descriptor);

            descriptor.ForceRebuild();
            return tab;
        }

        internal static void BuildContents(RectTransform container, PanelElementDescriptor descriptor)
        {
            for (int index = container.childCount - 1; index >= 0; index--)
                UnityEngine.Object.Destroy(container.GetChild(index).gameObject);

            PanelElementDescriptor group = PanelElementDescriptor.CreateNew(
                PanelElementDescriptor.ElementStyles.Group, container);

            if (BasisSsoAuthController.IsSignedIn)
            {
                string sub = BasisSsoAuthController.Current?.Sub ?? string.Empty;
                string provider = ActiveProviderLabel();
                group.SetTitle("Signed in");
                group.SetDescription($"Provider: {provider}\n{BasisSsoAuthController.ActiveDisplayName}\n<size=85%>{sub}</size>");

                PanelButton switchButton = PanelButton.CreateNew(container);
                switchButton.Descriptor.SetTitle("Switch account");
                switchButton.OnClicked += () => Confirm(
                    "Switch account",
                    "Sign out and sign in with a different account?",
                    () =>
                    {
                        BasisSsoAuthController.PendingPrompt = "login";
                        BasisSsoAuthController.SignOut();
                        CloseMenu();
                    });

                PanelButton signOutButton = PanelButton.CreateNew(container);
                signOutButton.Descriptor.SetTitle("Sign out");
                signOutButton.OnClicked += () => Confirm(
                    "Sign out",
                    "You will need to sign in again to connect to any server.",
                    () =>
                    {
                        BasisSsoAuthController.SignOut();
                        CloseMenu();
                    });
            }
            else
            {
                group.SetTitle("Not signed in");
                group.SetDescription(BasisSsoAuthController.IsConfigured
                    ? "Sign-in is required to connect."
                    : "SSO is not configured for this client.");

                if (BasisSsoAuthController.IsConfigured)
                {
                    var providers = BasisSsoAuthController.Providers;
                    if (providers != null && providers.Count > 0)
                    {
                        foreach (BasisOidcConfig.ProviderConfig provider in providers)
                        {
                            if (provider == null || string.IsNullOrWhiteSpace(provider.Id)) continue;
                            string providerId = provider.Id;
                            PanelButton signInButton = PanelButton.CreateNew(container);
                            signInButton.Descriptor.SetTitle($"Sign in with {ProviderLabel(provider)}");
                            signInButton.OnClicked += () => BasisSsoGateRunner.RequestInteractiveSignIn(providerId);
                        }
                    }
                    else
                    {
                        PanelButton signInButton = PanelButton.CreateNew(container);
                        signInButton.Descriptor.SetTitle("Sign in");
                        signInButton.OnClicked += () => BasisSsoGateRunner.RequestInteractiveSignIn(null);
                    }
                }
            }

            descriptor.ForceRebuild();
        }

        private static string ActiveProviderLabel()
        {
            string id = BasisSsoAuthController.SelectedProviderId;
            var providers = BasisSsoAuthController.Providers;
            if (providers != null)
            {
                foreach (BasisOidcConfig.ProviderConfig provider in providers)
                {
                    if (provider != null && string.Equals(provider.Id, id, StringComparison.OrdinalIgnoreCase))
                        return string.IsNullOrWhiteSpace(provider.Label) ? provider.Id : provider.Label;
                }
            }
            return string.IsNullOrWhiteSpace(id) ? "Unknown" : id;
        }

        private static string ProviderLabel(BasisOidcConfig.ProviderConfig provider)
        {
            if (string.Equals(provider?.Id, "google", StringComparison.OrdinalIgnoreCase)
                && string.Equals(provider.Label, "Google Workspace", StringComparison.OrdinalIgnoreCase))
                return "Google organization account";
            return !string.IsNullOrWhiteSpace(provider?.Label) ? provider.Label : provider?.Id ?? "provider";
        }

        private static void Confirm(string title, string body, System.Action onConfirmed)
        {
            if (!BasisMainMenu.Instance)
            {
                onConfirmed?.Invoke();
                return;
            }
            BasisMainMenu.Instance.OpenDialogue(title, body, "Confirm", "Cancel", ok =>
            {
                if (ok) onConfirmed?.Invoke();
            });
        }

        private static void CloseMenu()
        {
            // Drop the settings panel so the gate's sign-in dialog owns the screen.
            BasisMainMenu.Close();
        }
    }

    internal sealed class BasisSsoAccountTabLiveView : MonoBehaviour
    {
        private RectTransform _container;
        private PanelElementDescriptor _descriptor;
        private bool _rebuildQueued;

        internal void Initialize(RectTransform container, PanelElementDescriptor descriptor)
        {
            _container = container;
            _descriptor = descriptor;
            BasisSsoAuthController.StateChanged += QueueRebuild;
            BasisSsoAuthController.RuntimeConfigurationApplied += QueueRebuild;
            BasisSsoAccountTab.BuildContents(_container, _descriptor);
        }

        private void OnDestroy()
        {
            BasisSsoAuthController.StateChanged -= QueueRebuild;
            BasisSsoAuthController.RuntimeConfigurationApplied -= QueueRebuild;
        }

        private void QueueRebuild()
        {
            if (_rebuildQueued || !this) return;
            _rebuildQueued = true;
            StartCoroutine(RebuildNextFrame());
        }

        private IEnumerator RebuildNextFrame()
        {
            yield return null;
            _rebuildQueued = false;
            if (_container && _descriptor) BasisSsoAccountTab.BuildContents(_container, _descriptor);
        }
    }
}
