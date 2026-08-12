#if UNITY_WEBGL && !UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

public sealed class BasisWebMediaSource : IBasisPcmSource, IDisposable
{
    public event Action OnReady;
    public event Action<int, int> OnVideoSizeChanged;
    public event Action OnEndOfStream;
    public event Action<Exception> OnError;
    public event Action<BasisBitrateTrack> OnBitrateTrackChanged;
    public event Action<BasisAudioTrack> OnAudioTrackChanged;
    public event Action<BasisCaptionCue> OnCaptionCueChanged;

    public string Url { get; }
    public string AudioUrl { get; }
    public BasisMediaDelivery Delivery { get; }
    public Texture OutputTexture => outputTexture;
    public bool FrameIsTopLeftOrigin => false;
    public bool IsRunning => handle >= 0 && started && !disposed;
    public BasisMediaEngineState State => handle < 0 ? BasisMediaEngineState.Idle : (BasisMediaEngineState)BasisWebMediaGetState(handle);
    public long PositionUs => handle < 0 ? 0 : (long)(BasisWebMediaGetPosition(handle) * 1_000_000d);
    public string DebugInfo => null;
    public ulong DecodedFrameCount => decodedFrameCount;
    public IReadOnlyList<BasisBitrateTrack> BitrateTracks => Array.Empty<BasisBitrateTrack>();
    public IReadOnlyList<BasisAudioTrack> AudioTracks => Array.Empty<BasisAudioTrack>();
    public int SelectedBitrateIndex => -1;
    public int SelectedAudioTrackIndex => -1;

    private int handle = -1;
    private Texture2D outputTexture;
    private int textureWidth;
    private int textureHeight;
    private int lastErrorCode;
    private bool started;
    private bool disposed;
    private bool readyRaised;
    private bool endedRaised;
    private ulong decodedFrameCount;

    public BasisWebMediaSource(
        string url,
        string audioUrl = null,
        BasisMediaDelivery delivery = BasisMediaDelivery.Auto)
    {
        Url = url ?? throw new ArgumentNullException(nameof(url));
        AudioUrl = audioUrl;
        Delivery = delivery;
    }

    public TimeSpan Duration
    {
        get
        {
            double seconds = handle < 0 ? 0 : BasisWebMediaGetDuration(handle);
            return double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0
                ? TimeSpan.Zero
                : TimeSpan.FromSeconds(seconds);
        }
    }

    public void Start()
    {
        if (disposed) throw new ObjectDisposedException(nameof(BasisWebMediaSource));
        if (handle < 0)
        {
            handle = BasisWebMediaCreate(Url);
            if (handle < 0) throw new InvalidOperationException("The browser could not create the media element.");
        }

        started = true;
        endedRaised = false;
        Play();
    }

    public void Play()
    {
        if (handle < 0) return;
        started = true;
        endedRaised = false;
        BasisWebMediaPlay(handle);
    }

    public void Pause()
    {
        if (handle >= 0) BasisWebMediaPause(handle);
    }

    public void Stop()
    {
        if (handle < 0) return;
        BasisWebMediaPause(handle);
        BasisWebMediaSeek(handle, 0);
        started = false;
    }

    public void Seek(TimeSpan position)
    {
        if (handle < 0) throw new InvalidOperationException("Media is not loaded.");
        if (BasisWebMediaSeek(handle, Math.Max(0, position.TotalSeconds)) == 0)
            throw new NotSupportedException("The browser media source is not seekable.");
    }

    public bool SeekBackUs(long backUs)
    {
        if (handle < 0 || backUs <= 0) return false;
        return BasisWebMediaSeek(handle, Math.Max(0, BasisWebMediaGetPosition(handle) - backUs / 1_000_000d)) != 0;
    }

    public void SetPlaybackSettings(float volume, bool mute, float playbackRate, bool loop)
    {
        if (handle >= 0)
        {
            BasisWebMediaSetPlaybackSettings(handle, Mathf.Clamp01(volume), mute ? 1 : 0, Mathf.Clamp(playbackRate, 0.25f, 4f), loop ? 1 : 0);
        }
    }

    public void SetBuffer(BasisVideoBufferMode mode, int milliseconds) { }
    public bool SelectBitrate(int index) => false;
    public bool SelectAudioTrack(int index) => false;
    public bool TryGetPcmFormat(out int sampleRate, out int channels)
    {
        sampleRate = 0;
        channels = 0;
        return false;
    }
    public int ReadPcm(float[] buffer) => 0;

    public void Pump(bool verboseLogging = false)
    {
        if (handle < 0 || disposed) return;

        int width = BasisWebMediaGetWidth(handle);
        int height = BasisWebMediaGetHeight(handle);
        if (width > 0 && height > 0 && (outputTexture == null || width != textureWidth || height != textureHeight))
        {
            RecreateTexture(width, height);
        }

        if (outputTexture != null && BasisWebMediaUpdateTexture(handle, outputTexture.GetNativeTexturePtr().ToInt32()) != 0)
        {
            decodedFrameCount++;
            if (!readyRaised)
            {
                readyRaised = true;
                OnReady?.Invoke();
            }
        }

        int errorCode = BasisWebMediaGetError(handle);
        if (errorCode == 0) lastErrorCode = 0;
        if (errorCode != 0 && errorCode != lastErrorCode)
        {
            lastErrorCode = errorCode;
            OnError?.Invoke(new InvalidOperationException(WebErrorMessage(errorCode)));
        }

        if (State == BasisMediaEngineState.Ended && !endedRaised)
        {
            endedRaised = true;
            OnEndOfStream?.Invoke();
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (handle >= 0)
        {
            BasisWebMediaDestroy(handle);
            handle = -1;
        }
        if (outputTexture != null)
        {
            UnityEngine.Object.Destroy(outputTexture);
            outputTexture = null;
        }
    }

    private void RecreateTexture(int width, int height)
    {
        if (outputTexture != null) UnityEngine.Object.Destroy(outputTexture);
        textureWidth = width;
        textureHeight = height;
        outputTexture = new Texture2D(width, height, TextureFormat.RGBA32, false, false)
        {
            name = "BasisWebMedia.Texture",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
        };
        outputTexture.Apply(false, false);
        OnVideoSizeChanged?.Invoke(width, height);
    }

    private static string WebErrorMessage(int errorCode)
    {
        return errorCode switch
        {
            1 => "Browser media playback was blocked. Start playback from a user gesture.",
            2 => "Browser media loading failed. Verify the URL, browser codec support, and CORS response headers.",
            3 => "The browser blocked video texture access. Cross-origin media must send an Access-Control-Allow-Origin header.",
            4 => "An HTTPS page cannot load HTTP media.",
            5 => "The browser media source is not seekable.",
            _ => "Browser media playback failed.",
        };
    }

    [DllImport("__Internal")]
    private static extern int BasisWebMediaCreate(string url);
    [DllImport("__Internal")]
    private static extern void BasisWebMediaDestroy(int mediaId);
    [DllImport("__Internal")]
    private static extern void BasisWebMediaPlay(int mediaId);
    [DllImport("__Internal")]
    private static extern void BasisWebMediaPause(int mediaId);
    [DllImport("__Internal")]
    private static extern int BasisWebMediaSeek(int mediaId, double seconds);
    [DllImport("__Internal")]
    private static extern double BasisWebMediaGetPosition(int mediaId);
    [DllImport("__Internal")]
    private static extern double BasisWebMediaGetDuration(int mediaId);
    [DllImport("__Internal")]
    private static extern int BasisWebMediaGetWidth(int mediaId);
    [DllImport("__Internal")]
    private static extern int BasisWebMediaGetHeight(int mediaId);
    [DllImport("__Internal")]
    private static extern int BasisWebMediaGetState(int mediaId);
    [DllImport("__Internal")]
    private static extern int BasisWebMediaGetError(int mediaId);
    [DllImport("__Internal")]
    private static extern int BasisWebMediaUpdateTexture(int mediaId, int textureId);
    [DllImport("__Internal")]
    private static extern void BasisWebMediaSetPlaybackSettings(int mediaId, float volume, int mute, float playbackRate, int loop);
}
#endif
