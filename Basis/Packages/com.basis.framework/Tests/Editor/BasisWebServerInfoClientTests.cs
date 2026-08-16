using Basis.Scripts.Networking;
using NUnit.Framework;
using System;

namespace Basis.Tests.Networking
{
    public sealed class BasisWebServerInfoClientTests
    {
        [TestCase("ws://localhost:4297/basis", "http://localhost:4297/server-info")]
        [TestCase("wss://server.example/basis", "https://server.example/server-info")]
        public void BuildServerInfoUriUsesMatchingHttpScheme(string webSocketUri, string expectedServerInfoUri)
        {
            Uri parsedUri = new Uri(webSocketUri);

            Assert.That(BasisWebServerInfoClient.BuildServerInfoUri(parsedUri), Is.EqualTo(expectedServerInfoUri));
        }

        [Test]
        public void BuildServerInfoUriRejectsNonWebSocketUri()
        {
            Assert.Throws<ArgumentException>(() =>
                BasisWebServerInfoClient.BuildServerInfoUri(new Uri("https://server.example/basis")));
        }

        [Test]
        public void DeepLinkWebSocketUriCarriesMatchingServerInfoScheme()
        {
            bool parsed = BasisDeepLinkProvider.TryParseBasisUrl(
                "basisdemo://localhost:4296?websocketUri=ws%3A%2F%2Flocalhost%3A4297%2Fbasis",
                out ServerDirectoryEntry entry);

            Assert.That(parsed, Is.True);
            Assert.That(entry.ServerInfoUri, Is.EqualTo("http://localhost:4297/server-info"));
        }
    }
}
