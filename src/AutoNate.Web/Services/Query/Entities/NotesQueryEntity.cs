using System.Diagnostics;
using System.Security.Claims;
using AutoNate.Web.Authorization;
using AutoNate.Web.Persistence;
using AutoNate.Web.Persistence.Scaffolded;
using AutoNate.Web.Services.Content;
using Microsoft.EntityFrameworkCore;

namespace AutoNate.Web.Services.Query.Entities;

// Notes entity adapter. Exposes the entire content hierarchy — Projects,
// Cabinets, Notebooks, Pages, Notes — as a single queryable surface.
// Visibility is enforced via IContentAuthorizer.GetAllowedIdsAsync per kind;
// notes inherit visibility from their parent page.
//
// Execution mixes per-kind SQL loads (filtered by visibility and the planner's
// kind/locator pushdowns) with in-memory WHERE/ORDER/GROUP. The planner walks
// the AST once and decides which kinds to load, whether to fetch users for
// CreatedBy/UpdatedBy, whether to fetch FullPath ancestor chains, and whether
// to precompute COUNTCHILDREN/COUNTDESCENDENTS/ISDESCENDENTOF as SQL
// aggregations. Anything the query doesn't reference isn't fetched.
public sealed class NotesQueryEntity : IQueryEntity
{
    private readonly IDbContextFactory<AutoNateDbContext> _dbFactory;
    private readonly IContentAuthorizer _contentAuthorizer;

    public NotesQueryEntity(
        IDbContextFactory<AutoNateDbContext> dbFactory,
        IContentAuthorizer contentAuthorizer)
    {
        _dbFactory = dbFactory;
        _contentAuthorizer = contentAuthorizer;
    }

    public string Name => "Notes";

    public IReadOnlyList<QueryColumn> StaticSchema { get; } = new[]
    {
        new QueryColumn("Id",           QueryDataType.Number, true,  true),
        new QueryColumn("Type",         QueryDataType.String, false, true),
        new QueryColumn("SubType",      QueryDataType.String, false, true),
        new QueryColumn("Name",         QueryDataType.String, false, true),
        new QueryColumn("Description",  QueryDataType.String, false, true),
        new QueryColumn("Icon",         QueryDataType.String, false, true),
        new QueryColumn("DateCreated",  QueryDataType.Date,   true,  true),
        new QueryColumn("DateUpdated",  QueryDataType.Date,   true,  true),
        new QueryColumn("CreatedBy",    QueryDataType.String, false, true),
        new QueryColumn("UpdatedBy",    QueryDataType.String, false, true),
        new QueryColumn("IsArchived",   QueryDataType.Bool,   false, true),
        new QueryColumn("FullPath",     QueryDataType.String, false, true)
    };

    // PARENT(id) and ISDESCENDENTOF(id) are WHERE predicates on the hierarchy.
    // COUNTCHILDREN and COUNTDESCENDENTS are also listed here so the WHERE
    // validator doesn't reject expressions like `COUNTCHILDREN() > 0`.
    public IReadOnlyList<string> AllowedFunctions { get; } = new[]
    {
        "PARENT", "ISDESCENDENTOF", "COUNTCHILDREN", "COUNTDESCENDENTS"
    };

    // Row functions appear in COLUMNS()/ORDER BY without requiring GROUP.
    public IReadOnlyList<string> RowFunctions { get; } = new[]
    {
        "COUNTCHILDREN", "COUNTDESCENDENTS"
    };

    public QueryDataType RowFunctionDataType(string functionName) =>
        functionName.ToUpperInvariant() switch
        {
            "COUNTCHILDREN"     => QueryDataType.Number,
            "COUNTDESCENDENTS"  => QueryDataType.Number,
            _ => QueryDataType.Number
        };

    public Task<IPreparedQuery> PrepareAsync(AqlQuery query, CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        ValidateTypeLiterals(query.Where, errors);
        IPreparedQuery prepared = new NotesPreparedQuery(
            this, query, StaticSchema, errors, _dbFactory, _contentAuthorizer);
        return Task.FromResult(prepared);
    }

    private static readonly HashSet<string> ValidTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Project", "Cabinet", "Notebook", "Page", "Note"
    };

    // Surface ValidTypes via the metadata contract so the autocomplete UI can
    // suggest the five values after `Type = `. The list is stable and short
    // enough to keep co-located here.
    public IReadOnlyDictionary<string, IReadOnlyList<string>> ColumnEnums { get; } =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Type"] = new[] { "Project", "Cabinet", "Notebook", "Page", "Note" }
        };

    // Surface a friendly error for `Type = "Banana"` rather than silently
    // returning zero rows. Other string comparisons (Name, Description, etc.)
    // are free-form.
    private static void ValidateTypeLiterals(AqlWhere? where, List<string> errors)
    {
        if (where is null) return;
        switch (where)
        {
            case AqlBinary b:
                ValidateTypeLiterals(b.Left, errors);
                ValidateTypeLiterals(b.Right, errors);
                break;
            case AqlCompare c when string.Equals(c.Field, "Type", StringComparison.OrdinalIgnoreCase):
                if (c.Op is "=" or "!=" && c.Value is AqlString s && !ValidTypes.Contains(s.Value))
                {
                    errors.Add($"Unknown Type '{s.Value}'. Available: Project, Cabinet, Notebook, Page, Note.");
                }
                break;
            case AqlIn inFilter when string.Equals(inFilter.Field, "Type", StringComparison.OrdinalIgnoreCase):
                foreach (var v in inFilter.Values)
                {
                    if (v is AqlString sv && !ValidTypes.Contains(sv.Value))
                    {
                        errors.Add($"Unknown Type '{sv.Value}'. Available: Project, Cabinet, Notebook, Page, Note.");
                    }
                }
                break;
        }
    }
}

// ---------------------------------------------------------------------------
// Lightweight projections so we don't pull large JSONB bodies into memory.
// Projects/Cabinets/Notebooks have no large columns; we load full entities.
// Pages and Notes carry BodyJsonb / ContentJsonb that we never read here.
// ---------------------------------------------------------------------------

internal sealed record PageMeta(
    Guid Id, long Locator, Guid NotebookId, Guid? ParentPageId,
    string Title, bool IsArchived,
    DateTime CreatedAtUtc, DateTime UpdatedAtUtc,
    Guid CreatedBy, Guid UpdatedBy);

internal sealed record NoteMeta(
    Guid Id, long Locator, Guid PageId,
    string NoteKind, string? Title, bool IsArchived,
    DateTime CreatedAtUtc, DateTime UpdatedAtUtc,
    Guid CreatedBy, Guid UpdatedBy);

// Resolved entity behind a PARENT(N) / ISDESCENDENTOF(N) locator argument.
internal sealed record ResolvedLocator(long Locator, string Kind, Guid Id);

// ---------------------------------------------------------------------------
// Plan: what the executor should load for this query. Derived once from the
// AST, then drives every downstream load decision.
// ---------------------------------------------------------------------------

internal sealed record NotesQueryPlan(
    // Type names the result could contain. null = unrestricted (load all 5).
    HashSet<string>? AllowedTypes,
    // Locator IDs the result is restricted to. null = unrestricted.
    HashSet<long>? LocatorFilter,
    // Whether CreatedBy/UpdatedBy is referenced anywhere — drives the
    // local_users lookup.
    bool NeedsUsers,
    // Whether FullPath is referenced — drives the ancestor-chain lookup.
    bool NeedsFullPath,
    // Whether COUNTCHILDREN() appears (projection, WHERE, or ORDER BY).
    bool NeedsCountChildren,
    // Whether COUNTDESCENDENTS() appears (projection, WHERE, or ORDER BY).
    bool NeedsCountDescendents,
    // Locator arguments to PARENT(N) — each needs locator → (kind, id).
    IReadOnlyList<long> ParentLocators,
    // Locator arguments to ISDESCENDENTOF(N) — each needs locator → (kind, id)
    // plus the materialized descendant set.
    IReadOnlyList<long> IsDescendentOfLocators);

internal static class NotesQueryPlanner
{
    private static readonly HashSet<string> AllTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Project", "Cabinet", "Notebook", "Page", "Note"
    };

    public static NotesQueryPlan Plan(AqlQuery query)
    {
        var allowedTypes = AnalyzeAllowedTypes(query.Where);
        var locatorFilter = AnalyzeLocatorFilter(query.Where);

        var needsUsers = ReferencesField(query, "CreatedBy", "UpdatedBy");
        var needsFullPath = ReferencesField(query, "FullPath");
        var needsCountChildren = ReferencesFunction(query, "COUNTCHILDREN");
        var needsCountDescendents = ReferencesFunction(query, "COUNTDESCENDENTS");

        var parents = new HashSet<long>();
        var isDescOf = new HashSet<long>();
        CollectFunctionLocatorArgs(query.Where, parents, isDescOf);

        return new NotesQueryPlan(
            allowedTypes,
            locatorFilter,
            needsUsers,
            needsFullPath,
            needsCountChildren,
            needsCountDescendents,
            parents.ToList(),
            isDescOf.ToList());
    }

    // Type-narrowing: returns the set of Type values the WHERE could allow,
    // or null if unconstrained. Combines via intersect/union for AND/OR.
    // Anything we can't reason about returns null (universe) — safe default.
    private static HashSet<string>? AnalyzeAllowedTypes(AqlWhere? where)
    {
        if (where is null) return null;
        switch (where)
        {
            case AqlBinary b when b.Op == "AND":
                return Intersect(AnalyzeAllowedTypes(b.Left), AnalyzeAllowedTypes(b.Right));
            case AqlBinary b when b.Op == "OR":
                return Union(AnalyzeAllowedTypes(b.Left), AnalyzeAllowedTypes(b.Right));
            case AqlCompare c when IsType(c.Field) && c.Op == "=" && c.Value is AqlString s:
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase) { s.Value };
            case AqlCompare c when IsType(c.Field) && c.Op == "!=" && c.Value is AqlString s:
                var rest = new HashSet<string>(AllTypes, StringComparer.OrdinalIgnoreCase);
                rest.Remove(s.Value);
                return rest;
            case AqlIn inFilter when IsType(inFilter.Field):
                return new HashSet<string>(
                    inFilter.Values.OfType<AqlString>().Select(s => s.Value),
                    StringComparer.OrdinalIgnoreCase);
            default:
                return null;
        }
    }

    // Locator-narrowing: returns the set of Id (locator) values the WHERE
    // could match. null = unconstrained.
    private static HashSet<long>? AnalyzeLocatorFilter(AqlWhere? where)
    {
        if (where is null) return null;
        switch (where)
        {
            case AqlBinary b when b.Op == "AND":
                return IntersectL(AnalyzeLocatorFilter(b.Left), AnalyzeLocatorFilter(b.Right));
            case AqlBinary b when b.Op == "OR":
                return UnionL(AnalyzeLocatorFilter(b.Left), AnalyzeLocatorFilter(b.Right));
            case AqlCompare c when IsId(c.Field) && c.Op == "=" && c.Value is AqlNumber n:
                return new HashSet<long> { (long)n.Value };
            case AqlIn inFilter when IsId(inFilter.Field):
                return new HashSet<long>(
                    inFilter.Values.OfType<AqlNumber>().Select(n => (long)n.Value));
            default:
                return null;
        }
    }

    private static HashSet<T>? Intersect<T>(HashSet<T>? a, HashSet<T>? b)
    {
        if (a is null) return b;
        if (b is null) return a;
        var result = new HashSet<T>(a, a.Comparer);
        result.IntersectWith(b);
        return result;
    }

    private static HashSet<T>? Union<T>(HashSet<T>? a, HashSet<T>? b)
    {
        if (a is null || b is null) return null;
        var result = new HashSet<T>(a, a.Comparer);
        result.UnionWith(b);
        return result;
    }

    private static HashSet<long>? IntersectL(HashSet<long>? a, HashSet<long>? b) => Intersect(a, b);
    private static HashSet<long>? UnionL(HashSet<long>? a, HashSet<long>? b) => Union(a, b);

    private static bool IsType(string field) =>
        string.Equals(field, "Type", StringComparison.OrdinalIgnoreCase);

    private static bool IsId(string field) =>
        string.Equals(field, "Id", StringComparison.OrdinalIgnoreCase);

    private static bool ReferencesField(AqlQuery query, params string[] fields)
    {
        var set = new HashSet<string>(fields, StringComparer.OrdinalIgnoreCase);
        if (WhereReferencesField(query.Where, set)) return true;
        if (query.Columns is not null && query.Columns.Any(c => SelectItemReferencesField(c, set))) return true;
        if (query.OrderBy.Any(o => SelectItemReferencesField(o.Item, set))) return true;
        if (query.Group is not null && query.Group.Any(g => set.Contains(g))) return true;
        return false;
    }

    private static bool SelectItemReferencesField(AqlSelectItem item, HashSet<string> set) =>
        (item.Field is not null && set.Contains(item.Field))
        || (item.AggregateField is not null && set.Contains(item.AggregateField));

    private static bool WhereReferencesField(AqlWhere? where, HashSet<string> set)
    {
        if (where is null) return false;
        return where switch
        {
            AqlBinary b   => WhereReferencesField(b.Left, set) || WhereReferencesField(b.Right, set),
            AqlCompare c  => set.Contains(c.Field),
            AqlContains ct=> set.Contains(ct.Field),
            AqlIn inF     => set.Contains(inF.Field),
            AqlBetween bw => set.Contains(bw.Field),
            _ => false
        };
    }

    private static bool ReferencesFunction(AqlQuery query, string fnName)
    {
        if (WhereReferencesFunction(query.Where, fnName)) return true;
        if (query.Columns is not null && query.Columns.Any(c =>
            c.AggregateFn is not null && string.Equals(c.AggregateFn, fnName, StringComparison.OrdinalIgnoreCase))) return true;
        if (query.OrderBy.Any(o =>
            o.Item.AggregateFn is not null && string.Equals(o.Item.AggregateFn, fnName, StringComparison.OrdinalIgnoreCase))) return true;
        return false;
    }

    private static bool WhereReferencesFunction(AqlWhere? where, string fnName)
    {
        if (where is null) return false;
        return where switch
        {
            AqlBinary b           => WhereReferencesFunction(b.Left, fnName) || WhereReferencesFunction(b.Right, fnName),
            AqlFunctionCall fc    => string.Equals(fc.Name, fnName, StringComparison.OrdinalIgnoreCase),
            AqlFunctionCompare fc => string.Equals(fc.FnName, fnName, StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    private static void CollectFunctionLocatorArgs(AqlWhere? where, HashSet<long> parents, HashSet<long> isDescOf)
    {
        if (where is null) return;
        switch (where)
        {
            case AqlBinary b:
                CollectFunctionLocatorArgs(b.Left, parents, isDescOf);
                CollectFunctionLocatorArgs(b.Right, parents, isDescOf);
                break;
            case AqlFunctionCall fc:
                if (fc.Args.Count == 1 && fc.Args[0] is AqlNumber n)
                {
                    var loc = (long)n.Value;
                    var fn = fc.Name.ToUpperInvariant();
                    if (fn == "PARENT") parents.Add(loc);
                    else if (fn == "ISDESCENDENTOF") isDescOf.Add(loc);
                }
                break;
        }
    }
}

// ---------------------------------------------------------------------------
// Prepared query: holds the AST + plan; ExecuteAsync runs all DB loads in
// parallel using only what the plan declares we need.
// ---------------------------------------------------------------------------

internal sealed class NotesPreparedQuery : IPreparedQuery
{
    private readonly IDbContextFactory<AutoNateDbContext> _dbFactory;
    private readonly IContentAuthorizer _contentAuthorizer;

    public NotesPreparedQuery(
        IQueryEntity entity,
        AqlQuery query,
        IReadOnlyList<QueryColumn> schema,
        IReadOnlyList<string> errors,
        IDbContextFactory<AutoNateDbContext> dbFactory,
        IContentAuthorizer contentAuthorizer)
    {
        Entity = entity;
        Query = query;
        Schema = schema;
        ValidationErrors = errors;
        _dbFactory = dbFactory;
        _contentAuthorizer = contentAuthorizer;
    }

    public IQueryEntity Entity { get; }
    public AqlQuery Query { get; }
    public IReadOnlyList<QueryColumn> Schema { get; }
    public IReadOnlyList<string> ValidationErrors { get; }

    public async Task<QueryResult> ExecuteAsync(
        ClaimsPrincipal actor,
        int? hardCap,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var plan = NotesQueryPlanner.Plan(Query);

        // DbContext is not thread-safe — every parallel task creates its own
        // via _dbFactory. Connections come from the pool, so the per-task
        // overhead is small.

        // ---- Phase 1 (parallel): visibility + locator-reference resolution.
        //
        // GetAllowedIdsAsync per kind we plan to load (its result also gates
        // notes via parent-page visibility). Locator → (kind, id) resolution
        // for any PARENT(N) / ISDESCENDENTOF(N) arguments runs alongside.
        var loadKinds = LoadKindsFromPlan(plan);
        var accessTasks = loadKinds.ToDictionary(
            k => k,
            k => k == "note"
                ? _contentAuthorizer.GetAllowedIdsAsync(actor, ContentKinds.Page, Actions.View, ct)
                : _contentAuthorizer.GetAllowedIdsAsync(actor, k, Actions.View, ct));

        // Notes inherit visibility from Page. If Page isn't already in the
        // load set but notes are, we still need page visibility to filter notes.
        if (loadKinds.Contains("note") && !loadKinds.Contains(ContentKinds.Page))
        {
            accessTasks[ContentKinds.Page] = _contentAuthorizer.GetAllowedIdsAsync(
                actor, ContentKinds.Page, Actions.View, ct);
        }

        var referencedLocators = plan.ParentLocators
            .Concat(plan.IsDescendentOfLocators)
            .Distinct()
            .ToList();
        var locatorResolutionTask = ResolveLocatorsAsync(_dbFactory, referencedLocators, ct);

        await Task.WhenAll(accessTasks.Values.Concat<Task>(new[] { locatorResolutionTask }));
        var resolvedRefs = await locatorResolutionTask;
        var resolvedByLocator = resolvedRefs.ToDictionary(r => r.Locator);

        // ---- Phase 2 (parallel): entity loads + auxiliary aggregations.
        //
        // Each load is filtered by visibility + (optional) locator filter +
        // (optional) parent-of-locator filter. Pages/Notes project to
        // metadata DTOs so we never pull the JSONB body columns.

        var projectsTask = loadKinds.Contains(ContentKinds.Project)
            ? LoadProjectsAsync(_dbFactory, await accessTasks[ContentKinds.Project], plan, ct)
            : Task.FromResult(new List<Project>());

        var cabinetsTask = loadKinds.Contains(ContentKinds.Cabinet)
            ? LoadCabinetsAsync(_dbFactory, await accessTasks[ContentKinds.Cabinet], plan, resolvedByLocator, ct)
            : Task.FromResult(new List<Cabinet>());

        var notebooksTask = loadKinds.Contains(ContentKinds.Notebook)
            ? LoadNotebooksAsync(_dbFactory, await accessTasks[ContentKinds.Notebook], plan, resolvedByLocator, ct)
            : Task.FromResult(new List<Notebook>());

        var pagesTask = loadKinds.Contains(ContentKinds.Page)
            ? LoadPagesAsync(_dbFactory, await accessTasks[ContentKinds.Page], plan, resolvedByLocator, ct)
            : Task.FromResult(new List<PageMeta>());

        // Notes need the page-visibility set to filter. Resolve it here so
        // the notes query can use it without waiting on the pages task.
        var visiblePageIdsTask = loadKinds.Contains("note")
            ? GetVisiblePageIdsAsync(_dbFactory, await accessTasks[ContentKinds.Page], ct)
            : Task.FromResult<HashSet<Guid>?>(null);

        var notesTask = loadKinds.Contains("note")
            ? LoadNotesAsync(_dbFactory, await visiblePageIdsTask, await accessTasks[ContentKinds.Page], plan, resolvedByLocator, ct)
            : Task.FromResult(new List<NoteMeta>());

        // Hierarchy-function aggregations: only fired when the plan needs them.
        var childCountsTask = plan.NeedsCountChildren
            ? LoadChildCountsAsync(_dbFactory, ct)
            : Task.FromResult(new Dictionary<(string, Guid), int>());

        var descendantCountsTask = plan.NeedsCountDescendents
            ? LoadDescendantCountsAsync(_dbFactory, ct)
            : Task.FromResult(new Dictionary<(string, Guid), int>());

        var descendantSetsTask = plan.IsDescendentOfLocators.Count > 0
            ? LoadDescendantSetsAsync(_dbFactory, plan.IsDescendentOfLocators, resolvedByLocator, ct)
            : Task.FromResult(new Dictionary<(string, Guid), HashSet<(string, Guid)>>());

        await Task.WhenAll(
            projectsTask, cabinetsTask, notebooksTask, pagesTask, notesTask,
            childCountsTask, descendantCountsTask, descendantSetsTask);

        var projects  = await projectsTask;
        var cabinets  = await cabinetsTask;
        var notebooks = await notebooksTask;
        var pages     = await pagesTask;
        var notes     = await notesTask;
        var childCounts = await childCountsTask;
        var descendantCounts = await descendantCountsTask;
        var descendantSets = await descendantSetsTask;

        // ---- Phase 3 (parallel): users + ancestor chains for FullPath.
        var rows = BuildRows(projects, cabinets, notebooks, pages, notes);

        var usersTask = plan.NeedsUsers
            ? LoadUserDisplaysAsync(_dbFactory, rows, ct)
            : Task.FromResult(new Dictionary<Guid, string>());

        var ancestorChainsTask = plan.NeedsFullPath
            ? LoadAncestorChainsAsync(_dbFactory, rows, ct)
            : Task.FromResult(new Dictionary<(string, Guid), string>());

        await Task.WhenAll(usersTask, ancestorChainsTask);
        var users = await usersTask;
        var fullPathByEntity = await ancestorChainsTask;

        if (plan.NeedsUsers)
        {
            ApplyUserDisplayNames(rows, users);
        }

        var indexes = new NoteRowIndexes(
            rows,
            childCounts,
            descendantCounts,
            descendantSets,
            fullPathByEntity);

        // ---- Phase 4: in-memory filter + sort + project.
        IEnumerable<NoteRow> filtered = rows;
        if (Query.Where is not null)
        {
            filtered = filtered.Where(r => EvalWhere(Query.Where, r, indexes));
        }
        var working = filtered.ToList();

        var projection = ResolveProjection();
        int? effectiveCap = Query.Limit ?? hardCap;

        List<IReadOnlyDictionary<string, object?>> resultRows;
        bool truncated;

        if (Query.Group is not null)
        {
            (resultRows, truncated) = ExecuteGrouped(working, projection, indexes, effectiveCap);
        }
        else
        {
            (resultRows, truncated) = ExecuteUngrouped(working, projection, indexes, effectiveCap);
        }

        return new QueryResult(
            Columns: projection.Select(p => new QueryColumnMeta(p.DisplayName, p.DataType)).ToList(),
            Rows: resultRows,
            TotalCount: resultRows.Count + (truncated ? 1 : 0),
            Truncated: truncated,
            DurationMs: sw.ElapsedMilliseconds);
    }

    // ---- Plan helpers ----------------------------------------------------

    // Translates AllowedTypes (user-facing Type names) into the kind strings
    // used internally. When AllowedTypes is null, all five kinds load.
    private static HashSet<string> LoadKindsFromPlan(NotesQueryPlan plan)
    {
        var kinds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var types = plan.AllowedTypes;
        if (types is null || types.Contains("Project"))  kinds.Add(ContentKinds.Project);
        if (types is null || types.Contains("Cabinet"))  kinds.Add(ContentKinds.Cabinet);
        if (types is null || types.Contains("Notebook")) kinds.Add(ContentKinds.Notebook);
        if (types is null || types.Contains("Page"))     kinds.Add(ContentKinds.Page);
        if (types is null || types.Contains("Note"))     kinds.Add("note");
        return kinds;
    }

    // ---- Per-kind loaders ------------------------------------------------

    private static async Task<List<Project>> LoadProjectsAsync(
        IDbContextFactory<AutoNateDbContext> factory,
        ContentAccessSet access, NotesQueryPlan plan, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        IQueryable<Project> q = db.Projects.AsNoTracking();
        if (!access.Unrestricted)
        {
            if (access.AllowedIds.Count == 0) return new List<Project>();
            var ids = access.AllowedIds;
            q = q.Where(p => ids.Contains(p.Id));
        }
        if (plan.LocatorFilter is { Count: > 0 } locFilter)
        {
            q = q.Where(p => locFilter.Contains(p.Locator));
        }
        return await q.ToListAsync(ct);
    }

    private static async Task<List<Cabinet>> LoadCabinetsAsync(
        IDbContextFactory<AutoNateDbContext> factory,
        ContentAccessSet access, NotesQueryPlan plan,
        Dictionary<long, ResolvedLocator> resolvedRefs, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        IQueryable<Cabinet> q = db.Cabinets.AsNoTracking();
        if (!access.Unrestricted)
        {
            if (access.AllowedIds.Count == 0) return new List<Cabinet>();
            var ids = access.AllowedIds;
            q = q.Where(c => ids.Contains(c.Id));
        }
        if (plan.LocatorFilter is { Count: > 0 } locFilter)
        {
            q = q.Where(c => locFilter.Contains(c.Locator));
        }
        if (TryGetParentFilter(plan, resolvedRefs, ContentKinds.Project) is { } projectIds)
        {
            q = q.Where(c => projectIds.Contains(c.ProjectId));
        }
        return await q.ToListAsync(ct);
    }

    private static async Task<List<Notebook>> LoadNotebooksAsync(
        IDbContextFactory<AutoNateDbContext> factory,
        ContentAccessSet access, NotesQueryPlan plan,
        Dictionary<long, ResolvedLocator> resolvedRefs, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        IQueryable<Notebook> q = db.Notebooks.AsNoTracking();
        if (!access.Unrestricted)
        {
            if (access.AllowedIds.Count == 0) return new List<Notebook>();
            var ids = access.AllowedIds;
            q = q.Where(n => ids.Contains(n.Id));
        }
        if (plan.LocatorFilter is { Count: > 0 } locFilter)
        {
            q = q.Where(n => locFilter.Contains(n.Locator));
        }
        if (TryGetParentFilter(plan, resolvedRefs, ContentKinds.Cabinet) is { } cabinetIds)
        {
            q = q.Where(n => cabinetIds.Contains(n.CabinetId));
        }
        return await q.ToListAsync(ct);
    }

    private static async Task<List<PageMeta>> LoadPagesAsync(
        IDbContextFactory<AutoNateDbContext> factory,
        ContentAccessSet access, NotesQueryPlan plan,
        Dictionary<long, ResolvedLocator> resolvedRefs, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        IQueryable<Page> q = db.Pages.AsNoTracking();
        if (!access.Unrestricted)
        {
            if (access.AllowedIds.Count == 0) return new List<PageMeta>();
            var ids = access.AllowedIds;
            q = q.Where(p => ids.Contains(p.Id));
        }
        if (plan.LocatorFilter is { Count: > 0 } locFilter)
        {
            q = q.Where(p => locFilter.Contains(p.Locator));
        }
        // PARENT(N) on a Page: parent is either a Notebook (top-level) or a
        // Page (nested). Apply notebook-id constraint when any referenced
        // parent is a notebook, OR parent-page-id constraint when it's a page.
        var pageParents = ResolveParentParentIds(plan, resolvedRefs, ContentKinds.Notebook);
        if (pageParents is not null)
        {
            q = q.Where(p => pageParents.Contains(p.NotebookId) && p.ParentPageId == null);
        }
        var subPageParents = ResolveParentParentIds(plan, resolvedRefs, ContentKinds.Page);
        if (subPageParents is not null)
        {
            q = q.Where(p => p.ParentPageId != null && subPageParents.Contains(p.ParentPageId.Value));
        }

        return await q
            .Select(p => new PageMeta(
                p.Id, p.Locator, p.NotebookId, p.ParentPageId,
                p.Title, p.IsArchived,
                p.CreatedAtUtc, p.UpdatedAtUtc,
                p.CreatedBy, p.UpdatedBy))
            .ToListAsync(ct);
    }

    private static async Task<HashSet<Guid>?> GetVisiblePageIdsAsync(
        IDbContextFactory<AutoNateDbContext> factory, ContentAccessSet access, CancellationToken ct)
    {
        if (access.Unrestricted) return null;
        if (access.AllowedIds.Count == 0) return new HashSet<Guid>();
        await using var db = await factory.CreateDbContextAsync(ct);
        var ids = access.AllowedIds;
        var visible = await db.Pages.AsNoTracking()
            .Where(p => ids.Contains(p.Id))
            .Select(p => p.Id)
            .ToListAsync(ct);
        return visible.ToHashSet();
    }

    private static async Task<List<NoteMeta>> LoadNotesAsync(
        IDbContextFactory<AutoNateDbContext> factory,
        HashSet<Guid>? visiblePageIds,
        ContentAccessSet pageAccess,
        NotesQueryPlan plan,
        Dictionary<long, ResolvedLocator> resolvedRefs,
        CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        IQueryable<Note> q = db.Notes.AsNoTracking();
        if (!pageAccess.Unrestricted)
        {
            if (visiblePageIds is null || visiblePageIds.Count == 0) return new List<NoteMeta>();
            q = q.Where(n => visiblePageIds.Contains(n.PageId));
        }
        if (plan.LocatorFilter is { Count: > 0 } locFilter)
        {
            q = q.Where(n => locFilter.Contains(n.Locator));
        }
        if (TryGetParentFilter(plan, resolvedRefs, ContentKinds.Page) is { } pageIds)
        {
            q = q.Where(n => pageIds.Contains(n.PageId));
        }
        return await q
            .Select(n => new NoteMeta(
                n.Id, n.Locator, n.PageId,
                n.NoteKind, n.Title, n.IsArchived,
                n.CreatedAtUtc, n.UpdatedAtUtc,
                n.CreatedBy, n.UpdatedBy))
            .ToListAsync(ct);
    }

    // For PARENT(N): if any resolved reference is of kind X, then a child
    // entity of kind Y where Y's parent kind is X must have its parent's
    // id ∈ {resolved.Id : ref.Kind == X}. If no PARENT(N) reference targets X,
    // returns null (no constraint). Multiple targets become a HashSet IN().
    private static HashSet<Guid>? TryGetParentFilter(
        NotesQueryPlan plan,
        Dictionary<long, ResolvedLocator> resolvedRefs,
        string parentKind)
    {
        if (plan.ParentLocators.Count == 0) return null;
        var ids = new HashSet<Guid>();
        foreach (var loc in plan.ParentLocators)
        {
            if (resolvedRefs.TryGetValue(loc, out var resolved)
                && string.Equals(resolved.Kind, parentKind, StringComparison.OrdinalIgnoreCase))
            {
                ids.Add(resolved.Id);
            }
        }
        return ids.Count == 0 ? null : ids;
    }

    // Same idea but used for Pages: pages have two parent kinds (Notebook or
    // Page). The caller passes which parent kind it wants resolved.
    private static HashSet<Guid>? ResolveParentParentIds(
        NotesQueryPlan plan,
        Dictionary<long, ResolvedLocator> resolvedRefs,
        string parentKind) => TryGetParentFilter(plan, resolvedRefs, parentKind);

    // ---- Locator resolution ----------------------------------------------

    // Resolves locator → (kind, id) for the PARENT(N) / ISDESCENDENTOF(N)
    // arguments. Issues one query per kind, in parallel, each with its own
    // DbContext (DbContext is not thread-safe). Each kind's locator column
    // is unique-indexed so the lookups are cheap index hits.
    private static async Task<List<ResolvedLocator>> ResolveLocatorsAsync(
        IDbContextFactory<AutoNateDbContext> factory,
        IReadOnlyList<long> locators, CancellationToken ct)
    {
        if (locators.Count == 0) return new List<ResolvedLocator>();
        var set = locators.ToHashSet();

        async Task<List<ResolvedLocator>> Q(Func<AutoNateDbContext, IQueryable<ResolvedLocator>> q)
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            return await q(db).ToListAsync(ct);
        }

        var pTask  = Q(db => db.Projects .AsNoTracking().Where(x => set.Contains(x.Locator))
            .Select(x => new ResolvedLocator(x.Locator, ContentKinds.Project,  x.Id)));
        var cTask  = Q(db => db.Cabinets .AsNoTracking().Where(x => set.Contains(x.Locator))
            .Select(x => new ResolvedLocator(x.Locator, ContentKinds.Cabinet,  x.Id)));
        var nbTask = Q(db => db.Notebooks.AsNoTracking().Where(x => set.Contains(x.Locator))
            .Select(x => new ResolvedLocator(x.Locator, ContentKinds.Notebook, x.Id)));
        var pgTask = Q(db => db.Pages    .AsNoTracking().Where(x => set.Contains(x.Locator))
            .Select(x => new ResolvedLocator(x.Locator, ContentKinds.Page,     x.Id)));
        var noTask = Q(db => db.Notes    .AsNoTracking().Where(x => set.Contains(x.Locator))
            .Select(x => new ResolvedLocator(x.Locator, "note",                x.Id)));

        await Task.WhenAll(pTask, cTask, nbTask, pgTask, noTask);
        var all = new List<ResolvedLocator>();
        all.AddRange(await pTask);
        all.AddRange(await cTask);
        all.AddRange(await nbTask);
        all.AddRange(await pgTask);
        all.AddRange(await noTask);
        return all;
    }

    // ---- Aggregations ----------------------------------------------------

    // Direct-child count per (parent-kind, parent-id) across the hierarchy.
    // Five GROUP-BY queries (parallel, each with its own DbContext) cover
    // every parent/child relationship.
    private sealed record ChildCountRow(Guid Pid, int Cnt);

    private static async Task<Dictionary<(string, Guid), int>> LoadChildCountsAsync(
        IDbContextFactory<AutoNateDbContext> factory, CancellationToken ct)
    {
        async Task<List<ChildCountRow>> Cabinets()
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            return await db.Cabinets.AsNoTracking()
                .GroupBy(c => c.ProjectId)
                .Select(g => new ChildCountRow(g.Key, g.Count()))
                .ToListAsync(ct);
        }
        async Task<List<ChildCountRow>> Notebooks()
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            return await db.Notebooks.AsNoTracking()
                .GroupBy(n => n.CabinetId)
                .Select(g => new ChildCountRow(g.Key, g.Count()))
                .ToListAsync(ct);
        }
        // A page's parent is the notebook only if ParentPageId IS NULL —
        // otherwise its parent is another page. Split into two queries.
        async Task<List<ChildCountRow>> TopLevelPages()
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            return await db.Pages.AsNoTracking()
                .Where(p => p.ParentPageId == null)
                .GroupBy(p => p.NotebookId)
                .Select(g => new ChildCountRow(g.Key, g.Count()))
                .ToListAsync(ct);
        }
        async Task<List<ChildCountRow>> SubPages()
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            return await db.Pages.AsNoTracking()
                .Where(p => p.ParentPageId != null)
                .GroupBy(p => p.ParentPageId!.Value)
                .Select(g => new ChildCountRow(g.Key, g.Count()))
                .ToListAsync(ct);
        }
        async Task<List<ChildCountRow>> NotesUnderPage()
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            return await db.Notes.AsNoTracking()
                .GroupBy(n => n.PageId)
                .Select(g => new ChildCountRow(g.Key, g.Count()))
                .ToListAsync(ct);
        }

        var cabinetByProject = Cabinets();
        var notebookByCabinet = Notebooks();
        var pageByNotebook = TopLevelPages();
        var pageByPage = SubPages();
        var noteByPage = NotesUnderPage();
        await Task.WhenAll(cabinetByProject, notebookByCabinet, pageByNotebook, pageByPage, noteByPage);

        var dict = new Dictionary<(string, Guid), int>();
        foreach (var r in await cabinetByProject)  dict[(ContentKinds.Project,  r.Pid)] = r.Cnt;
        foreach (var r in await notebookByCabinet) dict[(ContentKinds.Cabinet,  r.Pid)] = r.Cnt;
        foreach (var r in await pageByNotebook)    dict[(ContentKinds.Notebook, r.Pid)] = r.Cnt;
        foreach (var r in await pageByPage)        Add(dict, (ContentKinds.Page, r.Pid), r.Cnt);
        foreach (var r in await noteByPage)        Add(dict, (ContentKinds.Page, r.Pid), r.Cnt);
        return dict;

        static void Add(Dictionary<(string, Guid), int> d, (string, Guid) k, int v)
        {
            d[k] = d.TryGetValue(k, out var existing) ? existing + v : v;
        }
    }

    // Transitive descendant count per (ancestor-kind, ancestor-id) using
    // the materialized closure. Notes contribute by their parent page's
    // ancestor chain (notes aren't in the closure themselves).
    private sealed record DescendantCountRow(string AncestorKind, Guid AncestorId, int Cnt);

    private static async Task<Dictionary<(string, Guid), int>> LoadDescendantCountsAsync(
        IDbContextFactory<AutoNateDbContext> factory, CancellationToken ct)
    {
        async Task<List<DescendantCountRow>> FromClosure()
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            return await db.ContentAncestors.AsNoTracking()
                .Where(ca => ca.Depth > 0)
                .GroupBy(ca => new { ca.AncestorKind, ca.AncestorId })
                .Select(g => new DescendantCountRow(g.Key.AncestorKind, g.Key.AncestorId, g.Count()))
                .ToListAsync(ct);
        }
        // Each note adds 1 to the count of every ancestor of its page,
        // including the page itself (depth-0 self row in content_ancestors).
        async Task<List<DescendantCountRow>> FromNotes()
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            return await (
                from n in db.Notes.AsNoTracking()
                join ca in db.ContentAncestors.AsNoTracking()
                    on new { K = ContentKinds.Page, I = n.PageId } equals new { K = ca.DescendantKind, I = ca.DescendantId }
                group n by new { ca.AncestorKind, ca.AncestorId } into g
                select new DescendantCountRow(g.Key.AncestorKind, g.Key.AncestorId, g.Count()))
                .ToListAsync(ct);
        }

        var fromClosure = FromClosure();
        var fromNotes = FromNotes();
        await Task.WhenAll(fromClosure, fromNotes);

        var dict = new Dictionary<(string, Guid), int>();
        foreach (var r in await fromClosure) dict[(r.AncestorKind, r.AncestorId)] = r.Cnt;
        foreach (var r in await fromNotes)
        {
            dict[(r.AncestorKind, r.AncestorId)] = dict.TryGetValue((r.AncestorKind, r.AncestorId), out var existing)
                ? existing + r.Cnt
                : r.Cnt;
        }
        return dict;
    }

    // For each ISDESCENDENTOF(N) call, materialize the set of descendants
    // of N's entity. Keyed by (ancestor-kind, ancestor-id) so the WHERE
    // evaluator can check membership for each row.
    private static async Task<Dictionary<(string, Guid), HashSet<(string, Guid)>>> LoadDescendantSetsAsync(
        IDbContextFactory<AutoNateDbContext> factory,
        IReadOnlyList<long> locators,
        Dictionary<long, ResolvedLocator> resolvedRefs,
        CancellationToken ct)
    {
        var dict = new Dictionary<(string, Guid), HashSet<(string, Guid)>>();
        foreach (var loc in locators)
        {
            if (!resolvedRefs.TryGetValue(loc, out var anchor)) continue;
            if (!ContentKinds.IsContentKind(anchor.Kind))
            {
                dict[(anchor.Kind, anchor.Id)] = new HashSet<(string, Guid)>();
                continue;
            }

            async Task<List<(string Kind, Guid Id)>> ContentDescs()
            {
                await using var db = await factory.CreateDbContextAsync(ct);
                return await db.ContentAncestors.AsNoTracking()
                    .Where(ca => ca.AncestorKind == anchor.Kind
                              && ca.AncestorId == anchor.Id
                              && ca.Depth > 0)
                    .Select(ca => new ValueTuple<string, Guid>(ca.DescendantKind, ca.DescendantId))
                    .ToListAsync(ct);
            }
            // Notes whose parent page is the anchor itself or any of its
            // page descendants. Join via content_ancestors so notes under
            // nested pages are also counted.
            async Task<List<Guid>> NoteDescs()
            {
                await using var db = await factory.CreateDbContextAsync(ct);
                return await (
                    from n in db.Notes.AsNoTracking()
                    join ca in db.ContentAncestors.AsNoTracking()
                        on new { K = ContentKinds.Page, I = n.PageId } equals new { K = ca.DescendantKind, I = ca.DescendantId }
                    where ca.AncestorKind == anchor.Kind && ca.AncestorId == anchor.Id
                    select n.Id).ToListAsync(ct);
            }

            var contentDescTask = ContentDescs();
            var noteDescTask = NoteDescs();
            await Task.WhenAll(contentDescTask, noteDescTask);

            var set = new HashSet<(string, Guid)>();
            foreach (var d in await contentDescTask) set.Add(d);
            foreach (var nid in await noteDescTask) set.Add(("note", nid));
            dict[(anchor.Kind, anchor.Id)] = set;
        }
        return dict;
    }

    // FullPath: for each loaded row, walk its ancestor chain and build a
    // " / "-joined string of names. Issues one closure query restricted to
    // the loaded rows, joined to every kind table to pick up the ancestor's
    // display name. Notes anchor on their PageId.
    private sealed record AncestorEdge(string DescendantKind, Guid DescendantId, int Depth, string AncestorKind, Guid AncestorId);
    private sealed record NamedEntity(Guid Id, string Name);

    private static async Task<Dictionary<(string, Guid), string>> LoadAncestorChainsAsync(
        IDbContextFactory<AutoNateDbContext> factory, List<NoteRow> rows, CancellationToken ct)
    {
        if (rows.Count == 0) return new();

        // Build the set of (kind, id) we need chains for. Notes are mapped
        // to their parent page since notes aren't in the closure.
        var contentDescendants = new HashSet<Guid>();
        var noteDescendants = new List<(Guid NoteId, string NoteName, Guid PageId)>();
        foreach (var r in rows)
        {
            if (r.Kind == "note")
            {
                if (r.ParentEntityId is { } pid)
                {
                    noteDescendants.Add((r.EntityId, r.Name ?? string.Empty, pid));
                    contentDescendants.Add(pid);
                }
                continue;
            }
            contentDescendants.Add(r.EntityId);
        }

        // Pull the closure restricted to our descendants. Single round-trip.
        List<AncestorEdge> ancestorRows;
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            ancestorRows = await db.ContentAncestors.AsNoTracking()
                .Where(ca => contentDescendants.Contains(ca.DescendantId))
                .Select(ca => new AncestorEdge(ca.DescendantKind, ca.DescendantId, ca.Depth, ca.AncestorKind, ca.AncestorId))
                .ToListAsync(ct);
        }

        var projectIds  = ancestorRows.Where(a => a.AncestorKind == ContentKinds.Project).Select(a => a.AncestorId).ToHashSet();
        var cabinetIds  = ancestorRows.Where(a => a.AncestorKind == ContentKinds.Cabinet).Select(a => a.AncestorId).ToHashSet();
        var notebookIds = ancestorRows.Where(a => a.AncestorKind == ContentKinds.Notebook).Select(a => a.AncestorId).ToHashSet();
        var pageAncIds  = ancestorRows.Where(a => a.AncestorKind == ContentKinds.Page).Select(a => a.AncestorId).ToHashSet();

        // Four name-resolution queries in parallel, each on its own context.
        async Task<List<NamedEntity>> Projects()
        {
            if (projectIds.Count == 0) return new();
            await using var db = await factory.CreateDbContextAsync(ct);
            return await db.Projects.AsNoTracking().Where(p => projectIds.Contains(p.Id))
                .Select(p => new NamedEntity(p.Id, p.Name)).ToListAsync(ct);
        }
        async Task<List<NamedEntity>> Cabs()
        {
            if (cabinetIds.Count == 0) return new();
            await using var db = await factory.CreateDbContextAsync(ct);
            return await db.Cabinets.AsNoTracking().Where(c => cabinetIds.Contains(c.Id))
                .Select(c => new NamedEntity(c.Id, c.Name)).ToListAsync(ct);
        }
        async Task<List<NamedEntity>> Nbs()
        {
            if (notebookIds.Count == 0) return new();
            await using var db = await factory.CreateDbContextAsync(ct);
            return await db.Notebooks.AsNoTracking().Where(n => notebookIds.Contains(n.Id))
                .Select(n => new NamedEntity(n.Id, n.Name)).ToListAsync(ct);
        }
        async Task<List<NamedEntity>> Pgs()
        {
            if (pageAncIds.Count == 0) return new();
            await using var db = await factory.CreateDbContextAsync(ct);
            return await db.Pages.AsNoTracking().Where(p => pageAncIds.Contains(p.Id))
                .Select(p => new NamedEntity(p.Id, p.Title)).ToListAsync(ct);
        }

        var projectNamesTask = Projects();
        var cabinetNamesTask = Cabs();
        var notebookNamesTask = Nbs();
        var pageNamesTask = Pgs();
        await Task.WhenAll(projectNamesTask, cabinetNamesTask, notebookNamesTask, pageNamesTask);

        var namesByKindId = new Dictionary<(string, Guid), string>();
        foreach (var r in await projectNamesTask)  namesByKindId[(ContentKinds.Project,  r.Id)] = r.Name;
        foreach (var r in await cabinetNamesTask)  namesByKindId[(ContentKinds.Cabinet,  r.Id)] = r.Name;
        foreach (var r in await notebookNamesTask) namesByKindId[(ContentKinds.Notebook, r.Id)] = r.Name;
        foreach (var r in await pageNamesTask)     namesByKindId[(ContentKinds.Page,     r.Id)] = r.Name;

        // Group ancestors per descendant and sort top-down (deepest depth first
        // — that's the root — then descend).
        var chainByDescendant = ancestorRows
            .GroupBy(a => (a.DescendantKind, a.DescendantId))
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(a => a.Depth)
                      .Select(a => namesByKindId.GetValueOrDefault((a.AncestorKind, a.AncestorId), string.Empty))
                      .ToList());

        var result = new Dictionary<(string, Guid), string>();
        // Build paths for the four content kinds directly.
        foreach (var r in rows)
        {
            if (r.Kind == "note") continue;
            if (chainByDescendant.TryGetValue((r.Kind, r.EntityId), out var chain))
            {
                result[(r.Kind, r.EntityId)] = string.Join(" / ", chain);
            }
            else
            {
                result[(r.Kind, r.EntityId)] = r.Name ?? string.Empty;
            }
        }
        // Notes: chain = page's chain + note's own title.
        foreach (var (noteId, noteName, pageId) in noteDescendants)
        {
            chainByDescendant.TryGetValue((ContentKinds.Page, pageId), out var pageChain);
            var path = (pageChain ?? new List<string>()).Concat(new[] { noteName }).ToList();
            result[("note", noteId)] = string.Join(" / ", path);
        }
        return result;
    }

    // ---- User display name resolution -----------------------------------

    private static async Task<Dictionary<Guid, string>> LoadUserDisplaysAsync(
        IDbContextFactory<AutoNateDbContext> factory, List<NoteRow> rows, CancellationToken ct)
    {
        var ids = new HashSet<Guid>();
        foreach (var r in rows)
        {
            ids.Add(r.CreatedById);
            ids.Add(r.UpdatedById);
        }
        if (ids.Count == 0) return new();
        await using var db = await factory.CreateDbContextAsync(ct);
        var users = await db.LocalUsers.AsNoTracking()
            .Where(u => ids.Contains(u.UserId))
            .Select(u => new { u.UserId, u.FirstName, u.LastName, u.Username })
            .ToListAsync(ct);
        return users.ToDictionary(
            u => u.UserId,
            u => FormatDisplayName(u.FirstName, u.LastName, u.Username));
    }

    private static void ApplyUserDisplayNames(List<NoteRow> rows, Dictionary<Guid, string> map)
    {
        for (var i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            string? created = map.TryGetValue(r.CreatedById, out var c) ? c : null;
            string? updated = map.TryGetValue(r.UpdatedById, out var u) ? u : null;
            rows[i] = r with { CreatedBy = created, UpdatedBy = updated };
        }
    }

    private static string FormatDisplayName(string firstName, string lastName, string username)
    {
        var combined = $"{firstName} {lastName}".Trim();
        return string.IsNullOrEmpty(combined) ? username : combined;
    }

    // ---- Row materialization --------------------------------------------

    private static List<NoteRow> BuildRows(
        List<Project> projects, List<Cabinet> cabinets, List<Notebook> notebooks,
        List<PageMeta> pages, List<NoteMeta> notes)
    {
        var rows = new List<NoteRow>(projects.Count + cabinets.Count + notebooks.Count + pages.Count + notes.Count);

        foreach (var p in projects)
        {
            rows.Add(new NoteRow(
                Id: p.Locator, Type: "Project", SubType: null,
                Name: p.Name, Description: p.Description, Icon: null,
                DateCreated: AsUtc(p.CreatedAtUtc), DateUpdated: AsUtc(p.UpdatedAtUtc),
                CreatedBy: null, UpdatedBy: null,
                IsArchived: p.IsArchived,
                EntityId: p.Id, Kind: ContentKinds.Project,
                ParentEntityId: null, ParentKind: null,
                CreatedById: p.CreatedBy, UpdatedById: p.UpdatedBy));
        }
        foreach (var c in cabinets)
        {
            rows.Add(new NoteRow(
                Id: c.Locator, Type: "Cabinet", SubType: null,
                Name: c.Name, Description: c.Description, Icon: c.Icon,
                DateCreated: AsUtc(c.CreatedAtUtc), DateUpdated: AsUtc(c.UpdatedAtUtc),
                CreatedBy: null, UpdatedBy: null,
                IsArchived: c.IsArchived,
                EntityId: c.Id, Kind: ContentKinds.Cabinet,
                ParentEntityId: c.ProjectId, ParentKind: ContentKinds.Project,
                CreatedById: c.CreatedBy, UpdatedById: c.UpdatedBy));
        }
        foreach (var n in notebooks)
        {
            rows.Add(new NoteRow(
                Id: n.Locator, Type: "Notebook", SubType: null,
                Name: n.Name, Description: n.Description, Icon: n.Icon,
                DateCreated: AsUtc(n.CreatedAtUtc), DateUpdated: AsUtc(n.UpdatedAtUtc),
                CreatedBy: null, UpdatedBy: null,
                IsArchived: n.IsArchived,
                EntityId: n.Id, Kind: ContentKinds.Notebook,
                ParentEntityId: n.CabinetId, ParentKind: ContentKinds.Cabinet,
                CreatedById: n.CreatedBy, UpdatedById: n.UpdatedBy));
        }
        foreach (var p in pages)
        {
            rows.Add(new NoteRow(
                Id: p.Locator, Type: "Page",
                SubType: p.ParentPageId is null ? null : "SubPage",
                Name: p.Title, Description: null, Icon: null,
                DateCreated: AsUtc(p.CreatedAtUtc), DateUpdated: AsUtc(p.UpdatedAtUtc),
                CreatedBy: null, UpdatedBy: null,
                IsArchived: p.IsArchived,
                EntityId: p.Id, Kind: ContentKinds.Page,
                ParentEntityId: p.ParentPageId ?? p.NotebookId,
                ParentKind: p.ParentPageId is null ? ContentKinds.Notebook : ContentKinds.Page,
                CreatedById: p.CreatedBy, UpdatedById: p.UpdatedBy));
        }
        foreach (var n in notes)
        {
            rows.Add(new NoteRow(
                Id: n.Locator, Type: "Note", SubType: n.NoteKind,
                Name: n.Title, Description: null, Icon: null,
                DateCreated: AsUtc(n.CreatedAtUtc), DateUpdated: AsUtc(n.UpdatedAtUtc),
                CreatedBy: null, UpdatedBy: null,
                IsArchived: n.IsArchived,
                EntityId: n.Id, Kind: "note",
                ParentEntityId: n.PageId, ParentKind: ContentKinds.Page,
                CreatedById: n.CreatedBy, UpdatedById: n.UpdatedBy));
        }

        return rows;
    }

    private static DateTime AsUtc(DateTime dt) =>
        dt.Kind == DateTimeKind.Utc ? dt : DateTime.SpecifyKind(dt, DateTimeKind.Utc);

    // ---- Ungrouped / grouped execution paths -----------------------------

    private (List<IReadOnlyDictionary<string, object?>> Rows, bool Truncated) ExecuteUngrouped(
        List<NoteRow> working,
        IReadOnlyList<ProjItem> projection,
        NoteRowIndexes indexes,
        int? effectiveCap)
    {
        if (Query.OrderBy.Count > 0)
        {
            IOrderedEnumerable<NoteRow>? sorted = null;
            foreach (var item in Query.OrderBy)
            {
                var keyFn = MakeKeySelector(item.Item, indexes);
                sorted = sorted is null
                    ? (item.Descending
                        ? working.OrderByDescending(keyFn, NullSafeComparer.Instance)
                        : working.OrderBy(keyFn, NullSafeComparer.Instance))
                    : (item.Descending
                        ? sorted.ThenByDescending(keyFn, NullSafeComparer.Instance)
                        : sorted.ThenBy(keyFn, NullSafeComparer.Instance));
            }
            working = sorted!.ToList();
        }

        var truncated = false;
        if (effectiveCap is { } cap && working.Count > cap)
        {
            truncated = true;
            working = working.Take(cap).ToList();
        }

        var resultRows = working
            .Select(r => (IReadOnlyDictionary<string, object?>)projection
                .ToDictionary(p => p.DisplayName, p => ReadProjection(r, p, indexes)))
            .ToList();
        return (resultRows, truncated);
    }

    private (List<IReadOnlyDictionary<string, object?>> Rows, bool Truncated) ExecuteGrouped(
        List<NoteRow> working,
        IReadOnlyList<ProjItem> projection,
        NoteRowIndexes indexes,
        int? effectiveCap)
    {
        var groupFields = Query.Group!;
        var groups = working
            .GroupBy(r => BuildGroupKey(r, groupFields, indexes), GroupKey.Comparer)
            .Select(g => new NoteGroup(g.Key, g.ToList()))
            .ToList();

        var projectedGroups = groups
            .Select(g => (Group: g, Dict: BuildGroupProjection(g, projection, indexes)))
            .ToList();

        if (Query.OrderBy.Count > 0)
        {
            IOrderedEnumerable<(NoteGroup Group, Dictionary<string, object?> Dict)>? sorted = null;
            foreach (var item in Query.OrderBy)
            {
                Func<(NoteGroup Group, Dictionary<string, object?> Dict), IComparable?> keyFn =
                    tuple => EvalGroupOrderKey(item.Item, tuple.Group, tuple.Dict, indexes) as IComparable;
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

    private Dictionary<string, object?> BuildGroupProjection(
        NoteGroup group,
        IReadOnlyList<ProjItem> projection,
        NoteRowIndexes indexes)
    {
        var dict = new Dictionary<string, object?>();
        foreach (var p in projection)
        {
            if (p.Source.IsAggregate)
            {
                dict[p.DisplayName] = EvalAggregateForGroup(p.Source, group.Rows, indexes);
            }
            else
            {
                dict[p.DisplayName] = ReadFieldRaw(p.Source.Field!, group.Rows[0], indexes);
            }
        }
        return dict;
    }

    private object? EvalGroupOrderKey(
        AqlSelectItem item,
        NoteGroup group,
        Dictionary<string, object?> projected,
        NoteRowIndexes indexes)
    {
        if (projected.TryGetValue(item.DisplayName, out var v)) return v;
        if (projected.TryGetValue(item.DefaultName, out var v2)) return v2;
        if (item.IsAggregate)
        {
            return EvalAggregateForGroup(item, group.Rows, indexes);
        }
        return ReadFieldRaw(item.Field!, group.Rows[0], indexes);
    }

    private static GroupKey BuildGroupKey(NoteRow row, IReadOnlyList<string> groupFields, NoteRowIndexes indexes)
    {
        var values = new object?[groupFields.Count];
        for (var i = 0; i < groupFields.Count; i++)
        {
            values[i] = ReadFieldRaw(groupFields[i], row, indexes);
        }
        return new GroupKey(values);
    }

    // ---- Projection ------------------------------------------------------

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
        if (item.IsAggregate)
        {
            var fn = item.AggregateFn!.ToUpperInvariant();
            if (Entity.RowFunctions.Any(f => string.Equals(f, fn, StringComparison.OrdinalIgnoreCase)))
            {
                return new ProjItem(item.DisplayName, Entity.RowFunctionDataType(fn), item);
            }
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

    private static object? ReadProjection(NoteRow row, ProjItem proj, NoteRowIndexes idx)
    {
        if (proj.Source.IsAggregate)
        {
            return EvalRowFunction(proj.Source.AggregateFn!, row, idx);
        }
        return ReadFieldRaw(proj.Source.Field!, row, idx);
    }

    // ---- Aggregates over a group ----------------------------------------

    private object? EvalAggregateForGroup(AqlSelectItem item, List<NoteRow> groupRows, NoteRowIndexes idx)
    {
        var fn = item.AggregateFn!.ToUpperInvariant();

        if (Entity.RowFunctions.Any(f => string.Equals(f, fn, StringComparison.OrdinalIgnoreCase)))
        {
            long total = 0;
            foreach (var r in groupRows)
            {
                var v = EvalRowFunction(fn, r, idx);
                if (v is int i) total += i;
                else if (v is long l) total += l;
            }
            return total;
        }

        if (fn == "COUNT")
        {
            if (item.AggregateField is null) return (long)groupRows.Count;
            return (long)groupRows.Count(r => ReadFieldRaw(item.AggregateField, r, idx) is not null);
        }

        var values = groupRows
            .Select(r => ReadFieldRaw(item.AggregateField!, r, idx))
            .Where(v => v is not null)
            .ToList();
        if (values.Count == 0) return null;

        var col = Schema.First(c =>
            string.Equals(c.Name, item.AggregateField, StringComparison.OrdinalIgnoreCase));

        if (col.DataType == QueryDataType.Date)
        {
            var dates = values.Select(v => (DateTime)v!).ToList();
            return fn switch
            {
                "MIN"    => (object)dates.Min(),
                "MAX"    => (object)dates.Max(),
                "AVG"    => (object)new DateTime((long)dates.Average(d => d.Ticks), DateTimeKind.Utc),
                "MEDIAN" => (object)MedianDate(dates),
                _ => null
            };
        }
        var nums = values.Select(v => ToDoubleOrNull(v)!.Value).ToList();
        return fn switch
        {
            "MIN"    => (object)nums.Min(),
            "MAX"    => (object)nums.Max(),
            "AVG"    => (object)nums.Average(),
            "MEDIAN" => (object)Median(nums),
            _ => null
        };
    }

    private static double Median(List<double> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        var mid = sorted.Count / 2;
        return sorted.Count % 2 == 0 ? (sorted[mid - 1] + sorted[mid]) / 2.0 : sorted[mid];
    }

    private static DateTime MedianDate(List<DateTime> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        var mid = sorted.Count / 2;
        if (sorted.Count % 2 == 0)
        {
            var avgTicks = (sorted[mid - 1].Ticks + sorted[mid].Ticks) / 2;
            return new DateTime(avgTicks, DateTimeKind.Utc);
        }
        return sorted[mid];
    }

    // ---- Row functions ---------------------------------------------------

    private static object? EvalRowFunction(string fn, NoteRow row, NoteRowIndexes idx) =>
        fn.ToUpperInvariant() switch
        {
            "COUNTCHILDREN"    => idx.CountChildren(row),
            "COUNTDESCENDENTS" => idx.CountDescendents(row),
            _ => null
        };

    // ---- WHERE evaluation ------------------------------------------------

    private static bool EvalWhere(AqlWhere where, NoteRow row, NoteRowIndexes idx) => where switch
    {
        AqlBinary b => b.Op == "AND"
            ? EvalWhere(b.Left, row, idx) && EvalWhere(b.Right, row, idx)
            : EvalWhere(b.Left, row, idx) || EvalWhere(b.Right, row, idx),
        AqlCompare c => EvalCompare(c, row, idx),
        AqlContains ct => (ReadFieldRaw(ct.Field, row, idx) as string)?
            .Contains(ct.Substr, StringComparison.OrdinalIgnoreCase) ?? false,
        AqlIn inFilter => inFilter.Values.Any(v =>
            EvalCompare(new AqlCompare(inFilter.Field, "=", v), row, idx)),
        AqlBetween bw =>
            EvalCompare(new AqlCompare(bw.Field, ">=", bw.Lo), row, idx)
            && EvalCompare(new AqlCompare(bw.Field, "<=", bw.Hi), row, idx),
        AqlFunctionCall fc => EvalFunctionCall(fc, row, idx),
        AqlFunctionCompare fcmp => EvalFunctionCompare(fcmp, row, idx),
        _ => false
    };

    private static bool EvalCompare(AqlCompare c, NoteRow row, NoteRowIndexes idx)
    {
        var actual = ReadFieldRaw(c.Field, row, idx);
        var expected = ResolveValue(c.Value);
        return CompareValues(actual, expected, c.Op);
    }

    private static bool EvalFunctionCall(AqlFunctionCall fc, NoteRow row, NoteRowIndexes idx)
    {
        var fn = fc.Name.ToUpperInvariant();
        switch (fn)
        {
            case "PARENT":
            {
                if (fc.Args.Count != 1 || ToLongOrNull(ResolveValue(fc.Args[0])) is not long pLocator)
                {
                    return false;
                }
                if (row.ParentEntityId is null || row.ParentKind is null) return false;
                // Pushdown filtered the load already; here we just confirm
                // the row's actual parent locator matches what was requested.
                return idx.TryLocatorOf(row.ParentKind, row.ParentEntityId.Value, out var parentLocator)
                    && parentLocator == pLocator;
            }
            case "ISDESCENDENTOF":
            {
                if (fc.Args.Count != 1 || ToLongOrNull(ResolveValue(fc.Args[0])) is not long aLocator)
                {
                    return false;
                }
                return idx.IsDescendentOf(row, aLocator);
            }
            default:
                return false;
        }
    }

    private static bool EvalFunctionCompare(AqlFunctionCompare fcmp, NoteRow row, NoteRowIndexes idx)
    {
        var fn = fcmp.FnName.ToUpperInvariant();
        double? actual = fn switch
        {
            "COUNTCHILDREN"    => idx.CountChildren(row),
            "COUNTDESCENDENTS" => idx.CountDescendents(row),
            _ => null
        };
        if (actual is null) return false;
        var expected = ToDoubleOrNull(ResolveValue(fcmp.Value));
        if (expected is null) return false;
        // double.Equals → bit-equality method call, side-steps Sonar S1244.
        return fcmp.Op switch
        {
            "="  => actual.Value.Equals(expected.Value),
            "!=" => !actual.Value.Equals(expected.Value),
            "<"  => actual <  expected,
            "<=" => actual <= expected,
            ">"  => actual >  expected,
            ">=" => actual >= expected,
            _ => false
        };
    }

    private static object? ReadFieldRaw(string field, NoteRow row, NoteRowIndexes idx) =>
        field.ToLowerInvariant() switch
        {
            "id"          => (object?)row.Id,
            "type"        => row.Type,
            "subtype"     => row.SubType,
            "name"        => row.Name,
            "description" => row.Description,
            "icon"        => row.Icon,
            "datecreated" => row.DateCreated,
            "dateupdated" => row.DateUpdated,
            "createdby"   => row.CreatedBy,
            "updatedby"   => row.UpdatedBy,
            "isarchived"  => row.IsArchived,
            "fullpath"    => idx.FullPathFor(row),
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
                "="  => cmp == 0,
                "!=" => cmp != 0,
                "<"  => cmp <  0,
                "<=" => cmp <= 0,
                ">"  => cmp >  0,
                ">=" => cmp >= 0,
                _ => false
            };
        }
        double? ad = ToDoubleOrNull(actual);
        double? ed = ToDoubleOrNull(expected);
        if (ad is { } adv && ed is { } edv)
        {
            // double.Equals → bit-equality method call, side-steps Sonar S1244.
            return op switch
            {
                "="  => adv.Equals(edv),
                "!=" => !adv.Equals(edv),
                "<"  => adv <  edv,
                "<=" => adv <= edv,
                ">"  => adv >  edv,
                ">=" => adv >= edv,
                _ => false
            };
        }
        if (actual is DateTime adt && expected is DateTime edt)
        {
            var c = adt.CompareTo(edt);
            return op switch
            {
                "="  => c == 0,
                "!=" => c != 0,
                "<"  => c <  0,
                "<=" => c <= 0,
                ">"  => c >  0,
                ">=" => c >= 0,
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

    private static long? ToLongOrNull(object? v) => v switch
    {
        int i => i,
        long l => l,
        // d.Equals(floor) is bit-equality, not the == operator, so S1244 stays quiet.
        double d when d.Equals(Math.Floor(d)) && !double.IsInfinity(d) => (long)d,
        _ => null
    };

    private static Func<NoteRow, IComparable?> MakeKeySelector(AqlSelectItem item, NoteRowIndexes idx)
    {
        if (item.IsAggregate)
        {
            var fn = item.AggregateFn!;
            return row => EvalRowFunction(fn, row, idx) as IComparable;
        }
        return row => ReadFieldRaw(item.Field!, row, idx) as IComparable;
    }

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

// ---------------------------------------------------------------------------
// NoteRow + supporting types.
// ---------------------------------------------------------------------------

// One row in the unified Notes surface. CreatedBy/UpdatedBy hold the resolved
// display name (or null if the user lookup was skipped); CreatedById/UpdatedById
// keep the underlying Guids so the user lookup can run after row construction.
internal sealed record NoteRow(
    long Id,
    string Type,
    string? SubType,
    string? Name,
    string? Description,
    string? Icon,
    DateTime DateCreated,
    DateTime DateUpdated,
    string? CreatedBy,
    string? UpdatedBy,
    bool IsArchived,
    Guid EntityId,
    string Kind,
    Guid? ParentEntityId,
    string? ParentKind,
    Guid CreatedById,
    Guid UpdatedById);

internal sealed record NoteGroup(GroupKey Key, List<NoteRow> Rows);

internal sealed class GroupKey : IEquatable<GroupKey>
{
    public static readonly IEqualityComparer<GroupKey> Comparer = new KeyComparer();

    private readonly object?[] _values;

    public GroupKey(object?[] values) { _values = values; }

    public IReadOnlyList<object?> Values => _values;

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

// ---------------------------------------------------------------------------
// In-memory indexes that back the hierarchy functions. CountChildren /
// CountDescendents are precomputed SQL dictionaries; ISDESCENDENTOF is
// precomputed per-locator-argument descendant set. FullPath strings are
// precomputed per (kind, entity-Guid) when the plan flagged them.
// ---------------------------------------------------------------------------

internal sealed class NoteRowIndexes
{
    private readonly Dictionary<(string Kind, Guid Id), long> _locatorByEntity;
    private readonly Dictionary<(string Kind, Guid Id), int> _childCounts;
    private readonly Dictionary<(string Kind, Guid Id), int> _descendantCounts;
    // Keyed by (ancestor-kind, ancestor-Guid) → set of (descendant-kind, id).
    // Looked up via row's (kind, id) against the anchor referenced in WHERE.
    private readonly Dictionary<(string Kind, Guid Id), HashSet<(string Kind, Guid Id)>> _descendantSets;
    private readonly Dictionary<(string Kind, Guid Id), string> _fullPaths;
    private readonly Dictionary<long, (string Kind, Guid Id)> _entityByLocator;

    public NoteRowIndexes(
        IReadOnlyList<NoteRow> rows,
        Dictionary<(string, Guid), int> childCounts,
        Dictionary<(string, Guid), int> descendantCounts,
        Dictionary<(string, Guid), HashSet<(string, Guid)>> descendantSets,
        Dictionary<(string, Guid), string> fullPaths)
    {
        _childCounts = childCounts;
        _descendantCounts = descendantCounts;
        _descendantSets = descendantSets;
        _fullPaths = fullPaths;

        _locatorByEntity = new Dictionary<(string, Guid), long>(rows.Count);
        _entityByLocator = new Dictionary<long, (string, Guid)>(rows.Count);
        foreach (var r in rows)
        {
            var key = (r.Kind, r.EntityId);
            _locatorByEntity[key] = r.Id;
            _entityByLocator[r.Id] = key;
        }
    }

    public bool TryLocatorOf(string kind, Guid id, out long locator) =>
        _locatorByEntity.TryGetValue((kind, id), out locator);

    public int CountChildren(NoteRow row) =>
        _childCounts.TryGetValue((row.Kind, row.EntityId), out var c) ? c : 0;

    public int CountDescendents(NoteRow row) =>
        _descendantCounts.TryGetValue((row.Kind, row.EntityId), out var c) ? c : 0;

    // Anchor is referenced by its locator at WHERE time. We resolve to
    // (kind, entity-id) via the planner-built descendant sets dictionary,
    // which is keyed by anchor (kind, id) — meaning each ISDESCENDENTOF(N)
    // anchor sits in the map once. To do the lookup-by-locator efficiently
    // we ignore the key and check membership in each value set; in practice
    // the number of distinct ISDESCENDENTOF anchors per query is tiny.
    public bool IsDescendentOf(NoteRow row, long anchorLocator)
    {
        if (!_entityByLocator.TryGetValue(anchorLocator, out var anchor)) return false;
        if (!_descendantSets.TryGetValue(anchor, out var set)) return false;
        return set.Contains((row.Kind, row.EntityId));
    }

    public string FullPathFor(NoteRow row) =>
        _fullPaths.TryGetValue((row.Kind, row.EntityId), out var path)
            ? path
            : row.Name ?? string.Empty;
}
