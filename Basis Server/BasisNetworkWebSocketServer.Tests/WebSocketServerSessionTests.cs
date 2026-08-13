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

    [Fact]
    public async Task RunAsync_DrainsPreAcceptDataAfterAcceptInFifoOrder()
    {
        List<string> events = new();
        FakeWebSocket socket = new(events,
            Frame(WebSocketFrameKind.Hello, new byte[] { 10 }),
            Frame(WebSocketFrameKind.Disconnect, Array.Empty<byte>()));
        QueueingHandler handler = new();
        await using WebSocketServerSession session = new(
            socket,
            handler,
            MaximumPayloadLength,
            new IPEndPoint(IPAddress.Loopback, 12345),
            PeerId,
            pendingSendCapacity: 2);

        await session.RunAsync(CancellationToken.None);

        Assert.Equal(
            new[] { WebSocketFrameKind.Accept, WebSocketFrameKind.Data, WebSocketFrameKind.Data },
            socket.SentFrames.Select(frame => frame.Kind));
        Assert.Equal(new byte[] { 1 }, socket.SentFrames[1].Payload);
        Assert.Equal(new byte[] { 2 }, socket.SentFrames[2].Payload);
    }

    [Fact]
    public async Task RunAsync_RejectsPreAcceptDataBeyondConfiguredCapacity()
    {
        List<string> events = new();
        FakeWebSocket socket = new(events, Frame(WebSocketFrameKind.Hello, new byte[] { 10 }));
        OverflowingHandler handler = new();
        await using WebSocketServerSession session = new(
            socket,
            handler,
            MaximumPayloadLength,
            new IPEndPoint(IPAddress.Loopback, 12345),
            PeerId,
            pendingSendCapacity: 1);

        await Assert.ThrowsAsync<InvalidOperationException>(() => session.RunAsync(CancellationToken.None));
    }

    [Theory]
    [InlineData(DeliveryMethod.Sequenced)]
    [InlineData(DeliveryMethod.Unreliable)]
    public async Task RunAsync_CoalescesDroppableDataWhenPendingQueueIsFull(DeliveryMethod deliveryMethod)
    {
        List<string> events = new();
        FakeWebSocket socket = new(events,
            Frame(WebSocketFrameKind.Hello, new byte[] { 10 }),
            Frame(WebSocketFrameKind.Disconnect, Array.Empty<byte>()));
        DroppableOverflowingHandler handler = new(deliveryMethod);
        await using WebSocketServerSession session = new(
            socket,
            handler,
            MaximumPayloadLength,
            new IPEndPoint(IPAddress.Loopback, 12345),
            PeerId,
            pendingSendCapacity: 1);

        await session.RunAsync(CancellationToken.None);

        Assert.Equal(
            new[] { WebSocketFrameKind.Accept, WebSocketFrameKind.Data },
            socket.SentFrames.Select(frame => frame.Kind));
        Assert.Equal(new byte[] { 2 }, socket.SentFrames[1].Payload);
    }

    [Fact]
    public async Task RunAsync_ReassemblesFragmentedBinaryMessages()
    {
        List<string> events = new();
        byte[] hello = Frame(WebSocketFrameKind.Hello, new byte[] { 10 });
        FakeWebSocket socket = new(events);
        socket.QueueReceive(hello[..2], WebSocketMessageType.Binary, endOfMessage: false);
        socket.QueueReceive(hello[2..], WebSocketMessageType.Binary, endOfMessage: true);
        socket.QueueReceive(Frame(WebSocketFrameKind.Disconnect, Array.Empty<byte>()), WebSocketMessageType.Binary, endOfMessage: true);
        RecordingHandler handler = new(events);
        await using WebSocketServerSession session = new(
            socket,
            handler,
            MaximumPayloadLength,
            new IPEndPoint(IPAddress.Loopback, 12345),
            PeerId);

        await session.RunAsync(CancellationToken.None);

        Assert.Equal(
            new[] { "handler:hello", "send:Accept", "handler:disconnected" },
            events);
    }

    [Fact]
    public async Task RunAsync_ClosesTextMessagesAsUnsupportedData()
    {
        List<string> events = new();
        FakeWebSocket socket = new(events);
        socket.QueueReceive(new byte[] { 1 }, WebSocketMessageType.Text, endOfMessage: true);
        await using WebSocketServerSession session = new(
            socket,
            new RecordingHandler(events),
            MaximumPayloadLength,
            new IPEndPoint(IPAddress.Loopback, 12345),
            PeerId);

        await session.RunAsync(CancellationToken.None);

        Assert.Equal(WebSocketCloseStatus.InvalidMessageType, socket.CloseStatus);
    }

    [Fact]
    public async Task RunAsync_ClosesOversizedMessagesAsMessageTooBig()
    {
        List<string> events = new();
        FakeWebSocket socket = new(events);
        socket.QueueReceive(new byte[MaximumPayloadLength], WebSocketMessageType.Binary, endOfMessage: false);
        socket.QueueReceive(new byte[WebSocketFrameCodec.HeaderLength + 1], WebSocketMessageType.Binary, endOfMessage: true);
        await using WebSocketServerSession session = new(
            socket,
            new RecordingHandler(events),
            MaximumPayloadLength,
            new IPEndPoint(IPAddress.Loopback, 12345),
            PeerId);

        await session.RunAsync(CancellationToken.None);

        Assert.Equal(WebSocketCloseStatus.MessageTooBig, socket.CloseStatus);
    }

    [Fact]
    public async Task RunAsync_EchoesClientCloseStatus()
    {
        List<string> events = new();
        FakeWebSocket socket = new(events);
        socket.QueueReceive(
            Array.Empty<byte>(),
            WebSocketMessageType.Close,
            endOfMessage: true,
            WebSocketCloseStatus.EndpointUnavailable,
            "server restart");
        await using WebSocketServerSession session = new(
            socket,
            new RecordingHandler(events),
            MaximumPayloadLength,
            new IPEndPoint(IPAddress.Loopback, 12345),
            PeerId);

        await session.RunAsync(CancellationToken.None);

        Assert.Equal(WebSocketCloseStatus.EndpointUnavailable, socket.CloseStatus);
        Assert.Equal("server restart", socket.CloseStatusDescription);
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

    private sealed class QueueingHandler : IWebSocketServerConnectionHandler
    {
        public ValueTask<WebSocketConnectionDecision> OnConnectionRequestedAsync(
            WebSocketServerSession session,
            ReadOnlyMemory<byte> helloPayload,
            CancellationToken cancellationToken)
        {
            session.QueueData(new byte[] { 1 }, 3, DeliveryMethod.ReliableOrdered);
            session.QueueData(new byte[] { 2 }, 4, DeliveryMethod.Sequenced);
            return ValueTask.FromResult(WebSocketConnectionDecision.Accept());
        }

        public ValueTask OnDataReceivedAsync(WebSocketServerSession session, byte channel, DeliveryMethod deliveryMethod, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
            => ValueTask.CompletedTask;

        public ValueTask OnDisconnectedAsync(WebSocketServerSession session, CancellationToken cancellationToken)
            => ValueTask.CompletedTask;
    }

    private sealed class OverflowingHandler : IWebSocketServerConnectionHandler
    {
        public ValueTask<WebSocketConnectionDecision> OnConnectionRequestedAsync(
            WebSocketServerSession session,
            ReadOnlyMemory<byte> helloPayload,
            CancellationToken cancellationToken)
        {
            session.QueueData(new byte[] { 1 }, 0, DeliveryMethod.ReliableOrdered);
            session.QueueData(new byte[] { 2 }, 0, DeliveryMethod.ReliableOrdered);
            return ValueTask.FromResult(WebSocketConnectionDecision.Accept());
        }

        public ValueTask OnDataReceivedAsync(WebSocketServerSession session, byte channel, DeliveryMethod deliveryMethod, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
            => ValueTask.CompletedTask;

        public ValueTask OnDisconnectedAsync(WebSocketServerSession session, CancellationToken cancellationToken)
            => ValueTask.CompletedTask;
    }

    private sealed class DroppableOverflowingHandler : IWebSocketServerConnectionHandler
    {
        private readonly DeliveryMethod _deliveryMethod;

        public DroppableOverflowingHandler(DeliveryMethod deliveryMethod)
        {
            _deliveryMethod = deliveryMethod;
        }

        public ValueTask<WebSocketConnectionDecision> OnConnectionRequestedAsync(
            WebSocketServerSession session,
            ReadOnlyMemory<byte> helloPayload,
            CancellationToken cancellationToken)
        {
            session.QueueData(new byte[] { 1 }, 62, _deliveryMethod);
            session.QueueData(new byte[] { 2 }, 62, _deliveryMethod);
            return ValueTask.FromResult(WebSocketConnectionDecision.Accept());
        }

        public ValueTask OnDataReceivedAsync(WebSocketServerSession session, byte channel, DeliveryMethod deliveryMethod, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
            => ValueTask.CompletedTask;

        public ValueTask OnDisconnectedAsync(WebSocketServerSession session, CancellationToken cancellationToken)
            => ValueTask.CompletedTask;
    }

    private sealed class FakeWebSocket : WebSocket
    {
        private readonly record struct ReceiveChunk(
            byte[] Payload,
            WebSocketMessageType MessageType,
            bool EndOfMessage,
            WebSocketCloseStatus? CloseStatus,
            string? CloseDescription);

        private readonly List<string> _events;
        private readonly Queue<ReceiveChunk> _receivedMessages;
        private WebSocketState _state = WebSocketState.Open;
        private WebSocketCloseStatus? _closeStatus;
        private string? _closeStatusDescription;

        public List<WebSocketFrame> SentFrames { get; } = new();

        public FakeWebSocket(List<string> events, params byte[][] receivedMessages)
        {
            _events = events;
            _receivedMessages = new Queue<ReceiveChunk>(receivedMessages.Select(message => new ReceiveChunk(
                message,
                WebSocketMessageType.Binary,
                true,
                null,
                null)));
        }

        public void QueueReceive(
            byte[] payload,
            WebSocketMessageType messageType,
            bool endOfMessage,
            WebSocketCloseStatus? closeStatus = null,
            string? closeDescription = null)
        {
            _receivedMessages.Enqueue(new ReceiveChunk(
                payload,
                messageType,
                endOfMessage,
                closeStatus,
                closeDescription));
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
            ReceiveChunk message = _receivedMessages.Dequeue();
            message.Payload.CopyTo(buffer.Array!, buffer.Offset);
            if (message.MessageType == WebSocketMessageType.Close)
            {
                _closeStatus = message.CloseStatus;
                _closeStatusDescription = message.CloseDescription;
            }
            return Task.FromResult(new WebSocketReceiveResult(
                message.Payload.Length,
                message.MessageType,
                message.EndOfMessage,
                message.CloseStatus,
                message.CloseDescription));
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
