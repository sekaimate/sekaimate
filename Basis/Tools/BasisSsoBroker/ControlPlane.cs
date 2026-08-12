using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

/// <summary>
/// Persistent state for the meeting control plane.  OAuth configuration remains in BrokerOptions;
/// this file contains per-meeting operational data and is intentionally never returned by public
/// endpoints.  Mount it on durable storage in production.
/// </summary>
sealed class MeetingStore
{
    private readonly object _gate = new();
    private readonly string _path;
    private ControlPlaneState _state = new();

    public MeetingStore()
    {
        _path = Environment.GetEnvironmentVariable("BASIS_CONTROL_PLANE_STORE_PATH")
            ?? Path.Combine(AppContext.BaseDirectory, "control-plane.json");
        Load();
    }

    public IReadOnlyList<MeetingRecord> List()
    {
        lock (_gate) return _state.Meetings.OrderByDescending(meeting => meeting.CreatedAt).Select(meeting => meeting.Copy()).ToArray();
    }

    public MeetingRecord? Find(string id)
    {
        lock (_gate) return _state.Meetings.FirstOrDefault(meeting => string.Equals(meeting.Id, id, StringComparison.Ordinal))?.Copy();
    }

    public MeetingRecord? FindInvite(string token)
    {
        lock (_gate) return _state.Meetings.FirstOrDefault(meeting => CryptographicEquals(meeting.InviteToken, token))?.Copy();
    }

    public bool Exists(string id)
    {
        lock (_gate) return _state.Meetings.Any(meeting => string.Equals(meeting.Id, id, StringComparison.Ordinal));
    }

    public void EnsureSingleComposeMeeting(string id, string title, string host, ushort port, string password, string transportPublicKey)
    {
        lock (_gate)
        {
            MeetingRecord? existing = _state.Meetings.FirstOrDefault(meeting => string.Equals(meeting.Id, id, StringComparison.Ordinal));
            if (existing != null)
            {
                // `local` is a projection of the one active Docker Compose server, not a
                // separately provisioned meeting. Refresh all connection material on every
                // broker start so changing Password, port, host, or regenerated SSO keys never
                // leaves invitation links pointing to stale credentials.
                bool changed = !string.Equals(existing.Title, title, StringComparison.Ordinal)
                    || !string.Equals(existing.Host, host, StringComparison.Ordinal)
                    || existing.Port != port
                    || !string.Equals(existing.Password, password, StringComparison.Ordinal)
                    || !string.Equals(existing.TransportPublicKey, transportPublicKey, StringComparison.Ordinal)
                    || !string.Equals(existing.Status, "ready", StringComparison.Ordinal);
                if (changed)
                {
                    existing.Title = title;
                    existing.Host = host;
                    existing.Port = port;
                    existing.Password = password;
                    existing.TransportPublicKey = transportPublicKey;
                    existing.Status = string.IsNullOrWhiteSpace(host) ? "provisioning" : "ready";
                    existing.StatusDetail = string.IsNullOrWhiteSpace(host)
                        ? "Set BASIS_MEETING_PUBLIC_HOST before sharing an invitation."
                        : "Running through the single-room Docker Compose deployment.";
                    existing.UpdatedAt = DateTimeOffset.UtcNow;
                    SaveLocked();
                }
                return;
            }
            if (_state.Meetings.Count > 0) return;
            _state.Meetings.Add(new MeetingRecord
            {
                Id = id,
                Title = title,
                Host = host,
                Port = port,
                Password = password,
                TransportPublicKey = transportPublicKey,
                InviteToken = MeetingIdentity.RandomToken(),
                Status = string.IsNullOrWhiteSpace(host) ? "provisioning" : "ready",
                StatusDetail = string.IsNullOrWhiteSpace(host)
                    ? "Set BASIS_MEETING_PUBLIC_HOST before sharing an invitation."
                    : "Running through the single-room Docker Compose deployment.",
            });
            SaveLocked();
        }
    }

    public void Add(MeetingRecord meeting)
    {
        lock (_gate)
        {
            if (_state.Meetings.Any(existing => string.Equals(existing.Id, meeting.Id, StringComparison.Ordinal)))
                throw new InvalidOperationException("A meeting with that ID already exists.");
            _state.Meetings.Add(meeting.Copy());
            SaveLocked();
        }
    }

    public bool UpdateStatus(string id, string status, string? detail = null, string? host = null, ushort? port = null)
    {
        lock (_gate)
        {
            MeetingRecord? meeting = _state.Meetings.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.Ordinal));
            if (meeting == null) return false;
            meeting.Status = status;
            meeting.StatusDetail = detail ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(host)) meeting.Host = host;
            if (port is { } value && value > 0) meeting.Port = value;
            meeting.UpdatedAt = DateTimeOffset.UtcNow;
            SaveLocked();
            return true;
        }
    }

    public bool Delete(string id, out MeetingRecord? meeting)
    {
        lock (_gate)
        {
            int index = _state.Meetings.FindIndex(item => string.Equals(item.Id, id, StringComparison.Ordinal));
            if (index < 0) { meeting = null; return false; }
            meeting = _state.Meetings[index].Copy();
            _state.Meetings.RemoveAt(index);
            SaveLocked();
            return true;
        }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_path)) return;
            ControlPlaneState? loaded = JsonSerializer.Deserialize<ControlPlaneState>(File.ReadAllText(_path));
            if (loaded?.Meetings != null) _state = loaded;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"Basis control plane: failed to load '{_path}': {error.Message}");
        }
    }

    private void SaveLocked()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path) ?? ".");
        string temporary = _path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(_state, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
        File.Move(temporary, _path, overwrite: true);
        try { File.SetUnixFileMode(_path, UnixFileMode.UserRead | UnixFileMode.UserWrite); } catch { /* Windows / unsupported FS */ }
    }

    private static bool CryptographicEquals(string? expected, string? actual)
    {
        if (string.IsNullOrEmpty(expected) || string.IsNullOrEmpty(actual)) return false;
        byte[] left = Encoding.UTF8.GetBytes(expected);
        byte[] right = Encoding.UTF8.GetBytes(actual);
        return left.Length == right.Length && CryptographicOperations.FixedTimeEquals(left, right);
    }
}

sealed class ControlPlaneState
{
    public List<MeetingRecord> Meetings { get; set; } = [];
}

sealed class MeetingRecord
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Status { get; set; } = "provisioning";
    public string StatusDetail { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public ushort Port { get; set; } = 4296;
    public string Password { get; set; } = string.Empty;
    public string InviteToken { get; set; } = string.Empty;
    public string TicketSigningKey { get; set; } = string.Empty;
    public string TransportPrivateKey { get; set; } = string.Empty;
    public string TransportPublicKey { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public MeetingRecord Copy() => (MeetingRecord)MemberwiseClone();
}

sealed record CreateMeetingRequest(string? Title, string? Id, string? Host, ushort? Port, string? Password);

static class MeetingIdentity
{
    public static string NewId(string? requestedId, string? title, Func<string, bool> exists)
    {
        string candidate = Normalize(requestedId);
        if (candidate.Length == 0) candidate = Normalize(title);
        if (candidate.Length == 0) candidate = "meeting";
        candidate = candidate[..Math.Min(candidate.Length, 42)];
        string baseId = candidate;
        for (int number = 0; ; number++)
        {
            string suffix = number == 0 ? "-" + RandomToken(5).ToLowerInvariant() : "-" + number.ToString(System.Globalization.CultureInfo.InvariantCulture);
            string id = (baseId[..Math.Min(baseId.Length, 48 - suffix.Length)] + suffix).Trim('-');
            if (!exists(id)) return id;
        }
    }

    public static bool IsValid(string? id) => !string.IsNullOrWhiteSpace(id) && id.Length <= 48 && id.All(character => char.IsLetterOrDigit(character) || character is '-' or '_');
    public static string RandomToken(int bytes = 24) => Convert.ToBase64String(RandomNumberGenerator.GetBytes(bytes)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    public static string RandomPassword() => RandomToken(18);

    // K8s resource names are DNS labels. Meeting IDs retain '_' support for user-facing URLs,
    // so normalize them separately when naming Kubernetes resources.
    public static string KubernetesName(string id) => Normalize(id).Replace('_', '-').Trim('-');

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var result = new StringBuilder();
        bool dash = false;
        foreach (char character in value.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character)) { result.Append(character); dash = false; }
            else if (character is '-' or '_' or ' ' && !dash) { result.Append('-'); dash = true; }
        }
        return result.ToString().Trim('-');
    }
}

/// <summary>
/// Generates a Curve25519 keypair using the OpenSSL-backed .NET implementation. Kubernetes
/// control-plane images run on Linux where X25519 is available. The raw key representation is
/// the base64url form consumed by BasisSsoTransportKeys.
/// </summary>
static class MeetingKeys
{
    public static (string PrivateKey, string PublicKey, string SigningKey) Generate()
    {
        using ECDiffieHellman agreement = ECDiffieHellman.Create(ECCurve.CreateFromFriendlyName("X25519"));
        byte[] privateDer = agreement.ExportPkcs8PrivateKey();
        byte[] publicDer = agreement.PublicKey.ExportSubjectPublicKeyInfo();
        if (privateDer.Length < 32 || publicDer.Length < 32) throw new CryptographicException("X25519 key generation returned an unexpected key size.");
        byte[] privateKey = privateDer[^32..];
        byte[] publicKey = publicDer[^32..];
        return (Encode(privateKey), Encode(publicKey), Encode(RandomNumberGenerator.GetBytes(48)));
    }

    private static string Encode(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
