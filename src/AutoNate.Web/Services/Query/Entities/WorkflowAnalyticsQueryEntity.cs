using System.Diagnostics;
using System.Globalization;
using System.Security.Claims;
using System.Text;
using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.Evaluator;
using AutoNate.Web.Persistence;
using AutoNate.Web.Services.Flowable.Cache.ColdTier;
using Microsoft.EntityFrameworkCore;

namespace AutoNate.Web.Services.Query.Entities;

// Analytical entity that unifies hot workflow_event_log_cache (Postgres) and
// cold Parquet archives via DuckDB. Designed for process-mining and
// dashboard-aggregate queries that span months/years of history — the kind
// of query the hot-only WorkflowHistoryQueryEntity slows down on.
//
// Supports COUNT/AVG/SUM/MIN/MAX aggregates and GROUP BY over allowlisted
// fields (process key, event type, actor, plus Day/Week/Month time
// buckets). The auth model mirrors WorkflowHistory: instance visibility
// comes from filtering workflow_execution_cache; only events for visible
// instances cross over into DuckDB.
public sealed class WorkflowAnalyticsQueryEntity : IQueryEntity
{
    private readonly IDbContextFactory<AutoNateDbContext> _dbFactory;
    private readonly IAuthorizer _authorizer;
    private readonly ColdTierLayout _layout;

    public WorkflowAnalyticsQueryEntity(
        IDbContextFactory<AutoNateDbContext> dbFactory,
        IAuthorizer authorizer,
        ColdTierLayout layout)
    {
        _dbFactory = dbFactory;
        _authorizer = authorizer;
        _layout = layout;
    }

    public string Name => "WorkflowAnalytics";

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
        // Derived time-bucket columns for GROUP BY in time-series queries.
        new QueryColumn("Day",          QueryDataType.Date,   false, true),
        new QueryColumn("Week",         QueryDataType.Date,   false, true),
        new QueryColumn("Month",        QueryDataType.Date,   false, true),
    };

    public IReadOnlyList<string> AllowedFunctions { get; } = Array.Empty<string>();

    public Task<IPreparedQuery> PrepareAsync(AqlQuery query, CancellationToken cancellationToken)
    {
        IPreparedQuery prepared = new WorkflowAnalyticsPreparedQuery(
            this, query, StaticSchema, _dbFactory, _authorizer, _layout);
        return Task.FromResult(prepared);
    }
}

internal sealed class WorkflowAnalyticsPreparedQuery : IPreparedQuery
{
    private static readonly Dictionary<string, string> FieldToSql = new(StringComparer.OrdinalIgnoreCase)
    {
        ["EventId"] = "event_id",
        ["InstanceId"] = "flowable_instance_id",
        ["ProcessKey"] = "process_definition_key",
        ["EventTime"] = "event_time",
        ["EventType"] = "event_type",
        ["ActivityId"] = "activity_id",
        ["ActivityName"] = "activity_name",
        ["ActivityType"] = "activity_type",
        ["TaskId"] = "task_id",
        ["VariableName"] = "variable_name",
        ["Actor"] = "actor",
        ["DurationMs"] = "duration_ms",
        ["Day"] = "date_trunc('day', event_time)",
        ["Week"] = "date_trunc('week', event_time)",
        ["Month"] = "date_trunc('month', event_time)",
    };

    private static readonly HashSet<string> AggregableFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "DurationMs", "EventTime"
    };

    private static readonly HashSet<string> AllowedAggregates = new(StringComparer.OrdinalIgnoreCase)
    {
        "COUNT", "SUM", "AVG", "MIN", "MAX"
    };

    private readonly IDbContextFactory<AutoNateDbContext> _dbFactory;
    private readonly IAuthorizer _authorizer;
    private readonly ColdTierLayout _layout;

    public WorkflowAnalyticsPreparedQuery(
        IQueryEntity entity,
        AqlQuery query,
        IReadOnlyList<QueryColumn> schema,
        IDbContextFactory<AutoNateDbContext> dbFactory,
        IAuthorizer authorizer,
        ColdTierLayout layout)
    {
        Entity = entity;
        Query = query;
        Schema = schema;
        ValidationErrors = Array.Empty<string>();
        _dbFactory = dbFactory;
        _authorizer = authorizer;
        _layout = layout;
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
            return EmptyResult(sw);
        }

        var hotRows = await db.WorkflowEventLogCache.AsNoTracking()
            .Where(e => visibleIds.Contains(e.FlowableInstanceId))
            .ToListAsync(cancellationToken);

        await using var runner = new DuckDbAnalyticsRunner(_layout);
        await runner.LoadHotEventsAsync(hotRows, cancellationToken);
        await runner.RegisterColdAsync(cancellationToken);

        var (sql, parameters, projection) = BuildSql(visibleIds, hardCap);
        var rows = await runner.QueryAsync(sql, parameters, cancellationToken);

        // DuckDB reader returns columns keyed by their `AS "alias"` name,
        // which is the projection's DisplayName. Normalize numeric types
        // (long → double) so the SPA renders aggregates consistently.
        var resultRows = rows
            .Select(r => (IReadOnlyDictionary<string, object?>)projection
                .ToDictionary(
                    p => p.DisplayName,
                    p => r.TryGetValue(p.DisplayName, out var v) ? Normalize(v) : null))
            .ToList();

        return new QueryResult(
            Columns: projection.Select(p => new QueryColumnMeta(p.DisplayName, p.DataType)).ToList(),
            Rows: resultRows,
            TotalCount: resultRows.Count,
            Truncated: false,
            DurationMs: sw.ElapsedMilliseconds);
    }

    private static QueryResult EmptyResult(Stopwatch sw) =>
        new(Columns: Array.Empty<QueryColumnMeta>(),
            Rows: Array.Empty<IReadOnlyDictionary<string, object?>>(),
            TotalCount: 0,
            Truncated: false,
            DurationMs: sw.ElapsedMilliseconds);

    private record ProjectionItem(
        string DisplayName,
        string SqlExpression,
        QueryDataType DataType);

    private (string Sql, IReadOnlyList<(string, object?)> Parameters, IReadOnlyList<ProjectionItem> Projection) BuildSql(
        IReadOnlyList<string> visibleIds, int? hardCap)
    {
        var parameters = new List<(string, object?)>();
        var projection = ResolveProjection();

        var sb = new StringBuilder();
        sb.Append("SELECT ");
        for (var i = 0; i < projection.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append(projection[i].SqlExpression);
            sb.Append(" AS \"");
            sb.Append(projection[i].DisplayName);
            sb.Append('"');
        }

        sb.Append(" FROM (").Append(DuckDbAnalyticsRunner.CombinedViewSql).Append(") AS events");

        // Auth restriction — always present. DuckDB.NET binds positional ?
        // parameters, so the IN list expands inline; each ? gets a row added
        // to `parameters` in order.
        sb.Append(" WHERE flowable_instance_id IN (");
        for (var i = 0; i < visibleIds.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append('?');
            parameters.Add(($"p{parameters.Count + 1}", visibleIds[i]));
        }
        sb.Append(')');

        if (Query.Where is not null)
        {
            sb.Append(" AND (");
            AppendWhere(Query.Where, sb, parameters);
            sb.Append(')');
        }

        if (Query.Group is { Count: > 0 })
        {
            sb.Append(" GROUP BY ");
            for (var i = 0; i < Query.Group.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(RequireFieldSql(Query.Group[i]));
            }
        }

        if (Query.OrderBy.Count > 0)
        {
            sb.Append(" ORDER BY ");
            for (var i = 0; i < Query.OrderBy.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                var item = Query.OrderBy[i].Item;
                sb.Append(item.IsAggregate
                    ? RequireAggregateSql(item)
                    : RequireFieldSql(item.Field!));
                if (Query.OrderBy[i].Descending) sb.Append(" DESC");
            }
        }

        var cap = Query.Limit ?? hardCap;
        if (cap is { } limit)
        {
            sb.Append(" LIMIT ").Append(limit.ToString(CultureInfo.InvariantCulture));
        }

        return (sb.ToString(), parameters, projection);
    }

    private List<ProjectionItem> ResolveProjection()
    {
        if (Query.Columns is null)
        {
            return Schema.Select(c =>
                new ProjectionItem(c.Name, RequireFieldSql(c.Name), c.DataType)).ToList();
        }

        var result = new List<ProjectionItem>(Query.Columns.Count);
        foreach (var col in Query.Columns)
        {
            if (col.IsAggregate)
            {
                result.Add(new ProjectionItem(
                    col.DisplayName,
                    BuildAggregateSql(col.AggregateFn!, col.AggregateField),
                    AggregateDataType(col.AggregateFn!)));
            }
            else
            {
                var sql = RequireFieldSql(col.Field!);
                var schemaCol = Schema.First(s => string.Equals(s.Name, col.Field, StringComparison.OrdinalIgnoreCase));
                result.Add(new ProjectionItem(col.DisplayName, sql, schemaCol.DataType));
            }
        }
        return result;
    }

    private static string BuildAggregateSql(string fn, string? arg)
    {
        var upper = fn.ToUpperInvariant();
        if (!AllowedAggregates.Contains(upper))
        {
            throw new AqlValidationException($"Aggregate '{fn}' is not supported on WorkflowAnalytics.");
        }
        if (string.IsNullOrEmpty(arg))
        {
            return upper + "(*)";
        }
        if (!FieldToSql.TryGetValue(arg, out var fieldSql))
        {
            throw new AqlValidationException($"Aggregate '{fn}({arg})' references an unknown field.");
        }
        if (!AggregableFields.Contains(arg) && !string.Equals(upper, "COUNT", StringComparison.OrdinalIgnoreCase))
        {
            throw new AqlValidationException($"Aggregate '{fn}' is not allowed on '{arg}'.");
        }
        return upper + "(" + fieldSql + ")";
    }

    private static QueryDataType AggregateDataType(string fn) => fn.ToUpperInvariant() switch
    {
        "COUNT" or "SUM" or "AVG" or "MIN" or "MAX" => QueryDataType.Number,
        _ => QueryDataType.Number
    };

    private static string RequireFieldSql(string field) =>
        FieldToSql.TryGetValue(field, out var sql)
            ? sql
            : throw new AqlValidationException($"Unknown WorkflowAnalytics field '{field}'.");

    private static string RequireAggregateSql(AqlSelectItem item) =>
        BuildAggregateSql(item.AggregateFn!, item.AggregateField);

    private static void AppendWhere(AqlWhere where, StringBuilder sb, List<(string, object?)> parameters)
    {
        switch (where)
        {
            case AqlBinary b:
                sb.Append('(');
                AppendWhere(b.Left, sb, parameters);
                sb.Append(") ").Append(b.Op == "AND" ? "AND" : "OR").Append(" (");
                AppendWhere(b.Right, sb, parameters);
                sb.Append(')');
                break;
            case AqlCompare c:
                sb.Append(RequireFieldSql(c.Field));
                sb.Append(' ').Append(NormalizeOp(c.Op)).Append(' ');
                AppendParameter(sb, parameters, ResolveValue(c.Value));
                break;
            case AqlBetween bw:
                sb.Append(RequireFieldSql(bw.Field));
                sb.Append(" BETWEEN ");
                AppendParameter(sb, parameters, ResolveValue(bw.Lo));
                sb.Append(" AND ");
                AppendParameter(sb, parameters, ResolveValue(bw.Hi));
                break;
            case AqlIn inFilter:
                sb.Append(RequireFieldSql(inFilter.Field));
                sb.Append(" IN (");
                for (var i = 0; i < inFilter.Values.Count; i++)
                {
                    if (i > 0) sb.Append(", ");
                    AppendParameter(sb, parameters, ResolveValue(inFilter.Values[i]));
                }
                sb.Append(')');
                break;
            case AqlContains ct:
                sb.Append(RequireFieldSql(ct.Field));
                sb.Append(" ILIKE ");
                AppendParameter(sb, parameters, "%" + EscapeLike(ct.Substr) + "%");
                break;
            default:
                throw new AqlValidationException(
                    $"WHERE clause node {where.GetType().Name} is not supported on WorkflowAnalytics.");
        }
    }

    private static void AppendParameter(StringBuilder sb, List<(string, object?)> parameters, object? value)
    {
        sb.Append('?');
        parameters.Add(($"p{parameters.Count + 1}", value));
    }

    private static string EscapeLike(string raw) =>
        raw.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    private static string NormalizeOp(string op) => op switch
    {
        "=" or "!=" or "<" or "<=" or ">" or ">=" => op,
        "<>" => "!=",
        _ => throw new AqlValidationException($"Comparison operator '{op}' is not supported on WorkflowAnalytics.")
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

    private static object? Normalize(object? value) => value switch
    {
        // DuckDB's COUNT comes back as long; coerce to double so the SPA
        // formats it uniformly with other numeric columns.
        long l => (double)l,
        decimal d => (double)d,
        _ => value
    };
}
