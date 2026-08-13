#if UNITY_WEBGL && !UNITY_EDITOR
using AOT;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

internal sealed class BasisWebClipboardBackend : IBasisClipboardBackend
{
    private delegate void OperationCompletedCallback(int requestId, int succeeded, IntPtr valuePointer);

    private static readonly OperationCompletedCallback OperationCompleted = HandleOperationCompleted;
    private static readonly Dictionary<int, TaskCompletionSource<string>> PendingOperations = new();
    private static int nextRequestId;

    public Task WriteTextAsync(string text)
    {
        int requestId = CreatePendingOperation(out TaskCompletionSource<string> completion);
        BasisWebClipboardWrite(text, requestId, OperationCompleted);
        return completion.Task;
    }

    public Task<string> ReadTextAsync()
    {
        int requestId = CreatePendingOperation(out TaskCompletionSource<string> completion);
        BasisWebClipboardRead(requestId, OperationCompleted);
        return completion.Task;
    }

    private static int CreatePendingOperation(out TaskCompletionSource<string> completion)
    {
        do
        {
            nextRequestId = nextRequestId == int.MaxValue ? 1 : nextRequestId + 1;
        }
        while (PendingOperations.ContainsKey(nextRequestId));

        completion = new TaskCompletionSource<string>();
        PendingOperations.Add(nextRequestId, completion);
        return nextRequestId;
    }

    [MonoPInvokeCallback(typeof(OperationCompletedCallback))]
    private static void HandleOperationCompleted(int requestId, int succeeded, IntPtr valuePointer)
    {
        if (!PendingOperations.TryGetValue(requestId, out TaskCompletionSource<string> completion))
        {
            return;
        }

        PendingOperations.Remove(requestId);
        string value = Marshal.PtrToStringUTF8(valuePointer) ?? string.Empty;
        if (succeeded == 1)
        {
            completion.TrySetResult(value);
        }
        else
        {
            completion.TrySetException(new InvalidOperationException(value));
        }
    }

    [DllImport("__Internal")]
    private static extern void BasisWebClipboardWrite(
        string text,
        int requestId,
        OperationCompletedCallback onCompleted);

    [DllImport("__Internal")]
    private static extern void BasisWebClipboardRead(
        int requestId,
        OperationCompletedCallback onCompleted);
}
#endif
