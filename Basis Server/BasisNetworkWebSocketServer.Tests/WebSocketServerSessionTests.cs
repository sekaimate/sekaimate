using System.Net;
using System.Net.WebSockets;
using Basis.Network.Core;
using Basis.Network.WebSocketServer;
using Xunit;

namespace BasisNetworkWebSocketServer.Tests;

public sealed class WebSocketServerSessionTests
{
    private const int MaximumPayloadLength = 1024;
    private const int PeerId = 0x01020304;

    [Fact]
    public async Task RunAsync_SendsAcceptBeforeDeliveringQueuedData()
    {
        List<string> events = new();
        FakeWebSocket socket = new(events,
            Frame(WebSocketFrameKind.Hello, new byte[] { 10 }),
            Frame(WebSocketFrameKind.Data, new byte[] { 20 }),
            Frame(WebSocketFrameKind.Disconnect, Array.Empty<byte>()));
        RecordingHandler handler = new(events);
        await using WebSocketServerSession session = new(
            socket,
            handler,
            MaximumPayloadLength,
            new IPEndPoint(IPAddress.Loopback, 12345),
            PeerId);

        await session.RunAsync(CancellationToken.None);

        Assert.Equal(
            new[] { "handler:hello", "send:Accept", "handler:data", "handler:disconnected" },
            events);
        Assert.Equal(PeerId, session.PeerId);
        WebSocketFrame accept = Assert.Single(socket.SentFrames);
        Assert.Equal(WebSocketFrameKind.Accept, accept.Kind);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, accept.Payload);
    }

    private static byte[] Frame(WebSocketFrameKind kind, byte[] payload)
    {
        return WebSocketFrameCodec.Encode(
            kind,
            0,
            DeliveryMethod.ReliableOrdered,
            payload,
            MaximumPayloadLength);
    }

    private sealed class RecordingHandler : IWebSocketServerConnectionHandler
    {
        private readonly List<string> _events;

        public RecordingHandler(List<string> events)
        {
            _events = events;
        }

        public ValueTask<WebSocketConnectionDecision> OnConnectionRequestedAsync(
            WebSocketServerSession session,
            ReadOnlyMemory<byte> helloPayload,
            CancellationToken cancellationToken)
        {
            Assert.Equal(new byte[] { 10 }, helloPayload.ToArray());
            _events.Add("handler:hello");
            return ValueTask.FromResult(WebSocketConnectionDecision.Accept());
        }

        public ValueTask OnDataReceivedAsync(
            WebSocketServerSession session,
            byte channel,
            DeliveryMethod deliveryMethod,
            ReadOnlyMemory<byte> payload,
            CancellationToken cancellationToken)
        {
            Assert.Equal(new byte[] { 20 }, payload.ToArray());
            _events.Add("handler:data");
            return ValueTask.CompletedTask;
        }

        public ValueTask OnDisconnectedAsync(WebSocketServerSession session, CancellationToken cancellationToken)
        {
            _events.Add("handler:disconnected");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeWebSocket : WebSocket
    {
        private readonly List<string> _events;
        private readonly Queue<byte[]> _receivedMessages;
        private WebSocketState _state = WebSocketState.Open;
        private WebSocketCloseStatus? _closeStatus;
        private string? _closeStatusDescription;

        public List<WebSocketFrame> SentFrames { get; } = new();

        public FakeWebSocket(List<string> events, params byte[][] receivedMessages)
        {
            _events = events;
            _receivedMessages = new Queue<byte[]>(receivedMessages);
        }

        public override WebSocketCloseStatus? CloseStatus => _closeStatus;
        public override string? CloseStatusDescription => _closeStatusDescription;
        public override WebSocketState State => _state;
        public override string? SubProtocol => null;

        public override void Abort()
        {
            _state = WebSocketState.Aborted;
        }

        public override Task CloseAsync(
            WebSocketCloseStatus closeStatus,
            string? statusDescription,
            CancellationToken cancellationToken)
        {
            _closeStatus = closeStatus;
            _closeStatusDescription = statusDescription;
            _state = WebSocketState.Closed;
            return Task.CompletedTask;
        }

        public override Task CloseOutputAsync(
            WebSocketCloseStatus closeStatus,
            string? statusDescription,
            CancellationToken cancellationToken)
        {
            return CloseAsync(closeStatus, statusDescription, cancellationToken);
        }

        public override void Dispose()
        {
            _state = WebSocketState.Closed;
        }

        public override Task<WebSocketReceiveResult> ReceiveAsync(
            ArraySegment<byte> buffer,
            CancellationToken cancellationToken)
        {
            byte[] message = _receivedMessages.Dequeue();
            message.CopyTo(buffer.Array!, buffer.Offset);
            return Task.FromResult(new WebSocketReceiveResult(
                message.Length,
                WebSocketMessageType.Binary,
                true));
        }

        public override Task SendAsync(
            ArraySegment<byte> buffer,
            WebSocketMessageType messageType,
            bool endOfMessage,
            CancellationToken cancellationToken)
        {
            Assert.Equal(WebSocketMessageType.Binary, messageType);
            Assert.True(endOfMessage);
            Assert.True(WebSocketFrameCodec.TryDecode(
                buffer.AsSpan(),
                MaximumPayloadLength,
                out WebSocketFrame frame,
                out _));
            SentFrames.Add(frame);
            _events.Add($"send:{frame.Kind}");
            return Task.CompletedTask;
        }
    }
}
