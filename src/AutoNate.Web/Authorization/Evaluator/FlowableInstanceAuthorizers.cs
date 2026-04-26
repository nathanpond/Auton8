using System.Security.Claims;
using AutoNate.Web.Authorization.Edges;
using AutoNate.Web.Authorization.Selectors;
using AutoNate.Web.Persistence;
using AutoNate.Web.Services.Flowable;
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

        var actorId = GetUserId(actor);
        if (actorId is null)
        {
            return false;
        }

        var task = await ResolveTaskAsync(targetId, actorId.Value, cancellationToken);
        if (task is null)
        {
            return false;
        }

        var outboundEdges = await ActorOutboundUserEdges
            .LoadAsync(_dbFactory, actorId.Value, cancellationToken);
        var evaluator = new InMemorySelectorEvaluator(actorId.Value, outboundEdges);
        var facts = BuildFacts(task);

        return await authorizer.IsAuthorizedAsync(
            actor, Kind, action,
            ast => evaluator.Matches(ast, task.Id, facts),
            cancellationToken);
    }

    private async Task<Models.FlowableTaskSummary?> ResolveTaskAsync(
        string taskId, Guid actorUserId, CancellationToken ct)
    {
        // IFlowableClient doesn't yet expose a "get task by id" — we scan the
        // tasks assigned to the actor. That covers the headline `complete my
        // task` flow but means multi-hop selectors (supervisor sees their
        // supervisees' tasks) won't resolve until the resolver broadens.
        // Records are unaffected — the SQL compiler walks the graph there.
        var assigned = await _flowable.GetTasksAssignedToUserAsync(actorUserId.ToString(), ct);
        return assigned.FirstOrDefault(t => string.Equals(t.Id, taskId, StringComparison.Ordinal));
    }

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

    private static Guid? GetUserId(ClaimsPrincipal actor)
    {
        var raw = actor.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(raw, out var id) ? id : null;
    }
}

public sealed class WorkflowExecutionInstanceAuthorizer : IInstanceAuthorizer
{
    private readonly IFlowableClient _flowable;
    private readonly IDbContextFactory<AutoNateDbContext> _dbFactory;

    public WorkflowExecutionInstanceAuthorizer(
        IFlowableClient flowable,
        IDbContextFactory<AutoNateDbContext> dbFactory)
    {
        _flowable = flowable;
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

        var instance = await _flowable.GetProcessInstanceAsync(targetId, cancellationToken);
        if (instance is null)
        {
            return false;
        }

        var actorId = GetUserId(actor);
        if (actorId is null)
        {
            return false;
        }

        var outboundEdges = await ActorOutboundUserEdges
            .LoadAsync(_dbFactory, actorId.Value, cancellationToken);
        var evaluator = new InMemorySelectorEvaluator(actorId.Value, outboundEdges);
        var facts = BuildFacts(instance);

        return await authorizer.IsAuthorizedAsync(
            actor, Kind, action,
            ast => evaluator.Matches(ast, instance.Id, facts),
            cancellationToken);
    }

    private static IReadOnlyDictionary<string, string?> BuildFacts(Models.FlowableProcessInstanceSummary instance) =>
        new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["processkey"] = ExtractProcessKey(instance.ProcessDefinitionId),
            ["definitionkey"] = instance.ProcessDefinitionId,
            ["startedby"] = instance.StartUserId
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

    private static Guid? GetUserId(ClaimsPrincipal actor)
    {
        var raw = actor.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(raw, out var id) ? id : null;
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
