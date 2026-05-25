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

    public Task<SubscribeGateResult> AuthorizeAsync(
        ChannelName channel,
        SubscriptionConnection connection,
        CancellationToken cancellationToken) =>
        ContentSubscribeGateHelper.AuthorizeAsync(
            scopeFactory, channel, connection, EntityKinds.Page, cancellationToken);
}

// `notebook:{notebookId}` — same IContentAuthorizer view check as page.
public sealed class NotebookChannelSubscribeGate(IServiceScopeFactory scopeFactory) : IChannelSubscribeGate
{
    public string Kind => ContentChannelNames.NotebookInstanceKind;

    public Task<SubscribeGateResult> AuthorizeAsync(
        ChannelName channel,
        SubscriptionConnection connection,
        CancellationToken cancellationToken) =>
        ContentSubscribeGateHelper.AuthorizeAsync(
            scopeFactory, channel, connection, EntityKinds.Notebook, cancellationToken);
}

// `cabinet:{cabinetId}` — same IContentAuthorizer view check as page.
public sealed class CabinetChannelSubscribeGate(IServiceScopeFactory scopeFactory) : IChannelSubscribeGate
{
    public string Kind => ContentChannelNames.CabinetInstanceKind;

    public Task<SubscribeGateResult> AuthorizeAsync(
        ChannelName channel,
        SubscriptionConnection connection,
        CancellationToken cancellationToken) =>
        ContentSubscribeGateHelper.AuthorizeAsync(
            scopeFactory, channel, connection, EntityKinds.Cabinet, cancellationToken);
}

// `project:{projectId}` — same IContentAuthorizer view check as page.
public sealed class ProjectChannelSubscribeGate(IServiceScopeFactory scopeFactory) : IChannelSubscribeGate
{
    public string Kind => ContentChannelNames.ProjectInstanceKind;

    public Task<SubscribeGateResult> AuthorizeAsync(
        ChannelName channel,
        SubscriptionConnection connection,
        CancellationToken cancellationToken) =>
        ContentSubscribeGateHelper.AuthorizeAsync(
            scopeFactory, channel, connection, EntityKinds.Project, cancellationToken);
}

internal static class ContentSubscribeGateHelper
{
    public static async Task<SubscribeGateResult> AuthorizeAsync(
        IServiceScopeFactory scopeFactory,
        ChannelName channel,
        SubscriptionConnection connection,
        string entityKind,
        CancellationToken cancellationToken)
    {
        if (channel.Parts.Count != 1 || !Guid.TryParse(channel.Parts[0], out var id))
        {
            return SubscribeGateResult.Forbid(
                SubscriptionRejectCode.UnknownChannel,
                $"expected {entityKind}:{{id}}");
        }

        if (connection.Snapshot.IsSuperAdmin) return SubscribeGateResult.Allow();

        var cacheKey = new SubscriptionAuthGate.CacheKey(entityKind, id.ToString(), Actions.View);

        await using var scope = scopeFactory.CreateAsyncScope();
        var contentAuthorizer = scope.ServiceProvider.GetRequiredService<IContentAuthorizer>();
        var decision = await contentAuthorizer.AuthorizeAsync(
            connection.Principal, entityKind, id, Actions.View, cancellationToken);
        connection.AuthGate.Set(cacheKey, decision.IsAllowed);
        return decision.IsAllowed
            ? SubscribeGateResult.Allow()
            : SubscribeGateResult.Forbid(SubscriptionRejectCode.Forbidden, $"no view grant on {entityKind}");
    }
}
