namespace AutoNate.Web.Services.BusWatcher.Subscriptions;

// Per-connection LRU cache of IAuthorizer decisions, keyed by (kind, id,
// action). Entries expire after TTL; the cache caps at _maxEntries and
// evicts least-recently-used when full. TryGet promotes a hit to most-recent
// so hot subscriptions stay resident even under cap pressure.
//
// One lock per gate. Contention is bounded — each connection has its own
// gate, and the fan-out path serializes per-connection delivery.
//
// Cleared whenever the connection's ActorAuthSnapshot is rebuilt
// (AuthChangeListener path) — every prior decision was made against the old
// snapshot's grant set and can't be trusted.
public sealed class SubscriptionAuthGate
{
    public static readonly TimeSpan DefaultTtl = TimeSpan.FromSeconds(30);
    public const int DefaultMaxEntries = 2048;

    public readonly record struct CacheKey(string Kind, string Id, string Action);

    private sealed record CacheEntry(CacheKey Key, bool Allowed, long ExpiresAtTicks);

    private readonly TimeSpan _ttl;
    private readonly int _maxEntries;
    private readonly object _lock = new();
    private readonly Dictionary<CacheKey, LinkedListNode<CacheEntry>> _index = new();
    private readonly LinkedList<CacheEntry> _accessOrder = new();

    public SubscriptionAuthGate(TimeSpan? ttl = null, int maxEntries = DefaultMaxEntries)
    {
        _ttl = ttl ?? DefaultTtl;
        _maxEntries = maxEntries;
    }

    public bool TryGet(CacheKey key, out bool allowed)
    {
        lock (_lock)
        {
            if (!_index.TryGetValue(key, out var node))
            {
                allowed = false;
                return false;
            }
            if (DateTime.UtcNow.Ticks > node.Value.ExpiresAtTicks)
            {
                _accessOrder.Remove(node);
                _index.Remove(key);
                allowed = false;
                return false;
            }
            // Promote to most-recently-used.
            _accessOrder.Remove(node);
            _accessOrder.AddFirst(node);
            allowed = node.Value.Allowed;
            return true;
        }
    }

    public void Set(CacheKey key, bool allowed)
    {
        var entry = new CacheEntry(key, allowed, DateTime.UtcNow.Add(_ttl).Ticks);
        lock (_lock)
        {
            if (_index.TryGetValue(key, out var existing))
            {
                _accessOrder.Remove(existing);
                _index.Remove(key);
            }
            while (_index.Count >= _maxEntries && _accessOrder.Last is { } lru)
            {
                _accessOrder.RemoveLast();
                _index.Remove(lru.Value.Key);
            }
            var node = new LinkedListNode<CacheEntry>(entry);
            _accessOrder.AddFirst(node);
            _index[key] = node;
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _index.Clear();
            _accessOrder.Clear();
        }
    }

    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _index.Count;
            }
        }
    }
}
