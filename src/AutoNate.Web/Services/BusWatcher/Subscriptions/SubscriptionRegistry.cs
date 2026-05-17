using System.Collections.Concurrent;

namespace AutoNate.Web.Services.BusWatcher.Subscriptions;

// Singleton. Indexes channel → connections (for fan-out) and
// connection → channels (for disconnect cleanup). Concurrent operations are
// safe; the small ABA window during empty-bucket removal is benign — a
// concurrent Subscribe just rebuilds the bucket.
public sealed class SubscriptionRegistry
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<Guid, SubscriptionConnection>> _byChannel = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<string, byte>> _byConnection = new();

    public void Subscribe(string channel, SubscriptionConnection connection)
    {
        var connections = _byChannel.GetOrAdd(channel, _ => new ConcurrentDictionary<Guid, SubscriptionConnection>());
        connections[connection.ConnectionId] = connection;

        var channels = _byConnection.GetOrAdd(connection.ConnectionId, _ => new ConcurrentDictionary<string, byte>(StringComparer.Ordinal));
        channels[channel] = 0;
    }

    public void Unsubscribe(string channel, Guid connectionId)
    {
        if (_byChannel.TryGetValue(channel, out var connections))
        {
            connections.TryRemove(connectionId, out _);
            if (connections.IsEmpty)
            {
                _byChannel.TryRemove(channel, out _);
            }
        }
        if (_byConnection.TryGetValue(connectionId, out var channels))
        {
            channels.TryRemove(channel, out _);
        }
    }

    public void RemoveConnection(Guid connectionId)
    {
        if (!_byConnection.TryRemove(connectionId, out var channels))
        {
            return;
        }
        foreach (var channel in channels.Keys)
        {
            if (!_byChannel.TryGetValue(channel, out var connections))
            {
                continue;
            }
            connections.TryRemove(connectionId, out _);
            if (connections.IsEmpty)
            {
                _byChannel.TryRemove(channel, out _);
            }
        }
    }

    // Snapshots the current subscriber set so the manager's fan-out doesn't
    // see a concurrent unsubscribe mid-iteration.
    public IReadOnlyList<SubscriptionConnection> SnapshotSubscribers(string channel)
    {
        if (!_byChannel.TryGetValue(channel, out var connections) || connections.IsEmpty)
        {
            return Array.Empty<SubscriptionConnection>();
        }
        return connections.Values.ToArray();
    }

    public int ChannelCount => _byChannel.Count;
    public int ConnectionCount => _byConnection.Count;

    // Snapshot of the channels a connection is currently subscribed to. Used
    // by the AuthChangeListener to address `invalidate` frames at exactly the
    // affected channels.
    public IReadOnlyList<string> GetChannelsForConnection(Guid connectionId) =>
        _byConnection.TryGetValue(connectionId, out var channels)
            ? channels.Keys.ToArray()
            : Array.Empty<string>();
}
