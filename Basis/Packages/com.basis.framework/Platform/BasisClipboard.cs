using System;
using System.Threading.Tasks;
using UnityEngine;

internal interface IBasisClipboardBackend
{
    Task WriteTextAsync(string text);
    Task<string> ReadTextAsync();
}

internal sealed class BasisClipboardService
{
    private readonly IBasisClipboardBackend backend;

    public BasisClipboardService(IBasisClipboardBackend backend)
    {
        this.backend = backend ?? throw new ArgumentNullException(nameof(backend));
    }

    public Task WriteTextAsync(string text)
    {
        return backend.WriteTextAsync(text ?? string.Empty);
    }

    public Task<string> ReadTextAsync()
    {
        return backend.ReadTextAsync();
    }
}

internal sealed class BasisUnityClipboardBackend : IBasisClipboardBackend
{
    public Task WriteTextAsync(string text)
    {
        GUIUtility.systemCopyBuffer = text;
        return Task.CompletedTask;
    }

    public Task<string> ReadTextAsync()
    {
        return Task.FromResult(GUIUtility.systemCopyBuffer);
    }
}

public static class BasisClipboard
{
#if UNITY_WEBGL && !UNITY_EDITOR
    private static readonly BasisClipboardService Service =
        new BasisClipboardService(new BasisWebClipboardBackend());
#else
    private static readonly BasisClipboardService Service =
        new BasisClipboardService(new BasisUnityClipboardBackend());
#endif

    public static Task WriteTextAsync(string text)
    {
        return Service.WriteTextAsync(text);
    }

    public static Task<string> ReadTextAsync()
    {
        return Service.ReadTextAsync();
    }

    public static void WriteText(string text)
    {
        WriteText(text, null);
    }

    public static async void WriteText(string text, Action onWritten)
    {
        try
        {
            await WriteTextAsync(text);
            onWritten?.Invoke();
        }
        catch (Exception exception)
        {
            BasisDebug.LogError($"Clipboard write failed: {exception.Message}");
        }
    }

    public static async void ReadText(Action<string> onTextRead)
    {
        if (onTextRead == null)
        {
            return;
        }

        try
        {
            onTextRead(await ReadTextAsync());
        }
        catch (Exception exception)
        {
            BasisDebug.LogError($"Clipboard read failed: {exception.Message}");
        }
    }
}
