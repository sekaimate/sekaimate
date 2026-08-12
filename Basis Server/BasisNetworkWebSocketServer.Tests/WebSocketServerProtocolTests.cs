using Basis.Network.Core;
using Basis.Network.WebSocketServer;
using Xunit;

namespace BasisNetworkWebSocketServer.Tests;

public sealed class WebSocketServerProtocolTests
{
    private const int MaximumPayloadLength = 1024;

    [Fact]
    public void Process_FirstHelloRequestsConnectionWithoutAutomaticAcceptance()
    {
        WebSocketServerProtocol protocol = new(MaximumPayloadLength);

        WebSocketServerProtocolEvent protocolEvent = protocol.Process(Frame(WebSocketFrameKind.Hello));

        Assert.Equal(WebSocketServerProtocolEvent.ConnectionRequested, protocolEvent);
        Assert.Equal(WebSocketServerProtocolState.AwaitingAcceptance, protocol.State);
    }

    [Fact]
    public void Accept_EnablesDataFrames()
    {
        WebSocketServerProtocol protocol = new(MaximumPayloadLength);
        protocol.Process(Frame(WebSocketFrameKind.Hello));

        protocol.Accept();
        WebSocketServerProtocolEvent protocolEvent = protocol.Process(Frame(WebSocketFrameKind.Data));

        Assert.Equal(WebSocketServerProtocolEvent.DataReceived, protocolEvent);
        Assert.Equal(WebSocketServerProtocolState.Connected, protocol.State);
    }

    [Fact]
    public void Reject_ClosesPendingConnection()
    {
        WebSocketServerProtocol protocol = new(MaximumPayloadLength);
        protocol.Process(Frame(WebSocketFrameKind.Hello));

        protocol.Reject();

        Assert.Equal(WebSocketServerProtocolState.Closed, protocol.State);
    }

    [Fact]
    public void Disconnect_ClosesAcceptedConnection()
    {
        WebSocketServerProtocol protocol = AcceptedProtocol();

        WebSocketServerProtocolEvent protocolEvent = protocol.Process(Frame(WebSocketFrameKind.Disconnect));

        Assert.Equal(WebSocketServerProtocolEvent.Disconnected, protocolEvent);
        Assert.Equal(WebSocketServerProtocolState.Closed, protocol.State);
    }

    [Theory]
    [InlineData(WebSocketFrameKind.Data)]
    [InlineData(WebSocketFrameKind.Reject)]
    [InlineData(WebSocketFrameKind.Disconnect)]
    public void Process_RejectsNonHelloFirstFrame(WebSocketFrameKind kind)
    {
        WebSocketServerProtocol protocol = new(MaximumPayloadLength);

        Assert.Throws<WebSocketProtocolException>(() => protocol.Process(Frame(kind)));
    }

    [Fact]
    public void Process_RejectsDataBeforeAcceptance()
    {
        WebSocketServerProtocol protocol = new(MaximumPayloadLength);
        protocol.Process(Frame(WebSocketFrameKind.Hello));

        Assert.Throws<WebSocketProtocolException>(() => protocol.Process(Frame(WebSocketFrameKind.Data)));
    }

    [Fact]
    public void Process_RejectsDuplicateHello()
    {
        WebSocketServerProtocol protocol = AcceptedProtocol();

        Assert.Throws<WebSocketProtocolException>(() => protocol.Process(Frame(WebSocketFrameKind.Hello)));
    }

    [Fact]
    public void Process_RejectsClientRejectFrameAfterAcceptance()
    {
        WebSocketServerProtocol protocol = AcceptedProtocol();

        Assert.Throws<WebSocketProtocolException>(() => protocol.Process(Frame(WebSocketFrameKind.Reject)));
    }

    [Fact]
    public void Process_RejectsMalformedFrameWithoutFallback()
    {
        WebSocketServerProtocol protocol = new(MaximumPayloadLength);

        WebSocketProtocolException exception = Assert.Throws<WebSocketProtocolException>(
            () => protocol.Process(new byte[] { 255, 0, (byte)DeliveryMethod.ReliableOrdered }));

        Assert.Equal(WebSocketFrameDecodeError.UnknownFrameKind, exception.DecodeError);
    }

    [Fact]
    public void Process_RejectsPayloadOverConfiguredMaximum()
    {
        WebSocketServerProtocol protocol = new(1);
        byte[] frame = WebSocketFrameCodec.Encode(
            WebSocketFrameKind.Hello,
            0,
            DeliveryMethod.ReliableOrdered,
            new byte[] { 1, 2 },
            2);

        WebSocketProtocolException exception = Assert.Throws<WebSocketProtocolException>(() => protocol.Process(frame));

        Assert.Equal(WebSocketFrameDecodeError.PayloadTooLarge, exception.DecodeError);
    }

    [Fact]
    public void AcceptAndReject_RejectInvalidStateTransitions()
    {
        WebSocketServerProtocol protocol = new(MaximumPayloadLength);

        Assert.Throws<InvalidOperationException>(protocol.Accept);
        Assert.Throws<InvalidOperationException>(protocol.Reject);

        protocol.Process(Frame(WebSocketFrameKind.Hello));
        protocol.Accept();

        Assert.Throws<InvalidOperationException>(protocol.Accept);
        Assert.Throws<InvalidOperationException>(protocol.Reject);
    }

    private static WebSocketServerProtocol AcceptedProtocol()
    {
        WebSocketServerProtocol protocol = new(MaximumPayloadLength);
        protocol.Process(Frame(WebSocketFrameKind.Hello));
        protocol.Accept();
        return protocol;
    }

    private static byte[] Frame(WebSocketFrameKind kind)
    {
        return WebSocketFrameCodec.Encode(
            kind,
            0,
            DeliveryMethod.ReliableOrdered,
            new byte[] { 1 },
            MaximumPayloadLength);
    }
}
