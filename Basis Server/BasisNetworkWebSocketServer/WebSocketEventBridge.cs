using System.Net;
using Basis.Network.Core;

namespace Basis.Network.WebSocketServer;

public sealed class WebSocketEventBridge : IWebSocketServerConnectionHandler
{
    private readonly EventBasedNetListener _listener;
    private readonly int _maximumPayloadLength;
    private readonly Dictionary<WebSocketServerSession, WebSocketServerPeer> _peers = new();
    private readonly object _peersLock = new();

    public WebSocketEventBridge(EventBasedNetListener listener, int maximumPayloadLength)
    {
        _listener = listener ?? throw new ArgumentNullException(nameof(listener));
        if (maximumPayloadLength <= 0) throw new ArgumentOutOfRangeException(nameof(maximumPayloadLength));
        _maximumPayloadLength = maximumPayloadLength;
    }

    public int ConnectedPeersCount
    {
        get
        {
            lock (_peersLock)
            {
                return _peers.Count;
            }
        }
    }

    public ValueTask<WebSocketConnectionDecision> OnConnectionRequestedAsync(
        WebSocketServerSession session,
        ReadOnlyMemory<byte> helloPayload,
        CancellationToken cancellationToken)
    {
        WebSocketServerPeer peer = new(session, _maximumPayloadLength);
        WebSocketConnectionRequest request = new(session.RemoteEndPoint, helloPayload, peer);
        _listener.RaiseConnectionRequest(request);

        if (!request.Accepted)
        {
            return ValueTask.FromResult(WebSocketConnectionDecision.Reject(request.RejectionPayload));
        }

        lock (_peersLock)
        {
            _peers.Add(session, peer);
        }
        return ValueTask.FromResult(WebSocketConnectionDecision.Accept());
    }

    public ValueTask OnDataReceivedAsync(
        WebSocketServerSession session,
        byte channel,
        DeliveryMethod deliveryMethod,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        WebSocketServerPeer peer = GetPeer(session);
        byte[] data = payload.ToArray();
        _listener.RaiseNetworkReceive(
            peer,
            NetPacketReader.Create(data, 0, data.Length, null),
            channel,
            deliveryMethod);
        return ValueTask.CompletedTask;
    }

    public ValueTask OnDisconnectedAsync(WebSocketServerSession session, CancellationToken cancellationToken)
    {
        WebSocketServerPeer? peer;
        lock (_peersLock)
        {
            _peers.Remove(session, out peer);
        }
        if (peer != null && peer.MarkDisconnected())
        {
            _listener.RaisePeerDisconnected(peer, new DisconnectInfo
            {
                Reason = DisconnectReason.RemoteConnectionClose,
                AdditionalData = NetPacketReader.Create(Array.Empty<byte>(), 0, 0, null),
            });
        }
        return ValueTask.CompletedTask;
    }

    private WebSocketServerPeer GetPeer(WebSocketServerSession session)
    {
        lock (_peersLock)
        {
            return _peers.TryGetValue(session, out WebSocketServerPeer? peer)
                ? peer
                : throw new InvalidOperationException("WebSocket session was not accepted.");
        }
    }
}

internal sealed class WebSocketConnectionRequest : ConnectionRequest
{
    private readonly WebSocketServerPeer _peer;
    private bool _decided;

    public WebSocketConnectionRequest(IPEndPoint remoteEndPoint, ReadOnlyMemory<byte> payload, WebSocketServerPeer peer)
    {
        RemoteEndPoint = remoteEndPoint;
        byte[] data = payload.ToArray();
        Data = new NetDataReader(data, 0, data.Length);
        _peer = peer;
    }

    public NetDataReader Data { get; }
    public IPEndPoint RemoteEndPoint { get; }
    public bool Accepted { get; private set; }
    public byte[] RejectionPayload { get; private set; } = Array.Empty<byte>();

    public NetPeer Accept()
    {
        if (_decided) throw new InvalidOperationException("The WebSocket connection request was already decided.");
        _decided = true;
        Accepted = true;
        return _peer;
    }

    public void Reject(NetDataWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        if (_decided) throw new InvalidOperationException("The WebSocket connection request was already decided.");
        _decided = true;
        RejectionPayload = writer.CopyData();
    }
}

internal sealed class WebSocketServerPeer : NetPeer
{
    private readonly WebSocketServerSession _session;
    private readonly int _maximumPayloadLength;
    private bool _disconnected;

    public WebSocketServerPeer(WebSocketServerSession session, int maximumPayloadLength)
    {
        _session = session;
        _maximumPayloadLength = maximumPayloadLength;
    }

    public int Id => _session.PeerId;
    public IPAddress Address => _session.RemoteEndPoint.Address;
    public int RemoteId => 0;
    public int RoundTripTime => 0;
    public float TimeSinceLastPacket => 0;
    public long RemoteTimeDelta => 0;
    public int Mtu => _maximumPayloadLength;
    public object Tag { get; set; } = new();

    public void Disconnect() => Disconnect(Array.Empty<byte>());

    public void Disconnect(byte[] payload)
    {
        if (!MarkDisconnected()) return;
        _ = _session.DisconnectAsync(payload ?? throw new ArgumentNullException(nameof(payload)), CancellationToken.None);
    }

    public void DisconnectForce() => Disconnect();

    public void Send(byte[] data, byte channelNumber, DeliveryMethod deliveryMethod)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (_disconnected) throw new InvalidOperationException("Cannot send through a disconnected WebSocket peer.");
        _session.QueueData(data, channelNumber, deliveryMethod);
    }

    public void Send(NetDataWriter data, byte channelNumber, DeliveryMethod deliveryMethod)
    {
        ArgumentNullException.ThrowIfNull(data);
        Send(data.CopyData(), channelNumber, deliveryMethod);
    }

    public void SendUnreliableRawMerge(byte[] data, int offset, int length, byte channelNumber, int patchOffset = -1, byte patchValue = 0)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (offset < 0 || length < 0 || offset > data.Length - length) throw new ArgumentOutOfRangeException(nameof(offset));
        byte[] payload = new byte[length];
        Buffer.BlockCopy(data, offset, payload, 0, length);
        if (patchOffset >= 0)
        {
            if (patchOffset >= length) throw new ArgumentOutOfRangeException(nameof(patchOffset));
            payload[patchOffset] = patchValue;
        }
        Send(payload, channelNumber, DeliveryMethod.Unreliable);
    }

    public int GetPacketsCountInQueue(byte channel, DeliveryMethod deliveryMethod) => 0;

    internal bool MarkDisconnected()
    {
        if (_disconnected) return false;
        _disconnected = true;
        return true;
    }
}
