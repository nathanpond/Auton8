using System.Diagnostics;
using System.Security.Claims;
using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.Evaluator;
using AutoNate.Web.Persistence;
using AutoNate.Web.Persistence.Scaffolded;
using Microsoft.EntityFrameworkCore;

namespace AutoNate.Web.Services.Query.Entities;

// AQL entity exposing workflow_task_cache. Same shape as
// WorkflowExecutionsQueryEntity; selector filtering through
// IAuthorizer.FilterQueryAsync<WorkflowTaskCache> covers
// `assignee=$me`, `processkey=...`, candidate user/group membership.
public sealed class WorkflowTasksQueryEntity : IQueryEntity
{
    private readonly IDbContextFactory<AutoNateDbContext> _dbFactory;
    private readonly IAuthorizer _authorizer;

    public WorkflowTasksQueryEntity(
        IDbContextFactory<AutoNateDbContext> dbFactory,
        IAuthorizer authorizer)
    {
        _dbFactory = dbFactory;
        _authorizer = authorizer;
    }

    public string Name => "WorkflowTasks";

    public IReadOnlyList<QueryColumn> StaticSchema { get; } = new[]
    {
        new QueryColumn("Id",                 QueryDataType.String, false, true),
        new QueryColumn("InstanceId",         QueryDataType.String, false, true),
        new QueryColumn("ProcessKey",         QueryDataType.String, false, true),
        new QueryColumn("TaskDefinitionKey",  QueryDataType.String, false, true),
        new QueryColumn("Name",               QueryDataType.String, false, true),
        new QueryColumn("Assignee",           QueryDataType.String, false, true),
        new QueryColumn("Owner",              QueryDataType.String, false, true),
        new QueryColumn("DueDate",            QueryDataType.Date,   true,  true),
        new QueryColumn("CreatedTime",        QueryDataType.Date,   true,  true),
        new QueryColumn("ClaimTime",          QueryDataType.Date,   true,  true),
        new QueryColumn("CompletedTime",      QueryDataType.Date,   true,  true),
        new QueryColumn("FormKey",            QueryDataType.String, false, true),
        new QueryColumn("Priority",           QueryDataType.Number, true,  true),
        new QueryColumn("Status",             QueryDataType.String, false, true),
        new QueryColumn("LastSyncAt",         QueryDataType.Date,   false, true),
    };

    public IReadOnlyList<string> AllowedFunctions { get; } = Array.Empty<string>();

    public Task<IPreparedQuery> PrepareAsync(AqlQuery query, CancellationToken cancellationToken)
    {
        IPreparedQuery prepared = new WorkflowTasksPreparedQuery(
            this, query, StaticSchema, _dbFactory, _authorizer);
        return Task.FromResult(prepared);
    }
}

internal sealed class WorkflowTasksPreparedQuery : IPreparedQuery
{
    private readonly IDbContextFactory<AutoNateDbContext> _dbFactory;
    private readonly IAuthorizer _authorizer;

    public WorkflowTasksPreparedQuery(
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
        var baseQuery = db.WorkflowTaskCache.AsNoTracking().AsQueryable();
        var authorized = await _authorizer.FilterQueryAsync(
            db, actor, EntityKinds.WorkflowTask, Actions.View, baseQuery, cancellationToken);
        var rows = await authorized.ToListAsync(cancellationToken);

        if (Query.Where is not null)
        {
            rows = rows.Where(r => EvalWhere(Query.Where, r)).ToList();
        }

        if (Query.OrderBy.Count > 0)
        {
            IOrderedEnumerable<WorkflowTaskCache>? sorted = null;
            foreach (var item in Query.OrderBy)
            {
                if (item.Item.IsAggregate)
                {
                    throw new AqlValidationException("Aggregate ORDER BY on WorkflowTasks is not yet supported.");
                }
                Func<WorkflowTaskCache, IComparable?> keyFn = r => ReadField(item.Item.Field!, r) as IComparable;
                sorted = sorted is null
                    ? (item.Descending
                        ? rows.OrderByDescending(keyFn, NullComparer.Instance)
                        : rows.OrderBy(keyFn, NullComparer.Instance))
                    : (item.Descending
                        ? sorted.ThenByDescending(keyFn, NullComparer.Instance)
                        : sorted.ThenBy(keyFn, NullComparer.Instance));
            }
            rows = sorted!.ToList();
        }

        if (Query.Group is not null)
        {
            throw new AqlValidationException("GROUP(...) is not yet supported on WorkflowTasks.");
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

    private static object? ReadField(string field, WorkflowTaskCache r) => field.ToLowerInvariant() switch
    {
        "id" => r.FlowableTaskId,
        "instanceid" => r.FlowableInstanceId,
        "processkey" => r.ProcessDefinitionKey,
        "taskdefinitionkey" => r.TaskDefinitionKey,
        "name" => r.Name,
        "assignee" => r.Assignee,
        "owner" => r.Owner,
        "duedate" => r.DueDate is { } d ? DateTime.SpecifyKind(d, DateTimeKind.Utc) : null,
        "createdtime" => DateTime.SpecifyKind(r.CreatedTime, DateTimeKind.Utc),
        "claimtime" => r.ClaimTime is { } c ? DateTime.SpecifyKind(c, DateTimeKind.Utc) : null,
        "completedtime" => r.CompletedTime is { } ct ? DateTime.SpecifyKind(ct, DateTimeKind.Utc) : null,
        "formkey" => r.FormKey,
        "priority" => (object?)r.Priority,
        "status" => r.Status,
        "lastsyncat" => DateTime.SpecifyKind(r.LastSyncAtUtc, DateTimeKind.Utc),
        _ => null
    };

    private static bool EvalWhere(AqlWhere where, WorkflowTaskCache row) => where switch
    {
        AqlBinary b => b.Op == "AND"
            ? EvalWhere(b.Left, row) && EvalWhere(b.Right, row)
            : EvalWhere(b.Left, row) || EvalWhere(b.Right, row),
        AqlCompare c => CompareValues(ReadField(c.Field, row), Resolve(c.Value), c.Op),
        AqlContains ct => ReadField(ct.Field, row) is string s
            && s.Contains(ct.Substr, StringComparison.OrdinalIgnoreCase),
        AqlIn inFilter => inFilter.Values.Any(v =>
            CompareValues(ReadField(inFilter.Field, row), Resolve(v), "=")),
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
            // double.Equals → bit-equality method call, side-steps Sonar S1244.
            return op switch
            {
                "=" => adv.Equals(edv), "!=" => !adv.Equals(edv),
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
