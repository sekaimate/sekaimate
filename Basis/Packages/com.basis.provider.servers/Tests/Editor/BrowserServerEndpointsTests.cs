using NUnit.Framework;

namespace Basis.BasisUI.Tests
{
    public sealed class BrowserServerEndpointsTests
    {
        [TestCase("global.kanaru.me", "wss://global.kanaru.me/basis", "https://global.kanaru.me/server-info")]
        [TestCase("  global.kanaru.me  ", "wss://global.kanaru.me/basis", "https://global.kanaru.me/server-info")]
        [TestCase("2001:db8::1", "wss://[2001:db8::1]/basis", "https://[2001:db8::1]/server-info")]
        [TestCase("localhost", "ws://localhost:4297/basis", "http://localhost:4297/server-info")]
        [TestCase("127.0.0.1", "ws://127.0.0.1:4297/basis", "http://127.0.0.1:4297/server-info")]
        public void BuildsFixedBrowserEndpoints(string address, string expectedWebSocketUri, string expectedServerInfoUri)
        {
            Assert.That(BrowserServerEndpoints.WebSocketUri(address), Is.EqualTo(expectedWebSocketUri));
            Assert.That(BrowserServerEndpoints.ServerInfoUri(address), Is.EqualTo(expectedServerInfoUri));
        }
    }
}
