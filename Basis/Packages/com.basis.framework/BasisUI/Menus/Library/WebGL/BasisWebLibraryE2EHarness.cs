#if UNITY_WEBGL && !UNITY_EDITOR && DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Basis.Scripts.Networking;
using Basis.Scripts.UI.UI_Panels;
using UnityEngine;

namespace Basis.BasisUI
{
    public sealed class BasisWebLibraryE2EHarness : MonoBehaviour
    {
        private const string EnabledParameter = "basisLibraryE2E=1";
        private const string GameObjectName = "Basis Web Library E2E";
        private string _lastSnapshot = string.Empty;
        private string _lastCommand = string.Empty;
        private string _lastError = string.Empty;
        private int _lastRequestId;

        [DllImport("__Internal")]
        private static extern void BasisWebLibraryE2EReport(string json);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Initialize()
        {
            if (Application.absoluteURL.IndexOf(EnabledParameter, StringComparison.Ordinal) < 0)
            {
                return;
            }

            GameObject gameObject = new GameObject(GameObjectName);
            DontDestroyOnLoad(gameObject);
            gameObject.AddComponent<BasisWebLibraryE2EHarness>();
        }

        private void Start()
        {
            PublishSnapshot();
        }

        private void Update()
        {
            PublishSnapshot();
        }

        public void Command(string json)
        {
            CommandPayload command = JsonUtility.FromJson<CommandPayload>(json);
            if (command == null || string.IsNullOrEmpty(command.action))
            {
                SetCommandResult(0, string.Empty, "Invalid command payload.");
                return;
            }

            try
            {
                Execute(command);
                SetCommandResult(command.requestId, command.action, string.Empty);
            }
            catch (Exception exception)
            {
                SetCommandResult(command.requestId, command.action, exception.Message);
                BasisDebug.LogError(exception);
            }
        }

        private void Execute(CommandPayload command)
        {
            switch (command.action)
            {
                case "open":
                    OpenLibrary();
                    break;
                case "select-tab":
                    ClickTitle(TabLocalizationKey(command.target));
                    break;
                case "search":
                    SetTextField(BasisLocalization.Get("ui.search"), command.value ?? string.Empty, true);
                    break;
                case "sort":
                    SetDropdown(string.Empty, command.value);
                    break;
                case "filter":
                    SetDropdown(string.Empty, command.value);
                    break;
                case "click-title-key":
                    ClickTitle(command.target);
                    break;
                case "click-tooltip-key":
                    ClickTooltip(command.target);
                    break;
                case "click-first-card":
                    ClickFirstCard();
                    break;
                case "set-text-key":
                    SetTextField(BasisLocalization.Get(command.target), command.value ?? string.Empty, false);
                    break;
                case "set-password-key":
                    SetPassword(BasisLocalization.Get(command.target), command.value ?? string.Empty);
                    break;
                case "set-dropdown-key":
                    SetDropdown(BasisLocalization.Get(command.target), NetworkDisplayValue(command.value));
                    break;
                case "toggle-key":
                    Toggle(BasisLocalization.Get(command.target));
                    break;
                default:
                    throw new InvalidOperationException($"Unknown library E2E action: {command.action}");
            }
        }

        private static void OpenLibrary()
        {
            if (LibraryProvider.panel != null && !LibraryProvider.panel.IsReleased)
            {
                return;
            }
            new LibraryProvider().RunAction();
        }

        private static void ClickTitle(string localizationKey)
        {
            string title = BasisLocalization.Get(localizationKey);
            PanelButton button = ActiveComponents<PanelButton>()
                .LastOrDefault(candidate => string.Equals(candidate.Descriptor.Title, title, StringComparison.Ordinal));
            if (button == null)
            {
                throw new InvalidOperationException($"Active library button was not found: {localizationKey}");
            }
            button.OnClick();
        }

        private static void ClickTooltip(string localizationKey)
        {
            string tooltip = BasisLocalization.Get(localizationKey);
            PanelButton button = ActiveComponents<PanelButton>()
                .FirstOrDefault(candidate => string.Equals(candidate.Descriptor.Tooltip, tooltip, StringComparison.Ordinal));
            if (button == null)
            {
                throw new InvalidOperationException($"Active library tooltip button was not found: {localizationKey}");
            }
            button.OnClick();
        }

        private static void ClickFirstCard()
        {
            PanelButton card = ActiveComponents<PanelButton>()
                .FirstOrDefault(candidate => candidate.Descriptor.HasTexture && !string.IsNullOrEmpty(candidate.Descriptor.Title));
            if (card == null)
            {
                throw new InvalidOperationException("No active library item card was found.");
            }
            card.OnClick();
        }

        private static void SetTextField(string title, string value, bool matchPlaceholder)
        {
            PanelTextField field = ActiveComponents<PanelTextField>().LastOrDefault(candidate =>
                string.Equals(candidate.Descriptor.Title, title, StringComparison.Ordinal)
                || (matchPlaceholder && string.Equals(candidate._placeholderLabel?.text, title, StringComparison.Ordinal)));
            if (field == null)
            {
                throw new InvalidOperationException($"Active library text field was not found: {title}");
            }
            field._inputField.SetTextWithoutNotify(value);
            field.SetValue(value);
        }

        private static void SetPassword(string title, string value)
        {
            PanelPasswordField field = ActiveComponents<PanelPasswordField>()
                .LastOrDefault(candidate => string.Equals(candidate.Descriptor.Title, title, StringComparison.Ordinal));
            if (field == null)
            {
                throw new InvalidOperationException($"Active library password field was not found: {title}");
            }
            field.SetPassword(value);
            field.OnComponentUsed();
        }

        private static void SetDropdown(string title, string value)
        {
            IEnumerable<PanelDropdown> dropdowns = ActiveComponents<PanelDropdown>();
            PanelDropdown dropdown = string.IsNullOrEmpty(title)
                ? dropdowns.LastOrDefault(candidate =>
                    string.IsNullOrEmpty(candidate.Descriptor.Title)
                    && candidate.Entries != null
                    && candidate.Entries.Contains(value))
                : dropdowns.LastOrDefault(candidate => string.Equals(candidate.Descriptor.Title, title, StringComparison.Ordinal));
            if (dropdown == null || dropdown.Entries == null || !dropdown.Entries.Contains(value))
            {
                throw new InvalidOperationException($"Active library dropdown value was not found: {title}/{value}");
            }
            dropdown.SetValue(value);
        }

        private static void Toggle(string title)
        {
            PanelToggle toggle = ActiveComponents<PanelToggle>()
                .LastOrDefault(candidate => string.Equals(candidate.Descriptor.Title, title, StringComparison.Ordinal));
            if (toggle == null)
            {
                throw new InvalidOperationException($"Active library toggle was not found: {title}");
            }
            toggle.SetValue(!toggle.Value);
        }

        private static IEnumerable<T> ActiveComponents<T>() where T : Component
        {
            Transform panelTransform = LibraryProvider.panel != null ? LibraryProvider.panel.transform : null;
            return Resources.FindObjectsOfTypeAll<T>()
                .Where(component => component != null
                    && component.gameObject.activeInHierarchy
                    && panelTransform != null
                    && component.transform.IsChildOf(panelTransform));
        }

        private static string TabLocalizationKey(string page)
        {
            return page switch
            {
                "Avatar" => "library.tab.avatars",
                "Prop" => "library.tab.props",
                "World" => "library.tab.worlds",
                "Instantiated" => "library.tab.instantiated",
                _ => throw new InvalidOperationException($"Unknown library page: {page}")
            };
        }

        private static string NetworkDisplayValue(string value)
        {
            return value switch
            {
                "Local" => BasisLocalization.Get("library.networkType.local"),
                "Networked" => BasisLocalization.Get("library.networkType.networked"),
                "Predownload" => BasisLocalization.Get("library.networkType.predownload"),
                "LoadOnBoot" => BasisLocalization.Get("library.networkType.loadOnBoot"),
                _ => value
            };
        }

        private void SetCommandResult(int requestId, string command, string error)
        {
            _lastRequestId = requestId;
            _lastCommand = command;
            _lastError = error;
            _lastSnapshot = string.Empty;
            PublishSnapshot();
        }

        private void PublishSnapshot()
        {
            SnapshotPayload payload = BuildSnapshot();
            string json = JsonUtility.ToJson(payload);
            if (string.Equals(json, _lastSnapshot, StringComparison.Ordinal))
            {
                return;
            }
            _lastSnapshot = json;
            BasisWebLibraryE2EReport(json);
        }

        private SnapshotPayload BuildSnapshot()
        {
            ButtonPayload[] buttons = ActiveComponents<PanelButton>()
                .Select(button => new ButtonPayload
                {
                    title = button.Descriptor.Title ?? string.Empty,
                    tooltip = button.Descriptor.Tooltip ?? string.Empty
                })
                .OrderBy(button => button.title, StringComparer.Ordinal)
                .ThenBy(button => button.tooltip, StringComparer.Ordinal)
                .ToArray();
            DropdownPayload[] dropdowns = ActiveComponents<PanelDropdown>()
                .Select(dropdown => new DropdownPayload
                {
                    title = dropdown.Descriptor.Title ?? string.Empty,
                    value = dropdown.Value ?? string.Empty,
                    entries = dropdown.Entries?.ToArray() ?? Array.Empty<string>()
                })
                .OrderBy(dropdown => dropdown.title, StringComparer.Ordinal)
                .ThenBy(dropdown => dropdown.value, StringComparer.Ordinal)
                .ToArray();
            KeyPayload[] keys = BasisDataStoreItemKeys.DisplayKeys()
                .Select(key => new KeyPayload
                {
                    mode = key.Mode.ToString(),
                    pinned = key.PinnedSettings.IsPinned,
                    title = CachedMetaData.TryGetMeta(key.Url ?? string.Empty, out CachedMetaData.CachedContent metadata)
                        ? metadata.Name ?? string.Empty
                        : string.Empty,
                    url = key.Url ?? string.Empty
                })
                .OrderBy(key => key.url, StringComparer.Ordinal)
                .ToArray();
            BasisRuntimeSpawnRegistry.SpawnInstance selected = PlacementManager.ActiveInstance;
            InstancePayload[] instances = BasisRuntimeSpawnRegistry.GetAll()
                .Select(instance => new InstancePayload
                {
                    id = instance.InstanceId ?? string.Empty,
                    mode = instance.SpawnMode.ToString(),
                    networked = instance.SpawnMethod == BasisRuntimeSpawnRegistry.SpawnMethod.Network,
                    persistent = instance.Persistent,
                    selected = selected != null && selected.InstanceId == instance.InstanceId,
                    @static = instance.Static,
                    url = instance.Url ?? string.Empty
                })
                .OrderBy(instance => instance.id, StringComparer.Ordinal)
                .ToArray();
            return new SnapshotPayload
            {
                buttons = buttons,
                connected = BasisNetworkConnection.LocalPlayerIsConnected,
                currentPage = CurrentPage(),
                dropdowns = dropdowns,
                instances = instances,
                keys = keys,
                lastCommand = _lastCommand,
                lastError = _lastError,
                lastRequestId = _lastRequestId,
                ready = true,
                search = ActiveComponents<PanelTextField>()
                    .FirstOrDefault(field => string.Equals(field._placeholderLabel?.text, BasisLocalization.Get("ui.search"), StringComparison.Ordinal))
                    ?.Value ?? string.Empty,
                shareables = BasisShareableRegistry.GetAll()
                    .Select(entry => new ShareablePayload
                    {
                        id = entry.Id ?? string.Empty,
                        kind = entry.Kind.ToString(),
                        title = entry.Title ?? string.Empty
                    })
                    .OrderBy(entry => entry.id, StringComparer.Ordinal)
                    .ToArray()
            };
        }

        private static string CurrentPage()
        {
            PanelTabGroup group = ActiveComponents<PanelTabGroup>().FirstOrDefault(candidate => candidate.Pages.Count == 4);
            return group?.Value switch
            {
                0 => "Prop",
                1 => "World",
                2 => "Avatar",
                3 => "Instantiated",
                _ => string.Empty
            };
        }

        [Serializable]
        private sealed class CommandPayload
        {
            public string action;
            public int requestId;
            public string target;
            public string value;
        }

        [Serializable]
        private sealed class SnapshotPayload
        {
            public ButtonPayload[] buttons;
            public bool connected;
            public string currentPage;
            public DropdownPayload[] dropdowns;
            public InstancePayload[] instances;
            public KeyPayload[] keys;
            public string lastCommand;
            public string lastError;
            public int lastRequestId;
            public bool ready;
            public string search;
            public ShareablePayload[] shareables;
        }

        [Serializable]
        private sealed class ButtonPayload
        {
            public string title;
            public string tooltip;
        }

        [Serializable]
        private sealed class DropdownPayload
        {
            public string[] entries;
            public string title;
            public string value;
        }

        [Serializable]
        private sealed class KeyPayload
        {
            public string mode;
            public bool pinned;
            public string title;
            public string url;
        }

        [Serializable]
        private sealed class InstancePayload
        {
            public string id;
            public string mode;
            public bool networked;
            public bool persistent;
            public bool selected;
            public bool @static;
            public string url;
        }

        [Serializable]
        private sealed class ShareablePayload
        {
            public string id;
            public string kind;
            public string title;
        }
    }
}
#endif
