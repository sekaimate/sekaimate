namespace Basis.Network.WebSocketServer;

public sealed class ServerInfoHttpEndpointOptions
{
    private readonly HashSet<string> _normalizedAllowedOrigins = new(StringComparer.OrdinalIgnoreCase);

    public string Path { get; set; } = string.Empty;
    public List<string> AllowedOrigins { get; } = new();

    public void Validate()
    {
        if (string.IsNullOrEmpty(Path)
            || Path[0] != '/'
            || Path.Length > 1 && Path[^1] == '/'
            || Path.Contains('?')
            || Path.Contains('#'))
        {
            throw new ArgumentException("Path must be an absolute request path without a trailing slash, query, or fragment.", nameof(Path));
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
