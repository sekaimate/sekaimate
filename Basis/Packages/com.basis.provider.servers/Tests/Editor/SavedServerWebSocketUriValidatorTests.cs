using NUnit.Framework;

namespace Basis.BasisUI.Tests
{
    public sealed class SavedServerWebSocketUriValidatorTests
    {
        [Test]
        public void Validate_AllowsMissingUriForNativePlayer()
        {
            string uri = SavedServerWebSocketUriValidator.Validate("  ", false);

            Assert.That(uri, Is.Empty);
        }

        [Test]
        public void Validate_RejectsMissingUriForWebGlPlayer()
        {
            Assert.That(
                () => SavedServerWebSocketUriValidator.Validate(string.Empty, true),
                Throws.InvalidOperationException.With.Message.EqualTo(
                    "The server directory entry does not provide a WebSocket URI."));
        }

        [Test]
        public void Validate_RejectsInvalidUri()
        {
            Assert.That(
                () => SavedServerWebSocketUriValidator.Validate("http://server.example/basis", false),
                Throws.TypeOf<System.FormatException>());
        }

        [Test]
        public void Validate_TrimsAndReturnsValidUri()
        {
            string uri = SavedServerWebSocketUriValidator.Validate(
                "  wss://server.example:8443/basis  ",
                true);

            Assert.That(uri, Is.EqualTo("wss://server.example:8443/basis"));
        }
    }
}
