using Basis.Network.Core;

namespace Basis.Network.WebSocketServer;

public sealed record ServerInfoSnapshot(
    ushort Online,
    ushort Max,
    ushort ProtocolVersion,
    string Name,
    string Motd)
{
    public static ServerInfoSnapshot FromConfiguration(Configuration configuration, int authenticatedPeerCount)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return new ServerInfoSnapshot(
            (ushort)Math.Clamp(authenticatedPeerCount, 0, ushort.MaxValue),
            (ushort)Math.Clamp(configuration.PeerLimit, 0, ushort.MaxValue),
            BasisNetworkCommons.ServerInfoProtocolVersion,
            configuration.ServerName ?? string.Empty,
            configuration.ServerMotd ?? string.Empty);
    }
}
