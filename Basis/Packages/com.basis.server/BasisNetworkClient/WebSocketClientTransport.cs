using System;
using System.Text;
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

    public static class WebSocketBrowserClosePayloadCodec
    {
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        public static byte[] Encode(WebSocketBrowserClose close)
        {
            byte[] reason = StrictUtf8.GetBytes(close.Reason);
            byte[] payload = new byte[2 + reason.Length];
            payload[0] = (byte)(close.Code >> 8);
            payload[1] = (byte)close.Code;
            Buffer.BlockCopy(reason, 0, payload, 2, reason.Length);
            return payload;
        }

        public static bool TryDecode(byte[] payload, out WebSocketBrowserClose close)
        {
            close = default;
            if (payload == null || payload.Length < 2)
            {
                return false;
            }
            try
            {
                ushort code = (ushort)((payload[0] << 8) | payload[1]);
                string reason = StrictUtf8.GetString(payload, 2, payload.Length - 2);
                close = new WebSocketBrowserClose(code, reason);
                return true;
            }
            catch (DecoderFallbackException)
            {
                return false;
            }
        }
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
        WebSocketBrowserSendResult Send(byte[] payload);
        void Close(ushort code, string reason);
    }

    public enum WebSocketBrowserSendResult
    {
        Queued = 1,
        NotOpen = 0,
        Backpressure = 2,
    }

    public interface IWebSocketBrowserBridge
    {
        IWebSocketBrowserConnection Open(
            string absoluteUri,
            int maximumBufferedAmount,
            IWebSocketBrowserEventSink sink);
    }

    public sealed class WebSocketClientTransport : IWebSocketBrowserEventSink
    {
        private const ushort NormalClosure = 1000;
        private const ushort ProtocolError = 1002;
        private const ushort PolicyViolation = 1008;
        private const ushort InternalError = 1011;

        private readonly IWebSocketBrowserBridge _bridge;
        private readonly int _maximumPayloadLength;
        private readonly int _maximumBufferedAmount;
        private IWebSocketBrowserConnection _connection;
        private byte[] _helloPayload;
        private bool _disconnectReported;

        public WebSocketClientTransport(
            IWebSocketBrowserBridge bridge,
            int maximumPayloadLength,
            int maximumBufferedAmount)
        {
            _bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
            if (maximumPayloadLength <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumPayloadLength));
            }
            if (maximumBufferedAmount < maximumPayloadLength)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumBufferedAmount));
            }
            _maximumPayloadLength = maximumPayloadLength;
            _maximumBufferedAmount = maximumBufferedAmount;
        }

        public WebSocketClientState State { get; private set; }

        public event Action<int> Connected;
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
                _connection = _bridge.Open(absoluteUri, _maximumBufferedAmount, this)
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
            WebSocketBrowserSendResult result = TrySendEncoded(hello);
            if (result != WebSocketBrowserSendResult.Queued)
            {
                Fail(SendFailureMessage(result, "hello frame"), InternalError);
                return;
            }
        }

        void IWebSocketBrowserEventSink.OnBrowserMessage(byte[] payload)
        {
            if ((State != WebSocketClientState.Connecting && State != WebSocketClientState.Connected)
                || payload == null)
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

            if (State == WebSocketClientState.Connecting)
            {
                ProcessHandshakeFrame(frame);
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
            if (frame.Kind == WebSocketFrameKind.Disconnect)
            {
                State = WebSocketClientState.Closed;
                _connection.Close(NormalClosure, "Server disconnected");
                ReportDisconnected(NormalClosure, "Server disconnected");
                return;
            }

            Fail($"Frame kind '{frame.Kind}' is invalid after connection.", ProtocolError);
        }

        private void ProcessHandshakeFrame(WebSocketFrame frame)
        {
            if (frame.Kind == WebSocketFrameKind.Accept)
            {
                if (!WebSocketAcceptPayloadCodec.TryDecode(frame.Payload, out int remotePeerId))
                {
                    Fail("Accept frame does not contain a valid server peer ID.", ProtocolError);
                    return;
                }
                State = WebSocketClientState.Connected;
                Connected?.Invoke(remotePeerId);
                return;
            }
            if (frame.Kind == WebSocketFrameKind.Reject)
            {
                State = WebSocketClientState.Closed;
                Rejected?.Invoke(frame.Payload);
                _connection.Close(PolicyViolation, "Connection rejected");
                return;
            }
            Fail($"Frame kind '{frame.Kind}' is invalid before acceptance.", ProtocolError);
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
            WebSocketBrowserSendResult result = TrySendEncoded(encoded);
            if (result != WebSocketBrowserSendResult.Queued)
            {
                string message = SendFailureMessage(result, "frame");
                Fail(message, InternalError);
                throw new InvalidOperationException(message);
            }
        }

        private WebSocketBrowserSendResult TrySendEncoded(byte[] encoded)
        {
            return _connection?.Send(encoded) ?? WebSocketBrowserSendResult.NotOpen;
        }

        private static string SendFailureMessage(WebSocketBrowserSendResult result, string frameDescription)
        {
            return result == WebSocketBrowserSendResult.Backpressure
                ? $"Browser WebSocket backpressure limit rejected the {frameDescription}."
                : $"Browser WebSocket is not open for the {frameDescription}.";
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
