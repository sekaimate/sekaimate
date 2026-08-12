using System;
using Basis.BasisUI;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.UI;

namespace HVR.LicenseReview
{
    public static class SettingsProviderLicenses
    {
        private const int MaxLicenseUrlLength = 2048;

        [RuntimeInitializeOnLoadMethod]
        static void Register()
        {
            SettingsProvider.LicensesBuilder = BuildSection;
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        static async void BuildSection(RectTransform container)
        {
            try
            {
                AsyncOperationHandle<LicenseManifest> handle =
                    Addressables.LoadAssetAsync<LicenseManifest>("BasisFrameworkLicenseManifest");
                LicenseManifest licenseManifest = await handle.Task;
                PopulateSection(container, licenseManifest);
                LayoutRebuilder.ForceRebuildLayoutImmediate(container);
            }
            catch (Exception exception)
            {
                Debug.LogError($"Failed to load the third-party license manifest: {exception}");
            }
        }
#else
        static void BuildSection(RectTransform container)
        {
            LicenseManifest licenseManifest = Addressables
                .LoadAssetAsync<LicenseManifest>("BasisFrameworkLicenseManifest")
                .WaitForCompletion();
            PopulateSection(container, licenseManifest);
        }
#endif

        static void PopulateSection(RectTransform container, LicenseManifest licenseManifest)
        {
            if (licenseManifest == null)
            {
                Debug.LogError("Could not load the third-party license manifest.");
                return;
            }

            foreach (var license in licenseManifest.baked)
            {
                var sectionToggle = PanelSectionToggle.CreateNewEntry(container);
                sectionToggle.SetTitle(license.productName);
                sectionToggle.Descriptor.SetDescription(license.fullLicenseName);

                var licenseText = PanelElementDescriptor.CreateNew(PanelElementDescriptor.ElementStyles.Group, container);
                licenseText.SetTitle($"{license.packageName}/{license.licensePath}");
                licenseText.SetDescription(license.fullLicenseText);

                var openUrl = PanelButton.CreateNew(PanelButton.ButtonStyles.StandardButton, licenseText);
                openUrl.SetSize(new Vector2(540, 60));
                openUrl.Descriptor.SetTitle(BasisLocalization.Get("settings.thirdpartylicenses.openlicenseurl"));
                var capturedUrl = license.licenseUrl;
                openUrl.OnClicked += () =>
                {
                    if (!TryGetSafeHttpsUrl(capturedUrl, out var safeUrl))
                    {
                        return;
                    }

                    BasisMainMenu.Instance.OpenDialogue(
                        BasisLocalization.Get("settings.thirdpartylicenses.openexternallink.title"),
                        string.Format(BasisLocalization.Get("settings.thirdpartylicenses.openexternallink.body"), safeUrl),
                        BasisLocalization.Get("settings.thirdpartylicenses.open"),
                        BasisLocalization.Get("ui.cancel"),
                        confirmed =>
                        {
                            if (confirmed) Application.OpenURL(safeUrl);
                        });
                };

                sectionToggle.RegisterContentContainer(licenseText);
                PanelSectionToggleHelpers.FinalizeCollapsibleGroup(sectionToggle, licenseText, false, null);
            }
        }

        static bool TryGetSafeHttpsUrl(string raw, out string safeUrl)
        {
            safeUrl = null;
            if (string.IsNullOrEmpty(raw)) return false;
            if (raw.Length > MaxLicenseUrlLength) return false;
            for (int i = 0; i < raw.Length; i++)
            {
                char c = raw[i];
                if (c < 0x20 || c == 0x7F) return false;
            }
            if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri)) return false;
            if (uri.Scheme != Uri.UriSchemeHttps) return false;
            if (!string.IsNullOrEmpty(uri.UserInfo)) return false;
            if (string.IsNullOrEmpty(uri.Host)) return false;
            safeUrl = uri.AbsoluteUri;
            return true;
        }
    }
}
