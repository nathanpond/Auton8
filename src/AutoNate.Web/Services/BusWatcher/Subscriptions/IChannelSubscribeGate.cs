namespace AutoNate.Web.Services.BusWatcher.Subscriptions;

// Subscribe-time authorization gate. Distinct from the resolver path:
// resolvers say which channels a bus message reaches; gates say which
// channels an actor is allowed to subscribe to in the first place. Keyed by
// the leftmost token of the channel name (the "kind").
public interface IChannelSubscribeGate
{
    // Channel kind this gate handles (e.g. "notification", "firehose").
    string Kind { get; }

    Task<SubscribeGateResult> AuthorizeAsync(
        ChannelName channel,
        SubscriptionConnection connection,
        CancellationToken cancellationToken);
}

public sealed record SubscribeGateResult(bool Allowed, string? RejectCode, string? RejectReason)
{
    public static SubscribeGateResult Allow() => new(true, null, null);

    public static SubscribeGateResult Forbid(string code, string? reason = null) =>
        new(false, code, reason);
}

public sealed class ChannelSubscribeGateRegistry
{
    private readonly IReadOnlyDictionary<string, IChannelSubscribeGate> _byKind;

    public ChannelSubscribeGateRegistry(IEnumerable<IChannelSubscribeGate> gates)
    {
        _byKind = gates.ToDictionary(g => g.Kind, StringComparer.Ordinal);
    }

    public IChannelSubscribeGate? Find(string kind) =>
        _byKind.TryGetValue(kind, out var gate) ? gate : null;
}
