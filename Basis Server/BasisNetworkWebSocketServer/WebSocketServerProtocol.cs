using Basis.Network.Core;

namespace Basis.Network.WebSocketServer;

public enum WebSocketServerProtocolState
{
    AwaitingHello,
    AwaitingAcceptance,
    Connected,
    Closed,
}

public enum WebSocketServerProtocolEvent
{
    ConnectionRequested,
    DataReceived,
    Disconnected,
}

public sealed class WebSocketProtocolException : Exception
{
    public WebSocketProtocolException(string message, WebSocketFrameDecodeError decodeError = WebSocketFrameDecodeError.None)
        : base(message)
    {
        DecodeError = decodeError;
    }

    public WebSocketFrameDecodeError DecodeError { get; }
}

public sealed class WebSocketServerProtocol
{
    private readonly int _maximumPayloadLength;

    public WebSocketServerProtocol(int maximumPayloadLength)
    {
        if (maximumPayloadLength <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumPayloadLength));
        }
        _maximumPayloadLength = maximumPayloadLength;
    }

    public WebSocketServerProtocolState State { get; private set; } = WebSocketServerProtocolState.AwaitingHello;
    public WebSocketFrame CurrentFrame { get; private set; }

    public WebSocketServerProtocolEvent Process(ReadOnlySpan<byte> encodedFrame)
    {
        if (!WebSocketFrameCodec.TryDecode(
                encodedFrame,
                _maximumPayloadLength,
                out WebSocketFrame frame,
                out WebSocketFrameDecodeError decodeError))
        {
            throw new WebSocketProtocolException($"Invalid WebSocket protocol frame: {decodeError}.", decodeError);
        }

        CurrentFrame = frame;
        return State switch
        {
            WebSocketServerProtocolState.AwaitingHello => ProcessHello(frame),
            WebSocketServerProtocolState.AwaitingAcceptance => throw InvalidFrame(frame),
            WebSocketServerProtocolState.Connected => ProcessConnected(frame),
            WebSocketServerProtocolState.Closed => throw InvalidFrame(frame),
            _ => throw new InvalidOperationException($"Unknown WebSocket protocol state '{State}'."),
        };
    }

    public void Accept()
    {
        if (State != WebSocketServerProtocolState.AwaitingAcceptance)
        {
            throw new InvalidOperationException($"Cannot accept a connection in state '{State}'.");
        }
        State = WebSocketServerProtocolState.Connected;
    }

    public void Reject()
    {
        if (State != WebSocketServerProtocolState.AwaitingAcceptance)
        {
            throw new InvalidOperationException($"Cannot reject a connection in state '{State}'.");
        }
        State = WebSocketServerProtocolState.Closed;
    }

    private WebSocketServerProtocolEvent ProcessHello(WebSocketFrame frame)
    {
        if (frame.Kind != WebSocketFrameKind.Hello)
        {
            throw InvalidFrame(frame);
        }
        State = WebSocketServerProtocolState.AwaitingAcceptance;
        return WebSocketServerProtocolEvent.ConnectionRequested;
    }

    private WebSocketServerProtocolEvent ProcessConnected(WebSocketFrame frame)
    {
        if (frame.Kind == WebSocketFrameKind.Data)
        {
            return WebSocketServerProtocolEvent.DataReceived;
        }
        if (frame.Kind == WebSocketFrameKind.Disconnect)
        {
            State = WebSocketServerProtocolState.Closed;
            return WebSocketServerProtocolEvent.Disconnected;
        }
        throw InvalidFrame(frame);
    }

    private WebSocketProtocolException InvalidFrame(WebSocketFrame frame)
    {
        return new WebSocketProtocolException($"Frame kind '{frame.Kind}' is invalid in state '{State}'.");
    }
}
