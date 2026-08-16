#if UNITY_WEBGL && !UNITY_EDITOR
using System;
using System.Collections;
using System.Globalization;
using System.Threading;
using Basis.Network.Core;
using UnityEngine;

namespace Basis.Scripts.Networking
{
    internal static class BasisWebMeetingAutoConnectClaim
    {
        private static int _claimed;

        internal static bool TryClaim()
        {
            return Interlocked.CompareExchange(ref _claimed, 1, 0) == 0;
        }
    }

    internal sealed class BasisWebMeetingBootstrap : MonoBehaviour
    {
        private const string MeetingParameter = "basisMeeting";
        private const string LegacyMeetingParameter = "basisNetworkE2E";
        private const string WebSocketParameter = "websocketUri";
        private const string PasswordParameter = "password";
        private const string UserNameParameter = "userName";
        private const float ConnectionGateWaitSeconds = 120f;

        private ServerDirectoryEntry _entry;
        private string _userName;
        private bool _connectStarted;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Initialize()
        {
            if (!TryReadConfiguration(Application.absoluteURL, out ServerDirectoryEntry entry, out string userName))
            {
                return;
            }
            if (!BasisWebMeetingAutoConnectClaim.TryClaim())
            {
                return;
            }

            GameObject gameObject = new GameObject(nameof(BasisWebMeetingBootstrap));
            DontDestroyOnLoad(gameObject);
            BasisWebMeetingBootstrap bootstrap = gameObject.AddComponent<BasisWebMeetingBootstrap>();
            bootstrap._entry = entry;
            bootstrap._userName = userName;
            bootstrap.Subscribe();
            bootstrap.StartCoroutine(bootstrap.ConnectWhenReady());
        }

        private void Subscribe()
        {
            BasisConnectionService.ConnectionPermissionChanged -= OnConnectionPermissionChanged;
            BasisConnectionService.ConnectionPermissionChanged += OnConnectionPermissionChanged;
        }

        private void OnDestroy()
        {
            BasisConnectionService.ConnectionPermissionChanged -= OnConnectionPermissionChanged;
        }

        private void OnConnectionPermissionChanged()
        {
            if (BasisNetworkManagement.IsInitialized && IsConnectionAllowed())
            {
                StartConnection();
            }
        }

        private IEnumerator ConnectWhenReady()
        {
            while (!BasisNetworkManagement.IsInitialized)
            {
                yield return null;
            }

            float deadline = Time.realtimeSinceStartup + ConnectionGateWaitSeconds;
            while (Time.realtimeSinceStartup < deadline)
            {
                if (IsConnectionAllowed())
                {
                    StartConnection();
                    yield break;
                }

                yield return null;
            }

            BasisDebug.LogError("Web meeting connection was blocked until the sign-in gate timed out.");
        }

        private bool IsConnectionAllowed()
        {
            return string.IsNullOrWhiteSpace(BasisConnectionService.ConnectionBlockedReason?.Invoke());
        }

        private void StartConnection()
        {
            if (_connectStarted || BasisNetworkConnection.LocalPlayerIsConnected)
            {
                return;
            }

            _connectStarted = true;
            _ = BasisConnectionService.ConnectAsync(_entry, _userName);
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

            string meeting = ReadQueryParameter(uri.Query, MeetingParameter);
            string legacyMeeting = ReadQueryParameter(uri.Query, LegacyMeetingParameter);
            if (meeting != "1" && legacyMeeting != "1")
            {
                return false;
            }

            string webSocketUri = ReadQueryParameter(uri.Query, WebSocketParameter);
            userName = ReadQueryParameter(uri.Query, UserNameParameter);
            if (string.IsNullOrWhiteSpace(webSocketUri) || string.IsNullOrWhiteSpace(userName)
                || !Uri.TryCreate(webSocketUri, UriKind.Absolute, out Uri parsedWebSocketUri))
            {
                return false;
            }

            string password = ReadQueryParameter(uri.Query, PasswordParameter);
            ConnectionTarget target = new ConnectionTarget(BasisNetworkStackRegistry.WebSocketId, webSocketUri);
            target.Set(ConnectionTarget.Keys.Address, parsedWebSocketUri.Host);
            target.Set(ConnectionTarget.Keys.Port, parsedWebSocketUri.Port.ToString(CultureInfo.InvariantCulture));
            entry = new ServerDirectoryEntry
            {
                Id = "__web_meeting__",
                SourceId = SavedServersDirectorySource.Id,
                DisplayName = "Web meeting",
                Target = target,
                WebSocketUri = webSocketUri,
                ServerInfoUri = $"https://{parsedWebSocketUri.Authority}/server-info",
                HasPassword = !string.IsNullOrEmpty(password),
                Password = password,
                CanEdit = false,
                CanRemove = false,
            };
            return true;
        }

        private static string ReadQueryParameter(string query, string key)
        {
            foreach (string parameter in query.TrimStart('?').Split('&'))
            {
                int separator = parameter.IndexOf('=');
                if (separator < 0 || !string.Equals(parameter.Substring(0, separator), key, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string value = parameter.Substring(separator + 1);
                try
                {
                    return Uri.UnescapeDataString(value);
                }
                catch (UriFormatException)
                {
                    return value;
                }
            }

            return string.Empty;
        }
    }
}
#endif
