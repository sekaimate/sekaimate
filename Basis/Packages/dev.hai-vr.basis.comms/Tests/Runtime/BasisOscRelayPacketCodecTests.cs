using System.Collections.Generic;
using HVR.Basis.Comms.OSC.Lyuma;
using NUnit.Framework;

namespace HVR.Basis.Comms.Tests
{
    public class BasisOscRelayPacketCodecTests
    {
        [Test]
        public void EncodeAndDecode_PreservesOscMessage()
        {
            SimpleOSC.OSCMessage source = new SimpleOSC.OSCMessage
            {
                path = "/avatar/parameters/WebRelay",
                arguments = new object[] { 0.75f, 7, "ready" },
            };

            byte[] packet = BasisOscRelayPacketCodec.Encode(source);

            Assert.That(BasisOscRelayPacketCodec.TryDecode(packet, out List<SimpleOSC.OSCMessage> decoded), Is.True);
            Assert.That(decoded, Has.Count.EqualTo(1));
            Assert.That(decoded[0].path, Is.EqualTo(source.path));
            Assert.That(decoded[0].arguments, Is.EqualTo(source.arguments));
        }

        [TestCase(null)]
        [TestCase(new byte[0])]
        [TestCase(new byte[] { 1, 2, 3 })]
        public void TryDecode_RejectsInvalidPackets(byte[] packet)
        {
            Assert.That(BasisOscRelayPacketCodec.TryDecode(packet, out List<SimpleOSC.OSCMessage> decoded), Is.False);
            Assert.That(decoded, Is.Empty);
        }
    }
}
