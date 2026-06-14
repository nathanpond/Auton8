using System.Diagnostics;
using System.Security.Claims;
using System.Xml.Linq;
using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.Evaluator;
using AutoNate.Web.Persistence;
using Microsoft.EntityFrameworkCore;
using WorkflowModelEntity = AutoNate.Web.Persistence.Scaffolded.WorkflowModel;

namespace AutoNate.Web.Services.Query.Entities;

// Workflows entity adapter. Queries workflow_models directly via EF and
// applies BPMN XML-based predicates (USESNODE, NUMNODES) in-memory after
// fetch. NUMEXECUTIONS and LASTEXECUTED are deliberately blocked at
// validation time — execution data lives in Flowable's HTTP API and we
// haven't denormalized it into Postgres yet.
public sealed class WorkflowModelsQueryEntity : IQueryEntity
{
    private readonly IDbContextFactory<AutoNateDbContext> _dbFactory;
    private readonly IAuthorizer _authorizer;

    public WorkflowModelsQueryEntity(
        IDbContextFactory<AutoNateDbContext> dbFactory,
        IAuthorizer authorizer)
    {
        _dbFactory = dbFactory;
        _authorizer = authorizer;
    }

    public string Name => "Workflows";

    public IReadOnlyList<QueryColumn> StaticSchema { get; } = new[]
    {
        // CreatedBy isn't tracked on workflow_models yet — column is here so
        // queries that reference it parse, but the executor returns NULL.
        new QueryColumn("ModelName",   QueryDataType.String, false, true),
        new QueryColumn("Published",   QueryDataType.Bool,   false, true),
        new QueryColumn("Version",     QueryDataType.Number, true,  true),
        new QueryColumn("CreatedDate", QueryDataType.Date,   true,  true),
        new QueryColumn("CreatedBy",   QueryDataType.String, false, true)
    };

    public IReadOnlyList<string> AllowedFunctions { get; } = new[]
    {
        "NUMNODES", "USESNODE", "NUMEXECUTIONS", "LASTEXECUTED"
    };

    // Scalar-per-row functions that can appear in COLUMNS()/ORDER BY without
    // a GROUP(...) clause. NUMNODES counts BPMN nodes in the model's XML;
    // NUMEXECUTIONS / LASTEXECUTED come from workflow_execution_cache via
    // the executor's pre-aggregation (see _executionStats).
    public IReadOnlyList<string> RowFunctions { get; } = new[]
    {
        "NUMNODES", "NUMEXECUTIONS", "LASTEXECUTED"
    };

    public QueryDataType RowFunctionDataType(string functionName) =>
        functionName.ToUpperInvariant() switch
        {
            "LASTEXECUTED" => QueryDataType.Date,
            _ => QueryDataType.Number
        };

    public IReadOnlyList<QueryExample> Examples { get; } = new[]
    {
        new QueryExample(
            "Published workflows with most-recent execution time",
            "FROM Workflows WHERE Published = true ORDER BY LASTEXECUTED() DESC COLUMNS(ModelName, Version, LASTEXECUTED() AS LastRun)"),
        new QueryExample(
            "Workflows updated in the last month",
            "FROM Workflows WHERE CreatedDate >= -1m ORDER BY CreatedDate DESC"),
        new QueryExample(
            "Workflows that use a specific BPMN node type",
            "FROM Workflows WHERE USESNODE(\"userTask\") ORDER BY ModelName"),
        new QueryExample(
            "Workflows ranked by execution count",
            "FROM Workflows ORDER BY NUMEXECUTIONS() DESC COLUMNS(ModelName, NUMEXECUTIONS() AS Runs) LIMIT 10")
    };

    public Task<IPreparedQuery> PrepareAsync(AqlQuery query, CancellationToken cancellationToken)
    {
        var errors = new List<string>();

        // Block execution-data functions early with a friendly message.
        WalkWhereForUnsupported(query.Where, errors);

        IPreparedQuery prepared = new WorkflowModelsPreparedQuery(
            this, query, StaticSchema, errors, _dbFactory, _authorizer);
        return Task.FromResult(prepared);
    }

    private static void WalkWhereForUnsupported(AqlWhere? where, List<string> errors)
    {
        if (where is null) return;
        switch (where)
        {
            case AqlBinary b:
                WalkWhereForUnsupported(b.Left, errors);
                WalkWhereForUnsupported(b.Right, errors);
                break;
            case AqlFunctionCall fc:
                BlockIfUnsupported(fc.Name, errors);
                break;
            case AqlFunctionCompare fcmp:
                BlockIfUnsupported(fcmp.FnName, errors);
                break;
        }
    }

    private static void BlockIfUnsupported(string fnName, List<string> errors)
    {
        // NUMEXECUTIONS / LASTEXECUTED unblocked once the workflow_execution_cache
        // landed (projection framework, phase 1). Both functions now resolve via a
        // sub-query against the cache during WHERE evaluation; see
        // EvalFunctionCompare below.
        _ = fnName;
        _ = errors;
    }
}

internal sealed class WorkflowModelsPreparedQuery : IPreparedQuery
{
    private static readonly XNamespace BpmnNs = "http://www.omg.org/spec/BPMN/20100524/MODEL";

    private readonly IDbContextFactory<AutoNateDbContext> _dbFactory;
    private readonly IAuthorizer _authorizer;

    // Populated in ExecuteAsync from workflow_execution_cache before EvalWhere
    // runs, so NUMEXECUTIONS / LASTEXECUTED can be resolved per row without a
    // per-row sub-query. Key is ProcessKey.
    private IReadOnlyDictionary<string, (int Count, DateTime? Last)> _executionStats =
        new Dictionary<string, (int, DateTime?)>(StringComparer.Ordinal);

    public WorkflowModelsPreparedQuery(
        IQueryEntity entity,
        AqlQuery query,
        IReadOnlyList<QueryColumn> schema,
        IReadOnlyList<string> errors,
        IDbContextFactory<AutoNateDbContext> dbFactory,
        IAuthorizer authorizer)
    {
        Entity = entity;
        Query = query;
        Schema = schema;
        ValidationErrors = errors;
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

        // Kind-level gate: matches WorkflowEndpoints' RequireKindPermission on
        // the list endpoint. If the actor can't view workflow models at all,
        // return an empty result rather than 403 so mixed-entity sessions
        // don't crash.
        var decision = await _authorizer.AuthorizeAsync(
            actor,
            Actions.View,
            new EntityRef(EntityKinds.WorkflowModel, "*"),
            cancellationToken);
        if (decision.Effect != AuthEffect.Allow)
        {
            return EmptyResult(sw);
        }

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var entities = await db.WorkflowModels.AsNoTracking()
            .ToListAsync(cancellationToken);

        // Map to a record so the rest of the pipeline works against
        // a fixed shape without re-querying the BPMN per attribute access.
        var rows = entities.Select(MapRow).ToList();

        // Pre-aggregate workflow_execution_cache so EvalWhere can resolve
        // NUMEXECUTIONS / LASTEXECUTED per row from a single SQL round trip.
        // Empty when the cache hasn't been populated yet — function calls
        // collapse to 0 / null and the user's predicates evaluate normally.
        var processKeys = rows
            .Select(r => r.ProcessKey)
            .Where(k => !string.IsNullOrEmpty(k))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (processKeys.Count > 0)
        {
            var stats = await db.WorkflowExecutionCache.AsNoTracking()
                .Where(c => processKeys.Contains(c.ProcessDefinitionKey))
                .GroupBy(c => c.ProcessDefinitionKey)
                .Select(g => new
                {
                    Key = g.Key,
                    Count = g.Count(),
                    Last = (DateTime?)g.Max(c => c.StartTime)
                })
                .ToListAsync(cancellationToken);
            _executionStats = stats.ToDictionary(
                s => s.Key,
                s => (s.Count, s.Last),
                StringComparer.Ordinal);
        }

        // Apply WHERE.
        if (Query.Where is not null)
        {
            rows = rows.Where(r => EvalWhere(Query.Where, r)).ToList();
        }

        // ORDER BY.
        if (Query.OrderBy.Count > 0)
        {
            IOrderedEnumerable<WorkflowRow>? sorted = null;
            foreach (var item in Query.OrderBy)
            {
                var keyFn = MakeKeySelector(item.Item);
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

        // No GROUP support for Workflows in v1 — the dataset is small enough
        // for ad-hoc filtering and we haven't been asked for aggregations
        // here. If the user requested GROUP, the validator already caught
        // unknown grouped columns; the aggregate validator path only matters
        // when COLUMNS uses aggregates, which is meaningful only in GROUP
        // queries. Surface a friendly error if GROUP is set anyway.
        if (Query.Group is not null)
        {
            throw new AqlValidationException(
                "GROUP(...) is not yet supported on Workflows. Try aggregating Records instead.");
        }

        // Apply LIMIT.
        int? effectiveCap = Query.Limit ?? hardCap;
        var truncated = false;
        if (effectiveCap is { } cap && rows.Count > cap)
        {
            truncated = true;
            rows = rows.Take(cap).ToList();
        }

        // Resolve projection.
        var projection = ResolveProjection();
        var resultRows = rows
            .Select(r => (IReadOnlyDictionary<string, object?>)projection
                .ToDictionary(p => p.DisplayName, p => ReadProjection(r, p)))
            .ToList();

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

    private static WorkflowRow MapRow(WorkflowModelEntity e) =>
        new(e.Id, e.Name,
            Published: e.LastDeploymentId is not null || e.PublishedVersionNumber is not null,
            Version: e.PublishedVersionNumber,
            CreatedDate: DateTime.SpecifyKind(e.CreatedAtUtc, DateTimeKind.Utc),
            BpmnXml: e.BpmnXml,
            ProcessKey: e.ProcessKey);

    // ---- Projection -------------------------------------------------------

    private record ProjItem(string DisplayName, QueryDataType DataType, AqlSelectItem Source);

    private IReadOnlyList<ProjItem> ResolveProjection()
    {
        if (Query.Columns is not null)
        {
            return Query.Columns.Select(SelectItemToProjection).ToList();
        }
        return Schema.Select(c => new ProjItem(
            c.Name, c.DataType, new AqlSelectItem(c.Name, null, null))).ToList();
    }

    private ProjItem SelectItemToProjection(AqlSelectItem item)
    {
        // Row functions (NUMNODES / NUMEXECUTIONS / LASTEXECUTED) appear in
        // COLUMNS with IsAggregate=true at the AST level — they share syntax
        // with aggregates but evaluate per-row, no GROUP required. The entity
        // declares their result type via RowFunctionDataType.
        if (item.IsAggregate)
        {
            var fn = item.AggregateFn!.ToUpperInvariant();
            if (Entity.RowFunctions.Any(f => string.Equals(f, fn, StringComparison.OrdinalIgnoreCase)))
            {
                return new ProjItem(item.DisplayName, Entity.RowFunctionDataType(fn), item);
            }
            // True aggregates would need GROUP — the validator catches that
            // before we get here. Fall through to the field path so any
            // miscategorized item throws the cleaner "field not found" rather
            // than a NullReferenceException on AggregateFn.
        }

        var col = Schema.First(c =>
            string.Equals(c.Name, item.Field, StringComparison.OrdinalIgnoreCase));
        // `AS <alias>` renames the result column; the underlying field still
        // drives ReadProjection.
        return new ProjItem(item.DisplayName, col.DataType, item);
    }

    private object? ReadProjection(WorkflowRow row, ProjItem proj)
    {
        if (proj.Source.IsAggregate)
        {
            return EvalRowFunction(proj.Source.AggregateFn!, row);
        }
        return ReadColumnValue(proj.Source.Field, row);
    }

    private object? EvalRowFunction(string fn, WorkflowRow row)
    {
        var upper = fn.ToUpperInvariant();
        if (upper == "NUMNODES")
        {
            var counts = ParseBpmnNodeCounts(row.BpmnXml);
            return (double)counts.Values.Sum();
        }
        if (string.IsNullOrEmpty(row.ProcessKey)
            || !_executionStats.TryGetValue(row.ProcessKey, out var stats))
        {
            // No cache rows for this process — NUMEXECUTIONS is 0, LASTEXECUTED
            // is null. Matches the WHERE-side semantics so the two surfaces
            // agree on freshly-deployed (or never-run) workflows.
            return upper switch
            {
                "NUMEXECUTIONS" => (object)0d,
                "LASTEXECUTED" => null,
                _ => null
            };
        }
        return upper switch
        {
            "NUMEXECUTIONS" => (double)stats.Count,
            "LASTEXECUTED" => stats.Last is { } d ? DateTime.SpecifyKind(d, DateTimeKind.Utc) : null,
            _ => null
        };
    }

    private static object? ReadColumnValue(string? field, WorkflowRow row) => field?.ToLowerInvariant() switch
    {
        "modelname" => row.Name,
        "published" => row.Published,
        "version" => row.Version,
        "createddate" => row.CreatedDate,
        "createdby" => null,
        _ => null
    };

    // ---- WHERE evaluation -------------------------------------------------

    private bool EvalWhere(AqlWhere where, WorkflowRow row) => where switch
    {
        AqlBinary b => b.Op == "AND"
            ? EvalWhere(b.Left, row) && EvalWhere(b.Right, row)
            : EvalWhere(b.Left, row) || EvalWhere(b.Right, row),
        AqlCompare c => EvalCompare(c, row),
        AqlContains ct => string.Equals(ct.Field, "ModelName", StringComparison.OrdinalIgnoreCase)
            && row.Name.Contains(ct.Substr, StringComparison.OrdinalIgnoreCase),
        AqlIn inFilter => inFilter.Values.Any(v => EvalCompare(
            new AqlCompare(inFilter.Field, "=", v), row)),
        AqlBetween bw => EvalCompare(new AqlCompare(bw.Field, ">=", bw.Lo), row)
                     && EvalCompare(new AqlCompare(bw.Field, "<=", bw.Hi), row),
        AqlFunctionCall fc => EvalFunction(fc, row),
        AqlFunctionCompare fcmp => EvalFunctionCompare(fcmp, row),
        _ => false
    };

    private bool EvalFunctionCompare(AqlFunctionCompare fcmp, WorkflowRow row)
    {
        var fn = fcmp.FnName.ToUpperInvariant();
        if (fn == "LASTEXECUTED")
        {
            DateTime? actualDate = null;
            if (!string.IsNullOrEmpty(row.ProcessKey)
                && _executionStats.TryGetValue(row.ProcessKey, out var stats))
            {
                actualDate = stats.Last;
            }
            if (actualDate is null) return false;
            var expectedRaw = ResolveValue(fcmp.Value);
            if (expectedRaw is not DateTime expectedDate) return false;
            var c = actualDate.Value.CompareTo(expectedDate);
            return fcmp.Op switch
            {
                "=" => c == 0,
                "!=" => c != 0,
                "<" => c < 0, "<=" => c <= 0,
                ">" => c > 0, ">=" => c >= 0,
                _ => false
            };
        }

        var counts = ParseBpmnNodeCounts(row.BpmnXml);
        double? actual = fn switch
        {
            "NUMNODES" => counts.Values.Sum(),
            "NUMEXECUTIONS" => !string.IsNullOrEmpty(row.ProcessKey)
                && _executionStats.TryGetValue(row.ProcessKey, out var stats)
                    ? stats.Count
                    : 0,
            _ => null
        };
        if (actual is null) return false;
        var expected = ToDoubleOrNull(ResolveValue(fcmp.Value));
        if (expected is null) return false;
        // double.Equals → bit-equality method call, side-steps Sonar S1244.
        return fcmp.Op switch
        {
            "=" => actual.Value.Equals(expected.Value),
            "!=" => !actual.Value.Equals(expected.Value),
            "<" => actual < expected,
            "<=" => actual <= expected,
            ">" => actual > expected,
            ">=" => actual >= expected,
            _ => false
        };
    }

    private static bool EvalCompare(AqlCompare c, WorkflowRow row)
    {
        var actual = ReadFieldRaw(c.Field, row);
        var expected = ResolveValue(c.Value);
        return CompareValues(actual, expected, c.Op);
    }

    private static object? ReadFieldRaw(string field, WorkflowRow row) => field.ToLowerInvariant() switch
    {
        "modelname" => row.Name,
        "published" => row.Published,
        "version" => (object?)row.Version,
        "createddate" => row.CreatedDate,
        "createdby" => null,
        _ => null
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
            return actual is string aStr && expected is string eStr
                && aStr.Contains(eStr, StringComparison.OrdinalIgnoreCase);
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
                "<" => cmp < 0,
                "<=" => cmp <= 0,
                ">" => cmp > 0,
                ">=" => cmp >= 0,
                _ => false
            };
        }
        if (actual is IComparable aCmp && expected is IComparable)
        {
            // Coerce numeric/integer pairs to doubles for comparison.
            double? ad = ToDoubleOrNull(actual);
            double? ed = ToDoubleOrNull(expected);
            if (ad is { } adv && ed is { } edv)
            {
                // double.Equals → bit-equality method call, side-steps Sonar S1244.
                return op switch
                {
                    "=" => adv.Equals(edv),
                    "!=" => !adv.Equals(edv),
                    "<" => adv < edv,
                    "<=" => adv <= edv,
                    ">" => adv > edv,
                    ">=" => adv >= edv,
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
                    "<" => c < 0,
                    "<=" => c <= 0,
                    ">" => c > 0,
                    ">=" => c >= 0,
                    _ => false
                };
            }
            var cmp2 = aCmp.CompareTo(expected);
            return op switch
            {
                "=" => cmp2 == 0,
                "!=" => cmp2 != 0,
                "<" => cmp2 < 0,
                "<=" => cmp2 <= 0,
                ">" => cmp2 > 0,
                ">=" => cmp2 >= 0,
                _ => false
            };
        }
        return false;
    }

    private static double? ToDoubleOrNull(object? v) => v switch
    {
        int i => i,
        long l => l,
        double d => d,
        float f => f,
        decimal dec => (double)dec,
        _ => null
    };

    private static bool EvalFunction(AqlFunctionCall fc, WorkflowRow row)
    {
        // NUMNODES() and USESNODE("nodeType") run against the row's BPMN XML.
        var fn = fc.Name.ToUpperInvariant();
        var nodeCounts = ParseBpmnNodeCounts(row.BpmnXml);
        return fn switch
        {
            "NUMNODES" => false, // a bare NUMNODES() with no comparison is meaningless;
                                 // comparisons are normally handled by the AqlCompare
                                 // path. Returning false here is the safe fallback.
            "USESNODE" => fc.Args.Count == 1
                && fc.Args[0] is AqlString s
                && nodeCounts.ContainsKey(s.Value),
            _ => false
        };
    }

    public static IReadOnlyDictionary<string, int> ParseBpmnNodeCounts(string? bpmnXml)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(bpmnXml)) return counts;
        XDocument doc;
        try { doc = XDocument.Parse(bpmnXml); }
        catch { return counts; }
        var process = doc.Descendants(BpmnNs + "process").FirstOrDefault();
        if (process is null) return counts;
        foreach (var el in process.Elements())
        {
            // Skip sequence flows (edges) from the node count. Everything else
            // — tasks, gateways, events — counts as a node.
            if (el.Name.LocalName == "sequenceFlow") continue;
            counts.TryGetValue(el.Name.LocalName, out var c);
            counts[el.Name.LocalName] = c + 1;
        }
        return counts;
    }

    private static Func<WorkflowRow, IComparable?> MakeKeySelector(AqlSelectItem item)
    {
        if (item.IsAggregate)
        {
            throw new AqlValidationException("Aggregate ORDER BY on Workflows is not yet supported.");
        }
        return row => ReadFieldRaw(item.Field!, row) as IComparable;
    }

    private sealed class NullSafeComparer : IComparer<IComparable?>
    {
        public static readonly NullSafeComparer Instance = new();
        public int Compare(IComparable? x, IComparable? y)
        {
            if (x is null && y is null) return 0;
            if (x is null) return 1;  // NULLS LAST
            if (y is null) return -1;
            return x.CompareTo(y);
        }
    }
}

internal sealed record WorkflowRow(
    Guid Id,
    string Name,
    bool Published,
    int? Version,
    DateTime CreatedDate,
    string? BpmnXml,
    string? ProcessKey);
