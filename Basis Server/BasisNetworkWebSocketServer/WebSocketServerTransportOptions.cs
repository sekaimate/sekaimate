using Basis.Network.Core;

namespace Basis.Network.WebSocketServer;

public sealed class WebSocketServerTransportOptions
{
    private readonly HashSet<string> _normalizedAllowedOrigins = new(StringComparer.OrdinalIgnoreCase);

    public int Port { get; set; }
    public string Path { get; set; } = string.Empty;
    public int MaximumPayloadLength { get; set; }
    public int PendingSendCapacity { get; set; } = 64;
    public TimeSpan KeepAliveInterval { get; set; } = TimeSpan.FromSeconds(30);
    public List<string> AllowedOrigins { get; } = new();

    public void Validate()
    {
        if (Port is < 1 or > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(Port));
        }
        if (string.IsNullOrEmpty(Path)
            || Path[0] != '/'
            || Path.Length > 1 && Path[^1] == '/'
            || Path.Contains('?')
            || Path.Contains('#'))
        {
            throw new ArgumentException("Path must be an absolute request path without a trailing slash, query, or fragment.", nameof(Path));
        }
        if (MaximumPayloadLength < WebSocketAcceptPayloadCodec.PayloadLength)
        {
            throw new ArgumentOutOfRangeException(nameof(MaximumPayloadLength));
        }
        if (KeepAliveInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(KeepAliveInterval));
        }
        if (PendingSendCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(PendingSendCapacity));
        }
        if (AllowedOrigins.Count == 0)
        {
            throw new ArgumentException("At least one allowed origin is required.", nameof(AllowedOrigins));
        }

        _normalizedAllowedOrigins.Clear();
        foreach (string origin in AllowedOrigins)
        {
            if (!TryNormalizeOrigin(origin, out string normalizedOrigin))
            {
                throw new ArgumentException($"Invalid allowed origin '{origin}'.", nameof(AllowedOrigins));
            }
            if (!_normalizedAllowedOrigins.Add(normalizedOrigin))
            {
                throw new ArgumentException($"Duplicate allowed origin '{origin}'.", nameof(AllowedOrigins));
            }
        }
    }

    public bool IsOriginAllowed(string? origin)
    {
        return TryNormalizeOrigin(origin, out string normalizedOrigin)
            && _normalizedAllowedOrigins.Contains(normalizedOrigin);
    }

    private static bool TryNormalizeOrigin(string? value, out string normalizedOrigin)
    {
        normalizedOrigin = string.Empty;
        if (string.IsNullOrWhiteSpace(value)
            || value == "*"
            || value.Contains('#')
            || !Uri.TryCreate(value, UriKind.Absolute, out Uri? uri)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query)
            || uri.AbsolutePath != "/"
            || string.IsNullOrEmpty(uri.Host)
            || uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        normalizedOrigin = uri.GetLeftPart(UriPartial.Authority);
        return true;
    }
}
