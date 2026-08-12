using Basis.Network.WebSocketServer;
using Xunit;

namespace BasisNetworkWebSocketServer.Tests;

public sealed class WebSocketPeerIdAllocatorTests
{
    [Fact]
    public void Allocate_ReturnsDistinctNonNegativeSessionIds()
    {
        WebSocketPeerIdAllocator allocator = new();

        Assert.Equal(0, allocator.Allocate());
        Assert.Equal(1, allocator.Allocate());
    }
}
