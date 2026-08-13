using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using HVR.Basis.Comms.OSC.Lyuma;

namespace HVR.Basis.Comms
{
    internal static class BasisOscRelayPacketCodec
    {
        private const int MaximumPacketLength = ushort.MaxValue;

        internal static byte[] Encode(SimpleOSC.OSCMessage message)
        {
            byte[] packet = new byte[MaximumPacketLength];
            int packetLength = 0;
            SimpleOSC.EncodeOSCInto(packet, ref packetLength, message);
            if (packetLength <= 0 || packetLength > MaximumPacketLength)
            {
                throw new InvalidOperationException("OSC relay packet length is invalid.");
            }

            Array.Resize(ref packet, packetLength);
            return packet;
        }

        internal static bool TryDecode(byte[] packet, out List<SimpleOSC.OSCMessage> messages)
        {
            messages = new List<SimpleOSC.OSCMessage>();
            if (packet == null || packet.Length == 0 || packet.Length > MaximumPacketLength)
            {
                return false;
            }

            ConcurrentQueue<SimpleOSC.OSCMessage> decoded = new ConcurrentQueue<SimpleOSC.OSCMessage>();
            try
            {
                SimpleOSC.DecodeOSCInto(decoded, packet, 0, packet.Length);
            }
            catch (Exception)
            {
                return false;
            }

            while (decoded.TryDequeue(out SimpleOSC.OSCMessage message))
            {
                messages.Add(message);
            }

            return messages.Count > 0;
        }
    }
}
