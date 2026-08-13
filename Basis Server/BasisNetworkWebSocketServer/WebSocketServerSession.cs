using System.Buffers;
using System.Net;
using System.Net.WebSockets;
using Basis.Network.Core;

namespace Basis.Network.WebSocketServer;

public sealed class WebSocketServerSession : IAsyncDisposable
{
    private sealed class PeerClosedException : Exception
    {
        public PeerClosedException(WebSocketCloseStatus? closeStatus, string? closeDescription)
        {
            CloseStatus = closeStatus;
            CloseDescription = closeDescription;
        }

        public WebSocketCloseStatus? CloseStatus { get; }
        public string? CloseDescription { get; }
    }

    private sealed class InvalidMessageTypeException : Exception;
    private sealed class MessageTooBigException : Exception;

    private readonly record struct QueuedFrame(byte Channel, DeliveryMethod DeliveryMethod, byte[] Payload);

    private readonly WebSocket _socket;
    private readonly IWebSocketServerConnectionHandler _handler;
    private readonly WebSocketServerProtocol _protocol;
    private readonly int _maximumPayloadLength;
    private readonly int _pendingSendCapacity;
    private readonly Queue<QueuedFrame> _pendingSends;
    private readonly object _pendingSendLock = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly SemaphoreSlim _drainLock = new(1, 1);
    private volatile bool _acceptSent;
    private int _disposed;

    internal WebSocketServerSession(
        WebSocket socket,
        IWebSocketServerConnectionHandler handler,
        int maximumPayloadLength,
        IPEndPoint remoteEndPoint,
        int peerId,
        int pendingSendCapacity = 64)
    {
        if (peerId < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(peerId));
        }
        if (pendingSendCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pendingSendCapacity));
        }
        _socket = socket;
        _handler = handler;
        _maximumPayloadLength = maximumPayloadLength;
        _pendingSendCapacity = pendingSendCapacity;
        _pendingSends = new Queue<QueuedFrame>(pendingSendCapacity);
        _protocol = new WebSocketServerProtocol(maximumPayloadLength);
        RemoteEndPoint = remoteEndPoint;
        PeerId = peerId;
    }

    public IPEndPoint RemoteEndPoint { get; }
    public int PeerId { get; }
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
                            WebSocketAcceptPayloadCodec.Encode(PeerId),
                            cancellationToken).ConfigureAwait(false);
                        _acceptSent = true;
                        await DrainPendingSendsAsync(cancellationToken).ConfigureAwait(false);
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
                    await DrainPendingSendsAsync(cancellationToken).ConfigureAwait(false);
                }
                else if (protocolEvent == WebSocketServerProtocolEvent.Disconnected)
                {
                    await CloseAsync(WebSocketCloseStatus.NormalClosure, "Client disconnected", cancellationToken)
                        .ConfigureAwait(false);
                }
            }
        }
        catch (PeerClosedException exception)
        {
            await CloseAsync(
                exception.CloseStatus ?? WebSocketCloseStatus.NormalClosure,
                exception.CloseDescription ?? string.Empty,
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (InvalidMessageTypeException)
        {
            await CloseAsync(
                WebSocketCloseStatus.InvalidMessageType,
                "Only binary WebSocket messages are accepted.",
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (MessageTooBigException)
        {
            await CloseAsync(
                WebSocketCloseStatus.MessageTooBig,
                "WebSocket message exceeds the configured maximum length.",
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (WebSocketProtocolException exception)
        {
            await CloseAsync(
                WebSocketCloseStatus.ProtocolError,
                exception.Message,
                CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            await _handler.OnDisconnectedAsync(this, CancellationToken.None).ConfigureAwait(false);
        }
    }

    public bool QueueData(ReadOnlyMemory<byte> payload, byte channel, DeliveryMethod deliveryMethod)
    {
        if (payload.Length > _maximumPayloadLength)
        {
            throw new ArgumentException("Payload exceeds the configured WebSocket maximum length.", nameof(payload));
        }
        lock (_pendingSendLock)
        {
            if (_protocol.State == WebSocketServerProtocolState.Closed)
            {
                return false;
            }
            if (_pendingSends.Count >= _pendingSendCapacity)
            {
                throw new InvalidOperationException("The WebSocket pending send queue is full.");
            }
            _pendingSends.Enqueue(new QueuedFrame(channel, deliveryMethod, payload.ToArray()));
        }

        if (_acceptSent)
        {
            _ = DrainPendingSendsSafelyAsync();
        }
        return true;
    }

    private async Task DrainPendingSendsSafelyAsync()
    {
        try
        {
            await DrainPendingSendsAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            _socket.Abort();
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

    private async Task DrainPendingSendsAsync(CancellationToken cancellationToken)
    {
        await _drainLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            while (true)
            {
                QueuedFrame queuedFrame;
                lock (_pendingSendLock)
                {
                    if (_pendingSends.Count == 0)
                    {
                        return;
                    }
                    queuedFrame = _pendingSends.Dequeue();
                }
                await SendFrameAsync(
                    WebSocketFrameKind.Data,
                    queuedFrame.Channel,
                    queuedFrame.DeliveryMethod,
                    queuedFrame.Payload,
                    cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _drainLock.Release();
        }
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
                throw new PeerClosedException(_socket.CloseStatus, _socket.CloseStatusDescription);
            }
            if (result.MessageType != WebSocketMessageType.Binary)
            {
                throw new InvalidMessageTypeException();
            }
            if (writer.WrittenCount + result.Count > maximumEncodedLength)
            {
                throw new MessageTooBigException();
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
        _drainLock.Dispose();
        _socket.Dispose();
    }
}
