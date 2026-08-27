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

    [Fact]
    public void Allocate_DescendingRangeSkipsIdsUsedByAnotherTransport()
    {
        WebSocketPeerIdAllocator allocator = new(0, 9, descending: true, id => id == 9 || id == 7);

        Assert.Equal(8, allocator.Allocate());
        Assert.Equal(6, allocator.Allocate());
    }

    [Fact]
    public void Release_MakesSessionIdAvailableAgain()
    {
        WebSocketPeerIdAllocator allocator = new(0, 1, descending: true);
        int first = allocator.Allocate();
        int second = allocator.Allocate();

        Assert.Throws<InvalidOperationException>(() => { allocator.Allocate(); });

        allocator.Release(first);

        Assert.Equal(first, allocator.Allocate());
        Assert.NotEqual(first, second);
    }
}
