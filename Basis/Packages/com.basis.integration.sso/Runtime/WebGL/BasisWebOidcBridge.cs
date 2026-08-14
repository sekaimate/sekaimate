#if UNITY_WEBGL && !UNITY_EDITOR
using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Basis.Integration.Sso
{
    internal static class BasisWebOidcBridge
    {
        private const string GameObjectName = "Basis Web OIDC";
        private static BasisWebOidcReceiver receiver;
        private static TaskCompletionSource<string> pending;

        public static bool HasPendingCallback => BasisWebOidcHasPendingCallback() != 0;

        public static Task<JObject> BeginAsync(string configJson, string prompt, CancellationToken cancellationToken)
        {
            return StartAsync(() => BasisWebOidcBegin(GameObjectName, configJson, prompt ?? string.Empty), cancellationToken);
        }

        public static Task<JObject> RefreshAsync(string configJson, string refreshToken, CancellationToken cancellationToken)
        {
            return StartAsync(() => BasisWebOidcRefresh(GameObjectName, configJson, refreshToken), cancellationToken);
        }

        private static async Task<JObject> StartAsync(Action start, CancellationToken cancellationToken)
        {
            EnsureReceiver();
            if (pending != null) throw new InvalidOperationException("An SSO browser flow is already active.");

            var completion = new TaskCompletionSource<string>();
            pending = completion;
            using (cancellationToken.Register(() => completion.TrySetCanceled()))
            {
                try
                {
                    start();
                    string result = await completion.Task;
                    return JObject.Parse(result);
                }
                finally
                {
                    if (ReferenceEquals(pending, completion)) pending = null;
                }
            }
        }

        private static void EnsureReceiver()
        {
            if (receiver != null) return;
            var host = new GameObject(GameObjectName);
            UnityEngine.Object.DontDestroyOnLoad(host);
            receiver = host.AddComponent<BasisWebOidcReceiver>();
        }

        internal sealed class BasisWebOidcReceiver : MonoBehaviour
        {
            public void HandleResult(string resultJson)
            {
                TaskCompletionSource<string> completion = pending;
                pending = null;
                completion?.TrySetResult(resultJson);
            }
        }

        [DllImport("__Internal")]
        private static extern void BasisWebOidcBegin(string gameObjectName, string configJson, string prompt);

        [DllImport("__Internal")]
        private static extern void BasisWebOidcRefresh(string gameObjectName, string configJson, string refreshToken);

        [DllImport("__Internal")]
        private static extern int BasisWebOidcHasPendingCallback();
    }
}
#endif
