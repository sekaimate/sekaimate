using System;
using Basis.Network.Core;

namespace Basis.Network.WebSocketClient
{
    public enum WebSocketClientState
    {
        Idle,
        Connecting,
        Connected,
        Closed,
    }

    public readonly struct WebSocketClientData
    {
        public WebSocketClientData(byte channel, DeliveryMethod deliveryMethod, byte[] payload)
        {
            Channel = channel;
            DeliveryMethod = deliveryMethod;
            Payload = payload ?? throw new ArgumentNullException(nameof(payload));
        }

        public byte Channel { get; }
        public DeliveryMethod DeliveryMethod { get; }
        public byte[] Payload { get; }
    }

    public readonly struct WebSocketBrowserClose
    {
        public WebSocketBrowserClose(ushort code, string reason)
        {
            Code = code;
            Reason = reason ?? string.Empty;
        }

        public ushort Code { get; }
        public string Reason { get; }
    }

    public interface IWebSocketBrowserEventSink
    {
        void OnBrowserOpen();
        void OnBrowserMessage(byte[] payload);
        void OnBrowserError(string message);
        void OnBrowserClose(ushort code, string reason);
    }

    public interface IWebSocketBrowserConnection
    {
        bool Send(byte[] payload);
        void Close(ushort code, string reason);
    }

    public interface IWebSocketBrowserBridge
    {
        IWebSocketBrowserConnection Open(string absoluteUri, IWebSocketBrowserEventSink sink);
    }

    public sealed class WebSocketClientTransport : IWebSocketBrowserEventSink
    {
        private const ushort NormalClosure = 1000;
        private const ushort ProtocolError = 1002;
        private const ushort PolicyViolation = 1008;
        private const ushort InternalError = 1011;

        private readonly IWebSocketBrowserBridge _bridge;
        private readonly int _maximumPayloadLength;
        private IWebSocketBrowserConnection _connection;
        private byte[] _helloPayload;
        private bool _disconnectReported;

        public WebSocketClientTransport(IWebSocketBrowserBridge bridge, int maximumPayloadLength)
        {
            _bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
            if (maximumPayloadLength <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumPayloadLength));
            }
            _maximumPayloadLength = maximumPayloadLength;
        }

        public WebSocketClientState State { get; private set; }

        public event Action Connected;
        public event Action<WebSocketClientData> DataReceived;
        public event Action<byte[]> Rejected;
        public event Action<WebSocketBrowserClose> Disconnected;
        public event Action<string> Error;

        public void Connect(string absoluteUri, byte[] helloPayload)
        {
            if (State != WebSocketClientState.Idle)
            {
                throw new InvalidOperationException($"Cannot connect in state '{State}'.");
            }
            if (helloPayload == null)
            {
                throw new ArgumentNullException(nameof(helloPayload));
            }

            ConnectionTarget target = new ConnectionTarget("websocket", absoluteUri);
            new WebSocketConnectionTargetParser().Parse(target);
            if (helloPayload.Length > _maximumPayloadLength)
            {
                throw new ArgumentException("Hello payload exceeds the configured maximum length.", nameof(helloPayload));
            }

            _helloPayload = (byte[])helloPayload.Clone();
            State = WebSocketClientState.Connecting;
            try
            {
                _connection = _bridge.Open(absoluteUri, this)
                    ?? throw new InvalidOperationException("The browser bridge did not create a connection.");
            }
            catch
            {
                State = WebSocketClientState.Closed;
                throw;
            }
        }

        public void Send(byte[] payload, byte channel, DeliveryMethod deliveryMethod)
        {
            if (State != WebSocketClientState.Connected)
            {
                throw new InvalidOperationException("Data can only be sent while connected.");
            }
            if (payload == null)
            {
                throw new ArgumentNullException(nameof(payload));
            }

            byte[] encoded = WebSocketFrameCodec.Encode(
                WebSocketFrameKind.Data,
                channel,
                deliveryMethod,
                payload,
                _maximumPayloadLength);
            SendEncoded(encoded);
        }

        public void Disconnect(byte[] payload)
        {
            if (State != WebSocketClientState.Connected)
            {
                throw new InvalidOperationException("Only a connected transport can disconnect.");
            }
            if (payload == null)
            {
                throw new ArgumentNullException(nameof(payload));
            }

            byte[] encoded = WebSocketFrameCodec.Encode(
                WebSocketFrameKind.Disconnect,
                0,
                DeliveryMethod.ReliableOrdered,
                payload,
                _maximumPayloadLength);
            SendEncoded(encoded);
            State = WebSocketClientState.Closed;
            _connection.Close(NormalClosure, "Client disconnected");
        }

        void IWebSocketBrowserEventSink.OnBrowserOpen()
        {
            if (State != WebSocketClientState.Connecting)
            {
                return;
            }

            byte[] hello = WebSocketFrameCodec.Encode(
                WebSocketFrameKind.Hello,
                0,
                DeliveryMethod.ReliableOrdered,
                _helloPayload,
                _maximumPayloadLength);
            if (!TrySendEncoded(hello))
            {
                Fail("Browser WebSocket could not send the hello frame.", InternalError);
                return;
            }

            State = WebSocketClientState.Connected;
            Connected?.Invoke();
        }

        void IWebSocketBrowserEventSink.OnBrowserMessage(byte[] payload)
        {
            if (State != WebSocketClientState.Connected || payload == null)
            {
                return;
            }
            if (!WebSocketFrameCodec.TryDecode(
                    payload,
                    _maximumPayloadLength,
                    out WebSocketFrame frame,
                    out WebSocketFrameDecodeError decodeError))
            {
                Fail($"Invalid WebSocket protocol frame: {decodeError}.", ProtocolError);
                return;
            }

            if (frame.Kind == WebSocketFrameKind.Data)
            {
                DataReceived?.Invoke(new WebSocketClientData(
                    frame.Channel,
                    frame.DeliveryMethod,
                    frame.Payload));
                return;
            }
            if (frame.Kind == WebSocketFrameKind.Reject)
            {
                State = WebSocketClientState.Closed;
                Rejected?.Invoke(frame.Payload);
                _connection.Close(PolicyViolation, "Connection rejected");
                return;
            }
            if (frame.Kind == WebSocketFrameKind.Disconnect)
            {
                State = WebSocketClientState.Closed;
                _connection.Close(NormalClosure, "Server disconnected");
                ReportDisconnected(NormalClosure, "Server disconnected");
                return;
            }

            Fail($"Frame kind '{frame.Kind}' is invalid after connection.", ProtocolError);
        }

        void IWebSocketBrowserEventSink.OnBrowserError(string message)
        {
            if (State == WebSocketClientState.Closed)
            {
                return;
            }
            Fail(string.IsNullOrEmpty(message) ? "Browser WebSocket error." : message, InternalError);
        }

        void IWebSocketBrowserEventSink.OnBrowserClose(ushort code, string reason)
        {
            State = WebSocketClientState.Closed;
            ReportDisconnected(code, reason);
        }

        private void SendEncoded(byte[] encoded)
        {
            if (!TrySendEncoded(encoded))
            {
                Fail("Browser WebSocket could not queue the frame.", InternalError);
                throw new InvalidOperationException("Browser WebSocket could not queue the frame.");
            }
        }

        private bool TrySendEncoded(byte[] encoded)
        {
            return _connection != null && _connection.Send(encoded);
        }

        private void Fail(string message, ushort closeCode)
        {
            State = WebSocketClientState.Closed;
            Error?.Invoke(message);
            _connection?.Close(closeCode, message);
        }

        private void ReportDisconnected(ushort code, string reason)
        {
            if (_disconnectReported)
            {
                return;
            }
            _disconnectReported = true;
            Disconnected?.Invoke(new WebSocketBrowserClose(code, reason));
        }
    }
}
