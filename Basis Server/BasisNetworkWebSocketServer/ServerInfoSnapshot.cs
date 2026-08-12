namespace Basis.Network.WebSocketServer;

public sealed record ServerInfoSnapshot(
    ushort Online,
    ushort Max,
    ushort ProtocolVersion,
    string Name,
    string Motd);
