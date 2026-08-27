using Basis.Network.Core;
using Xunit;

namespace BasisServerTests;

[Collection("BasisServer shared network statics")]
public sealed class MixedTransportPopulationTests
{
    [Fact]
    public void ConnectedPeerCountIncludesAdditionalTransportPeers()
    {
        NetManager previousServer = NetworkServer.Server;
        Func<int> previousAdditionalCountProvider = NetworkServer.AdditionalConnectedPeersCountProvider;
        try
        {
            NetworkServer.Server = new FakeNetManager { ConnectedPeersCount = 3 };
            NetworkServer.AdditionalConnectedPeersCountProvider = () => 2;

            Assert.Equal(5, NetworkServer.ConnectedPeerCount);
        }
        finally
        {
            NetworkServer.Server = previousServer;
            NetworkServer.AdditionalConnectedPeersCountProvider = previousAdditionalCountProvider;
        }
    }
}
