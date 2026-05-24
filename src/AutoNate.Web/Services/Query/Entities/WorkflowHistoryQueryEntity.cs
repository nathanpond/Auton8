using System.Diagnostics;
using System.Security.Claims;
using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.Evaluator;
using AutoNate.Web.Persistence;
using AutoNate.Web.Persistence.Scaffolded;
using Microsoft.EntityFrameworkCore;

namespace AutoNate.Web.Services.Query.Entities;

// AQL entity exposing the append-only workflow_event_log_cache. Authorization
// inherits from the parent execution (the actor must be able to view the
// instance the event belongs to). Supports the time-series predicate set
// (BETWEEN on EventTime, =/!=/comparison on EventType) that dashboard widgets
// and process-mining queries want.
public sealed class WorkflowHistoryQueryEntity : IQueryEntity
{
    private readonly IDbContextFactory<AutoNateDbContext> _dbFactory;
    private readonly IAuthorizer _authorizer;

    public WorkflowHistoryQueryEntity(
        IDbContextFactory<AutoNateDbContext> dbFactory,
        IAuthorizer authorizer)
    {
        _dbFactory = dbFactory;
        _authorizer = authorizer;
    }

    public string Name => "WorkflowHistory";

    public IReadOnlyList<QueryColumn> StaticSchema { get; } = new[]
    {
        new QueryColumn("EventId",      QueryDataType.String, false, true),
        new QueryColumn("InstanceId",   QueryDataType.String, false, true),
        new QueryColumn("ProcessKey",   QueryDataType.String, false, true),
        new QueryColumn("EventTime",    QueryDataType.Date,   true,  true),
        new QueryColumn("EventType",    QueryDataType.String, false, true),
        new QueryColumn("ActivityId",   QueryDataType.String, false, true),
        new QueryColumn("ActivityName", QueryDataType.String, false, true),
        new QueryColumn("ActivityType", QueryDataType.String, false, true),
        new QueryColumn("TaskId",       QueryDataType.String, false, true),
        new QueryColumn("VariableName", QueryDataType.String, false, true),
        new QueryColumn("Actor",        QueryDataType.String, false, true),
        new QueryColumn("DurationMs",   QueryDataType.Number, true,  true),
    };

    public IReadOnlyList<string> AllowedFunctions { get; } = Array.Empty<string>();

    public Task<IPreparedQuery> PrepareAsync(AqlQuery query, CancellationToken cancellationToken)
    {
        IPreparedQuery prepared = new WorkflowHistoryPreparedQuery(
            this, query, StaticSchema, _dbFactory, _authorizer);
        return Task.FromResult(prepared);
    }
}

internal sealed class WorkflowHistoryPreparedQuery : IPreparedQuery
{
    private readonly IDbContextFactory<AutoNateDbContext> _dbFactory;
    private readonly IAuthorizer _authorizer;

    public WorkflowHistoryPreparedQuery(
        IQueryEntity entity,
        AqlQuery query,
        IReadOnlyList<QueryColumn> schema,
        IDbContextFactory<AutoNateDbContext> dbFactory,
        IAuthorizer authorizer)
    {
        Entity = entity;
        Query = query;
        Schema = schema;
        ValidationErrors = Array.Empty<string>();
        _dbFactory = dbFactory;
        _authorizer = authorizer;
    }

    public IQueryEntity Entity { get; }
    public AqlQuery Query { get; }
    public IReadOnlyList<QueryColumn> Schema { get; }
    public IReadOnlyList<string> ValidationErrors { get; }

    public async Task<QueryResult> ExecuteAsync(
        ClaimsPrincipal actor,
        int? hardCap,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        var visibleInstances = await _authorizer.FilterQueryAsync(
            db, actor, EntityKinds.WorkflowExecution, Actions.View,
            db.WorkflowExecutionCache.AsNoTracking().AsQueryable(),
            cancellationToken);
        var visibleIds = await visibleInstances.Select(c => c.FlowableInstanceId).ToListAsync(cancellationToken);

        if (visibleIds.Count == 0)
        {
            return new QueryResult(
                Columns: Schema.Select(c => new QueryColumnMeta(c.Name, c.DataType)).ToList(),
                Rows: Array.Empty<IReadOnlyDictionary<string, object?>>(),
                TotalCount: 0,
                Truncated: false,
                DurationMs: sw.ElapsedMilliseconds);
        }

        var rows = await db.WorkflowEventLogCache.AsNoTracking()
            .Where(e => visibleIds.Contains(e.FlowableInstanceId))
            .ToListAsync(cancellationToken);

        if (Query.Where is not null)
        {
            rows = rows.Where(r => EvalWhere(Query.Where, r)).ToList();
        }

        if (Query.OrderBy.Count > 0)
        {
            IOrderedEnumerable<WorkflowEventLogCache>? sorted = null;
            foreach (var item in Query.OrderBy)
            {
                if (item.Item.IsAggregate)
                {
                    throw new AqlValidationException("Aggregate ORDER BY on WorkflowHistory is not yet supported.");
                }
                Func<WorkflowEventLogCache, IComparable?> keyFn = r => ReadField(item.Item.Field!, r) as IComparable;
                sorted = sorted is null
                    ? (item.Descending ? rows.OrderByDescending(keyFn, NullComparer.Instance) : rows.OrderBy(keyFn, NullComparer.Instance))
                    : (item.Descending ? sorted.ThenByDescending(keyFn, NullComparer.Instance) : sorted.ThenBy(keyFn, NullComparer.Instance));
            }
            rows = sorted!.ToList();
        }
        else
        {
            // Time-series default: most-recent first.
            rows = rows.OrderByDescending(r => r.EventTime).ToList();
        }

        if (Query.Group is not null)
        {
            throw new AqlValidationException("GROUP(...) is not yet supported on WorkflowHistory.");
        }

        var cap = Query.Limit ?? hardCap;
        var truncated = false;
        if (cap is { } c && rows.Count > c)
        {
            truncated = true;
            rows = rows.Take(c).ToList();
        }

        var projection = ResolveProjection();
        var resultRows = rows.Select(r => (IReadOnlyDictionary<string, object?>)projection
            .ToDictionary(p => p.DisplayName, p => ReadField(p.Field, r)))
            .ToList();

        return new QueryResult(
            Columns: projection.Select(p => new QueryColumnMeta(p.DisplayName, p.DataType)).ToList(),
            Rows: resultRows,
            TotalCount: resultRows.Count + (truncated ? 1 : 0),
            Truncated: truncated,
            DurationMs: sw.ElapsedMilliseconds);
    }

    private record ProjItem(string DisplayName, string Field, QueryDataType DataType);

    private IReadOnlyList<ProjItem> ResolveProjection()
    {
        if (Query.Columns is not null)
        {
            return Query.Columns.Select(c =>
            {
                var col = Schema.First(s => string.Equals(s.Name, c.Field, StringComparison.OrdinalIgnoreCase));
                return new ProjItem(c.DisplayName, c.Field!, col.DataType);
            }).ToList();
        }
        return Schema.Select(c => new ProjItem(c.Name, c.Name, c.DataType)).ToList();
    }

    private static object? ReadField(string field, WorkflowEventLogCache r) => field.ToLowerInvariant() switch
    {
        "eventid" => r.EventId,
        "instanceid" => r.FlowableInstanceId,
        "processkey" => r.ProcessDefinitionKey,
        "eventtime" => DateTime.SpecifyKind(r.EventTime, DateTimeKind.Utc),
        "eventtype" => r.EventType,
        "activityid" => r.ActivityId,
        "activityname" => r.ActivityName,
        "activitytype" => r.ActivityType,
        "taskid" => r.TaskId,
        "variablename" => r.VariableName,
        "actor" => r.Actor,
        "durationms" => (object?)r.DurationMs,
        _ => null
    };

    private bool EvalWhere(AqlWhere where, WorkflowEventLogCache row) => where switch
    {
        AqlBinary b => b.Op == "AND"
            ? EvalWhere(b.Left, row) && EvalWhere(b.Right, row)
            : EvalWhere(b.Left, row) || EvalWhere(b.Right, row),
        AqlCompare c => CompareValues(ReadField(c.Field, row), Resolve(c.Value), c.Op),
        AqlContains ct => ReadField(ct.Field, row) is string s
            && s.Contains(ct.Substr, StringComparison.OrdinalIgnoreCase),
        AqlIn inFilter => inFilter.Values.Any(v => CompareValues(ReadField(inFilter.Field, row), Resolve(v), "=")),
        AqlBetween bw => CompareValues(ReadField(bw.Field, row), Resolve(bw.Lo), ">=")
                     && CompareValues(ReadField(bw.Field, row), Resolve(bw.Hi), "<="),
        _ => false
    };

    private static object? Resolve(AqlValue v) => v switch
    {
        AqlString s => s.Value,
        AqlNumber n => n.Value,
        AqlBool b => b.Value,
        AqlNull => null,
        AqlRelativeDate r => r.Resolve(DateTime.UtcNow),
        _ => null
    };

    private static bool CompareValues(object? actual, object? expected, string op)
    {
        if (op == "=" && actual is null) return expected is null;
        if (op == "!=" && actual is null) return expected is not null;
        if (actual is null) return false;
        if (op == "~")
        {
            return actual is string a && expected is string e && a.Contains(e, StringComparison.OrdinalIgnoreCase);
        }
        if (actual is bool ab && expected is bool eb)
        {
            return op switch { "=" => ab == eb, "!=" => ab != eb, _ => false };
        }
        if (actual is string @as && expected is string es)
        {
            var cmp = string.Compare(@as, es, StringComparison.OrdinalIgnoreCase);
            return op switch
            {
                "=" => cmp == 0, "!=" => cmp != 0,
                "<" => cmp < 0, "<=" => cmp <= 0,
                ">" => cmp > 0, ">=" => cmp >= 0,
                _ => false
            };
        }
        if (actual is DateTime adt && expected is DateTime edt)
        {
            var c = adt.CompareTo(edt);
            return op switch
            {
                "=" => c == 0, "!=" => c != 0,
                "<" => c < 0, "<=" => c <= 0,
                ">" => c > 0, ">=" => c >= 0,
                _ => false
            };
        }
        double? ad = ToDouble(actual); double? ed = ToDouble(expected);
        if (ad is { } adv && ed is { } edv)
        {
            return op switch
            {
                "=" => adv == edv, "!=" => adv != edv,
                "<" => adv < edv, "<=" => adv <= edv,
                ">" => adv > edv, ">=" => adv >= edv,
                _ => false
            };
        }
        return false;
    }

    private static double? ToDouble(object? v) => v switch
    {
        int i => i, long l => l, double d => d, float f => f, decimal dec => (double)dec,
        _ => null
    };

    private sealed class NullComparer : IComparer<IComparable?>
    {
        public static readonly NullComparer Instance = new();
        public int Compare(IComparable? x, IComparable? y)
        {
            if (x is null && y is null) return 0;
            if (x is null) return 1;
            if (y is null) return -1;
            return x.CompareTo(y);
        }
    }
}
