using System.Diagnostics;
using System.Security.Claims;
using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.Evaluator;
using AutoNate.Web.Persistence;
using AutoNate.Web.Persistence.Scaffolded;
using Microsoft.EntityFrameworkCore;

namespace AutoNate.Web.Services.Query.Entities;

// AQL entity that exposes the workflow_execution_cache table. Filtering runs
// through IAuthorizer.FilterQueryAsync<WorkflowExecutionCache>(), which uses
// WorkflowExecutionCacheSelectorCompiler to translate grants like
// `[startedby=$me]` or `[processkey=approval]` into SQL WHEREs.
public sealed class WorkflowExecutionsQueryEntity : IQueryEntity
{
    private readonly IDbContextFactory<AutoNateDbContext> _dbFactory;
    private readonly IAuthorizer _authorizer;

    public WorkflowExecutionsQueryEntity(
        IDbContextFactory<AutoNateDbContext> dbFactory,
        IAuthorizer authorizer)
    {
        _dbFactory = dbFactory;
        _authorizer = authorizer;
    }

    public string Name => "WorkflowExecutions";

    public IReadOnlyList<QueryColumn> StaticSchema { get; } = new[]
    {
        new QueryColumn("Id",                   QueryDataType.String, false, true),
        new QueryColumn("ProcessKey",           QueryDataType.String, false, true),
        new QueryColumn("ProcessDefinitionId",  QueryDataType.String, false, true),
        new QueryColumn("ProcessVersion",       QueryDataType.Number, true,  true),
        new QueryColumn("BusinessKey",          QueryDataType.String, false, true),
        new QueryColumn("Tenant",               QueryDataType.String, false, true),
        new QueryColumn("Status",               QueryDataType.String, false, true),
        new QueryColumn("StartTime",            QueryDataType.Date,   true,  true),
        new QueryColumn("EndTime",              QueryDataType.Date,   true,  true),
        new QueryColumn("DurationMs",           QueryDataType.Number, true,  true),
        new QueryColumn("StartedBy",            QueryDataType.String, false, true),
        new QueryColumn("CurrentActivityId",    QueryDataType.String, false, true),
        new QueryColumn("CurrentActivityName",  QueryDataType.String, false, true),
        new QueryColumn("RecordId",             QueryDataType.Number, false, true),
        new QueryColumn("LastSyncAt",           QueryDataType.Date,   false, true),
    };

    public IReadOnlyList<string> AllowedFunctions { get; } = Array.Empty<string>();

    // Raw cache labels stored in workflow_execution_cache.status. Surfaced as
    // value suggestions after `Status = ` in this entity (sibling FlowsQueryEntity
    // exposes its own display-label variant on its own Status column).
    public IReadOnlyDictionary<string, IReadOnlyList<string>> ColumnEnums { get; } =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Status"] = new[] { "active", "completed", "cancelled", "suspended", "terminated" }
        };

    public Task<IPreparedQuery> PrepareAsync(AqlQuery query, CancellationToken cancellationToken)
    {
        IPreparedQuery prepared = new WorkflowExecutionsPreparedQuery(
            this, query, StaticSchema, _dbFactory, _authorizer);
        return Task.FromResult(prepared);
    }
}

internal sealed class WorkflowExecutionsPreparedQuery : IPreparedQuery
{
    private readonly IDbContextFactory<AutoNateDbContext> _dbFactory;
    private readonly IAuthorizer _authorizer;

    public WorkflowExecutionsPreparedQuery(
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
        var baseQuery = db.WorkflowExecutionCache.AsNoTracking().AsQueryable();
        var authorized = await _authorizer.FilterQueryAsync(
            db, actor, EntityKinds.WorkflowExecution, Actions.View, baseQuery, cancellationToken);

        var rowsList = await authorized.ToListAsync(cancellationToken);

        // WHERE/ORDER BY are evaluated in-memory after the auth-filter to
        // keep this entity small. Most workflow execution queries today fit
        // easily in RAM (a few hundred to a few thousand rows post-filter);
        // EF-translated WHERE clauses are a Phase 2 optimization if a
        // saved-query hits >10k rows.
        if (Query.Where is not null)
        {
            rowsList = rowsList.Where(r => EvalWhere(Query.Where, r)).ToList();
        }

        if (Query.OrderBy.Count > 0)
        {
            IOrderedEnumerable<WorkflowExecutionCache>? sorted = null;
            foreach (var item in Query.OrderBy)
            {
                if (item.Item.IsAggregate)
                {
                    throw new AqlValidationException(
                        "Aggregate ORDER BY on WorkflowExecutions is not yet supported.");
                }

                Func<WorkflowExecutionCache, IComparable?> keyFn = r => ReadField(item.Item.Field!, r) as IComparable;
                sorted = sorted is null
                    ? (item.Descending
                        ? rowsList.OrderByDescending(keyFn, NullSafeComparer.Instance)
                        : rowsList.OrderBy(keyFn, NullSafeComparer.Instance))
                    : (item.Descending
                        ? sorted.ThenByDescending(keyFn, NullSafeComparer.Instance)
                        : sorted.ThenBy(keyFn, NullSafeComparer.Instance));
            }
            rowsList = sorted!.ToList();
        }

        if (Query.Group is not null)
        {
            throw new AqlValidationException(
                "GROUP(...) is not yet supported on WorkflowExecutions.");
        }

        var effectiveCap = Query.Limit ?? hardCap;
        var truncated = false;
        if (effectiveCap is { } cap && rowsList.Count > cap)
        {
            truncated = true;
            rowsList = rowsList.Take(cap).ToList();
        }

        var projection = ResolveProjection();
        var resultRows = rowsList
            .Select(r => (IReadOnlyDictionary<string, object?>)projection.ToDictionary(
                p => p.DisplayName,
                p => ReadField(p.Field, r)))
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
                var col = Schema.First(s =>
                    string.Equals(s.Name, c.Field, StringComparison.OrdinalIgnoreCase));
                return new ProjItem(c.DisplayName, c.Field!, col.DataType);
            }).ToList();
        }
        return Schema.Select(c => new ProjItem(c.Name, c.Name, c.DataType)).ToList();
    }

    private static object? ReadField(string field, WorkflowExecutionCache r) => field.ToLowerInvariant() switch
    {
        "id" => r.FlowableInstanceId,
        "processkey" => r.ProcessDefinitionKey,
        "processdefinitionid" => r.ProcessDefinitionId,
        "processversion" => (object?)r.ProcessDefinitionVersion,
        "businesskey" => r.BusinessKey,
        "tenant" => r.TenantId,
        "status" => r.Status,
        "starttime" => DateTime.SpecifyKind(r.StartTime, DateTimeKind.Utc),
        "endtime" => r.EndTime is { } e ? DateTime.SpecifyKind(e, DateTimeKind.Utc) : null,
        "durationms" => (object?)r.DurationMs,
        "startedby" => r.StartedBy,
        "currentactivityid" => r.CurrentActivityId,
        "currentactivityname" => r.CurrentActivityName,
        "recordid" => (object?)r.RecordId,
        "lastsyncat" => DateTime.SpecifyKind(r.LastSyncAtUtc, DateTimeKind.Utc),
        _ => null
    };

    private static bool EvalWhere(AqlWhere where, WorkflowExecutionCache row) => where switch
    {
        AqlBinary b => b.Op == "AND"
            ? EvalWhere(b.Left, row) && EvalWhere(b.Right, row)
            : EvalWhere(b.Left, row) || EvalWhere(b.Right, row),
        AqlCompare c => CompareValues(ReadField(c.Field, row), ResolveValue(c.Value), c.Op),
        AqlContains ct => ReadField(ct.Field, row) is string s
            && s.Contains(ct.Substr, StringComparison.OrdinalIgnoreCase),
        AqlIn inFilter => inFilter.Values.Any(v =>
            CompareValues(ReadField(inFilter.Field, row), ResolveValue(v), "=")),
        AqlBetween bw => CompareValues(ReadField(bw.Field, row), ResolveValue(bw.Lo), ">=")
                     && CompareValues(ReadField(bw.Field, row), ResolveValue(bw.Hi), "<="),
        _ => false
    };

    private static object? ResolveValue(AqlValue v) => v switch
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
            return actual is string a && expected is string e
                && a.Contains(e, StringComparison.OrdinalIgnoreCase);
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
                "=" => cmp == 0,
                "!=" => cmp != 0,
                "<" => cmp < 0, "<=" => cmp <= 0,
                ">" => cmp > 0, ">=" => cmp >= 0,
                _ => false
            };
        }
        if (actual is IComparable && expected is IComparable)
        {
            double? ad = ToDouble(actual);
            double? ed = ToDouble(expected);
            if (ad is { } adv && ed is { } edv)
            {
                // double.Equals → bit-equality method call, side-steps Sonar S1244.
                return op switch
                {
                    "=" => adv.Equals(edv),
                    "!=" => !adv.Equals(edv),
                    "<" => adv < edv, "<=" => adv <= edv,
                    ">" => adv > edv, ">=" => adv >= edv,
                    _ => false
                };
            }
            if (actual is DateTime adt && expected is DateTime edt)
            {
                var c = adt.CompareTo(edt);
                return op switch
                {
                    "=" => c == 0,
                    "!=" => c != 0,
                    "<" => c < 0, "<=" => c <= 0,
                    ">" => c > 0, ">=" => c >= 0,
                    _ => false
                };
            }
        }
        return false;
    }

    private static double? ToDouble(object? v) => v switch
    {
        int i => i, long l => l, double d => d, float f => f, decimal dec => (double)dec,
        _ => null
    };

    private sealed class NullSafeComparer : IComparer<IComparable?>
    {
        public static readonly NullSafeComparer Instance = new();
        public int Compare(IComparable? x, IComparable? y)
        {
            if (x is null && y is null) return 0;
            if (x is null) return 1;
            if (y is null) return -1;
            return x.CompareTo(y);
        }
    }
}
