namespace Basis.Network.WebSocketServer;

public sealed class WebSocketPeerIdAllocator
{
    private readonly int _minimumId;
    private readonly int _maximumId;
    private readonly bool _descending;
    private readonly Func<int, bool> _isUnavailable;
    private readonly HashSet<int> _leasedIds = new();
    private readonly object _sync = new();
    private int _nextId;

    public WebSocketPeerIdAllocator()
        : this(0, int.MaxValue, false)
    {
    }

    public WebSocketPeerIdAllocator(
        int minimumId,
        int maximumId,
        bool descending,
        Func<int, bool>? isUnavailable = null)
    {
        if (minimumId < 0) throw new ArgumentOutOfRangeException(nameof(minimumId));
        if (maximumId < minimumId) throw new ArgumentOutOfRangeException(nameof(maximumId));
        _minimumId = minimumId;
        _maximumId = maximumId;
        _descending = descending;
        _isUnavailable = isUnavailable ?? (_ => false);
        _nextId = descending ? maximumId : minimumId;
    }

    public int Allocate()
    {
        lock (_sync)
        {
            long rangeLength = (long)_maximumId - _minimumId + 1;
            for (long attempt = 0; attempt < rangeLength; attempt++)
            {
                int candidate = _nextId;
                _nextId = Next(candidate);
                if (!_leasedIds.Contains(candidate) && !_isUnavailable(candidate))
                {
                    _leasedIds.Add(candidate);
                    return candidate;
                }
            }
        }
        throw new InvalidOperationException("WebSocket peer ID space is exhausted.");
    }

    public void Release(int peerId)
    {
        lock (_sync)
        {
            _leasedIds.Remove(peerId);
        }
    }

    public bool IsLeased(int peerId)
    {
        lock (_sync)
        {
            return _leasedIds.Contains(peerId);
        }
    }

    public int LeasedCount
    {
        get
        {
            lock (_sync)
            {
                return _leasedIds.Count;
            }
        }
    }

    private int Next(int current)
    {
        if (_descending)
        {
            return current == _minimumId ? _maximumId : current - 1;
        }
        return current == _maximumId ? _minimumId : current + 1;
    }
}
