#if UNITY_WEBGL && !UNITY_EDITOR && DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using Basis.BasisUI;
using Basis.Network.Core;
using Basis.Scripts.Networking;
using UnityEngine;

internal sealed class BasisWebSettingsE2EHarness : MonoBehaviour
{
    private const string GameObjectName = "Basis Web Settings E2E";
    private const string QueryKey = "basisSettingsE2E=";

    private static readonly string[] RegularTabKeys =
    {
        "settings.tab.general",
        "settings.tab.audio",
        "settings.tab.microphone",
        "settings.tab.graphics",
        "settings.tab.myavatar",
        "settings.tab.controls",
        "settings.tab.chat",
        "settings.tab.bodytracking",
        "settings.tab.trackerlinking",
        "settings.tab.downloadsurls",
        "settings.tab.developer",
        "settings.tab.thirdpartylicenses",
    };

    private bool addedModeratorPermission;
    private bool addedAdminPermission;
    private bool authorized;
    private PanelTabGroup settingsTabs;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Initialize()
    {
        int queryIndex = Application.absoluteURL.IndexOf(QueryKey, StringComparison.Ordinal);
        if (queryIndex < 0)
        {
            return;
        }

        BasisWebSettingsE2EInitialize(GameObjectName);
        GameObject harnessObject = new GameObject(GameObjectName);
        DontDestroyOnLoad(harnessObject);
        BasisWebSettingsE2EHarness harness = harnessObject.AddComponent<BasisWebSettingsE2EHarness>();
        harness.authorized = Application.absoluteURL.IndexOf(
            QueryKey + "authorized", StringComparison.Ordinal) >= 0;
        harness.StartCoroutine(harness.Prepare());
    }

    private IEnumerator Prepare()
    {
        if (authorized)
        {
            addedModeratorPermission = BasisNetworkManagement.LocalPermissions.Add(PermNodes.PlayerModeration);
            addedAdminPermission = BasisNetworkManagement.LocalPermissions.Add(PermNodes.PermissionsView);
        }

        while (BasisMainMenu.Instance == null)
        {
            yield return null;
        }

        SettingsProvider.OpenToTab("settings.tab.general");
        for (int attempt = 0; attempt < 300 && settingsTabs == null; attempt++)
        {
            yield return null;
            settingsTabs = FindSettingsTabs();
        }

        if (settingsTabs == null)
        {
            Publish(new SettingsResult
            {
                operation = "ready",
                succeeded = false,
                authorized = authorized,
                error = "Settings tab group did not become available",
            });
            yield break;
        }

        Publish(new SettingsResult
        {
            operation = "ready",
            succeeded = true,
            authorized = authorized,
        });
    }

    public void HandleRequest(string requestJson)
    {
        SettingsRequest request = JsonUtility.FromJson<SettingsRequest>(requestJson);
        StartCoroutine(RunRequest(request));
    }

    private IEnumerator RunRequest(SettingsRequest request)
    {
        SettingsResult result = new SettingsResult
        {
            requestId = request.requestId,
            operation = request.operation,
            authorized = authorized,
        };

        if (settingsTabs == null)
        {
            result.error = "Settings tab group is unavailable";
            Publish(result);
            yield break;
        }

        try
        {
            if (request.operation == "restore")
            {
                RestoreValues(request.restoreValues ?? Array.Empty<RestoreValue>());
            }

            bool mutate = request.operation == "exerciseAll";
            Dictionary<string, string> originals = new Dictionary<string, string>();
            Dictionary<string, string> desiredValues = new Dictionary<string, string>();
            List<(string key, int index)> tabs = ResolveTabs();

            for (int tabIndex = 0; tabIndex < tabs.Count; tabIndex++)
            {
                (string key, int index) tab = tabs[tabIndex];
                settingsTabs.SelectionButtons[tab.index].OnClicked?.Invoke();
                yield return null;

                PanelTabPage page = settingsTabs.Pages[tab.index];
                TabResult tabResult = new TabResult
                {
                    key = tab.key,
                    title = settingsTabs.SelectionButtons[tab.index].Descriptor.Title,
                    opened = settingsTabs.Value == tab.index && page.gameObject.activeInHierarchy,
                };
                CaptureControls(page, mutate, originals, desiredValues, tabResult.controls);
                result.tabs.Add(tabResult);
            }

            BasisSettingsSystem.SaveAllSettings();
            result.succeeded = true;
        }
        catch (Exception exception)
        {
            result.error = exception.ToString();
        }

        Publish(result);
    }

    private List<(string key, int index)> ResolveTabs()
    {
        Dictionary<string, string> knownTitles = new Dictionary<string, string>();
        for (int i = 0; i < RegularTabKeys.Length; i++)
        {
            knownTitles[BasisLocalization.Get(RegularTabKeys[i])] = RegularTabKeys[i];
        }
        knownTitles[BasisLocalization.Get("settings.tab.moderator")] = "settings.tab.moderator";
        knownTitles[BasisLocalization.Get("settings.tab.admin")] = "settings.tab.admin";

        List<(string key, int index)> tabs = new List<(string key, int index)>();
        for (int i = 0; i < settingsTabs.SelectionButtons.Count; i++)
        {
            string title = settingsTabs.SelectionButtons[i].Descriptor.Title;
            string key = knownTitles.TryGetValue(title, out string knownKey)
                ? knownKey
                : "external:" + title;
            tabs.Add((key, i));
        }
        return tabs;
    }

    private static void CaptureControls(
        PanelTabPage page,
        bool mutate,
        Dictionary<string, string> originals,
        Dictionary<string, string> desiredValues,
        List<ControlResult> results)
    {
        PanelToggle[] toggles = page.GetComponentsInChildren<PanelToggle>(true);
        for (int i = 0; i < toggles.Length; i++)
        {
            PanelToggle control = toggles[i];
            string bindingKey = control.SettingsBinding?.BindingKey ?? string.Empty;
            string before = control.Value ? "true" : "false";
            string current = before;
            string outcome = "no-binding";
            if (!string.IsNullOrEmpty(bindingKey))
            {
                before = RememberOriginal(originals, bindingKey, before);
                current = mutate ? GetDesired(desiredValues, bindingKey, before == "true" ? "false" : "true") : (control.Value ? "true" : "false");
                if (mutate) control.SetValue(current == "true");
                outcome = mutate ? "mutated" : "no-alternative";
            }
            results.Add(CreateControl(control, bindingKey, "toggle", before, current, outcome));
        }

        PanelSlider[] sliders = page.GetComponentsInChildren<PanelSlider>(true);
        for (int i = 0; i < sliders.Length; i++)
        {
            PanelSlider control = sliders[i];
            string bindingKey = control.SettingsBinding?.BindingKey ?? string.Empty;
            string observed = control.Value.ToString("R", CultureInfo.InvariantCulture);
            string before = observed;
            string current = observed;
            string outcome = "no-binding";
            if (!string.IsNullOrEmpty(bindingKey))
            {
                before = RememberOriginal(originals, bindingKey, observed);
                float original = float.Parse(before, CultureInfo.InvariantCulture);
                float candidate = Mathf.Approximately(original, control.SliderComponent.minValue)
                    ? control.SliderComponent.maxValue
                    : control.SliderComponent.minValue;
                current = mutate
                    ? GetDesired(desiredValues, bindingKey, candidate.ToString("R", CultureInfo.InvariantCulture))
                    : observed;
                if (mutate) control.SetValue(float.Parse(current, CultureInfo.InvariantCulture));
                outcome = mutate ? "mutated" : "no-alternative";
            }
            results.Add(CreateControl(control, bindingKey, "slider", before, current, outcome));
        }

        PanelDropdown[] dropdowns = page.GetComponentsInChildren<PanelDropdown>(true);
        for (int i = 0; i < dropdowns.Length; i++)
        {
            PanelDropdown control = dropdowns[i];
            string bindingKey = control.SettingsBinding?.BindingKey ?? string.Empty;
            string before = control.Value ?? string.Empty;
            string current = before;
            string outcome = "no-binding";
            if (!string.IsNullOrEmpty(bindingKey))
            {
                before = RememberOriginal(originals, bindingKey, before);
                if (control.Entries != null && control.Entries.Count > 1)
                {
                    int originalIndex = control.Entries.FindIndex(entry => string.Equals(entry, before, StringComparison.OrdinalIgnoreCase));
                    string candidate = control.Entries[(Mathf.Max(originalIndex, 0) + 1) % control.Entries.Count];
                    current = mutate ? GetDesired(desiredValues, bindingKey, candidate) : control.Value;
                    if (mutate) control.SetValue(current);
                    outcome = mutate ? "mutated" : "no-alternative";
                }
                else
                {
                    outcome = "no-alternative";
                }
            }
            results.Add(CreateControl(control, bindingKey, "dropdown", before, current, outcome));
        }

        PanelTextField[] textFields = page.GetComponentsInChildren<PanelTextField>(true);
        for (int i = 0; i < textFields.Length; i++)
        {
            PanelTextField control = textFields[i];
            string bindingKey = control.SettingsBinding?.BindingKey ?? string.Empty;
            string before = control.Value ?? string.Empty;
            string current = before;
            string outcome = "no-binding";
            if (!string.IsNullOrEmpty(bindingKey))
            {
                before = RememberOriginal(originals, bindingKey, before);
                string candidate = before == "basis-e2e" ? "basis-e2e-alt" : "basis-e2e";
                current = mutate ? GetDesired(desiredValues, bindingKey, candidate) : control.Value;
                if (mutate) control.SetValue(current);
                outcome = mutate ? "mutated" : "no-alternative";
            }
            results.Add(CreateControl(control, bindingKey, "text", before, current, outcome));
        }
    }

    private static string RememberOriginal(Dictionary<string, string> originals, string bindingKey, string observed)
    {
        if (!originals.TryGetValue(bindingKey, out string original))
        {
            originals[bindingKey] = observed;
            return observed;
        }
        return original;
    }

    private static string GetDesired(Dictionary<string, string> desiredValues, string bindingKey, string candidate)
    {
        if (!desiredValues.TryGetValue(bindingKey, out string desired))
        {
            desiredValues[bindingKey] = candidate;
            return candidate;
        }
        return desired;
    }

    private static ControlResult CreateControl(
        PanelComponent control,
        string bindingKey,
        string type,
        string before,
        string current,
        string outcome)
    {
        return new ControlResult
        {
            bindingKey = bindingKey,
            title = control.Descriptor?.Title ?? string.Empty,
            type = type,
            before = before,
            current = current,
            outcome = outcome,
        };
    }

    private static void RestoreValues(RestoreValue[] values)
    {
        for (int i = 0; i < values.Length; i++)
        {
            RestoreValue entry = values[i];
            switch (entry.type)
            {
                case "toggle":
                    BasisSettingsSystem.SaveBool(entry.bindingKey, entry.value == "true");
                    break;
                case "slider":
                    BasisSettingsSystem.SaveFloat(entry.bindingKey, float.Parse(entry.value, CultureInfo.InvariantCulture));
                    break;
                case "dropdown":
                case "text":
                    BasisSettingsSystem.SaveString(entry.bindingKey, entry.value);
                    break;
                default:
                    throw new InvalidOperationException("Unsupported restore type: " + entry.type);
            }
        }
        BasisSettingsDefaults.LoadAll();
        BasisSettingsSystem.NotifyFinishedChanges();
    }

    private static PanelTabGroup FindSettingsTabs()
    {
        string generalTitle = BasisLocalization.Get("settings.tab.general");
        PanelTabGroup[] groups = FindObjectsByType<PanelTabGroup>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        return groups.FirstOrDefault(group => group.SelectionButtons.Any(button =>
            button != null && button.Descriptor != null && button.Descriptor.Title == generalTitle));
    }

    private void OnDestroy()
    {
        if (addedModeratorPermission)
        {
            BasisNetworkManagement.LocalPermissions.Remove(PermNodes.PlayerModeration);
        }
        if (addedAdminPermission)
        {
            BasisNetworkManagement.LocalPermissions.Remove(PermNodes.PermissionsView);
        }
    }

    private static void Publish(SettingsResult result)
    {
        BasisWebSettingsE2EPublish(JsonUtility.ToJson(result));
    }

    [Serializable]
    private sealed class SettingsRequest
    {
        public int requestId;
        public string operation;
        public RestoreValue[] restoreValues;
    }

    [Serializable]
    private sealed class RestoreValue
    {
        public string bindingKey;
        public string type;
        public string value;
    }

    [Serializable]
    private sealed class SettingsResult
    {
        public int requestId;
        public string operation;
        public bool succeeded;
        public bool authorized;
        public string error = string.Empty;
        public List<TabResult> tabs = new List<TabResult>();
    }

    [Serializable]
    private sealed class TabResult
    {
        public string key;
        public string title;
        public bool opened;
        public List<ControlResult> controls = new List<ControlResult>();
    }

    [Serializable]
    private sealed class ControlResult
    {
        public string bindingKey;
        public string title;
        public string type;
        public string before;
        public string current;
        public string outcome;
    }

    [DllImport("__Internal")]
    private static extern void BasisWebSettingsE2EInitialize(string gameObjectName);

    [DllImport("__Internal")]
    private static extern void BasisWebSettingsE2EPublish(string resultJson);
}
#endif
