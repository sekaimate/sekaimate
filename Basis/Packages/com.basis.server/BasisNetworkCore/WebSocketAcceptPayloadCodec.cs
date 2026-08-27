using System;

namespace Basis.Network.Core
{
    public static class WebSocketAcceptPayloadCodec
    {
        public const int PayloadLength = sizeof(int);

        public static byte[] Encode(int peerId)
        {
            if (peerId < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(peerId));
            }

            return new[]
            {
                (byte)(peerId >> 24),
                (byte)(peerId >> 16),
                (byte)(peerId >> 8),
                (byte)peerId,
            };
        }

        public static bool TryDecode(ReadOnlySpan<byte> payload, out int peerId)
        {
            peerId = 0;
            if (payload.Length != PayloadLength || (payload[0] & 0x80) != 0)
            {
                return false;
            }

            peerId = (payload[0] << 24)
                | (payload[1] << 16)
                | (payload[2] << 8)
                | payload[3];
            return true;
        }
    }
}
