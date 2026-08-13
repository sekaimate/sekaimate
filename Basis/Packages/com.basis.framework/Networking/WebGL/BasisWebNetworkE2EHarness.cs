#if UNITY_WEBGL && !UNITY_EDITOR && DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;
using Basis.BasisUI;
using Basis.Network.Core;
using Basis.Scripts.Device_Management;
using Basis.Scripts.Drivers;
using Basis.Scripts.BasisSdk.Players;
using UnityEngine;
using UnityEngine.SceneManagement;

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
        private bool _observeConnectionState = true;
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
            if (_observeConnectionState && !_acceptedReported && BasisNetworkConnection.LocalPlayerPeer != null)
            {
                _acceptedReported = true;
                Report("transport-accepted");
            }

            if (_observeConnectionState && !_authenticatedReported && BasisNetworkConnection.LocalPlayerIsConnected)
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

        public void ShareContent(string json)
        {
            ContentShareInput input = JsonUtility.FromJson<ContentShareInput>(json);
            if (input == null
                || string.IsNullOrWhiteSpace(input.sphereId)
                || string.IsNullOrWhiteSpace(input.contentUrl)
                || !Enum.TryParse(input.contentType, false, out SerializableBasis.ContentShareType contentType))
            {
                Report("content-share-rejected");
                return;
            }

            NetPeer peer = BasisNetworkConnection.LocalPlayerPeer;
            if (peer == null)
            {
                Report("content-share-rejected", "not-connected");
                return;
            }

            SerializableBasis.ContentShareMessage contentShare = new SerializableBasis.ContentShareMessage
            {
                SphereNetID = input.sphereId,
                ContentURL = input.contentUrl,
                UnlockPassword = input.unlockPassword ?? string.Empty,
                ContentType = contentType,
                PositionX = input.positionX,
                PositionY = input.positionY,
                PositionZ = input.positionZ,
            };
            NetDataWriter writer = new NetDataWriter();
            contentShare.Serialize(writer);
            peer.Send(
                writer,
                BasisNetworkCommons.ContentShareChannel,
                DeliveryMethod.ReliableOrdered);
            ReportContent("content-sent", contentShare);
        }

        public void RemoveContent(string sphereId)
        {
            if (string.IsNullOrWhiteSpace(sphereId))
            {
                Report("content-remove-rejected");
                return;
            }

            BasisContentShareManager.RequestRemoveSphere(sphereId);
            Report("content-remove-sent", sphereId, sphereId: sphereId);
        }

        public async void LoadContent(string sphereId)
        {
            if (!BasisContentShareManager.TryGetSphere(sphereId, out BasisContentSphere sphere)
                || sphere == null
                || sphere.ContentType == SerializableBasis.ContentShareType.Server)
            {
                Report("content-load-failed", "content-not-loadable", sphereId: sphereId);
                return;
            }

            ReportSphere("content-load-started", sphere);
            try
            {
                BasisLoadableBundle bundle = sphere.ToLoadableBundle();
                string loadedName;
                if (sphere.ContentType == SerializableBasis.ContentShareType.World)
                {
                    Scene scene = await BasisSceneLoad.LoadSceneAssetBundle(bundle, false, false);
                    if (!scene.IsValid() || !scene.isLoaded)
                    {
                        throw new InvalidOperationException("World scene did not finish loading.");
                    }
                    loadedName = scene.name;
                }
                else
                {
                    BundledContentHolder.Selector selector = sphere.ContentType == SerializableBasis.ContentShareType.Avatar
                        ? BundledContentHolder.Selector.Avatar
                        : BundledContentHolder.Selector.Prop;
                    GameObject loadedObject = await BasisLoadHandler.LoadGameObjectBundle(
                        BasisDeviceManagement.Instance.CreationGameobject,
                        bundle,
                        true,
                        new BasisProgressReport(),
                        CancellationToken.None,
                        sphere.transform.position,
                        Quaternion.identity,
                        Vector3.one,
                        false,
                        selector,
                        transform);
                    if (loadedObject == null)
                    {
                        throw new InvalidOperationException($"{sphere.ContentType} did not finish instantiating.");
                    }
                    loadedName = loadedObject.name;
                }

                ReportSphere("content-load-complete", sphere, loadedName);
            }
            catch (Exception exception)
            {
                ReportSphere("content-load-failed", sphere, exception.Message);
            }
        }

        public void OpenPlayerList()
        {
            BasisMainMenu.OpenWithProvider(UserListProvider.StaticTitle);
            StartCoroutine(ReportPlayerListAfterLayout());
        }

        public void SetPlayerSearch(string query)
        {
            if (TryFindActiveComponent(
                    BasisLocalization.Get("ui.search.label"),
                    out PanelTextField searchField))
            {
                searchField.SetValue(query ?? string.Empty);
            }
            StartCoroutine(ReportPlayerListAfterLayout());
        }

        public void SetPlayerSort(string sort)
        {
            if (TryFindActiveComponent(
                    BasisLocalization.Get("menu.players.sortMode"),
                    out PanelDropdown sortDropdown))
            {
                sortDropdown.SetValue(sort ?? string.Empty);
            }
            StartCoroutine(ReportPlayerListAfterLayout());
        }

        public void OpenPlayer(string displayName)
        {
            if (TryFindActiveButton(displayName, out PanelButton playerButton))
            {
                playerButton.OnClick();
                StartCoroutine(ReportPlayerStateAfterUiUpdate());
                return;
            }
            Report("player-ui-action-rejected", displayName);
        }

        public void PlayerUiAction(string localizationKey)
        {
            string title = BasisLocalization.Get(localizationKey ?? string.Empty);
            if (TryFindActiveButton(title, out PanelButton button))
            {
                button.OnClick();
                StartCoroutine(ReportPlayerStateAfterUiUpdate());
                return;
            }
            Report("player-ui-action-rejected", localizationKey);
        }

        public void SetPlayerVolume(string serializedVolume)
        {
            if (float.TryParse(
                    serializedVolume,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out float volume)
                && TryFindActiveComponent(
                    BasisLocalization.Get("menu.individualPlayer.volumeOverride"),
                    out PanelSlider slider))
            {
                slider.SetValue(Mathf.Clamp(volume, 0f, 1.5f));
                StartCoroutine(ReportPlayerStateAfterUiUpdate());
                return;
            }
            Report("player-ui-action-rejected", "volume");
        }

        public void ConfirmDialogue(string accepted)
        {
            BasisMenuDialoguePanel dialogue = BasisMainMenu.Instance?.Dialogue;
            if (dialogue == null)
            {
                Report("player-ui-action-rejected", "dialogue");
                return;
            }

            if (accepted == "1")
            {
                dialogue.AcceptButton.OnClick();
            }
            else
            {
                dialogue.DeclineButton.OnClick();
            }
            StartCoroutine(ReportPlayerStateAfterUiUpdate());
        }

        public async void ReportPlayerState()
        {
            BasisRemotePlayer player = IndividualPlayerProvider.remotePlayer;
            if (player == null)
            {
                Report("individual-player-state-rejected", "no-selected-player");
                return;
            }

            BasisPlayerSettingsData settings = await BasisPlayerSettingsManager.RequestPlayerSettings(player.UUID);
            Report(
                "individual-player-state",
                player.DisplayName,
                volume: settings.VolumeLevel,
                pinned: PinnedPlayers.IsPinned(player.UUID),
                highlighted: IndividualPlayerProvider.HasHighlight,
                avatarVisible: settings.AvatarVisible,
                chatVisible: settings.ChatVisible,
                blocked: settings.IsBlocked,
                temporarilyBlocked: player.TempBlocked,
                availableAdminActions: GetAvailableAdminActions());
        }

        public async void Reconnect()
        {
            Report("reconnect-started");
            _observeConnectionState = false;
            await BasisConnectionService.ConnectAsync(_entry, _userName);
            ResetObservedConnectionState();
            _observeConnectionState = true;
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

        private IEnumerator ReportPlayerListAfterLayout()
        {
            yield return null;
            yield return null;

            List<PlayerListEntry> entries = new List<PlayerListEntry>();
            foreach (BasisNetworkPlayer player in BasisNetworkPlayers.Players.Values)
            {
                string visibleTitle = player.Player != null && player.Player.IsLocal
                    ? BasisLocalization.Get("menu.players.you", player.SafeDisplayName)
                    : player.SafeDisplayName;
                if (TryFindActiveButton(visibleTitle, out PanelButton button))
                {
                    entries.Add(new PlayerListEntry
                    {
                        displayName = player.SafeDisplayName,
                        siblingIndex = button.transform.GetSiblingIndex(),
                    });
                }
            }
            entries.Sort((left, right) => left.siblingIndex.CompareTo(right.siblingIndex));

            string[] labels = new string[entries.Count];
            for (int index = 0; index < entries.Count; index++)
            {
                labels[index] = entries[index].displayName;
            }
            Report("player-list-state", visibleLabels: labels);
        }

        private IEnumerator ReportPlayerStateAfterUiUpdate()
        {
            yield return new WaitForSecondsRealtime(0.25f);
            ReportPlayerState();
        }

        private static bool TryFindActiveButton(string title, out PanelButton result)
        {
            return TryFindActiveComponent(title, out result);
        }

        private static bool TryFindActiveComponent<T>(string title, out T result)
            where T : PanelComponent
        {
            T[] components = FindObjectsByType<T>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int index = 0; index < components.Length; index++)
            {
                T component = components[index];
                if (component != null
                    && component.isActiveAndEnabled
                    && component.Descriptor != null
                    && string.Equals(component.Descriptor.Title, title, StringComparison.Ordinal))
                {
                    result = component;
                    return true;
                }
            }
            result = null;
            return false;
        }

        private static string[] GetAvailableAdminActions()
        {
            List<string> available = new List<string>();
            foreach (IndividualPlayerAdminAction action in Enum.GetValues(typeof(IndividualPlayerAdminAction)))
            {
                if (IndividualPlayerActionPermissions.CanUse(BasisNetworkManagement.LocalPermissions, action)
                    && TryFindActiveButton(BasisLocalization.Get(GetAdminActionTitleKey(action)), out _))
                {
                    available.Add(action.ToString());
                }
            }
            return available.ToArray();
        }

        private static string GetAdminActionTitleKey(IndividualPlayerAdminAction action)
        {
            return action switch
            {
                IndividualPlayerAdminAction.Kick => "menu.individualPlayer.kick",
                IndividualPlayerAdminAction.Ban => "menu.individualPlayer.ban",
                IndividualPlayerAdminAction.IpBan => "menu.individualPlayer.ipBan",
                IndividualPlayerAdminAction.Teleport => "menu.individualPlayer.teleportTo",
                IndividualPlayerAdminAction.Shout => "menu.individualPlayer.shout.enable",
                IndividualPlayerAdminAction.Message => "menu.individualPlayer.sendMessage",
                IndividualPlayerAdminAction.EditPermissions => "menu.individualPlayer.grantPermission",
                _ => string.Empty,
            };
        }

        private void Subscribe()
        {
            BasisNetworkHandleChat.OnChatMessageReceived -= OnChatMessageReceived;
            BasisNetworkHandleChat.OnChatMessageReceived += OnChatMessageReceived;
            BasisContentShareManager.OnSphereCreated -= OnContentSphereCreated;
            BasisContentShareManager.OnSphereCreated += OnContentSphereCreated;
            BasisContentShareManager.OnSphereRemoved -= OnContentSphereRemoved;
            BasisContentShareManager.OnSphereRemoved += OnContentSphereRemoved;
        }

        private void OnDestroy()
        {
            BasisNetworkHandleChat.OnChatMessageReceived -= OnChatMessageReceived;
            BasisContentShareManager.OnSphereCreated -= OnContentSphereCreated;
            BasisContentShareManager.OnSphereRemoved -= OnContentSphereRemoved;
        }

        private void OnChatMessageReceived(ushort senderPlayerId, string message)
        {
            Report("chat-received", message, senderPlayerId);
        }

        private void OnContentSphereCreated(BasisContentSphere sphere)
        {
            Report(
                "content-created",
                sphere.ContentURL,
                sphere.CreatorPlayerID,
                sphere.SphereNetID,
                sphere.ContentType.ToString(),
                sphere.ContentURL);
        }

        private void OnContentSphereRemoved(string sphereId)
        {
            Report("content-removed", sphereId, sphereId: sphereId);
        }

        private void ResetObservedConnectionState()
        {
            _acceptedReported = false;
            _authenticatedReported = false;
            _remotePlayerCount = -1;
        }

        private void ReportContent(string type, SerializableBasis.ContentShareMessage contentShare)
        {
            Report(
                type,
                contentShare.ContentURL,
                sphereId: contentShare.SphereNetID,
                contentType: contentShare.ContentType.ToString(),
                contentUrl: contentShare.ContentURL);
        }

        private void ReportSphere(string type, BasisContentSphere sphere, string message = "")
        {
            Report(
                type,
                message,
                sphere.CreatorPlayerID,
                sphere.SphereNetID,
                sphere.ContentType.ToString(),
                sphere.ContentURL);
        }

        private void Report(
            string type,
            string message = "",
            ushort senderPlayerId = 0,
            string sphereId = "",
            string contentType = "",
            string contentUrl = "",
            string[] visibleLabels = null,
            float volume = 0f,
            bool pinned = false,
            bool highlighted = false,
            bool avatarVisible = false,
            bool chatVisible = false,
            bool blocked = false,
            bool temporarilyBlocked = false,
            string[] availableAdminActions = null)
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
                sphereId = sphereId ?? string.Empty,
                contentType = contentType ?? string.Empty,
                contentUrl = contentUrl ?? string.Empty,
                visibleLabels = visibleLabels ?? Array.Empty<string>(),
                volume = volume,
                pinned = pinned,
                highlighted = highlighted,
                avatarVisible = avatarVisible,
                chatVisible = chatVisible,
                blocked = blocked,
                temporarilyBlocked = temporarilyBlocked,
                availableAdminActions = availableAdminActions ?? Array.Empty<string>(),
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
            public string sphereId;
            public string contentType;
            public string contentUrl;
            public string[] visibleLabels;
            public float volume;
            public bool pinned;
            public bool highlighted;
            public bool avatarVisible;
            public bool chatVisible;
            public bool blocked;
            public bool temporarilyBlocked;
            public string[] availableAdminActions;
        }

        private sealed class PlayerListEntry
        {
            public string displayName;
            public int siblingIndex;
        }

        [Serializable]
        private sealed class ContentShareInput
        {
            public string sphereId;
            public string contentUrl;
            public string unlockPassword;
            public string contentType;
            public float positionX;
            public float positionY;
            public float positionZ;
        }
    }
}
#endif
