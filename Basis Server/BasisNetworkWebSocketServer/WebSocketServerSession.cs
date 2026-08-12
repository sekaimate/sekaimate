using System.Buffers;
using System.Net;
using System.Net.WebSockets;
using Basis.Network.Core;

namespace Basis.Network.WebSocketServer;

public sealed class WebSocketServerSession : IAsyncDisposable
{
    private readonly WebSocket _socket;
    private readonly IWebSocketServerConnectionHandler _handler;
    private readonly WebSocketServerProtocol _protocol;
    private readonly int _maximumPayloadLength;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private int _disposed;

    internal WebSocketServerSession(
        WebSocket socket,
        IWebSocketServerConnectionHandler handler,
        int maximumPayloadLength,
        IPEndPoint remoteEndPoint)
    {
        _socket = socket;
        _handler = handler;
        _maximumPayloadLength = maximumPayloadLength;
        _protocol = new WebSocketServerProtocol(maximumPayloadLength);
        RemoteEndPoint = remoteEndPoint;
    }

    public IPEndPoint RemoteEndPoint { get; }
    public WebSocketServerProtocolState State => _protocol.State;

    internal async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (_socket.State == WebSocketState.Open && _protocol.State != WebSocketServerProtocolState.Closed)
            {
                byte[] encodedFrame = await ReceiveBinaryMessageAsync(cancellationToken).ConfigureAwait(false);
                WebSocketServerProtocolEvent protocolEvent = _protocol.Process(encodedFrame);
                WebSocketFrame frame = _protocol.CurrentFrame;

                if (protocolEvent == WebSocketServerProtocolEvent.ConnectionRequested)
                {
                    WebSocketConnectionDecision decision = await _handler
                        .OnConnectionRequestedAsync(this, frame.Payload, cancellationToken)
                        .ConfigureAwait(false);
                    if (decision.Accepted)
                    {
                        _protocol.Accept();
                        await SendFrameAsync(
                            WebSocketFrameKind.Accept,
                            0,
                            DeliveryMethod.ReliableOrdered,
                            ReadOnlyMemory<byte>.Empty,
                            cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        _protocol.Reject();
                        await SendFrameAsync(
                            WebSocketFrameKind.Reject,
                            0,
                            DeliveryMethod.ReliableOrdered,
                            decision.RejectionPayload,
                            cancellationToken).ConfigureAwait(false);
                        await CloseAsync(WebSocketCloseStatus.PolicyViolation, "Connection rejected", cancellationToken)
                            .ConfigureAwait(false);
                    }
                }
                else if (protocolEvent == WebSocketServerProtocolEvent.DataReceived)
                {
                    await _handler.OnDataReceivedAsync(
                        this,
                        frame.Channel,
                        frame.DeliveryMethod,
                        frame.Payload,
                        cancellationToken).ConfigureAwait(false);
                }
                else if (protocolEvent == WebSocketServerProtocolEvent.Disconnected)
                {
                    await CloseAsync(WebSocketCloseStatus.NormalClosure, "Client disconnected", cancellationToken)
                        .ConfigureAwait(false);
                }
            }
        }
        finally
        {
            await _handler.OnDisconnectedAsync(this, CancellationToken.None).ConfigureAwait(false);
        }
    }

    public Task SendDataAsync(
        byte channel,
        DeliveryMethod deliveryMethod,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        if (_protocol.State != WebSocketServerProtocolState.Connected)
        {
            throw new InvalidOperationException("Data can only be sent after the connection is accepted.");
        }
        return SendFrameAsync(WebSocketFrameKind.Data, channel, deliveryMethod, payload, cancellationToken);
    }

    public Task DisconnectAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        if (_protocol.State != WebSocketServerProtocolState.Connected)
        {
            throw new InvalidOperationException("Only an accepted connection can be disconnected.");
        }
        return DisconnectCoreAsync(payload, cancellationToken);
    }

    private async Task DisconnectCoreAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        await SendFrameAsync(
            WebSocketFrameKind.Disconnect,
            0,
            DeliveryMethod.ReliableOrdered,
            payload,
            cancellationToken).ConfigureAwait(false);
        await CloseAsync(WebSocketCloseStatus.NormalClosure, "Server disconnected", cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<byte[]> ReceiveBinaryMessageAsync(CancellationToken cancellationToken)
    {
        int maximumEncodedLength = checked(WebSocketFrameCodec.HeaderLength + _maximumPayloadLength);
        ArrayBufferWriter<byte> writer = new(Math.Min(maximumEncodedLength, 4096));

        while (true)
        {
            Memory<byte> buffer = writer.GetMemory(Math.Min(4096, maximumEncodedLength - writer.WrittenCount + 1));
            ValueWebSocketReceiveResult result = await _socket.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                throw new WebSocketException("The client closed the WebSocket without a Basis disconnect frame.");
            }
            if (result.MessageType != WebSocketMessageType.Binary)
            {
                throw new WebSocketProtocolException("Only binary WebSocket messages are accepted.");
            }
            if (writer.WrittenCount + result.Count > maximumEncodedLength)
            {
                throw new WebSocketProtocolException("WebSocket message exceeds the configured maximum length.");
            }

            writer.Advance(result.Count);
            if (result.EndOfMessage)
            {
                return writer.WrittenSpan.ToArray();
            }
        }
    }

    private async Task SendFrameAsync(
        WebSocketFrameKind kind,
        byte channel,
        DeliveryMethod deliveryMethod,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        byte[] encoded = WebSocketFrameCodec.Encode(
            kind,
            channel,
            deliveryMethod,
            payload.Span,
            _maximumPayloadLength);

        await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _socket.SendAsync(encoded, WebSocketMessageType.Binary, true, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private Task CloseAsync(WebSocketCloseStatus status, string description, CancellationToken cancellationToken)
    {
        return _socket.State is WebSocketState.Open or WebSocketState.CloseReceived
            ? _socket.CloseAsync(status, description, cancellationToken)
            : Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }
        await CloseAsync(WebSocketCloseStatus.NormalClosure, "Server stopping", CancellationToken.None)
            .ConfigureAwait(false);
        _sendLock.Dispose();
        _socket.Dispose();
    }
}
