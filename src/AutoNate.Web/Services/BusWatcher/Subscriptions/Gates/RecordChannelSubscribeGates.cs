using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.Evaluator;
using AutoNate.Web.Services.BusWatcher.Subscriptions.Resolvers;
using Microsoft.Extensions.DependencyInjection;

namespace AutoNate.Web.Services.BusWatcher.Subscriptions.Gates;

// `record:{recordId}` — actor must hold view on this specific record. The
// IAuthorizer decision is cached in the connection's AuthGate so the first
// per-message check on this target is a hit, not a miss.
public sealed class RecordInstanceChannelSubscribeGate(IServiceScopeFactory scopeFactory) : IChannelSubscribeGate
{
    public string Kind => RecordChannelNames.InstanceKind;

    public async Task<SubscribeGateResult> AuthorizeAsync(
        ChannelName channel,
        SubscriptionConnection connection,
        CancellationToken cancellationToken)
    {
        if (channel.Parts.Count != 1 || !Guid.TryParse(channel.Parts[0], out var recordId))
        {
            return SubscribeGateResult.Forbid(
                SubscriptionRejectCode.UnknownChannel,
                "expected record:{recordId}");
        }

        if (connection.Snapshot.IsSuperAdmin)
        {
            return SubscribeGateResult.Allow();
        }

        var target = new EntityRef(EntityKinds.Record, recordId.ToString());
        var cacheKey = new SubscriptionAuthGate.CacheKey(target.Kind, target.Id, Actions.View);

        await using var scope = scopeFactory.CreateAsyncScope();
        var authorizer = scope.ServiceProvider.GetRequiredService<IAuthorizer>();
        var decision = await authorizer.AuthorizeAsync(connection.Principal, Actions.View, target, cancellationToken);
        connection.AuthGate.Set(cacheKey, decision.IsAllowed);

        return decision.IsAllowed
            ? SubscribeGateResult.Allow()
            : SubscribeGateResult.Forbid(SubscriptionRejectCode.Forbidden, "no view grant on record");
    }
}

// `records:visible` and `records:assigned-to:{userId}` — list channels. No
// subscribe-time auth check (per-message GateTarget is the real filter).
// Only the assigned-to scope requires the userId to match the connecting
// actor; visible is open to any authenticated user (zero-grant actors just
// receive zero events).
public sealed class RecordsListChannelSubscribeGate : IChannelSubscribeGate
{
    public string Kind => RecordChannelNames.ListKind;

    public Task<SubscribeGateResult> AuthorizeAsync(
        ChannelName channel,
        SubscriptionConnection connection,
        CancellationToken cancellationToken)
    {
        if (channel.Parts.Count == 0)
        {
            return Task.FromResult(SubscribeGateResult.Forbid(
                SubscriptionRejectCode.UnknownChannel,
                "expected records:visible or records:assigned-to:{userId}"));
        }

        var scope = channel.Parts[0];
        if (string.Equals(scope, "visible", StringComparison.Ordinal))
        {
            if (channel.Parts.Count != 1)
            {
                return Task.FromResult(SubscribeGateResult.Forbid(
                    SubscriptionRejectCode.UnknownChannel,
                    "records:visible takes no parameters"));
            }
            return Task.FromResult(SubscribeGateResult.Allow());
        }

        if (string.Equals(scope, "assigned-to", StringComparison.Ordinal))
        {
            if (channel.Parts.Count != 2 || !Guid.TryParse(channel.Parts[1], out var requestedUserId))
            {
                return Task.FromResult(SubscribeGateResult.Forbid(
                    SubscriptionRejectCode.UnknownChannel,
                    "expected records:assigned-to:{userId}"));
            }
            if (requestedUserId != connection.Snapshot.UserId)
            {
                return Task.FromResult(SubscribeGateResult.Forbid(
                    SubscriptionRejectCode.Forbidden,
                    "userId must match the connecting actor"));
            }
            return Task.FromResult(SubscribeGateResult.Allow());
        }

        return Task.FromResult(SubscribeGateResult.Forbid(
            SubscriptionRejectCode.UnknownChannel,
            $"unknown records scope '{scope}'"));
    }
}
