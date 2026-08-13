#if UNITY_WEBGL && !UNITY_EDITOR && DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Runtime.InteropServices;
using Basis.Network.Core;
using UnityEngine;

namespace Basis.Scripts.Networking
{
    public sealed class BasisWebNetworkE2EHarness : MonoBehaviour
    {
        private const string GameObjectName = "Basis Web Network E2E";
        private const string EnabledParameter = "basisNetworkE2E";
        private const string WebSocketParameter = "websocketUri";
        private const string PasswordParameter = "password";
        private const string UserNameParameter = "userName";

        private ServerDirectoryEntry _entry;
        private string _userName;
        private bool _acceptedReported;
        private bool _authenticatedReported;
        private int _remotePlayerCount = -1;

        [DllImport("__Internal")]
        private static extern void BasisWebNetworkE2EReport(string json);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Initialize()
        {
            if (!TryReadConfiguration(Application.absoluteURL, out ServerDirectoryEntry entry, out string userName))
            {
                return;
            }

            GameObject gameObject = new GameObject(GameObjectName);
            DontDestroyOnLoad(gameObject);
            BasisWebNetworkE2EHarness harness = gameObject.AddComponent<BasisWebNetworkE2EHarness>();
            harness._entry = entry;
            harness._userName = userName;
            harness.Subscribe();
            harness.Report("harness-ready");
            harness.StartCoroutine(harness.ConnectWhenReady());
        }

        private void Update()
        {
            if (!_acceptedReported && BasisNetworkConnection.LocalPlayerPeer != null)
            {
                _acceptedReported = true;
                Report("transport-accepted");
            }

            if (!_authenticatedReported && BasisNetworkConnection.LocalPlayerIsConnected)
            {
                _authenticatedReported = true;
                Report("authenticated");
            }

            int remotePlayerCount = BasisNetworkPlayers.RemotePlayers.Count;
            if (_remotePlayerCount != remotePlayerCount)
            {
                _remotePlayerCount = remotePlayerCount;
                Report("remote-state");
            }
        }

        public void SendChat(string message)
        {
            BasisNetworkHandleChat.SendChatMessage(message, false);
            Report("chat-sent", message);
        }

        public async void Reconnect()
        {
            Report("reconnect-started");
            await BasisNetworkLifeCycle.Destroy();
            await BasisNetworkLifeCycle.Initialize();
            Subscribe();
            ResetObservedConnectionState();
            await BasisConnectionService.ConnectAsync(_entry, _userName);
            Report("reconnect-requested");
        }

        private IEnumerator ConnectWhenReady()
        {
            while (!BasisNetworkManagement.IsInitialized)
            {
                yield return null;
            }

            Report("connect-requested");
            _ = BasisConnectionService.ConnectAsync(_entry, _userName);
        }

        private void Subscribe()
        {
            BasisNetworkHandleChat.OnChatMessageReceived -= OnChatMessageReceived;
            BasisNetworkHandleChat.OnChatMessageReceived += OnChatMessageReceived;
        }

        private void OnDestroy()
        {
            BasisNetworkHandleChat.OnChatMessageReceived -= OnChatMessageReceived;
        }

        private void OnChatMessageReceived(ushort senderPlayerId, string message)
        {
            Report("chat-received", message, senderPlayerId);
        }

        private void ResetObservedConnectionState()
        {
            _acceptedReported = false;
            _authenticatedReported = false;
            _remotePlayerCount = -1;
        }

        private void Report(string type, string message = "", ushort senderPlayerId = 0)
        {
            int localPlayerId = BasisNetworkConnection.LocalPlayerPeer?.RemoteId ?? -1;
            bool avatarStateReady = BasisNetworkManagement.Transmitter != null;
            EventPayload payload = new EventPayload
            {
                type = type,
                message = message ?? string.Empty,
                senderPlayerId = senderPlayerId,
                localPlayerId = localPlayerId,
                connected = BasisNetworkConnection.LocalPlayerIsConnected,
                remotePlayerCount = BasisNetworkPlayers.RemotePlayers.Count,
                avatarStateReady = avatarStateReady,
            };
            string json = JsonUtility.ToJson(payload);
            Debug.Log("[BasisWebNetworkE2E] " + json);
            BasisWebNetworkE2EReport(json);
        }

        private static bool TryReadConfiguration(
            string absoluteUrl,
            out ServerDirectoryEntry entry,
            out string userName)
        {
            entry = null;
            userName = string.Empty;
            if (!Uri.TryCreate(absoluteUrl, UriKind.Absolute, out Uri uri))
            {
                return false;
            }

            string enabled = ReadQueryParameter(uri.Query, EnabledParameter);
            string webSocketUri = ReadQueryParameter(uri.Query, WebSocketParameter);
            userName = ReadQueryParameter(uri.Query, UserNameParameter);
            if (enabled != "1" || string.IsNullOrWhiteSpace(webSocketUri) || string.IsNullOrWhiteSpace(userName))
            {
                return false;
            }

            string password = ReadQueryParameter(uri.Query, PasswordParameter);
            ConnectionTarget target = new ConnectionTarget(BasisNetworkStackRegistry.DefaultId, "localhost:4296");
            target.Set(ConnectionTarget.Keys.Address, "localhost");
            target.Set(ConnectionTarget.Keys.Port, "4296");
            entry = new ServerDirectoryEntry
            {
                Id = "__web_network_e2e__",
                SourceId = "web-network-e2e",
                DisplayName = "Web Network E2E",
                Target = target,
                WebSocketUri = webSocketUri,
                HasPassword = !string.IsNullOrEmpty(password),
                Password = password,
                CanEdit = false,
                CanRemove = false,
            };
            return true;
        }

        private static string ReadQueryParameter(string query, string name)
        {
            string trimmedQuery = query.TrimStart('?');
            foreach (string parameter in trimmedQuery.Split('&'))
            {
                int separator = parameter.IndexOf('=');
                if (separator < 0 || !string.Equals(
                        Uri.UnescapeDataString(parameter.Substring(0, separator)),
                        name,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                return Uri.UnescapeDataString(parameter.Substring(separator + 1));
            }
            return string.Empty;
        }

        [Serializable]
        private sealed class EventPayload
        {
            public string type;
            public string message;
            public int senderPlayerId;
            public int localPlayerId;
            public bool connected;
            public int remotePlayerCount;
            public bool avatarStateReady;
        }
    }
}
#endif
