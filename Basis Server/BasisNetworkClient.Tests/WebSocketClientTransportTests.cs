using Basis.Network.Core;
using Basis.Network.WebSocketClient;
using Xunit;

namespace BasisNetworkClient.Tests;

public sealed class WebSocketClientTransportTests
{
    [Theory]
    [InlineData("example.com:443/basis")]
    [InlineData("https://example.com/basis")]
    [InlineData("ws://example.com/basis#fragment")]
    public void Connect_RejectsEndpointThatIsNotAnAbsoluteWebSocketUri(string endpoint)
    {
        FakeBrowserBridge bridge = new();
        WebSocketClientTransport transport = new(bridge, 1024);

        Assert.Throws<FormatException>(() => transport.Connect(endpoint, Array.Empty<byte>()));
        Assert.Null(bridge.Endpoint);
    }

    [Fact]
    public void Connect_PreservesExplicitSchemeHostPortPathAndQuery()
    {
        FakeBrowserBridge bridge = new();
        WebSocketClientTransport transport = new(bridge, 1024);

        transport.Connect("wss://example.com:8443/network/basis?token=abc", Array.Empty<byte>());

        Assert.Equal("wss://example.com:8443/network/basis?token=abc", bridge.Endpoint);
        Assert.Equal(WebSocketClientState.Connecting, transport.State);
    }

    [Fact]
    public void BrowserOpen_SendsHelloAndWaitsForAccept()
    {
        FakeBrowserBridge bridge = new();
        WebSocketClientTransport transport = new(bridge, 1024);
        List<string> sequence = new();
        bridge.BeforeSend = () => sequence.Add("hello");
        transport.Connected += () => sequence.Add("connected");
        transport.Connect("ws://127.0.0.1:4297/basis", new byte[] { 4, 2 });

        bridge.Open();

        Assert.Equal(new[] { "hello" }, sequence);
        Assert.Equal(WebSocketClientState.Connecting, transport.State);
        Assert.True(WebSocketFrameCodec.TryDecode(
            Assert.Single(bridge.SentFrames), 1024, out WebSocketFrame frame, out _));
        Assert.Equal(WebSocketFrameKind.Hello, frame.Kind);
        Assert.Equal(new byte[] { 4, 2 }, frame.Payload);
    }

    [Fact]
    public void AcceptFrame_TransitionsToConnectedAndReportsItOnce()
    {
        FakeBrowserBridge bridge = new();
        WebSocketClientTransport transport = ConnectingTransport(bridge);
        int connectedCount = 0;
        transport.Connected += () => connectedCount++;

        bridge.Message(ControlFrame(WebSocketFrameKind.Accept));

        Assert.Equal(WebSocketClientState.Connected, transport.State);
        Assert.Equal(1, connectedCount);
    }

    [Fact]
    public void Send_RequiresConnectedStateAndEncodesDataFrame()
    {
        FakeBrowserBridge bridge = new();
        WebSocketClientTransport transport = new(bridge, 1024);
        transport.Connect("ws://127.0.0.1:4297/basis", Array.Empty<byte>());

        Assert.Throws<InvalidOperationException>(() => transport.Send(
            new byte[] { 1 }, 7, DeliveryMethod.Sequenced));

        bridge.Open();
        Assert.Throws<InvalidOperationException>(() => transport.Send(
            new byte[] { 1 }, 7, DeliveryMethod.Sequenced));

        bridge.Message(ControlFrame(WebSocketFrameKind.Accept));
        transport.Send(new byte[] { 1, 3, 5 }, 7, DeliveryMethod.Sequenced);

        Assert.True(WebSocketFrameCodec.TryDecode(
            bridge.SentFrames[1], 1024, out WebSocketFrame frame, out _));
        Assert.Equal(WebSocketFrameKind.Data, frame.Kind);
        Assert.Equal((byte)7, frame.Channel);
        Assert.Equal(DeliveryMethod.Sequenced, frame.DeliveryMethod);
        Assert.Equal(new byte[] { 1, 3, 5 }, frame.Payload);
    }

    [Fact]
    public void DataBeforeAccept_ClosesConnectionAsProtocolError()
    {
        FakeBrowserBridge bridge = new();
        WebSocketClientTransport transport = ConnectingTransport(bridge);
        string? error = null;
        transport.Error += message => error = message;

        bridge.Message(WebSocketFrameCodec.Encode(
            WebSocketFrameKind.Data,
            0,
            DeliveryMethod.ReliableOrdered,
            Array.Empty<byte>(),
            1024));

        Assert.Equal(WebSocketClientState.Closed, transport.State);
        Assert.Equal((ushort)1002, bridge.CloseCode);
        Assert.Contains("Data", error);
    }

    [Fact]
    public void DisconnectBeforeAccept_ClosesConnectionAsProtocolError()
    {
        FakeBrowserBridge bridge = new();
        WebSocketClientTransport transport = ConnectingTransport(bridge);
        string? error = null;
        transport.Error += message => error = message;

        bridge.Message(ControlFrame(WebSocketFrameKind.Disconnect));

        Assert.Equal(WebSocketClientState.Closed, transport.State);
        Assert.Equal((ushort)1002, bridge.CloseCode);
        Assert.Contains("Disconnect", error);
    }

    [Fact]
    public void BrowserMessage_DecodesDataBeforeDispatchingIt()
    {
        FakeBrowserBridge bridge = new();
        WebSocketClientTransport transport = ConnectedTransport(bridge);
        WebSocketClientData? received = null;
        transport.DataReceived += data => received = data;
        byte[] encoded = WebSocketFrameCodec.Encode(
            WebSocketFrameKind.Data,
            12,
            DeliveryMethod.ReliableOrdered,
            new byte[] { 8, 13 },
            1024);

        bridge.Message(encoded);

        Assert.NotNull(received);
        Assert.Equal((byte)12, received.Value.Channel);
        Assert.Equal(DeliveryMethod.ReliableOrdered, received.Value.DeliveryMethod);
        Assert.Equal(new byte[] { 8, 13 }, received.Value.Payload);
    }

    [Fact]
    public void InvalidBrowserMessage_ClosesConnectionAsProtocolError()
    {
        FakeBrowserBridge bridge = new();
        WebSocketClientTransport transport = ConnectedTransport(bridge);
        string? error = null;
        transport.Error += message => error = message;

        bridge.Message(new byte[] { 255, 0, 0 });

        Assert.Equal(WebSocketClientState.Closed, transport.State);
        Assert.Equal((ushort)1002, bridge.CloseCode);
        Assert.Contains("UnknownFrameKind", error);
    }

    [Fact]
    public void RejectFrame_ReportsPayloadAndClosesConnection()
    {
        FakeBrowserBridge bridge = new();
        WebSocketClientTransport transport = ConnectingTransport(bridge);
        byte[]? rejection = null;
        transport.Rejected += payload => rejection = payload;
        byte[] encoded = WebSocketFrameCodec.Encode(
            WebSocketFrameKind.Reject,
            0,
            DeliveryMethod.ReliableOrdered,
            new byte[] { 9, 9 },
            1024);

        bridge.Message(encoded);

        Assert.Equal(new byte[] { 9, 9 }, rejection);
        Assert.Equal(WebSocketClientState.Closed, transport.State);
        Assert.Equal((ushort)1008, bridge.CloseCode);
    }

    [Fact]
    public void DuplicateAccept_ClosesConnectionAsProtocolError()
    {
        FakeBrowserBridge bridge = new();
        WebSocketClientTransport transport = ConnectedTransport(bridge);
        string? error = null;
        transport.Error += message => error = message;

        bridge.Message(ControlFrame(WebSocketFrameKind.Accept));

        Assert.Equal(WebSocketClientState.Closed, transport.State);
        Assert.Equal((ushort)1002, bridge.CloseCode);
        Assert.Contains("Accept", error);
    }

    [Fact]
    public void Disconnect_SendsProtocolFrameBeforeNormalBrowserClose()
    {
        FakeBrowserBridge bridge = new();
        WebSocketClientTransport transport = ConnectedTransport(bridge);

        transport.Disconnect(new byte[] { 2, 1 });

        Assert.True(WebSocketFrameCodec.TryDecode(
            bridge.SentFrames[1], 1024, out WebSocketFrame frame, out _));
        Assert.Equal(WebSocketFrameKind.Disconnect, frame.Kind);
        Assert.Equal(new byte[] { 2, 1 }, frame.Payload);
        Assert.Equal((ushort)1000, bridge.CloseCode);
        Assert.Equal(WebSocketClientState.Closed, transport.State);
    }

    [Fact]
    public void BrowserClose_ReportsDisconnectOnlyOnce()
    {
        FakeBrowserBridge bridge = new();
        WebSocketClientTransport transport = ConnectedTransport(bridge);
        int disconnectCount = 0;
        WebSocketBrowserClose? browserClose = null;
        transport.Disconnected += close =>
        {
            disconnectCount++;
            browserClose = close;
        };

        bridge.CloseFromBrowser(1001, "going away");
        bridge.CloseFromBrowser(1001, "going away");

        Assert.Equal(1, disconnectCount);
        Assert.Equal((ushort)1001, browserClose?.Code);
        Assert.Equal("going away", browserClose?.Reason);
        Assert.Equal(WebSocketClientState.Closed, transport.State);
    }

    private static WebSocketClientTransport ConnectedTransport(FakeBrowserBridge bridge)
    {
        WebSocketClientTransport transport = ConnectingTransport(bridge);
        bridge.Message(ControlFrame(WebSocketFrameKind.Accept));
        return transport;
    }

    private static WebSocketClientTransport ConnectingTransport(FakeBrowserBridge bridge)
    {
        WebSocketClientTransport transport = new(bridge, 1024);
        transport.Connect("ws://127.0.0.1:4297/basis", Array.Empty<byte>());
        bridge.Open();
        return transport;
    }

    private static byte[] ControlFrame(WebSocketFrameKind kind)
    {
        return WebSocketFrameCodec.Encode(
            kind,
            0,
            DeliveryMethod.ReliableOrdered,
            Array.Empty<byte>(),
            1024);
    }

    private sealed class FakeBrowserBridge : IWebSocketBrowserBridge, IWebSocketBrowserConnection
    {
        private IWebSocketBrowserEventSink? _sink;

        public string? Endpoint { get; private set; }
        public List<byte[]> SentFrames { get; } = new();
        public ushort? CloseCode { get; private set; }
        public string? CloseReason { get; private set; }
        public Action? BeforeSend { get; set; }

        public IWebSocketBrowserConnection Open(string absoluteUri, IWebSocketBrowserEventSink sink)
        {
            Endpoint = absoluteUri;
            _sink = sink;
            return this;
        }

        public bool Send(byte[] payload)
        {
            BeforeSend?.Invoke();
            SentFrames.Add(payload);
            return true;
        }

        public void Close(ushort code, string reason)
        {
            CloseCode = code;
            CloseReason = reason;
        }

        public void Open() => _sink!.OnBrowserOpen();
        public void Message(byte[] payload) => _sink!.OnBrowserMessage(payload);
        public void CloseFromBrowser(ushort code, string reason) => _sink!.OnBrowserClose(code, reason);
    }
}
