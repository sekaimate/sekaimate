using Basis.Network.Core;

namespace Basis.Network.WebSocketServer;

public readonly struct WebSocketConnectionDecision
{
    private WebSocketConnectionDecision(bool accepted, byte[] rejectionPayload)
    {
        Accepted = accepted;
        RejectionPayload = rejectionPayload;
    }

    public bool Accepted { get; }
    public byte[] RejectionPayload { get; }

    public static WebSocketConnectionDecision Accept()
    {
        return new WebSocketConnectionDecision(true, Array.Empty<byte>());
    }

    public static WebSocketConnectionDecision Reject(byte[] rejectionPayload)
    {
        ArgumentNullException.ThrowIfNull(rejectionPayload);
        return new WebSocketConnectionDecision(false, rejectionPayload);
    }
}

public interface IWebSocketServerConnectionHandler
{
    ValueTask<WebSocketConnectionDecision> OnConnectionRequestedAsync(
        WebSocketServerSession session,
        ReadOnlyMemory<byte> helloPayload,
        CancellationToken cancellationToken);

    ValueTask OnDataReceivedAsync(
        WebSocketServerSession session,
        byte channel,
        DeliveryMethod deliveryMethod,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken);

    ValueTask OnDisconnectedAsync(WebSocketServerSession session, CancellationToken cancellationToken);
}
