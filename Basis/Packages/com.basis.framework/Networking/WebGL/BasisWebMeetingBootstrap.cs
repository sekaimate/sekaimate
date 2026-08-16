#if UNITY_WEBGL && !UNITY_EDITOR
using System;
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

    internal static class BasisWebMeetingBootstrap
    {
        private const string MeetingParameter = "basisMeeting";
        private const string LegacyMeetingParameter = "basisNetworkE2E";
        private const string WebSocketParameter = "websocketUri";
        private const string PasswordParameter = "password";
        private const string UserNameParameter = "userName";
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
            BasisConnectionService.AutoConnectAttempted = true;
            BasisConnectionService.RequestWebMeetingConnection(entry, userName);
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
