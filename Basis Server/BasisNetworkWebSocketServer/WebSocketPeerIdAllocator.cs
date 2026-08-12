namespace Basis.Network.WebSocketServer;

internal sealed class WebSocketPeerIdAllocator
{
    private int _lastPeerId = -1;

    public int Allocate()
    {
        int peerId = Interlocked.Increment(ref _lastPeerId);
        return peerId >= 0
            ? peerId
            : throw new InvalidOperationException("WebSocket peer ID space is exhausted.");
    }
}
