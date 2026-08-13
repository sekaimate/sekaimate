using Basis.Network.Core;

internal static class BasisServerOscRelay
{
    internal const string MessageName = "basis.osc.v1";
    private const int MaximumPacketLength = ushort.MaxValue;

    internal static void Register()
    {
        BasisServerMessageRegistry.RegisterServerPlugin(
            MessageName,
            DeliveryMethod.ReliableSequenced,
            Relay);
    }

    private static void Relay(NetPeer sender, NetPacketReader reader, byte channel, DeliveryMethod deliveryMethod)
    {
        int packetLength = reader.AvailableBytes;
        if (packetLength == 0 || packetLength > MaximumPacketLength)
        {
            reader.Recycle();
            return;
        }

        byte[] packet = reader.GetRemainingBytes();
        reader.Recycle();

        NetPeer[] peers = NetworkServer.PeerSnapshot;
        int peerCount = peers.Length;
        for (int i = 0; i < peerCount; i++)
        {
            NetPeer peer = peers[i];
            if (peer.Id != sender.Id)
            {
                BasisServerMessageRegistry.SendToPeer(peer, MessageName, writer => writer.Put(packet));
            }
        }
    }
}
