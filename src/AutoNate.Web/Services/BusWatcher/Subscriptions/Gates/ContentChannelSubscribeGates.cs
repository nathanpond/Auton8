using AutoNate.Web.Authorization;
using AutoNate.Web.Services.BusWatcher.Subscriptions.Resolvers;
using AutoNate.Web.Services.Content;
using Microsoft.Extensions.DependencyInjection;

namespace AutoNate.Web.Services.BusWatcher.Subscriptions.Gates;

// `page:{pageId}` — IContentAuthorizer view check (closest-ancestor share
// semantics), not the generic IAuthorizer. Cached in the connection's
// AuthGate so the per-message check is a hit.
public sealed class PageChannelSubscribeGate(IServiceScopeFactory scopeFactory) : IChannelSubscribeGate
{
    public string Kind => ContentChannelNames.PageInstanceKind;

    public async Task<SubscribeGateResult> AuthorizeAsync(
        ChannelName channel,
        SubscriptionConnection connection,
        CancellationToken cancellationToken)
    {
        if (channel.Parts.Count != 1 || !Guid.TryParse(channel.Parts[0], out var pageId))
        {
            return SubscribeGateResult.Forbid(
                SubscriptionRejectCode.UnknownChannel,
                "expected page:{pageId}");
        }

        if (connection.Snapshot.IsSuperAdmin) return SubscribeGateResult.Allow();

        var cacheKey = new SubscriptionAuthGate.CacheKey(EntityKinds.Page, pageId.ToString(), Actions.View);

        await using var scope = scopeFactory.CreateAsyncScope();
        var contentAuthorizer = scope.ServiceProvider.GetRequiredService<IContentAuthorizer>();
        var decision = await contentAuthorizer.AuthorizeAsync(
            connection.Principal, EntityKinds.Page, pageId, Actions.View, cancellationToken);
        connection.AuthGate.Set(cacheKey, decision.IsAllowed);
        return decision.IsAllowed
            ? SubscribeGateResult.Allow()
            : SubscribeGateResult.Forbid(SubscriptionRejectCode.Forbidden, "no view grant on page");
    }
}
