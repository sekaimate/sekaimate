using UnityEngine;
public static class BasisStaticLogInitializer
{
    [RuntimeInitializeOnLoadMethod]
    private static void OnRuntimeMethodLoad()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        Application.logMessageReceived += BasisLogManager.HandleLog;
        GameObject pumpObject = new GameObject(nameof(BasisWebLogPump));
        pumpObject.hideFlags = HideFlags.HideAndDontSave;
        Object.DontDestroyOnLoad(pumpObject);
        pumpObject.AddComponent<BasisWebLogPump>();
#else
        Application.logMessageReceivedThreaded += BasisLogManager.HandleLog;
#endif
    }

#if UNITY_WEBGL && !UNITY_EDITOR
    private sealed class BasisWebLogPump : MonoBehaviour
    {
        private void Update()
        {
            BasisLogManager.ProcessQueuedLogs();
        }
    }
#endif
}
