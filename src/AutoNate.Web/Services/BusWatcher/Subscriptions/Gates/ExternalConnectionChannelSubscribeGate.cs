using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.Evaluator;
using AutoNate.Web.Services.BusWatcher.Subscriptions.Resolvers;
using Microsoft.Extensions.DependencyInjection;

namespace AutoNate.Web.Services.BusWatcher.Subscriptions.Gates;

public sealed class ExternalConnectionChannelSubscribeGate(IServiceScopeFactory scopeFactory) : IChannelSubscribeGate
{
    public string Kind => ExternalConnectionChannelNames.InstanceKind;

    public async Task<SubscribeGateResult> AuthorizeAsync(
        ChannelName channel,
        SubscriptionConnection connection,
        CancellationToken cancellationToken)
    {
        if (channel.Parts.Count != 1 || !Guid.TryParse(channel.Parts[0], out var connectionId))
        {
            return SubscribeGateResult.Forbid(
                SubscriptionRejectCode.UnknownChannel,
                "expected external-connection:{id}");
        }

        if (connection.Snapshot.IsSuperAdmin) return SubscribeGateResult.Allow();

        var target = new EntityRef(EntityKinds.ExternalConnection, connectionId.ToString());
        var cacheKey = new SubscriptionAuthGate.CacheKey(target.Kind, target.Id, Actions.View);

        await using var scope = scopeFactory.CreateAsyncScope();
        var authorizer = scope.ServiceProvider.GetRequiredService<IAuthorizer>();
        var decision = await authorizer.AuthorizeAsync(connection.Principal, Actions.View, target, cancellationToken);
        connection.AuthGate.Set(cacheKey, decision.IsAllowed);
        return decision.IsAllowed
            ? SubscribeGateResult.Allow()
            : SubscribeGateResult.Forbid(SubscriptionRejectCode.Forbidden, "no view grant on external connection");
    }
}
