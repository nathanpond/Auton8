using AutoNate.Web.Authorization;

namespace AutoNate.Web.Services.BusWatcher.Subscriptions;

// One Dapr message → zero-or-more deliveries. Each delivery names a channel
// plus the per-recipient gate to apply when fanning out:
//
//   - FastGate: synchronous predicate evaluated against the recipient's
//     ActorAuthSnapshot. Used for cheap user-scoped checks (e.g.
//     notification:user:{me} — compare payload.userId to snapshot.UserId).
//
//   - GateTarget: the entity the recipient must be authorized to view. Routed
//     through IAuthorizer (with the per-connection auth cache, once added).
//     Used for entity-instance channels (record:{id}, workflow-execution:{id},
//     page:{id}, etc.). Phase 2 first cut has no GateTarget-bearing
//     resolvers; they arrive with the record/workflow/page resolvers.
//
//   - Neither: the channel itself is the gate (the firehose channels — only
//     subscribed by superadmins, validated at subscribe time).
public readonly record struct ResolvedDelivery(
    string ChannelName,
    EntityRef? GateTarget,
    Func<ActorAuthSnapshot, bool>? FastGate);

public interface IChannelResolver
{
    // The Dapr topic this resolver consumes. Use AnyTopic for resolvers that
    // run for every message (the firehose fallback).
    string Topic { get; }

    IReadOnlyList<ResolvedDelivery> Resolve(BusWatcherStreamService.BusWatcherMessage message);

    // Async hook for resolvers that need I/O (e.g. ancestor closure lookups)
    // to decide their channel fan-out. The default falls through to the sync
    // path so existing resolvers don't need updating. Override only when the
    // delivery list can't be computed purely from the payload.
    Task<IReadOnlyList<ResolvedDelivery>> ResolveAsync(
        BusWatcherStreamService.BusWatcherMessage message,
        IServiceProvider services,
        CancellationToken cancellationToken) =>
        Task.FromResult(Resolve(message));
}

public static class ChannelResolverTopics
{
    public const string AnyTopic = "*";
}
