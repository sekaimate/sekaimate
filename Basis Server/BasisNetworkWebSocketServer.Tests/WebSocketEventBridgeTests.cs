using System.Net;
using System.Net.WebSockets;
using Basis.Network.Core;
using Basis.Network.WebSocketServer;
using Xunit;

namespace BasisNetworkWebSocketServer.Tests;

public sealed class WebSocketEventBridgeTests
{
    [Fact]
    public async Task ConnectionRequest_RaisesExistingListenerAndQueuesChallengeUntilAccept()
    {
        EventBasedNetListener listener = new();
        WebSocketEventBridge bridge = new(listener, maximumPayloadLength: 1024);
        byte[] hello = new byte[] { 7, 8, 9 };
        NetPeer? acceptedPeer = null;

        listener.ConnectionRequestEvent += request =>
        {
            Assert.Equal(hello, request.Data.GetRemainingBytes());
            acceptedPeer = request.Accept();
            acceptedPeer.Send(new byte[] { 42 }, 6, DeliveryMethod.ReliableOrdered);
        };

        FakeWebSocket socket = new(
            Frame(WebSocketFrameKind.Hello, hello),
            Frame(WebSocketFrameKind.Disconnect, Array.Empty<byte>()));
        await using WebSocketServerSession session = new(
            socket,
            bridge,
            1024,
            new IPEndPoint(IPAddress.Loopback, 12345),
            60000,
            pendingSendCapacity: 4);

        await session.RunAsync(CancellationToken.None);

        Assert.NotNull(acceptedPeer);
        Assert.Equal(60000, acceptedPeer.Id);
        Assert.Equal(
            new[] { WebSocketFrameKind.Accept, WebSocketFrameKind.Data },
            socket.SentFrames.Select(frame => frame.Kind));
        Assert.Equal(new byte[] { 42 }, socket.SentFrames[1].Payload);
    }

    [Fact]
    public async Task AcceptedConnection_RaisesPeerConnectedBeforeReceivingData()
    {
        EventBasedNetListener listener = new();
        WebSocketEventBridge bridge = new(listener, maximumPayloadLength: 1024);
        List<string> events = new();
        listener.ConnectionRequestEvent += request =>
        {
            events.Add("request");
            request.Accept();
        };
        listener.PeerConnectedEvent += peer => events.Add($"connected:{peer.Id}");
        listener.NetworkReceiveEvent += (peer, reader, channel, method) =>
        {
            events.Add($"data:{peer.Id}");
            reader.Recycle();
        };

        FakeWebSocket socket = new(
            Frame(WebSocketFrameKind.Hello, Array.Empty<byte>()),
            WebSocketFrameCodec.Encode(
                WebSocketFrameKind.Data,
                0,
                DeliveryMethod.ReliableOrdered,
                new byte[] { 1 },
                1024),
            Frame(WebSocketFrameKind.Disconnect, Array.Empty<byte>()));
        await using WebSocketServerSession session = new(
            socket,
            bridge,
            1024,
            new IPEndPoint(IPAddress.Loopback, 12345),
            60002,
            pendingSendCapacity: 4);

        await session.RunAsync(CancellationToken.None);

        Assert.Equal(new[] { "request", "connected:60002", "data:60002" }, events);
    }

    [Fact]
    public async Task DataAndDisconnect_RaiseExistingListenerEvents()
    {
        EventBasedNetListener listener = new();
        WebSocketEventBridge bridge = new(listener, maximumPayloadLength: 1024);
        List<string> events = new();
        listener.ConnectionRequestEvent += request => request.Accept();
        listener.NetworkReceiveEvent += (peer, reader, channel, method) =>
        {
            events.Add($"data:{peer.Id}:{channel}:{method}:{reader.GetByte()}");
            reader.Recycle();
        };
        listener.PeerDisconnectedEvent += (peer, info) => events.Add($"disconnect:{peer.Id}:{info.Reason}");

        FakeWebSocket socket = new(
            Frame(WebSocketFrameKind.Hello, Array.Empty<byte>()),
            WebSocketFrameCodec.Encode(WebSocketFrameKind.Data, 5, DeliveryMethod.Sequenced, new byte[] { 11 }, 1024),
            Frame(WebSocketFrameKind.Disconnect, Array.Empty<byte>()));
        await using WebSocketServerSession session = new(
            socket,
            bridge,
            1024,
            new IPEndPoint(IPAddress.Loopback, 12345),
            60001,
            pendingSendCapacity: 4);

        await session.RunAsync(CancellationToken.None);

        Assert.Equal(new[] { "data:60001:5:Sequenced:11", "disconnect:60001:RemoteConnectionClose" }, events);
    }

    private static byte[] Frame(WebSocketFrameKind kind, byte[] payload)
        => WebSocketFrameCodec.Encode(kind, 0, DeliveryMethod.ReliableOrdered, payload, 1024);

    private sealed class FakeWebSocket : WebSocket
    {
        private readonly Queue<byte[]> _messages;
        private WebSocketState _state = WebSocketState.Open;
        public List<WebSocketFrame> SentFrames { get; } = new();

        public FakeWebSocket(params byte[][] messages) => _messages = new Queue<byte[]>(messages);
        public override WebSocketCloseStatus? CloseStatus => null;
        public override string? CloseStatusDescription => null;
        public override WebSocketState State => _state;
        public override string? SubProtocol => null;
        public override void Abort() => _state = WebSocketState.Aborted;
        public override Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken)
        {
            _state = WebSocketState.Closed;
            return Task.CompletedTask;
        }
        public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken)
            => CloseAsync(closeStatus, statusDescription, cancellationToken);
        public override void Dispose() => _state = WebSocketState.Closed;
        public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken)
        {
            byte[] message = _messages.Dequeue();
            message.CopyTo(buffer.Array!, buffer.Offset);
            return Task.FromResult(new WebSocketReceiveResult(message.Length, WebSocketMessageType.Binary, true));
        }
        public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken)
        {
            Assert.True(WebSocketFrameCodec.TryDecode(buffer.AsSpan(), 1024, out WebSocketFrame frame, out _));
            SentFrames.Add(frame);
            return Task.CompletedTask;
        }
    }
}
