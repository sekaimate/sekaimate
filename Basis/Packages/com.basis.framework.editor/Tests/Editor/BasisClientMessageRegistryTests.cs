using System;
using System.Collections.Generic;
using System.Net;
using Basis.Network.Core;
using Basis.Scripts.Networking;
using NUnit.Framework;

public class BasisClientMessageRegistryTests
{
    private const string PluginName = "test.late.registration";

    [TearDown]
    public void TearDown()
    {
        BasisNetworkConnection.LocalPlayerPeer = null;
        BasisClientMessageRegistry.UnregisterClientPlugin(PluginName);
    }

    [Test]
    public void RegisterClientPlugin_AfterSupply_ResendsSubscription()
    {
        RecordingPeer peer = new RecordingPeer();
        BasisNetworkConnection.LocalPlayerPeer = peer;
        SerializableBasis.BasisMessageDescriptor descriptor = new SerializableBasis.BasisMessageDescriptor
        {
            Id = 500,
            Version = 1,
            Channel = BasisNetworkCommons.GetPluginChannelForDelivery(DeliveryMethod.ReliableSequenced),
            Flags = (byte)SerializableBasis.BasisMessageFlags.Multiplexed,
            Name = PluginName,
        };

        BasisClientMessageRegistry.ApplySupply(new SerializableBasis.BasisMessageSupply
        {
            Descriptors = new[] { descriptor },
        }, peer);

        BasisClientMessageRegistry.RegisterClientPlugin(PluginName, (_, reader, _, _) => reader.Recycle());

        Assert.That(peer.SentPayloads, Has.Count.EqualTo(2));
        NetDataReader subscriptionReader = new NetDataReader(peer.SentPayloads[1]);
        Assert.That(subscriptionReader.GetByte(), Is.EqualTo(BasisNetworkCommons.RegistrySub_Subscribe));
        SerializableBasis.BasisMessageSubscribe subscription = new SerializableBasis.BasisMessageSubscribe();
        subscription.Deserialize(subscriptionReader);
        Assert.That(subscription.Ids, Is.EqualTo(new ushort[] { descriptor.Id }));
    }

    private sealed class RecordingPeer : NetPeer
    {
        public List<byte[]> SentPayloads { get; } = new List<byte[]>();
        public int Id => 1;
        public IPAddress Address => IPAddress.Loopback;
        public int RemoteId => 1;
        public int RoundTripTime => 0;
        public float TimeSinceLastPacket => 0;
        public long RemoteTimeDelta => 0;
        public int Mtu => ushort.MaxValue;
        public object Tag { get; set; }

        public void Disconnect() { }
        public void Disconnect(byte[] payload) { }
        public void DisconnectForce() { }
        public void Send(byte[] data, byte channelNumber, DeliveryMethod deliveryMethod) => SentPayloads.Add((byte[])data.Clone());
        public void Send(NetDataWriter data, byte channelNumber, DeliveryMethod deliveryMethod) => SentPayloads.Add(data.CopyData());
        public void SendUnreliableRawMerge(byte[] data, int offset, int length, byte channelNumber, int patchOffset = -1, byte patchValue = 0) => throw new NotSupportedException();
        public int GetPacketsCountInQueue(byte channel, DeliveryMethod deliveryMethod) => 0;
    }
}
