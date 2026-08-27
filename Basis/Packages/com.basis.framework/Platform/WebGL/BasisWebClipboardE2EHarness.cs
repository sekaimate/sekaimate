#if UNITY_WEBGL && !UNITY_EDITOR && DEVELOPMENT_BUILD
using AOT;
using System;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

internal static class BasisWebClipboardE2EHarness
{
    private delegate void WriteRequestedCallback(IntPtr textPointer, int textLength);
    private delegate void ReadRequestedCallback();

    private static readonly WriteRequestedCallback WriteRequested = HandleWriteRequested;
    private static readonly ReadRequestedCallback ReadRequested = HandleReadRequested;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Initialize()
    {
        if (BasisWebClipboardE2EConfiguration.IsEnabled(Application.absoluteURL))
        {
            BasisWebClipboardE2EInitialize(WriteRequested, ReadRequested);
        }
    }

    [MonoPInvokeCallback(typeof(WriteRequestedCallback))]
    private static async void HandleWriteRequested(IntPtr textPointer, int textLength)
    {
        try
        {
            string text = DecodeUtf8(textPointer, textLength);
            await BasisClipboard.WriteTextAsync(text);
            Publish("write", true, string.Empty, string.Empty);
        }
        catch (Exception exception)
        {
            Publish("write", false, string.Empty, exception.Message);
        }
    }

    private static string DecodeUtf8(IntPtr pointer, int length)
    {
        if (pointer == IntPtr.Zero || length <= 0)
        {
            return string.Empty;
        }

        byte[] bytes = new byte[length];
        Marshal.Copy(pointer, bytes, 0, length);
        return Encoding.UTF8.GetString(bytes);
    }

    [MonoPInvokeCallback(typeof(ReadRequestedCallback))]
    private static async void HandleReadRequested()
    {
        try
        {
            string text = await BasisClipboard.ReadTextAsync();
            Publish("read", true, text, string.Empty);
        }
        catch (Exception exception)
        {
            Publish("read", false, string.Empty, exception.Message);
        }
    }

    private static void Publish(string operation, bool succeeded, string text, string error)
    {
        var result = new Result
        {
            operation = operation,
            succeeded = succeeded,
            text = text,
            error = error
        };
        BasisWebClipboardE2EPublish(JsonUtility.ToJson(result));
    }

    [Serializable]
    private sealed class Result
    {
        public string operation;
        public bool succeeded;
        public string text;
        public string error;
    }

    [DllImport("__Internal")]
    private static extern void BasisWebClipboardE2EInitialize(
        WriteRequestedCallback onWriteRequested,
        ReadRequestedCallback onReadRequested);

    [DllImport("__Internal")]
    private static extern void BasisWebClipboardE2EPublish(string resultJson);
}
#endif
