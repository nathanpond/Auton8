using System.Diagnostics;
using System.Security.Claims;
using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.Evaluator;
using AutoNate.Web.Persistence;
using AutoNate.Web.Persistence.Scaffolded;
using Microsoft.EntityFrameworkCore;

namespace AutoNate.Web.Services.Query.Entities;

// User-facing AQL view of workflow_execution_cache. The sibling
// WorkflowExecutionsQueryEntity exposes the same table with raw cache
// column names ("Status" = "active") for debugging; Flows surfaces
// human-friendly labels ("Status" = "In-progress"), a "FlowName" joined
// from workflow_models, the CURRENTSTEP() row function for the active
// task on each flow, and full GROUP BY + aggregate support so dashboard
// widgets can render "flows by status" counts in one query.
//
// Both entities share the same EntityKinds.WorkflowExecution auth path
// via FilterQueryAsync — selector grants like [startedby=$me] or
// [processkey=approval] apply uniformly.
public sealed class FlowsQueryEntity : IQueryEntity
{
    private readonly IDbContextFactory<AutoNateDbContext> _dbFactory;
    private readonly IAuthorizer _authorizer;

    public FlowsQueryEntity(
        IDbContextFactory<AutoNateDbContext> dbFactory,
        IAuthorizer authorizer)
    {
        _dbFactory = dbFactory;
        _authorizer = authorizer;
    }

    public string Name => "Flows";

    public IReadOnlyList<QueryColumn> StaticSchema { get; } = new[]
    {
        new QueryColumn("Id",           QueryDataType.String, false, true),
        new QueryColumn("FlowName",     QueryDataType.String, false, true),
        new QueryColumn("ProcessKey",   QueryDataType.String, false, true),
        new QueryColumn("ProcessVersion", QueryDataType.Number, true,  true),
        new QueryColumn("BusinessKey",  QueryDataType.String, false, true),
        new QueryColumn("Tenant",       QueryDataType.String, false, true),
        new QueryColumn("Status",       QueryDataType.String, false, true),
        new QueryColumn("StartDate",    QueryDataType.Date,   true,  true),
        new QueryColumn("EndDate",      QueryDataType.Date,   true,  true),
        new QueryColumn("DurationMs",   QueryDataType.Number, true,  true),
        new QueryColumn("StartedBy",    QueryDataType.String, false, true),
    };

    public IReadOnlyList<string> AllowedFunctions { get; } = new[]
    {
        "CURRENTSTEP"
    };

    public IReadOnlyList<string> RowFunctions { get; } = new[]
    {
        "CURRENTSTEP"
    };

    // The set of display labels FlowsPreparedQuery.StatusDisplay maps to.
    // Keep in sync with that switch — the autocomplete surfaces these as
    // suggestions after `Status = `, and NormalizeStatusInput accepts every
    // common spelling and folds back to this set.
    private static readonly IReadOnlyList<string> StatusDisplayLabels = new[]
    {
        "In-progress", "Completed", "Cancelled", "Suspended", "Terminated", "Errored"
    };

    public IReadOnlyDictionary<string, IReadOnlyList<string>> ColumnEnums { get; } =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Status"] = StatusDisplayLabels
        };

    public QueryDataType RowFunctionDataType(string functionName) =>
        functionName.ToUpperInvariant() switch
        {
            // CURRENTSTEP(<arg>) — arg is one of {Name, Assignee, ActivityId,
            // TaskId, DueDate, CreatedTime}. All but DueDate / CreatedTime
            // return strings, which is the safe default for the validator.
            "CURRENTSTEP" => QueryDataType.String,
            _ => QueryDataType.String
        };

    // CURRENTSTEP requires an arg (Name / Assignee / etc.) — without one
    // there's nothing to return. The validator uses this to allow
    // CURRENTSTEP(Name) through where a parameterless COUNTCHILDREN() shape
    // would have been rejected.
    public bool RowFunctionAcceptsArgument(string functionName) =>
        string.Equals(functionName, "CURRENTSTEP", StringComparison.OrdinalIgnoreCase);

    // Legal argument tokens for CURRENTSTEP — matches the switch in
    // EvalRowFunction below. Keep these two in sync; the autocomplete and
    // help modal read this list verbatim.
    private static readonly IReadOnlyList<string> CurrentStepArguments = new[]
    {
        "Name", "Assignee", "ActivityId", "TaskId", "DueDate", "CreatedTime", "Priority"
    };

    public IReadOnlyList<string> RowFunctionArguments(string functionName) =>
        string.Equals(functionName, "CURRENTSTEP", StringComparison.OrdinalIgnoreCase)
            ? CurrentStepArguments
            : Array.Empty<string>();

    // Verified shapes the chatbot can copy directly. Each one parses and
    // validates against this entity's schema. Keep these idiomatic — the
    // model uses them as a "what does correct AQL look like for Flows?"
    // template, so prefer the natural phrasing over showing every operator.
    public IReadOnlyList<QueryExample> Examples { get; } = new[]
    {
        new QueryExample(
            "Workflows started in the past two weeks, newest first",
            "FROM Flows WHERE StartDate >= -2w ORDER BY StartDate DESC"),
        new QueryExample(
            "Workflows started in a date window using BETWEEN",
            "FROM Flows WHERE BETWEEN(StartDate, 2w ago, NOW) ORDER BY StartDate DESC"),
        new QueryExample(
            "Active (in-progress) workflows",
            "FROM Flows WHERE Status = \"In-progress\" ORDER BY StartDate DESC"),
        new QueryExample(
            "Workflows currently in an error state",
            "FROM Flows WHERE Status = \"Errored\" ORDER BY StartDate DESC"),
        new QueryExample(
            "Top 10 longest-running completed workflows",
            "FROM Flows WHERE Status = \"Completed\" ORDER BY DurationMs DESC LIMIT 10"),
        new QueryExample(
            "Counts grouped by status",
            "FROM Flows COLUMNS(Status, COUNT() AS Total) GROUP(Status) ORDER BY Total DESC"),
        new QueryExample(
            "In-progress workflows with their current step and assignee",
            "FROM Flows WHERE Status = \"In-progress\" COLUMNS(Id, FlowName, CURRENTSTEP(Name) AS Step, CURRENTSTEP(Assignee) AS Assignee) ORDER BY StartDate DESC")
    };

    public Task<IPreparedQuery> PrepareAsync(AqlQuery query, CancellationToken cancellationToken)
    {
        IPreparedQuery prepared = new FlowsPreparedQuery(
            this, query, StaticSchema, _dbFactory, _authorizer);
        return Task.FromResult(prepared);
    }
}

internal sealed class FlowsPreparedQuery : IPreparedQuery
{
    private readonly IDbContextFactory<AutoNateDbContext> _dbFactory;
    private readonly IAuthorizer _authorizer;

    public FlowsPreparedQuery(
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

        // 1. Auth-filter the cache via the existing selector compiler.
        var baseQuery = db.WorkflowExecutionCache.AsNoTracking().AsQueryable();
        var authorized = await _authorizer.FilterQueryAsync(
            db, actor, EntityKinds.WorkflowExecution, Actions.View, baseQuery, cancellationToken);
        var cacheRows = await authorized.ToListAsync(cancellationToken);

        if (cacheRows.Count == 0)
        {
            return EmptyResult(sw);
        }

        // 2. Resolve FlowName via a single bulk JOIN to workflow_models.
        //    Process keys missing from workflow_models fall back to the key
        //    itself so the column is never null.
        var processKeys = cacheRows
            .Select(r => r.ProcessDefinitionKey)
            .Where(k => !string.IsNullOrEmpty(k))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var nameByKey = processKeys.Count == 0
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : await db.WorkflowModels.AsNoTracking()
                .Where(m => processKeys.Contains(m.ProcessKey))
                .ToDictionaryAsync(m => m.ProcessKey, m => m.Name, StringComparer.Ordinal, cancellationToken);

        // 3. If CURRENTSTEP() is referenced anywhere, bulk-load the current
        //    open task per instance from workflow_task_cache. One task per
        //    instance — we pick the oldest non-completed one (matches the
        //    "first available step" semantics most dashboards expect).
        var needsCurrentStep = QueryReferencesFunction("CURRENTSTEP");
        Dictionary<string, WorkflowTaskCache> currentByInstance =
            needsCurrentStep
                ? await LoadCurrentStepsAsync(db, cacheRows, cancellationToken)
                : new Dictionary<string, WorkflowTaskCache>(StringComparer.Ordinal);

        // 4. Bulk-load instance IDs with any workflow_execution_errors rows
        //    so Status can promote to "Errored". Precedence mirrors
        //    ExecutionEndpoints: Cancelled wins over Errored (operator
        //    intent supersedes a stale failure); Errored wins over
        //    Running/Complete (a process with a failed job is no longer
        //    healthy even if Flowable still flags it Running).
        var instanceIds = cacheRows.Select(c => c.FlowableInstanceId).ToList();
        var erroredSet = instanceIds.Count == 0
            ? new HashSet<string>(StringComparer.Ordinal)
            : new HashSet<string>(
                await db.WorkflowExecutionErrors.AsNoTracking()
                    .Where(e => instanceIds.Contains(e.ProcessInstanceId))
                    .Select(e => e.ProcessInstanceId)
                    .Distinct()
                    .ToListAsync(cancellationToken),
                StringComparer.Ordinal);

        // 5. Resolve assignee Guids → "FirstName LastName (username)" via
        //    a single bulk lookup against local_users. The Flowable
        //    assignee field is a Guid string per AutoNate's auth-startup
        //    convention; non-Guid values pass through unchanged so
        //    externally-provisioned assignees aren't lost.
        var assigneeDisplayByRawId = await ResolveAssigneeDisplayNamesAsync(
            db, currentByInstance.Values, cancellationToken);

        // 6. Build FlowRow projection objects so the WHERE/ORDER BY/GROUP
        //    pipeline operates on a stable shape.
        var rows = cacheRows
            .Select(c => new FlowRow(
                c,
                nameByKey.GetValueOrDefault(c.ProcessDefinitionKey) ?? c.ProcessDefinitionKey,
                currentByInstance.GetValueOrDefault(c.FlowableInstanceId),
                Errored: erroredSet.Contains(c.FlowableInstanceId),
                AssigneeDisplayByRawId: assigneeDisplayByRawId))
            .ToList();

        // 5. WHERE.
        if (Query.Where is not null)
        {
            rows = rows.Where(r => EvalWhere(Query.Where, r)).ToList();
        }

        // 6. GROUP path (with aggregates) vs row path.
        var projection = ResolveProjection();
        var truncated = false;
        var effectiveCap = Query.Limit ?? hardCap;
        List<IReadOnlyDictionary<string, object?>> resultRows;

        if (Query.Group is { Count: > 0 })
        {
            (resultRows, truncated) = ExecuteGrouped(rows, projection, effectiveCap);
        }
        else
        {
            (resultRows, truncated) = ExecuteUngrouped(rows, projection, effectiveCap);
        }

        return new QueryResult(
            Columns: projection.Select(p => new QueryColumnMeta(p.DisplayName, p.DataType)).ToList(),
            Rows: resultRows,
            TotalCount: resultRows.Count + (truncated ? 1 : 0),
            Truncated: truncated,
            DurationMs: sw.ElapsedMilliseconds);
    }

    private static QueryResult EmptyResult(Stopwatch sw) =>
        new(Columns: Array.Empty<QueryColumnMeta>(),
            Rows: Array.Empty<IReadOnlyDictionary<string, object?>>(),
            TotalCount: 0,
            Truncated: false,
            DurationMs: sw.ElapsedMilliseconds);

    // ---- Pre-load helpers ------------------------------------------------

    private static async Task<Dictionary<string, WorkflowTaskCache>> LoadCurrentStepsAsync(
        AutoNateDbContext db,
        List<WorkflowExecutionCache> cacheRows,
        CancellationToken ct)
    {
        var instanceIds = cacheRows.Select(c => c.FlowableInstanceId).ToList();
        var openTasks = await db.WorkflowTaskCache.AsNoTracking()
            .Where(t => instanceIds.Contains(t.FlowableInstanceId)
                     && t.Status == "active"
                     && t.CompletedTime == null)
            .OrderBy(t => t.CreatedTime)
            .ToListAsync(ct);

        // First (oldest) open task per instance — multiple open tasks (e.g.
        // parallel gateway branches) all resolve to the earliest one. If
        // the dashboard ever needs all branches, CURRENTSTEPS() (plural,
        // returning a string array) would be a follow-on enhancement.
        var byInstance = new Dictionary<string, WorkflowTaskCache>(StringComparer.Ordinal);
        foreach (var task in openTasks)
        {
            byInstance.TryAdd(task.FlowableInstanceId, task);
        }
        return byInstance;
    }

    // Bulk-resolves Flowable assignee strings → "FirstName LastName (username)".
    // The Flowable assignee field is a Guid string per AutoNate convention,
    // so we filter out non-Guid values (preserved as-is at lookup time) and
    // hit local_users in a single bulk query. Returns a dictionary keyed by
    // the raw assignee string so CURRENTSTEP(Assignee)'s switch can do a
    // straight TryGetValue without re-parsing the Guid per row.
    private static async Task<IReadOnlyDictionary<string, string>> ResolveAssigneeDisplayNamesAsync(
        AutoNateDbContext db,
        IEnumerable<WorkflowTaskCache> tasks,
        CancellationToken ct)
    {
        var assigneeStrings = tasks
            .Select(t => t.Assignee)
            .Where(a => !string.IsNullOrEmpty(a))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (assigneeStrings.Count == 0)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        var guidByRaw = new Dictionary<string, Guid>(StringComparer.Ordinal);
        foreach (var raw in assigneeStrings)
        {
            if (Guid.TryParse(raw, out var g) && g != Guid.Empty)
            {
                guidByRaw[raw!] = g;
            }
        }
        if (guidByRaw.Count == 0)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        var guidList = guidByRaw.Values.Distinct().ToList();
        var users = await db.LocalUsers.AsNoTracking()
            .Where(u => guidList.Contains(u.UserId))
            .Select(u => new { u.UserId, u.FirstName, u.LastName, u.Username })
            .ToListAsync(ct);
        var displayByUserId = users.ToDictionary(
            u => u.UserId,
            u => FormatAssigneeDisplay(u.FirstName, u.LastName, u.Username));

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (raw, userId) in guidByRaw)
        {
            if (displayByUserId.TryGetValue(userId, out var display))
            {
                result[raw] = display;
            }
        }
        return result;
    }

    // "FirstName LastName (username)" — full name with username in parens.
    // Falls back gracefully when name parts are missing: a user with only
    // a first name renders "First (username)"; missing both renders just
    // the username (with no parens, since the bare username carries the
    // same info).
    private static string FormatAssigneeDisplay(string? firstName, string? lastName, string username)
    {
        var fn = (firstName ?? string.Empty).Trim();
        var ln = (lastName ?? string.Empty).Trim();
        var fullName = (fn, ln) switch
        {
            ("", "") => string.Empty,
            ("", _) => ln,
            (_, "") => fn,
            _ => $"{fn} {ln}"
        };
        return fullName.Length == 0 ? username : $"{fullName} ({username})";
    }

    private bool QueryReferencesFunction(string functionName)
    {
        bool MatchesAggregate(AqlSelectItem item) =>
            item.IsAggregate
            && string.Equals(item.AggregateFn, functionName, StringComparison.OrdinalIgnoreCase);

        if (Query.Columns is not null && Query.Columns.Any(MatchesAggregate))
        {
            return true;
        }
        if (Query.OrderBy.Any(o => MatchesAggregate(o.Item)))
        {
            return true;
        }
        return false;
    }

    // ---- Row + projection types ------------------------------------------

    private sealed record FlowRow(
        WorkflowExecutionCache Cache,
        string FlowName,
        WorkflowTaskCache? CurrentTask,
        bool Errored,
        // Shared across every row in the query so CURRENTSTEP(Assignee) can
        // dereference the raw Flowable assignee string (a Guid in the
        // typical case) into "FirstName LastName (username)" without a
        // per-row DB hit. Keys are raw assignee strings; values are the
        // formatted display name (or the original key if no LocalUser row
        // matched, e.g. externally-provisioned assignees).
        IReadOnlyDictionary<string, string> AssigneeDisplayByRawId);

    private sealed record ProjItem(string DisplayName, QueryDataType DataType, AqlSelectItem Source);

    private IReadOnlyList<ProjItem> ResolveProjection()
    {
        if (Query.Columns is null)
        {
            return Schema.Select(c => new ProjItem(
                c.Name, c.DataType, new AqlSelectItem(c.Name, null, null))).ToList();
        }
        return Query.Columns.Select(SelectItemToProjection).ToList();
    }

    private ProjItem SelectItemToProjection(AqlSelectItem item)
    {
        if (item.IsAggregate)
        {
            var fn = item.AggregateFn!.ToUpperInvariant();
            // Row functions (CURRENTSTEP) — declared in entity's RowFunctions
            // and the validator already let them through without GROUP.
            if (Entity.RowFunctions.Any(f => string.Equals(f, fn, StringComparison.OrdinalIgnoreCase)))
            {
                return new ProjItem(item.DisplayName, Entity.RowFunctionDataType(fn), item);
            }
            // Standard aggregates — validator already enforced GROUP-required.
            if (fn == "COUNT")
            {
                return new ProjItem(item.DisplayName, QueryDataType.Number, item);
            }
            var aggCol = Schema.First(c =>
                string.Equals(c.Name, item.AggregateField, StringComparison.OrdinalIgnoreCase));
            return new ProjItem(item.DisplayName, aggCol.DataType, item);
        }
        var col = Schema.First(c =>
            string.Equals(c.Name, item.Field, StringComparison.OrdinalIgnoreCase));
        return new ProjItem(item.DisplayName, col.DataType, item);
    }

    // ---- Ungrouped path --------------------------------------------------

    private (List<IReadOnlyDictionary<string, object?>> Rows, bool Truncated) ExecuteUngrouped(
        List<FlowRow> rows,
        IReadOnlyList<ProjItem> projection,
        int? effectiveCap)
    {
        if (Query.OrderBy.Count > 0)
        {
            IOrderedEnumerable<FlowRow>? sorted = null;
            foreach (var item in Query.OrderBy)
            {
                if (item.Item.IsAggregate
                    && !Entity.RowFunctions.Any(f =>
                        string.Equals(f, item.Item.AggregateFn, StringComparison.OrdinalIgnoreCase)))
                {
                    throw new AqlValidationException(
                        "Aggregate ORDER BY on Flows requires a GROUP(...) clause.");
                }
                Func<FlowRow, IComparable?> keyFn = r => ProjectionValue(item.Item, r) as IComparable;
                sorted = sorted is null
                    ? (item.Descending
                        ? rows.OrderByDescending(keyFn, NullSafeComparer.Instance)
                        : rows.OrderBy(keyFn, NullSafeComparer.Instance))
                    : (item.Descending
                        ? sorted.ThenByDescending(keyFn, NullSafeComparer.Instance)
                        : sorted.ThenBy(keyFn, NullSafeComparer.Instance));
            }
            rows = sorted!.ToList();
        }

        var truncated = false;
        if (effectiveCap is { } cap && rows.Count > cap)
        {
            truncated = true;
            rows = rows.Take(cap).ToList();
        }

        var resultRows = rows
            .Select(r => (IReadOnlyDictionary<string, object?>)projection.ToDictionary(
                p => p.DisplayName,
                p => ProjectionValue(p.Source, r)))
            .ToList();
        return (resultRows, truncated);
    }

    // ---- Grouped path ----------------------------------------------------

    private sealed record FlowGroup(GroupKey Key, List<FlowRow> Rows);

    private (List<IReadOnlyDictionary<string, object?>> Rows, bool Truncated) ExecuteGrouped(
        List<FlowRow> rows,
        IReadOnlyList<ProjItem> projection,
        int? effectiveCap)
    {
        var groupFields = Query.Group!;
        foreach (var f in groupFields)
        {
            if (!Schema.Any(c => string.Equals(c.Name, f, StringComparison.OrdinalIgnoreCase)))
            {
                throw new AqlValidationException(
                    $"GROUP field '{f}' is not a column on Flows.");
            }
        }

        var groups = rows
            .GroupBy(r => BuildGroupKey(r, groupFields), GroupKey.Comparer)
            .Select(g => new FlowGroup(g.Key, g.ToList()))
            .ToList();

        var projectedGroups = groups
            .Select(g => (Group: g, Dict: BuildGroupProjection(g, projection)))
            .ToList();

        if (Query.OrderBy.Count > 0)
        {
            IOrderedEnumerable<(FlowGroup, Dictionary<string, object?>)>? sorted = null;
            foreach (var item in Query.OrderBy)
            {
                Func<(FlowGroup Group, Dictionary<string, object?> Dict), IComparable?> keyFn =
                    tuple => EvalGroupOrderKey(item.Item, tuple.Group, tuple.Dict) as IComparable;
                sorted = sorted is null
                    ? (item.Descending
                        ? projectedGroups.OrderByDescending(keyFn, NullSafeComparer.Instance)
                        : projectedGroups.OrderBy(keyFn, NullSafeComparer.Instance))
                    : (item.Descending
                        ? sorted.ThenByDescending(keyFn, NullSafeComparer.Instance)
                        : sorted.ThenBy(keyFn, NullSafeComparer.Instance));
            }
            projectedGroups = sorted!.ToList();
        }

        var truncated = false;
        if (effectiveCap is { } cap && projectedGroups.Count > cap)
        {
            truncated = true;
            projectedGroups = projectedGroups.Take(cap).ToList();
        }

        var resultRows = projectedGroups
            .Select(t => (IReadOnlyDictionary<string, object?>)t.Dict)
            .ToList();
        return (resultRows, truncated);
    }

    private static GroupKey BuildGroupKey(FlowRow row, IReadOnlyList<string> groupFields)
    {
        var values = new object?[groupFields.Count];
        for (var i = 0; i < groupFields.Count; i++)
        {
            values[i] = ReadColumn(groupFields[i], row);
        }
        return new GroupKey(values);
    }

    private Dictionary<string, object?> BuildGroupProjection(
        FlowGroup group,
        IReadOnlyList<ProjItem> projection)
    {
        var dict = new Dictionary<string, object?>();
        foreach (var p in projection)
        {
            if (p.Source.IsAggregate
                && !Entity.RowFunctions.Any(f =>
                    string.Equals(f, p.Source.AggregateFn, StringComparison.OrdinalIgnoreCase)))
            {
                dict[p.DisplayName] = EvalAggregateForGroup(p.Source, group.Rows);
            }
            else
            {
                dict[p.DisplayName] = ProjectionValue(p.Source, group.Rows[0]);
            }
        }
        return dict;
    }

    private object? EvalGroupOrderKey(
        AqlSelectItem item,
        FlowGroup group,
        Dictionary<string, object?> projected)
    {
        if (projected.TryGetValue(item.DisplayName, out var v)) return v;
        if (projected.TryGetValue(item.DefaultName, out var v2)) return v2;
        if (item.IsAggregate
            && !Entity.RowFunctions.Any(f =>
                string.Equals(f, item.AggregateFn, StringComparison.OrdinalIgnoreCase)))
        {
            return EvalAggregateForGroup(item, group.Rows);
        }
        return ProjectionValue(item, group.Rows[0]);
    }

    private static object? EvalAggregateForGroup(AqlSelectItem item, List<FlowRow> groupRows)
    {
        var fn = item.AggregateFn!.ToUpperInvariant();
        if (fn == "COUNT")
        {
            if (item.AggregateField is null) return (long)groupRows.Count;
            return (long)groupRows.Count(r => ReadColumn(item.AggregateField, r) is not null);
        }

        var values = groupRows
            .Select(r => ToDoubleOrNull(ReadColumn(item.AggregateField!, r)))
            .Where(v => v.HasValue)
            .Select(v => v!.Value)
            .ToList();
        if (values.Count == 0) return null;
        return fn switch
        {
            "MIN" => (object)values.Min(),
            "MAX" => values.Max(),
            "AVG" => values.Average(),
            "MEDIAN" => Median(values),
            _ => throw new AqlValidationException($"Aggregate '{fn}' is not supported on Flows.")
        };
    }

    private static double Median(List<double> sorted)
    {
        sorted.Sort();
        var n = sorted.Count;
        if (n % 2 == 1) return sorted[n / 2];
        return (sorted[n / 2 - 1] + sorted[n / 2]) / 2.0;
    }

    // ---- Field readers ---------------------------------------------------

    // Common path for both plain field projections and ORDER BY keys.
    // Aggregates short-circuit elsewhere; this is for everything else,
    // including the CURRENTSTEP row function.
    private object? ProjectionValue(AqlSelectItem item, FlowRow row)
    {
        if (item.IsAggregate
            && Entity.RowFunctions.Any(f =>
                string.Equals(f, item.AggregateFn, StringComparison.OrdinalIgnoreCase)))
        {
            return EvalRowFunction(item.AggregateFn!, item.AggregateField, row);
        }
        if (item.IsAggregate)
        {
            // Bare aggregate outside a GROUP context — the ungrouped path
            // refuses this before we get here. Defensive fallback.
            return null;
        }
        return ReadColumn(item.Field!, row);
    }

    private static object? ReadColumn(string field, FlowRow row) => field.ToLowerInvariant() switch
    {
        "id" => row.Cache.FlowableInstanceId,
        "flowname" => row.FlowName,
        "processkey" => row.Cache.ProcessDefinitionKey,
        "processversion" => (object?)row.Cache.ProcessDefinitionVersion,
        "businesskey" => row.Cache.BusinessKey,
        "tenant" => row.Cache.TenantId,
        "status" => StatusDisplay(row.Cache.Status, row.Errored),
        "startdate" => DateTime.SpecifyKind(row.Cache.StartTime, DateTimeKind.Utc),
        "enddate" => row.Cache.EndTime is { } e ? DateTime.SpecifyKind(e, DateTimeKind.Utc) : null,
        "durationms" => (object?)row.Cache.DurationMs,
        "startedby" => row.Cache.StartedBy,
        _ => null
    };

    private static object? EvalRowFunction(string fn, string? arg, FlowRow row)
    {
        if (!string.Equals(fn, "CURRENTSTEP", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        var task = row.CurrentTask;
        if (task is null) return null;
        var which = (arg ?? "name").ToLowerInvariant();
        return which switch
        {
            "name" => task.Name,
            // Assignee is bulk-resolved at execute time so this is a dict
            // lookup, not a per-row DB call. Falls back to the raw string
            // when no LocalUser row matched (e.g. externally-provisioned
            // assignees where the id isn't a Guid we recognize).
            "assignee" => task.Assignee is { } a && row.AssigneeDisplayByRawId.TryGetValue(a, out var display)
                ? display
                : task.Assignee,
            "activityid" => task.TaskDefinitionKey,
            "taskid" => task.FlowableTaskId,
            "duedate" => task.DueDate is { } d ? DateTime.SpecifyKind(d, DateTimeKind.Utc) : null,
            "createdtime" => DateTime.SpecifyKind(task.CreatedTime, DateTimeKind.Utc),
            "priority" => (object?)task.Priority,
            _ => null
        };
    }

    // ---- WHERE evaluator -------------------------------------------------

    private static bool EvalWhere(AqlWhere where, FlowRow row) => where switch
    {
        AqlBinary b => b.Op == "AND"
            ? EvalWhere(b.Left, row) && EvalWhere(b.Right, row)
            : EvalWhere(b.Left, row) || EvalWhere(b.Right, row),
        AqlCompare c => EvalCompare(c.Field, c.Op, c.Value, row),
        AqlContains ct => ReadColumn(ct.Field, row) is string s
            && s.Contains(ct.Substr, StringComparison.OrdinalIgnoreCase),
        AqlIn inFilter => inFilter.Values.Any(v =>
            EvalCompare(inFilter.Field, "=", v, row)),
        AqlBetween bw => EvalCompare(bw.Field, ">=", bw.Lo, row)
                     && EvalCompare(bw.Field, "<=", bw.Hi, row),
        _ => false
    };

    private static bool EvalCompare(string field, string op, AqlValue value, FlowRow row)
    {
        var actual = ReadColumn(field, row);
        var expected = ResolveValue(value);

        // Status takes either the display form ("In-progress") or any of
        // its raw-cache synonyms ("active", "running"). NormalizeStatusInput
        // re-maps the incoming literal into the display form so equality
        // works regardless of which the user typed.
        if (string.Equals(field, "Status", StringComparison.OrdinalIgnoreCase)
            && expected is string s)
        {
            expected = NormalizeStatusInput(s);
        }

        return CompareValues(actual, expected, op);
    }

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
        double? ad = ToDoubleOrNull(actual); double? ed = ToDoubleOrNull(expected);
        if (ad is { } adv && ed is { } edv)
        {
            // double.Equals is bit-equality (method call, not the == operator),
            // which gives the right semantics for the int/long values that
            // dominate cache columns and side-steps Sonar S1244.
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

    private static double? ToDoubleOrNull(object? v) => v switch
    {
        int i => i, long l => l, double d => d, float f => f, decimal dec => (double)dec,
        _ => null
    };

    // ---- Status normalization --------------------------------------------

    // Cache stores normalized lowercase ("active", "completed", ...). The
    // Flows entity surfaces a display label; this maps cache → display
    // with the same precedence the executions list endpoint applies:
    // Cancelled > Errored > base. "Errored" is a derived overlay — if any
    // workflow_execution_errors row exists for the instance and the
    // process hasn't been operator-cancelled, the display flips to
    // "Errored" regardless of whether Flowable still says "active".
    private static string StatusDisplay(string raw, bool errored)
    {
        var baseLabel = raw.ToLowerInvariant() switch
        {
            "active" or "running" or "in-progress" => "In-progress",
            "completed" or "complete" => "Completed",
            "cancelled" or "canceled" => "Cancelled",
            "suspended" => "Suspended",
            "terminated" => "Terminated",
            _ => raw
        };
        if (string.Equals(baseLabel, "Cancelled", StringComparison.Ordinal))
        {
            return "Cancelled";
        }
        return errored ? "Errored" : baseLabel;
    }

    // The WHERE side: accept any common spelling and re-map to the display
    // form so equality compares apples-to-apples regardless of which form
    // the user typed.
    private static string NormalizeStatusInput(string raw) => raw.Trim().ToLowerInvariant() switch
    {
        "active" or "running" or "in-progress" or "inprogress" => "In-progress",
        "completed" or "complete" or "done" or "finished" => "Completed",
        "cancelled" or "canceled" => "Cancelled",
        "suspended" or "paused" => "Suspended",
        "terminated" => "Terminated",
        "errored" or "error" or "failed" => "Errored",
        _ => raw  // fall through unchanged — comparison will likely fail, which is the right signal
    };

    // ---- Helpers ---------------------------------------------------------

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

    // Local copy of the GroupKey pattern used by NotesQueryEntity. Kept
    // private to this file so changing one entity's grouping semantics
    // doesn't subtly affect another.
    private sealed class GroupKey : IEquatable<GroupKey>
    {
        public static readonly IEqualityComparer<GroupKey> Comparer = new KeyComparer();
        private readonly object?[] _values;
        public GroupKey(object?[] values) { _values = values; }

        public bool Equals(GroupKey? other)
        {
            if (other is null || other._values.Length != _values.Length) return false;
            for (var i = 0; i < _values.Length; i++)
            {
                if (!ValueEquals(_values[i], other._values[i])) return false;
            }
            return true;
        }

        public override bool Equals(object? obj) => obj is GroupKey gk && Equals(gk);

        public override int GetHashCode()
        {
            var hash = new HashCode();
            foreach (var v in _values)
            {
                hash.Add(v switch
                {
                    null => 0,
                    string s => StringComparer.OrdinalIgnoreCase.GetHashCode(s),
                    _ => v.GetHashCode()
                });
            }
            return hash.ToHashCode();
        }

        private static bool ValueEquals(object? a, object? b)
        {
            if (a is null && b is null) return true;
            if (a is null || b is null) return false;
            if (a is string sa && b is string sb)
            {
                return string.Equals(sa, sb, StringComparison.OrdinalIgnoreCase);
            }
            return a.Equals(b);
        }

        private sealed class KeyComparer : IEqualityComparer<GroupKey>
        {
            public bool Equals(GroupKey? x, GroupKey? y) =>
                ReferenceEquals(x, y) || (x is not null && x.Equals(y));
            public int GetHashCode(GroupKey obj) => obj.GetHashCode();
        }
    }
}
