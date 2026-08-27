using Basis.Scripts.Networking;
using NUnit.Framework;
using System.Linq;
using System.Threading.Tasks;

namespace Basis.Tests.Networking
{
    public sealed class SavedServersDirectorySourceTests
    {
        [Test]
        public async Task DefaultServerIncludesBrowserEndpoints()
        {
            SavedServersDirectorySource source = new SavedServersDirectorySource();
            var entries = await source.ListAsync(default);
            ServerDirectoryEntry entry = entries.Single(candidate =>
                candidate.Id == SavedServersDirectorySource.DefaultServerId);

            Assert.That(entry.WebSocketUri, Is.EqualTo("wss://server1.basisvr.org/basis"));
            Assert.That(entry.ServerInfoUri, Is.EqualTo("https://server1.basisvr.org/server-info"));
        }
    }
}
