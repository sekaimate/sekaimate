using System;
using Basis.BasisUI;
using Basis.Scripts.Networking;
using UnityEngine;

namespace Basis.Streaming
{
    /// <summary>
    /// Owns the <see cref="BasisStreamingMetaServer"/> lifecycle. Subscribes to
    /// <see cref="BasisSettingsDefaults.EnableStreamingMeta"/> so the listener
    /// starts/stops the moment the user flips the toggle.
    /// </summary>
    public sealed class BasisStreamingMetaRuntime : MonoBehaviour
    {
        public const string Host = "127.0.0.1";
        public const int DefaultPort = 9080;
        private const float PublishInterval = 0.1f;

        private static BasisStreamingMetaRuntime instance;

#if UNITY_WEBGL && !UNITY_EDITOR
        private bool browserPublishing;
#else
        private BasisStreamingMetaServer server;
#endif
        private bool subscribed;
        private int activePort;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (instance != null)
            {
                return;
            }

            GameObject go = new GameObject(nameof(BasisStreamingMetaRuntime));
            DontDestroyOnLoad(go);
            instance = go.AddComponent<BasisStreamingMetaRuntime>();
        }

        /// <summary>
        /// Re-applies the persisted <see cref="BasisSettingsDefaults.EnableStreamingMeta"/>
        /// value once settings have been loaded from disk. Called by
        /// <see cref="BasisSettingsDefaults.LoadAll"/>.
        /// </summary>
        /// <remarks>
        /// <see cref="Bootstrap"/> runs at <see cref="RuntimeInitializeLoadType.AfterSceneLoad"/>,
        /// which is before <see cref="BasisSettingsDefaults.LoadAll"/> populates the bindings, so
        /// the initial <see cref="OnEnable"/> read sees the construction-time default (off) and
        /// <see cref="BasisSettingsBinding{T}.LoadBindingValue"/> writes the loaded value without
        /// firing <c>OnChanged</c>. Without this re-apply the listener would only come up after a
        /// manual toggle — not on startup when the user already had it enabled.
        /// </remarks>
        public static void ApplyFromSettings()
        {
            if (instance == null)
            {
                // AfterSceneLoad normally creates the instance before LoadAll runs; if the
                // ordering ever differs, create it now and let OnEnable apply the loaded value.
                Bootstrap();
                return;
            }

            instance.ApplyCurrentSetting();
        }

        private void OnEnable()
        {
            if (!subscribed)
            {
                BasisSettingsDefaults.EnableStreamingMeta.OnChanged += HandleEnabledChanged;
                BasisSettingsDefaults.StreamingMetaPort.OnChanged += HandlePortChanged;
                subscribed = true;
            }

            ApplyCurrentSetting();
        }

        private void OnDisable()
        {
            if (subscribed)
            {
                BasisSettingsDefaults.EnableStreamingMeta.OnChanged -= HandleEnabledChanged;
                BasisSettingsDefaults.StreamingMetaPort.OnChanged -= HandlePortChanged;
                subscribed = false;
            }

            StopServer();
        }

        private void OnDestroy()
        {
            StopServer();
            if (instance == this)
            {
                instance = null;
            }
        }

        private void HandleEnabledChanged(bool _) => ApplyCurrentSetting();

        private void HandlePortChanged(string _)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return;
#else
            if (!BasisSettingsDefaults.EnableStreamingMeta.RawValue)
            {
                return;
            }

            if (server != null && ResolvePort() == activePort)
            {
                return;
            }

            StopServer();
            StartServer();
#endif
        }

        private void ApplyCurrentSetting()
        {
            if (BasisSettingsDefaults.EnableStreamingMeta.RawValue)
            {
                StartServer();
            }
            else
            {
                StopServer();
            }
        }

        private static int ResolvePort()
        {
            string raw = BasisSettingsDefaults.StreamingMetaPort.RawValue;
            if (int.TryParse(raw, out int parsed) && parsed > 0 && parsed <= 65535)
            {
                return parsed;
            }
            return DefaultPort;
        }

        private void StartServer()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if (browserPublishing)
            {
                return;
            }

            browserPublishing = true;
            InvokeRepeating(nameof(PublishTick), PublishInterval, PublishInterval);
#else
            if (server != null)
            {
                return;
            }

            int port = ResolvePort();
            try
            {
                server = new BasisStreamingMetaServer(Host, port);
                activePort = port;
                BasisDebug.Log($"[BasisStreamingMeta] overlay available at {server.Prefix}overlay.html", BasisDebug.LogTag.LocalNetwork);
                InvokeRepeating(nameof(PublishTick), PublishInterval, PublishInterval);
            }
            catch (Exception ex)
            {
                BasisDebug.LogWarning($"[BasisStreamingMeta] failed to bind http://{Host}:{port}: {ex.Message}", BasisDebug.LogTag.LocalNetwork);
                server = null;
            }
#endif
        }

        private void StopServer()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if (!browserPublishing)
            {
                return;
            }

            CancelInvoke(nameof(PublishTick));
            BasisWebStreamingMetaBridge.Clear();
            browserPublishing = false;
#else
            if (server == null)
            {
                return;
            }

            CancelInvoke(nameof(PublishTick));
            server.Dispose();
            server = null;
#endif
        }

        private void PublishTick()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if (!browserPublishing)
#else
            if (server == null)
#endif
            {
                return;
            }

            float dt = Time.smoothDeltaTime;
            float fps = dt > 0f ? 1f / dt : 0f;

            var snapshot = new BasisStreamingMetaServer.Snapshot
            {
                Fps = fps,
                TimeUtc = DateTimeOffset.UtcNow,
            };

            var peer = BasisNetworkConnection.LocalPlayerPeer;
            if (peer != null)
            {
                snapshot.Connected = true;
                snapshot.Ccu = BasisNetworkPlayers.ReceiverCount + 1;
                snapshot.PeerLimit = BasisNetworkManagement.ServerMetaDataMessage.PeerLimit;
                snapshot.RoundTripMs = peer.RoundTripTime;
                snapshot.PingMs = peer.Ping;
            }

#if UNITY_WEBGL && !UNITY_EDITOR
            BasisWebStreamingMetaBridge.Publish(snapshot);
#else
            server.PublishSnapshot(snapshot);
#endif
        }
    }
}
