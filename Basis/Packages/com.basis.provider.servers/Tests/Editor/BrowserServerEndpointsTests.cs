using NUnit.Framework;

namespace Basis.BasisUI.Tests
{
    public sealed class BrowserServerEndpointsTests
    {
        [TestCase("global.kanaru.me", "wss://global.kanaru.me/basis", "https://global.kanaru.me/server-info")]
        [TestCase("  global.kanaru.me  ", "wss://global.kanaru.me/basis", "https://global.kanaru.me/server-info")]
        [TestCase("2001:db8::1", "wss://[2001:db8::1]/basis", "https://[2001:db8::1]/server-info")]
        public void BuildsFixedBrowserEndpoints(string address, string expectedWebSocketUri, string expectedServerInfoUri)
        {
            Assert.That(BrowserServerEndpoints.WebSocketUri(address), Is.EqualTo(expectedWebSocketUri));
            Assert.That(BrowserServerEndpoints.ServerInfoUri(address), Is.EqualTo(expectedServerInfoUri));
        }
    }
}
