using System.Security.Claims;
using System.Collections.Concurrent;
using AutoNate.Web.Authorization.EntityTypes;
using AutoNate.Web.Authorization.Edges;
using AutoNate.Web.Authorization.Selectors;
using AutoNate.Web.Persistence;
using AutoNate.Web.Services.Flowable;
using AutoNate.Web.Services.Flowable.Cache;
using AutoNate.Web.Persistence.Scaffolded;
using Microsoft.EntityFrameworkCore;

namespace AutoNate.Web.Authorization.Evaluator;

// Flowable lives outside our database, so its instance authorizers fetch the
// entity's metadata over HTTP and evaluate selectors in memory rather than
// as a LINQ subquery. Multi-hop predicates (e.g. `[supervisor=user]` nested
// inside an outer `=user`) need the actor's outbound user→user edge graph;
// each authorizer pre-loads it once per request and hands it to the
// evaluator.
public sealed class WorkflowTaskInstanceAuthorizer : IInstanceAuthorizer
{
    private readonly IFlowableClient _flowable;
    private readonly IDbContextFactory<AutoNateDbContext> _dbFactory;

    public WorkflowTaskInstanceAuthorizer(
        IFlowableClient flowable,
        IDbContextFactory<AutoNateDbContext> dbFactory)
    {
        _flowable = flowable;
        _dbFactory = dbFactory;
    }

    public string Kind => EntityKinds.WorkflowTask;

    public async Task<bool> ExistsAndAuthorizedAsync(
        IAuthorizer authorizer,
        ClaimsPrincipal actor,
        string action,
        string targetId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(targetId))
        {
            return false;
        }

        var actorId = actor.TryGetUserId();
        if (actorId is null)
        {
            return false;
        }

        var task = await _flowable.GetTaskAsync(targetId, cancellationToken);
        if (task is null)
        {
            return false;
        }

        var outboundEdges = await ActorOutboundUserEdges
            .LoadAsync(_dbFactory, actorId.Value, cancellationToken);
        // #632. The tags this kind ADVERTISES, so a selector naming a withdrawn
        // one throws instead of quietly evaluating to false -- which on a DENY
        // meant the deny did not fire.
        var evaluator = new InMemorySelectorEvaluator(actorId.Value, outboundEdges)
        {
            KnownTags = AdvertisedTags.For(Kind)
        };
        var facts = BuildFacts(task);

        return await authorizer.IsAuthorizedAsync(
            actor, Kind, action,
            ast => evaluator.Matches(ast, task.Id, facts),
            cancellationToken);
    }

    // Tag set mirrors CoreEntityTypes.WorkflowTask.tags. `candidategroup` was
    // dropped from the registry because Flowable's task summary endpoint
    // doesn't return identity links; reintroduce only after IFlowableClient
    // exposes them, otherwise grants like `[candidategroup=...]` silently miss.
    private static IReadOnlyDictionary<string, string?> BuildFacts(Models.FlowableTaskSummary task) =>
        new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["assignee"] = task.Assignee,
            ["processkey"] = ExtractProcessKey(task.ProcessDefinitionId),
            ["definitionkey"] = task.TaskDefinitionKey
        };

    private static string? ExtractProcessKey(string? processDefinitionId)
    {
        if (string.IsNullOrEmpty(processDefinitionId))
        {
            return null;
        }

        var sep = processDefinitionId.IndexOf(':');
        return sep > 0 ? processDefinitionId[..sep] : processDefinitionId;
    }

}

public sealed class WorkflowExecutionInstanceAuthorizer : IInstanceAuthorizer
{
    private readonly IFlowableReadThrough _readThrough;
    private readonly IDbContextFactory<AutoNateDbContext> _dbFactory;

    // #579. Was IFlowableClient, which made every
    // RequirePermission(..., "processInstanceId") gate a per-request round trip
    // to the engine -- so a single-execution read failed the whole page whenever
    // Flowable was unreachable, and the list authorized from the cache while
    // single reads authorized from live Flowable. Two sources of truth for one
    // decision.
    //
    // IFlowableReadThrough is cache-first, reads through on miss or staleness,
    // and returns the cached row when the live call throws. This is its first
    // consumer, which closes #19.
    public WorkflowExecutionInstanceAuthorizer(
        IFlowableReadThrough readThrough,
        IDbContextFactory<AutoNateDbContext> dbFactory)
    {
        _readThrough = readThrough;
        _dbFactory = dbFactory;
    }

    public string Kind => EntityKinds.WorkflowExecution;

    public async Task<bool> ExistsAndAuthorizedAsync(
        IAuthorizer authorizer,
        ClaimsPrincipal actor,
        string action,
        string targetId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(targetId))
        {
            return false;
        }

        // Null means one of two things, and both answer the same way:
        //   * no such instance -- the read-through asked Flowable and got nothing;
        //   * a cache miss while Flowable is unreachable -- no row, no way to ask.
        // The second is the interesting one. With no facts, no selector can be
        // evaluated, and admitting on absent evidence is exactly the failure #577
        // closed on the other path. So it refuses.
        var instance = await _readThrough.GetInstanceAsync(targetId, cancellationToken);
        if (instance is null)
        {
            return false;
        }

        var actorId = actor.TryGetUserId();
        if (actorId is null)
        {
            return false;
        }

        var outboundEdges = await ActorOutboundUserEdges
            .LoadAsync(_dbFactory, actorId.Value, cancellationToken);
        // #632. The tags this kind ADVERTISES, so a selector naming a withdrawn
        // one throws instead of quietly evaluating to false -- which on a DENY
        // meant the deny did not fire.
        var evaluator = new InMemorySelectorEvaluator(actorId.Value, outboundEdges)
        {
            KnownTags = AdvertisedTags.For(Kind)
        };
        var facts = BuildFacts(instance);

        return await authorizer.IsAuthorizedAsync(
            actor, Kind, action,
            ast => evaluator.Matches(ast, instance.FlowableInstanceId, facts),
            cancellationToken);
    }

    // Tag set mirrors CoreEntityTypes.WorkflowExecution.tags. `assignee` was
    // dropped from the registry — Flowable's process-instance summary has no
    // assignee field (assignees live on tasks). Reintroduce only if IFlowableClient
    // gains a way to enumerate the instance's task assignees up front.
    // internal for the same reason as the twin in ExecutionEndpoints (#576).
    internal static IReadOnlyDictionary<string, string?> BuildFacts(WorkflowExecutionCache instance) =>
        new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            // Read from the cache row's own columns (#579).
            //
            // `processkey` is NOT re-derived here: the projection already ran
            // ExtractProcessKey when it wrote the row, so taking the column
            // cannot drift from what the list path computes. Parity with
            // ExecutionEndpoints.BuildFacts is asserted by a test rather than
            // assumed.
            ["processkey"] = instance.ProcessDefinitionKey,
            ["definitionkey"] = instance.ProcessDefinitionId,
            ["startedby"] = instance.StartedBy,

            // `status` comes straight off the row, and that is a real
            // improvement rather than a translation. #576 had to INFER status
            // here from FlowableProcessInstanceSummary.Suspended, because the
            // runtime shape carries no status string and anything that endpoint
            // returns is still running. The cache column is the projection's
            // normalized value, so completed, cancelled and terminated are now
            // reachable on this path -- states the suspension inference could
            // not express at all.
            ["status"] = instance.Status
        };

    private static string? ExtractProcessKey(string? processDefinitionId)
    {
        if (string.IsNullOrEmpty(processDefinitionId))
        {
            return null;
        }

        var sep = processDefinitionId.IndexOf(':');
        return sep > 0 ? processDefinitionId[..sep] : processDefinitionId;
    }

}

// Loads the actor's outbound user→user edges grouped by edge_kind. Used by
// the in-memory evaluator to answer nested predicates like
// `[supervisor=user]` without re-querying the DB on every fact lookup.
internal static class ActorOutboundUserEdges
{
    public static async Task<IReadOnlyDictionary<string, IReadOnlySet<string>>> LoadAsync(
        IDbContextFactory<AutoNateDbContext> dbFactory,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var actorString = actorUserId.ToString();

        var rows = await db.EntityEdges.AsNoTracking()
            .Where(e => e.FromKind == EntityKinds.User
                     && e.FromId == actorString
                     && e.ToKind == EntityKinds.User)
            .Select(e => new { e.EdgeKind, e.ToId })
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
        {
            return new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal);
        }

        return rows
            .GroupBy(r => r.EdgeKind, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlySet<string>)g.Select(r => r.ToId).ToHashSet(StringComparer.Ordinal),
                StringComparer.Ordinal);
    }
}

// #112. Messages are addressed by process key plus a correlation value, never by
// instance id, so the endpoint gates with RequireKindPermission and no instance
// check is reached on the normal path.
//
// This exists anyway, and denies. Authorizer.cs:131 returns "no instance handler
// for kind" when one is missing, which denies everyone except super-admins — and
// under Authorization:DryRun=true it ALLOWS everyone and merely logs. So the
// absence of a handler is not a safe default in both configurations, and five
// kinds have already shipped that way (see the note in Program.cs). Registering a
// deliberate deny makes "there is no such thing as an instance-level message
// grant" a decision rather than an omission.
public sealed class WorkflowMessageInstanceAuthorizer : IInstanceAuthorizer
{
    public string Kind => EntityKinds.WorkflowMessage;

    public Task<bool> ExistsAndAuthorizedAsync(
        IAuthorizer authorizer,
        ClaimsPrincipal actor,
        string action,
        string targetId,
        CancellationToken cancellationToken) => Task.FromResult(false);
}

/// <summary>The tags a kind advertises, from the registry (#632).</summary>
/// <remarks>
/// Read from <see cref="CoreEntityTypes"/> rather than copied, so withdrawing a
/// tag there -- as #576 did for <c>tenant</c> and #581 for the candidate pair --
/// narrows the in-memory evaluator in the same commit. A second hand-maintained
/// list is how the SQL and in-memory paths drifted apart in the first place.
/// </remarks>
internal static class AdvertisedTags
{
    private static readonly ConcurrentDictionary<string, IReadOnlySet<string>> Cache = new(StringComparer.OrdinalIgnoreCase);

    internal static IReadOnlySet<string> For(string kind) => Cache.GetOrAdd(kind, static k =>
        CoreEntityTypes.All
            .Where(t => string.Equals(t.Kind, k, StringComparison.OrdinalIgnoreCase))
            .SelectMany(t => t.Tags)
            .ToHashSet(StringComparer.OrdinalIgnoreCase));
}
