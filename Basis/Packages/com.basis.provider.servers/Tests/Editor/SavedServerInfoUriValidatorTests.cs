using Basis.Scripts.Networking;
using NUnit.Framework;

namespace Basis.BasisUI.Tests
{
    public sealed class SavedServerInfoUriValidatorTests
    {
        [Test]
        public void Validate_AllowsMissingUriForNativePlayer()
        {
            string uri = SavedServerInfoUriValidator.Validate("  ", false);

            Assert.That(uri, Is.Empty);
        }

        [Test]
        public void Validate_RejectsMissingUriForWebGlPlayer()
        {
            Assert.That(
                () => SavedServerInfoUriValidator.Validate(string.Empty, true),
                Throws.InvalidOperationException.With.Message.EqualTo(
                    "The server directory entry does not provide a server-info URI."));
        }

        [Test]
        public void Validate_RejectsInsecureNonLoopbackUri()
        {
            Assert.That(
                () => SavedServerInfoUriValidator.Validate("http://server.example/server-info", true),
                Throws.TypeOf<System.FormatException>());
        }

        [Test]
        public void Validate_TrimsAndReturnsValidUri()
        {
            string uri = SavedServerInfoUriValidator.Validate(
                "  https://server.example/server-info  ",
                true);

            Assert.That(uri, Is.EqualTo("https://server.example/server-info"));
        }

        [Test]
        public void ParseResponse_MapsServerInfoPayload()
        {
            Basis.Network.Core.ServerProbeResult result = BasisWebServerInfoClient.ParseResponse(
                "{\"online\":2,\"max\":16,\"protocolVersion\":1,\"name\":\"Basis\",\"motd\":\"Hello\"}");

            Assert.That(result.Reachable, Is.True);
            Assert.That(result.Online, Is.EqualTo(2));
            Assert.That(result.Max, Is.EqualTo(16));
            Assert.That(result.Name, Is.EqualTo("Basis"));
            Assert.That(result.Motd, Is.EqualTo("Hello"));
        }

        [Test]
        public void ParseResponse_MapsBasisServerHealthPayload()
        {
            Basis.Network.Core.ServerProbeResult result = BasisWebServerInfoClient.ParseResponse(
                "{\"listening\":true,\"ready\":true,\"visitors\":3,\"capacity\":24,\"version\":\"1.2.3\"}");

            Assert.That(result.Reachable, Is.True);
            Assert.That(result.Online, Is.EqualTo(3));
            Assert.That(result.Max, Is.EqualTo(24));
            Assert.That(result.Extras["version"], Is.EqualTo("1.2.3"));
        }
    }
}
