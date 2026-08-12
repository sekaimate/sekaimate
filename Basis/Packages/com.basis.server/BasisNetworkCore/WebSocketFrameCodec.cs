using System;

namespace Basis.Network.Core
{
    public enum WebSocketFrameKind : byte
    {
        Hello = 1,
        Accept = 2,
        Data = 3,
        Reject = 4,
        Disconnect = 5,
    }

    public enum WebSocketFrameDecodeError
    {
        None,
        HeaderTooShort,
        UnknownFrameKind,
        InvalidChannel,
        UnknownDeliveryMethod,
        PayloadTooLarge,
    }

    public readonly struct WebSocketFrame
    {
        public WebSocketFrame(
            WebSocketFrameKind kind,
            byte channel,
            DeliveryMethod deliveryMethod,
            byte[] payload)
        {
            Kind = kind;
            Channel = channel;
            DeliveryMethod = deliveryMethod;
            Payload = payload ?? throw new ArgumentNullException(nameof(payload));
        }

        public WebSocketFrameKind Kind { get; }
        public byte Channel { get; }
        public DeliveryMethod DeliveryMethod { get; }
        public byte[] Payload { get; }
    }

    public static class WebSocketFrameCodec
    {
        public const int HeaderLength = 3;

        public static byte[] Encode(
            WebSocketFrameKind kind,
            byte channel,
            DeliveryMethod deliveryMethod,
            ReadOnlySpan<byte> payload,
            int maxPayloadLength)
        {
            ValidateMaxPayloadLength(maxPayloadLength);
            if (!IsKnownFrameKind(kind))
            {
                throw new ArgumentOutOfRangeException(nameof(kind));
            }
            if (channel >= BasisNetworkCommons.TotalChannels)
            {
                throw new ArgumentOutOfRangeException(nameof(channel));
            }
            if (!IsKnownDeliveryMethod(deliveryMethod))
            {
                throw new ArgumentOutOfRangeException(nameof(deliveryMethod));
            }
            if (payload.Length > maxPayloadLength)
            {
                throw new ArgumentException("Payload exceeds the configured maximum length.", nameof(payload));
            }

            byte[] encoded = new byte[HeaderLength + payload.Length];
            encoded[0] = (byte)kind;
            encoded[1] = channel;
            encoded[2] = (byte)deliveryMethod;
            payload.CopyTo(encoded.AsSpan(HeaderLength));
            return encoded;
        }

        public static bool TryDecode(
            ReadOnlySpan<byte> encoded,
            int maxPayloadLength,
            out WebSocketFrame frame,
            out WebSocketFrameDecodeError error)
        {
            ValidateMaxPayloadLength(maxPayloadLength);
            frame = default;

            if (encoded.Length < HeaderLength)
            {
                error = WebSocketFrameDecodeError.HeaderTooShort;
                return false;
            }

            WebSocketFrameKind kind = (WebSocketFrameKind)encoded[0];
            if (!IsKnownFrameKind(kind))
            {
                error = WebSocketFrameDecodeError.UnknownFrameKind;
                return false;
            }

            byte channel = encoded[1];
            if (channel >= BasisNetworkCommons.TotalChannels)
            {
                error = WebSocketFrameDecodeError.InvalidChannel;
                return false;
            }

            DeliveryMethod deliveryMethod = (DeliveryMethod)encoded[2];
            if (!IsKnownDeliveryMethod(deliveryMethod))
            {
                error = WebSocketFrameDecodeError.UnknownDeliveryMethod;
                return false;
            }

            int payloadLength = encoded.Length - HeaderLength;
            if (payloadLength > maxPayloadLength)
            {
                error = WebSocketFrameDecodeError.PayloadTooLarge;
                return false;
            }

            frame = new WebSocketFrame(
                kind,
                channel,
                deliveryMethod,
                encoded.Slice(HeaderLength).ToArray());
            error = WebSocketFrameDecodeError.None;
            return true;
        }

        private static void ValidateMaxPayloadLength(int maxPayloadLength)
        {
            if (maxPayloadLength < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxPayloadLength));
            }
        }

        private static bool IsKnownFrameKind(WebSocketFrameKind kind)
        {
            return kind == WebSocketFrameKind.Hello
                || kind == WebSocketFrameKind.Accept
                || kind == WebSocketFrameKind.Data
                || kind == WebSocketFrameKind.Reject
                || kind == WebSocketFrameKind.Disconnect;
        }

        private static bool IsKnownDeliveryMethod(DeliveryMethod deliveryMethod)
        {
            return deliveryMethod == DeliveryMethod.Unreliable
                || deliveryMethod == DeliveryMethod.ReliableUnordered
                || deliveryMethod == DeliveryMethod.Sequenced
                || deliveryMethod == DeliveryMethod.ReliableOrdered
                || deliveryMethod == DeliveryMethod.ReliableSequenced;
        }
    }
}
