using System;
using System.Collections.Generic;
using Basis.Network.Core;
using HVR.Basis.Comms.OSC.Lyuma;

namespace HVR.Basis.Comms
{
    internal static class BasisOscRelayClient
    {
        internal const string MessageName = "basis.osc.v1";
        private static Action<List<SimpleOSC.OSCMessage>> _messageReceived;

        internal static void Start(Action<List<SimpleOSC.OSCMessage>> messageReceived)
        {
            _messageReceived = messageReceived ?? throw new ArgumentNullException(nameof(messageReceived));
            BasisClientMessageRegistry.RegisterClientPlugin(MessageName, Receive);
        }

        internal static void Stop()
        {
            BasisClientMessageRegistry.UnregisterClientPlugin(MessageName);
            _messageReceived = null;
        }

        internal static bool Send(SimpleOSC.OSCMessage message)
        {
            byte[] packet = BasisOscRelayPacketCodec.Encode(message);
            return BasisClientMessageRegistry.Send(MessageName, writer => writer.Put(packet));
        }

        private static void Receive(NetPeer peer, NetPacketReader reader, byte channel, DeliveryMethod deliveryMethod)
        {
            byte[] packet = reader.GetRemainingBytes();
            reader.Recycle();
            if (BasisOscRelayPacketCodec.TryDecode(packet, out List<SimpleOSC.OSCMessage> messages))
            {
                _messageReceived?.Invoke(messages);
            }
        }
    }
}
