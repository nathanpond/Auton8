using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.Edges;
using AutoNate.Web.Authorization.Evaluator;
using AutoNate.Web.Services.BusWatcher.Subscriptions.Resolvers;
using Microsoft.Extensions.DependencyInjection;

namespace AutoNate.Web.Services.BusWatcher.Subscriptions.Gates;

// `workflow-execution:{processInstanceId}` — IAuthorizer view check (routes
// to FlowableInstanceAuthorizers for WorkflowExecution). Cached in the
// connection's AuthGate so the per-message check is a hit.
public sealed class WorkflowExecutionInstanceChannelSubscribeGate(IServiceScopeFactory scopeFactory)
    : IChannelSubscribeGate
{
    public string Kind => WorkflowChannelNames.ExecutionInstanceKind;

    public async Task<SubscribeGateResult> AuthorizeAsync(
        ChannelName channel,
        SubscriptionConnection connection,
        CancellationToken cancellationToken)
    {
        if (channel.Parts.Count != 1 || string.IsNullOrWhiteSpace(channel.Parts[0]))
        {
            return SubscribeGateResult.Forbid(
                SubscriptionRejectCode.UnknownChannel,
                "expected workflow-execution:{processInstanceId}");
        }

        if (connection.Snapshot.IsSuperAdmin) return SubscribeGateResult.Allow();

        var target = new EntityRef(EntityKinds.WorkflowExecution, channel.Parts[0]);
        var cacheKey = new SubscriptionAuthGate.CacheKey(target.Kind, target.Id, Actions.View);

        await using var scope = scopeFactory.CreateAsyncScope();
        var authorizer = scope.ServiceProvider.GetRequiredService<IAuthorizer>();
        var decision = await authorizer.AuthorizeAsync(connection.Principal, Actions.View, target, cancellationToken);
        connection.AuthGate.Set(cacheKey, decision.IsAllowed);
        return decision.IsAllowed
            ? SubscribeGateResult.Allow()
            : SubscribeGateResult.Forbid(SubscriptionRejectCode.Forbidden, "no view grant on workflow execution");
    }
}

// `workflow-task:{taskId}` — IAuthorizer view check on WorkflowTask.
public sealed class WorkflowTaskInstanceChannelSubscribeGate(IServiceScopeFactory scopeFactory)
    : IChannelSubscribeGate
{
    public string Kind => WorkflowChannelNames.TaskInstanceKind;

    public async Task<SubscribeGateResult> AuthorizeAsync(
        ChannelName channel,
        SubscriptionConnection connection,
        CancellationToken cancellationToken)
    {
        if (channel.Parts.Count != 1 || string.IsNullOrWhiteSpace(channel.Parts[0]))
        {
            return SubscribeGateResult.Forbid(
                SubscriptionRejectCode.UnknownChannel,
                "expected workflow-task:{taskId}");
        }

        if (connection.Snapshot.IsSuperAdmin) return SubscribeGateResult.Allow();

        var target = new EntityRef(EntityKinds.WorkflowTask, channel.Parts[0]);
        var cacheKey = new SubscriptionAuthGate.CacheKey(target.Kind, target.Id, Actions.View);

        await using var scope = scopeFactory.CreateAsyncScope();
        var authorizer = scope.ServiceProvider.GetRequiredService<IAuthorizer>();
        var decision = await authorizer.AuthorizeAsync(connection.Principal, Actions.View, target, cancellationToken);
        connection.AuthGate.Set(cacheKey, decision.IsAllowed);
        return decision.IsAllowed
            ? SubscribeGateResult.Allow()
            : SubscribeGateResult.Forbid(SubscriptionRejectCode.Forbidden, "no view grant on workflow task");
    }
}

// `workflow-executions:visible` — list channel; open to any authenticated
// actor (per-message GateTarget filters).
public sealed class WorkflowExecutionsListChannelSubscribeGate : IChannelSubscribeGate
{
    public string Kind => WorkflowChannelNames.ExecutionsListKind;

    public Task<SubscribeGateResult> AuthorizeAsync(
        ChannelName channel,
        SubscriptionConnection connection,
        CancellationToken cancellationToken)
    {
        if (channel.Parts.Count != 1 || !string.Equals(channel.Parts[0], "visible", StringComparison.Ordinal))
        {
            return Task.FromResult(SubscribeGateResult.Forbid(
                SubscriptionRejectCode.UnknownChannel,
                "expected workflow-executions:visible"));
        }
        return Task.FromResult(SubscribeGateResult.Allow());
    }
}

// `workflow-tasks:assigned-to:{userId}` — list channel for task events
// assigned to a specific user (must be the connecting actor).
public sealed class WorkflowTasksListChannelSubscribeGate : IChannelSubscribeGate
{
    public string Kind => WorkflowChannelNames.TasksListKind;

    public Task<SubscribeGateResult> AuthorizeAsync(
        ChannelName channel,
        SubscriptionConnection connection,
        CancellationToken cancellationToken)
    {
        if (channel.Parts.Count != 2
            || !string.Equals(channel.Parts[0], "assigned-to", StringComparison.Ordinal))
        {
            return Task.FromResult(SubscribeGateResult.Forbid(
                SubscriptionRejectCode.UnknownChannel,
                "expected workflow-tasks:assigned-to:{userId}"));
        }
        if (!Guid.TryParse(channel.Parts[1], out var requestedUserId)
            || requestedUserId != connection.Snapshot.UserId)
        {
            return Task.FromResult(SubscribeGateResult.Forbid(
                SubscriptionRejectCode.Forbidden,
                "userId must match the connecting actor"));
        }
        return Task.FromResult(SubscribeGateResult.Allow());
    }
}

// `tasks:assigned-to:{userId}` and `tasks:supervisees-of-me` — the "my tasks"
// surface. Assigned-to requires own userId; supervisees-of-me requires the
// actor to have at least one outbound supervisor edge (otherwise the
// subscription would never fire).
public sealed class MyTasksListChannelSubscribeGate : IChannelSubscribeGate
{
    public string Kind => WorkflowChannelNames.MyTasksListKind;

    public Task<SubscribeGateResult> AuthorizeAsync(
        ChannelName channel,
        SubscriptionConnection connection,
        CancellationToken cancellationToken)
    {
        if (channel.Parts.Count == 1
            && string.Equals(channel.Parts[0], "supervisees-of-me", StringComparison.Ordinal))
        {
            var hasSupervisees = connection.Snapshot.OutboundUserEdges
                .TryGetValue(EdgeKinds.Supervisor, out var supervisees) && supervisees.Count > 0;
            return Task.FromResult(hasSupervisees || connection.Snapshot.IsSuperAdmin
                ? SubscribeGateResult.Allow()
                : SubscribeGateResult.Forbid(
                    SubscriptionRejectCode.Forbidden,
                    "actor has no supervisees"));
        }

        if (channel.Parts.Count == 2
            && string.Equals(channel.Parts[0], "assigned-to", StringComparison.Ordinal))
        {
            if (!Guid.TryParse(channel.Parts[1], out var requestedUserId)
                || requestedUserId != connection.Snapshot.UserId)
            {
                return Task.FromResult(SubscribeGateResult.Forbid(
                    SubscriptionRejectCode.Forbidden,
                    "userId must match the connecting actor"));
            }
            return Task.FromResult(SubscribeGateResult.Allow());
        }

        return Task.FromResult(SubscribeGateResult.Forbid(
            SubscriptionRejectCode.UnknownChannel,
            "expected tasks:assigned-to:{userId} or tasks:supervisees-of-me"));
    }
}
