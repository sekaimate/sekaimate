#if UNITY_WEBGL && !UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using AOT;

public static class BasisWebPersistence
{
    private static readonly Dictionary<int, TaskCompletionSource<bool>> PendingOperations =
        new Dictionary<int, TaskCompletionSource<bool>>();

    private static Task _initializationTask;
    private static int _nextRequestId;

    public static Task EnsureInitializedAsync()
    {
        if (_initializationTask == null)
        {
            _initializationTask = BeginSync(populate: true);
        }

        return _initializationTask;
    }

    public static async Task FlushAsync()
    {
        await EnsureInitializedAsync();
        await BeginSync(populate: false);
    }

    private static Task BeginSync(bool populate)
    {
        int requestId = ++_nextRequestId;
        var completion = new TaskCompletionSource<bool>();
        PendingOperations.Add(requestId, completion);
        BasisWebPersistenceSync(requestId, populate ? 1 : 0, HandleSyncComplete);
        return completion.Task;
    }

    [MonoPInvokeCallback(typeof(SyncCallback))]
    private static void HandleSyncComplete(int requestId, int succeeded)
    {
        if (!PendingOperations.TryGetValue(requestId, out TaskCompletionSource<bool> completion))
        {
            return;
        }

        PendingOperations.Remove(requestId);
        if (succeeded == 1)
        {
            completion.SetResult(true);
        }
        else
        {
            completion.SetException(new InvalidOperationException("Browser persistent storage synchronization failed."));
        }
    }

    private delegate void SyncCallback(int requestId, int succeeded);

    [DllImport("__Internal")]
    private static extern void BasisWebPersistenceSync(int requestId, int populate, SyncCallback callback);
}
#endif
