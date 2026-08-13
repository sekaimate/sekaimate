#if UNITY_WEBGL && !UNITY_EDITOR && DEVELOPMENT_BUILD
using Basis.Scripts.Networking;
using Basis.Scripts.Common;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Basis.BasisUI
{
    public partial class ServersProvider
    {
        internal static ServersProvider ActiveInstance { get; private set; }

        [Serializable]
        internal sealed class E2EEditorInput
        {
            public string address;
            public string port;
            public string password;
            public string webSocketUri;
            public string serverInfoUri;
            public bool connectable;
        }

        [Serializable]
        private sealed class E2EEntryState
        {
            public string id;
            public string title;
            public string description;
            public string address;
            public string port;
            public string webSocketUri;
            public string serverInfoUri;
        }

        [Serializable]
        private sealed class E2EState
        {
            public bool ready;
            public bool panelOpen;
            public bool connected;
            public string username;
            public bool autoConnect;
            public bool hostControlsVisible;
            public List<E2EEntryState> entries = new List<E2EEntryState>();
        }

        internal string E2ESnapshotJson()
        {
            E2EState state = new E2EState
            {
                ready = true,
                panelOpen = _panel != null,
                connected = BasisNetworkConnection.LocalPlayerIsConnected,
                username = _usernameField != null
                    ? _usernameField.Value
                    : BasisDataStore.LoadString(BasisConnectionService.UsernameFileName, string.Empty),
                autoConnect = BasisSettingsDefaults.AutoConnect.RawValue,
                hostControlsVisible = _hostButton != null && _hostButton.gameObject.activeInHierarchy,
            };

            foreach (ServerDirectoryEntry entry in _entries)
            {
                if (entry == null) continue;
                _rows.TryGetValue(entry.Id, out ServerRow row);
                state.entries.Add(new E2EEntryState
                {
                    id = entry.Id ?? string.Empty,
                    title = row?.Group?.Title ?? entry.DisplayName ?? string.Empty,
                    description = row?.Group?.Description ?? string.Empty,
                    address = entry.Target?.Get(Basis.Network.Core.ConnectionTarget.Keys.Address) ?? string.Empty,
                    port = entry.Target?.Get(Basis.Network.Core.ConnectionTarget.Keys.Port) ?? string.Empty,
                    webSocketUri = entry.WebSocketUri ?? string.Empty,
                    serverInfoUri = entry.ServerInfoUri ?? string.Empty,
                    connectable = row?.ConnectButton?.IsInteractable ?? false,
                });
            }
            return JsonUtility.ToJson(state);
        }

        internal void E2ESetUsername(string value)
        {
            if (_usernameField == null) return;
            _usernameField.SetValue(value ?? string.Empty);
        }

        internal void E2ESetAutoConnect(bool value)
        {
            if (_autoConnectToggle == null) return;
            _autoConnectToggle.SetValue(value);
        }

        internal void E2EClickAddServer() => _addServerButton?.OnClick();

        internal void E2ESetEditor(E2EEditorInput input)
        {
            if (input == null || _editorSection == null || !_editorSection.gameObject.activeInHierarchy) return;
            _editAddress.SetValue(input.address ?? string.Empty);
            _editPort.SetValue(input.port ?? string.Empty);
            _editPassword.SetPassword(input.password ?? string.Empty);
            _editWebSocketUri.SetValue(input.webSocketUri ?? string.Empty);
            _editServerInfoUri.SetValue(input.serverInfoUri ?? string.Empty);
        }

        internal void E2EClickSave() => _editSaveButton?.OnClick();

        internal void E2EClickRefreshAll() => _refreshAllButton?.OnClick();

        internal void E2EClickConnect(string id)
        {
            if (string.IsNullOrEmpty(id) || !_rows.TryGetValue(id, out ServerRow row)) return;
            row.ConnectButton?.OnClick();
        }

        internal void E2EClickEdit(string id)
        {
            if (string.IsNullOrEmpty(id) || !_rows.TryGetValue(id, out ServerRow row)) return;
            row.EditButton?.OnClick();
        }

        internal void E2EClickRemove() => _editRemoveButton?.OnClick();

        internal static bool E2EConfirmRemove()
        {
            PanelButton[] buttons = UnityEngine.Object.FindObjectsByType<PanelButton>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);
            string yes = BasisLocalization.Get("ui.yes");
            foreach (PanelButton button in buttons)
            {
                if (button != null && string.Equals(button.Descriptor.Title, yes, StringComparison.Ordinal))
                {
                    button.OnClick();
                    return true;
                }
            }
            return false;
        }
    }
}
#endif
