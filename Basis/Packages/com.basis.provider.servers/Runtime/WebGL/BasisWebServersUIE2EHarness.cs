#if UNITY_WEBGL && !UNITY_EDITOR && DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Basis.BasisUI
{
    public sealed class BasisWebServersUIE2EHarness : MonoBehaviour
    {
        private const string GameObjectName = "Basis Web Servers UI E2E";
        private const string EnabledParameter = "basisServersUIE2E";
        private float _nextReportTime;
        private string _lastState;

        [Serializable]
        private sealed class CommandPayload
        {
            public string type;
            public string value;
            public bool boolValue;
            public string id;
            public string address;
            public string port;
            public string password;
        }

        [DllImport("__Internal")]
        private static extern void BasisWebServersUIE2EReport(string json);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Initialize()
        {
            if (!IsEnabled(Application.absoluteURL)) return;
            GameObject gameObject = new GameObject(GameObjectName);
            DontDestroyOnLoad(gameObject);
            BasisWebServersUIE2EHarness harness = gameObject.AddComponent<BasisWebServersUIE2EHarness>();
            harness.StartCoroutine(harness.OpenWhenReady());
        }

        private IEnumerator OpenWhenReady()
        {
            while (!HasServersProvider()) yield return null;
            yield return new WaitForSecondsRealtime(1f);
            BasisMainMenu.OpenWithProvider(ServersProvider.TitleStatic);
        }

        private void Update()
        {
            if (Time.unscaledTime < _nextReportTime) return;
            _nextReportTime = Time.unscaledTime + 0.25f;
            ServersProvider provider = ServersProvider.ActiveInstance;
            if (provider == null) return;
            string state = provider.E2ESnapshotJson();
            if (string.Equals(state, _lastState, StringComparison.Ordinal)) return;
            _lastState = state;
            BasisWebServersUIE2EReport(state);
        }

        public void Command(string json)
        {
            CommandPayload command = JsonUtility.FromJson<CommandPayload>(json);
            if (command == null || string.IsNullOrEmpty(command.type)) return;
            if (command.type == "open")
            {
                BasisMainMenu.OpenWithProvider(ServersProvider.TitleStatic);
                return;
            }
            ServersProvider provider = ServersProvider.ActiveInstance;
            if (provider == null)
            {
                return;
            }

            switch (command.type)
            {
                case "set-username": provider.E2ESetUsername(command.value); break;
                case "set-auto-connect": provider.E2ESetAutoConnect(command.boolValue); break;
                case "add-start": provider.E2EClickAddServer(); break;
                case "editor-set":
                    provider.E2ESetEditor(new ServersProvider.E2EEditorInput
                    {
                        address = command.address,
                        port = command.port,
                        password = command.password,
                    });
                    break;
                case "editor-save": provider.E2EClickSave(); break;
                case "refresh": provider.E2EClickRefreshAll(); break;
                case "connect": provider.E2EClickConnect(command.id); break;
                case "edit": provider.E2EClickEdit(command.id); break;
                case "remove-request": provider.E2EClickRemove(); break;
                case "remove-confirm": StartCoroutine(ConfirmRemoveWhenReady()); break;
            }
        }

        private static IEnumerator ConfirmRemoveWhenReady()
        {
            float deadline = Time.realtimeSinceStartup + 5f;
            while (Time.realtimeSinceStartup < deadline)
            {
                if (ServersProvider.E2EConfirmRemove()) yield break;
                yield return null;
            }
        }

        private static bool HasServersProvider()
        {
            foreach (BasisMenuActionProvider<BasisMainMenu> provider in BasisMainMenu.Providers)
            {
                if (provider is ServersProvider) return true;
            }
            return false;
        }

        private static bool IsEnabled(string absoluteUrl)
        {
            if (!Uri.TryCreate(absoluteUrl, UriKind.Absolute, out Uri uri)) return false;
            string query = uri.Query.TrimStart('?');
            foreach (string pair in query.Split('&'))
            {
                string[] parts = pair.Split(new[] { '=' }, 2);
                if (parts.Length == 2
                    && Uri.UnescapeDataString(parts[0]) == EnabledParameter
                    && Uri.UnescapeDataString(parts[1]) == "1") return true;
            }
            return false;
        }
    }
}
#endif
