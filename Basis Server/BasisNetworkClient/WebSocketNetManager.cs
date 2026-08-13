using System;
using System.Collections.Generic;
using System.Net;
using Basis.Network.Core;

namespace Basis.Network.WebSocketClient
{
    public sealed class WebSocketNetManager : NetManager
    {
        private readonly EventBasedNetListener _listener;
        private readonly IWebSocketBrowserBridge _bridge;
        private readonly int _maximumPayloadLength;
        private readonly int _pendingSendCapacity;
        private readonly int _maximumBufferedAmount;
        private readonly NetStatistics _statistics = new NetStatistics();
        private WebSocketNetPeer _peer;
        private bool _started;

        public WebSocketNetManager(
            EventBasedNetListener listener,
            Configuration configuration,
            IWebSocketBrowserBridge bridge,
            int maximumPayloadLength,
            int pendingSendCapacity,
            int maximumBufferedAmount)
        {
            _listener = listener ?? throw new ArgumentNullException(nameof(listener));
            _ = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
            if (maximumPayloadLength <= 0) throw new ArgumentOutOfRangeException(nameof(maximumPayloadLength));
            if (pendingSendCapacity <= 0) throw new ArgumentOutOfRangeException(nameof(pendingSendCapacity));
            if (maximumBufferedAmount < maximumPayloadLength) throw new ArgumentOutOfRangeException(nameof(maximumBufferedAmount));
            _maximumPayloadLength = maximumPayloadLength;
            _pendingSendCapacity = pendingSendCapacity;
            _maximumBufferedAmount = maximumBufferedAmount;
        }

        public int ConnectedPeersCount => _peer != null && _peer.IsConnected ? 1 : 0;

        public NetStatistics Statistics => _statistics;

        public void Start()
        {
            Start(IPAddress.Any, IPAddress.IPv6Any, 0);
        }

        public void Start(IPAddress ipv4Address, IPAddress ipv6Address, int port)
        {
            if (_started) throw new InvalidOperationException("The WebSocket network manager is already started.");
            _started = true;
        }

        public void Stop()
        {
            _peer?.Disconnect();
            _peer = null;
            _started = false;
        }

        public NetPeer Connect(string absoluteUri, int port, NetDataWriter writer)
        {
            if (!_started) throw new InvalidOperationException("Start the WebSocket network manager before connecting.");
            if (_peer != null) throw new InvalidOperationException("The WebSocket network manager supports one server peer.");
            if (writer == null) throw new ArgumentNullException(nameof(writer));

            WebSocketClientTransport transport = new WebSocketClientTransport(
                _bridge,
                _maximumPayloadLength,
                _maximumBufferedAmount);
            WebSocketNetPeer peer = new WebSocketNetPeer(
                transport,
                absoluteUri,
                _pendingSendCapacity,
                _maximumPayloadLength,
                _statistics);
            _peer = peer;

            transport.Connected += remotePeerId =>
            {
                peer.Accept(remotePeerId);
                _listener.RaisePeerConnected(peer);
            };
            transport.DataReceived += data =>
            {
                _statistics.PacketsReceived++;
                _statistics.BytesReceived += data.Payload.Length;
                NetPacketReader reader = NetPacketReader.Create(data.Payload, 0, data.Payload.Length, null);
                _listener.RaiseNetworkReceive(peer, reader, data.Channel, data.DeliveryMethod);
            };
            transport.Rejected += payload => RaiseDisconnected(peer, DisconnectReason.ConnectionRejected, payload);
            transport.Disconnected += _ => RaiseDisconnected(peer, DisconnectReason.RemoteConnectionClose, Array.Empty<byte>());
            transport.Error += _ => RaiseDisconnected(peer, DisconnectReason.ConnectionFailed, Array.Empty<byte>());

            transport.Connect(absoluteUri, writer.CopyData());
            return peer;
        }

        public bool SendUnconnectedMessage(NetDataWriter writer, IPEndPoint remoteEndPoint)
        {
            return false;
        }

        private void RaiseDisconnected(WebSocketNetPeer peer, DisconnectReason reason, byte[] additionalData)
        {
            if (!peer.MarkDisconnected()) return;
            NetPacketReader reader = NetPacketReader.Create(additionalData, 0, additionalData.Length, null);
            _listener.RaisePeerDisconnected(peer, new DisconnectInfo
            {
                Reason = reason,
                AdditionalData = reader,
            });
        }
    }

    public sealed class WebSocketNetPeer : NetPeer
    {
        private readonly struct PendingSend
        {
            public PendingSend(byte[] payload, byte channel, DeliveryMethod deliveryMethod)
            {
                Payload = payload;
                Channel = channel;
                DeliveryMethod = deliveryMethod;
            }

            public byte[] Payload { get; }
            public byte Channel { get; }
            public DeliveryMethod DeliveryMethod { get; }
        }

        private readonly WebSocketClientTransport _transport;
        private readonly Queue<PendingSend> _pendingSends;
        private readonly int _pendingSendCapacity;
        private readonly int _maximumPayloadLength;
        private readonly NetStatistics _statistics;
        private bool _disconnected;
        private int _remoteId;

        internal WebSocketNetPeer(
            WebSocketClientTransport transport,
            string absoluteUri,
            int pendingSendCapacity,
            int maximumPayloadLength,
            NetStatistics statistics)
        {
            _transport = transport;
            _pendingSendCapacity = pendingSendCapacity;
            _maximumPayloadLength = maximumPayloadLength;
            _pendingSends = new Queue<PendingSend>(pendingSendCapacity);
            _statistics = statistics;
            Mtu = maximumPayloadLength;
            if (Uri.TryCreate(absoluteUri, UriKind.Absolute, out Uri uri)
                && IPAddress.TryParse(uri.Host, out IPAddress address))
            {
                Address = address;
            }
            else
            {
                Address = IPAddress.None;
            }
        }

        public bool IsConnected { get; private set; }
        public int Id => 0;
        public IPAddress Address { get; }
        public int RemoteId => _remoteId;
        public int RoundTripTime => 0;
        public float TimeSinceLastPacket => 0;
        public long RemoteTimeDelta => 0;
        public int Mtu { get; }
        public object Tag { get; set; }

        internal void Accept(int remoteId)
        {
            if (_disconnected) return;
            if (remoteId < 0) throw new ArgumentOutOfRangeException(nameof(remoteId));
            _remoteId = remoteId;
            IsConnected = true;
            while (_pendingSends.Count > 0)
            {
                PendingSend pending = _pendingSends.Dequeue();
                SendNow(pending.Payload, pending.Channel, pending.DeliveryMethod);
            }
        }

        internal bool MarkDisconnected()
        {
            if (_disconnected) return false;
            _disconnected = true;
            IsConnected = false;
            _pendingSends.Clear();
            return true;
        }

        public void Disconnect()
        {
            Disconnect(Array.Empty<byte>());
        }

        public void Disconnect(byte[] payload)
        {
            if (!IsConnected || !MarkDisconnected()) return;
            _transport.Disconnect(payload ?? throw new ArgumentNullException(nameof(payload)));
        }

        public void DisconnectForce()
        {
            Disconnect();
        }

        public void Send(byte[] data, byte channelNumber, DeliveryMethod deliveryMethod)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (_disconnected) throw new InvalidOperationException("Cannot send through a disconnected WebSocket peer.");
            if (data.Length > _maximumPayloadLength)
            {
                throw new ArgumentException("Payload exceeds the configured WebSocket maximum length.", nameof(data));
            }
            byte[] payload = (byte[])data.Clone();
            if (!IsConnected)
            {
                if (_pendingSends.Count >= _pendingSendCapacity)
                {
                    throw new InvalidOperationException("The WebSocket pre-accept send queue is full.");
                }
                _pendingSends.Enqueue(new PendingSend(payload, channelNumber, deliveryMethod));
                return;
            }
            SendNow(payload, channelNumber, deliveryMethod);
        }

        public void Send(NetDataWriter data, byte channelNumber, DeliveryMethod deliveryMethod)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            Send(data.CopyData(), channelNumber, deliveryMethod);
        }

        public void SendUnreliableRawMerge(
            byte[] data,
            int offset,
            int length,
            byte channelNumber,
            int patchOffset = -1,
            byte patchValue = 0)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (offset < 0 || length < 0 || offset > data.Length - length) throw new ArgumentOutOfRangeException(nameof(offset));
            byte[] payload = new byte[length];
            Buffer.BlockCopy(data, offset, payload, 0, length);
            if (patchOffset >= 0)
            {
                if (patchOffset >= payload.Length) throw new ArgumentOutOfRangeException(nameof(patchOffset));
                payload[patchOffset] = patchValue;
            }
            Send(payload, channelNumber, DeliveryMethod.Unreliable);
        }

        public int GetPacketsCountInQueue(byte channel, DeliveryMethod deliveryMethod)
        {
            int count = 0;
            foreach (PendingSend pending in _pendingSends)
            {
                if (pending.Channel == channel && pending.DeliveryMethod == deliveryMethod) count++;
            }
            return count;
        }

        private void SendNow(byte[] payload, byte channel, DeliveryMethod deliveryMethod)
        {
            _transport.Send(payload, channel, deliveryMethod);
            _statistics.PacketsSent++;
            _statistics.BytesSent += payload.Length;
        }
    }
}
