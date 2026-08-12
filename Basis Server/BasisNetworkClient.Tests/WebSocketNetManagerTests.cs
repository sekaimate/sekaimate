using Basis.Network.Core;
using Basis.Network.WebSocketClient;
using Xunit;

namespace BasisNetworkClient.Tests;

public sealed class WebSocketNetManagerTests
{
    [Fact]
    public void ResolveClientStackId_KeepsNativeDefaultAndSelectsWebSocketForWebGlPlayer()
    {
        Assert.Equal(BasisNetworkStackRegistry.LiteNetLibId, NetworkStackSelection.ResolveClientStackId("", false));
        Assert.Equal(BasisNetworkStackRegistry.WebSocketId, NetworkStackSelection.ResolveClientStackId("", true));
        Assert.Equal("custom", NetworkStackSelection.ResolveClientStackId("custom", true));
    }

    [Fact]
    public void Connect_NotifiesListenerOnlyAfterServerAccept()
    {
        FakeBrowserBridge bridge = new();
        EventBasedNetListener listener = new();
        List<string> sequence = new();
        listener.PeerConnectedEvent += _ => sequence.Add("connected");
        WebSocketNetManager manager = StartedManager(listener, bridge, pendingSendCapacity: 2);

        NetPeer peer = manager.Connect("ws://127.0.0.1:4297/basis", 4297, Writer(1));
        bridge.Open();

        Assert.Empty(sequence);

        bridge.Accept(73);

        Assert.Equal(new[] { "connected" }, sequence);
        Assert.Equal(73, peer.RemoteId);
    }

    [Fact]
    public void SendBeforeAccept_IsBoundedAndDrainsInFifoOrderAfterAccept()
    {
        FakeBrowserBridge bridge = new();
        WebSocketNetManager manager = StartedManager(new EventBasedNetListener(), bridge, pendingSendCapacity: 2);
        NetPeer peer = manager.Connect("ws://127.0.0.1:4297/basis", 4297, Writer(1));
        bridge.Open();

        peer.Send(new byte[] { 10 }, 2, DeliveryMethod.ReliableOrdered);
        peer.Send(new byte[] { 20 }, 3, DeliveryMethod.Sequenced);

        Assert.Throws<InvalidOperationException>(() =>
            peer.Send(new byte[] { 30 }, 4, DeliveryMethod.Unreliable));
        Assert.Single(bridge.SentFrames);

        bridge.Accept(1);

        Assert.Equal(3, bridge.SentFrames.Count);
        AssertFrame(bridge.SentFrames[1], 2, DeliveryMethod.ReliableOrdered, 10);
        AssertFrame(bridge.SentFrames[2], 3, DeliveryMethod.Sequenced, 20);
    }

    [Fact]
    public void BrowserData_RaisesNetworkReceiveForConnectedPeer()
    {
        FakeBrowserBridge bridge = new();
        EventBasedNetListener listener = new();
        byte[]? received = null;
        listener.NetworkReceiveEvent += (_, reader, channel, deliveryMethod) =>
        {
            Assert.Equal((byte)7, channel);
            Assert.Equal(DeliveryMethod.ReliableSequenced, deliveryMethod);
            received = reader.GetRemainingBytes();
        };
        WebSocketNetManager manager = StartedManager(listener, bridge, pendingSendCapacity: 2);
        manager.Connect("ws://127.0.0.1:4297/basis", 4297, Writer(1));
        bridge.Open();
        bridge.Accept(1);

        bridge.Data(7, DeliveryMethod.ReliableSequenced, new byte[] { 4, 5, 6 });

        Assert.Equal(new byte[] { 4, 5, 6 }, received);
    }

    private static WebSocketNetManager StartedManager(
        EventBasedNetListener listener,
        FakeBrowserBridge bridge,
        int pendingSendCapacity)
    {
        WebSocketNetManager manager = new(listener, new Configuration(), bridge, 1024, pendingSendCapacity);
        manager.Start();
        return manager;
    }

    private static NetDataWriter Writer(byte value)
    {
        NetDataWriter writer = new();
        writer.Put(value);
        return writer;
    }

    private static void AssertFrame(byte[] encoded, byte channel, DeliveryMethod deliveryMethod, byte payload)
    {
        Assert.True(WebSocketFrameCodec.TryDecode(encoded, 1024, out WebSocketFrame frame, out _));
        Assert.Equal(WebSocketFrameKind.Data, frame.Kind);
        Assert.Equal(channel, frame.Channel);
        Assert.Equal(deliveryMethod, frame.DeliveryMethod);
        Assert.Equal(new[] { payload }, frame.Payload);
    }

    private sealed class FakeBrowserBridge : IWebSocketBrowserBridge, IWebSocketBrowserConnection
    {
        private IWebSocketBrowserEventSink? _sink;

        public List<byte[]> SentFrames { get; } = new();

        public IWebSocketBrowserConnection Open(string absoluteUri, IWebSocketBrowserEventSink sink)
        {
            _sink = sink;
            return this;
        }

        public bool Send(byte[] payload)
        {
            SentFrames.Add(payload);
            return true;
        }

        public void Close(ushort code, string reason) { }

        public void Open() => _sink!.OnBrowserOpen();

        public void Accept(int peerId) => _sink!.OnBrowserMessage(WebSocketFrameCodec.Encode(
            WebSocketFrameKind.Accept,
            0,
            DeliveryMethod.ReliableOrdered,
            WebSocketAcceptPayloadCodec.Encode(peerId),
            1024));

        public void Data(byte channel, DeliveryMethod deliveryMethod, byte[] payload) =>
            _sink!.OnBrowserMessage(WebSocketFrameCodec.Encode(
                WebSocketFrameKind.Data,
                channel,
                deliveryMethod,
                payload,
                1024));
    }
}
