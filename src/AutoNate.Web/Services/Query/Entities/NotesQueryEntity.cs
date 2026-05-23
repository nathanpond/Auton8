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
// Execution is in-memory: the content hierarchy is configuration-scale (not
// hot-path), and the row functions (COUNTCHILDREN, COUNTDESCENDENTS, FullPath)
// span heterogeneous tables, which makes a single SQL projection awkward.
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
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        // Build visibility filters per kind. Notes inherit from their page.
        var projectAccess  = await _contentAuthorizer.GetAllowedIdsAsync(actor, ContentKinds.Project,  Actions.View, cancellationToken);
        var cabinetAccess  = await _contentAuthorizer.GetAllowedIdsAsync(actor, ContentKinds.Cabinet,  Actions.View, cancellationToken);
        var notebookAccess = await _contentAuthorizer.GetAllowedIdsAsync(actor, ContentKinds.Notebook, Actions.View, cancellationToken);
        var pageAccess     = await _contentAuthorizer.GetAllowedIdsAsync(actor, ContentKinds.Page,     Actions.View, cancellationToken);

        var projects = await LoadVisible(db.Projects.AsNoTracking(), projectAccess,
            (q, ids) => q.Where(p => ids.Contains(p.Id)), cancellationToken);
        var cabinets = await LoadVisible(db.Cabinets.AsNoTracking(), cabinetAccess,
            (q, ids) => q.Where(c => ids.Contains(c.Id)), cancellationToken);
        var notebooks = await LoadVisible(db.Notebooks.AsNoTracking(), notebookAccess,
            (q, ids) => q.Where(n => ids.Contains(n.Id)), cancellationToken);
        var pages = await LoadVisible(db.Pages.AsNoTracking(), pageAccess,
            (q, ids) => q.Where(p => ids.Contains(p.Id)), cancellationToken);

        // Notes are visible iff their parent page is visible.
        var visiblePageIds = pages.Select(p => p.Id).ToHashSet();
        List<Note> notes;
        if (pageAccess.Unrestricted)
        {
            notes = await db.Notes.AsNoTracking().ToListAsync(cancellationToken);
        }
        else if (visiblePageIds.Count == 0)
        {
            notes = new List<Note>();
        }
        else
        {
            notes = await db.Notes.AsNoTracking()
                .Where(n => visiblePageIds.Contains(n.PageId))
                .ToListAsync(cancellationToken);
        }

        // Resolve display names for CreatedBy/UpdatedBy from local_users in
        // a single round trip, then lookup per-row.
        var userIds = new HashSet<Guid>();
        foreach (var p in projects)  { userIds.Add(p.CreatedBy); userIds.Add(p.UpdatedBy); }
        foreach (var c in cabinets)  { userIds.Add(c.CreatedBy); userIds.Add(c.UpdatedBy); }
        foreach (var n in notebooks) { userIds.Add(n.CreatedBy); userIds.Add(n.UpdatedBy); }
        foreach (var p in pages)     { userIds.Add(p.CreatedBy); userIds.Add(p.UpdatedBy); }
        foreach (var n in notes)     { userIds.Add(n.CreatedBy); userIds.Add(n.UpdatedBy); }

        var userDisplay = await db.LocalUsers.AsNoTracking()
            .Where(u => userIds.Contains(u.UserId))
            .Select(u => new { u.UserId, u.FirstName, u.LastName, u.Username })
            .ToListAsync(cancellationToken);
        var displayByUser = userDisplay.ToDictionary(
            u => u.UserId,
            u => FormatDisplayName(u.FirstName, u.LastName, u.Username));

        // content_ancestors covers project/cabinet/notebook/page. Notes are
        // not in the closure — we synthesize their ancestor edges via PageId.
        var ancestors = await db.ContentAncestors.AsNoTracking()
            .ToListAsync(cancellationToken);

        // Map each entity to a uniform NoteRow.
        var rows = new List<NoteRow>(projects.Count + cabinets.Count + notebooks.Count + pages.Count + notes.Count);
        foreach (var p in projects)
        {
            rows.Add(new NoteRow(
                Id: p.Locator,
                Type: "Project",
                SubType: null,
                Name: p.Name,
                Description: p.Description,
                Icon: null,
                DateCreated: AsUtc(p.CreatedAtUtc),
                DateUpdated: AsUtc(p.UpdatedAtUtc),
                CreatedBy: DisplayOrNull(displayByUser, p.CreatedBy),
                UpdatedBy: DisplayOrNull(displayByUser, p.UpdatedBy),
                IsArchived: p.IsArchived,
                EntityId: p.Id,
                Kind: ContentKinds.Project,
                ParentEntityId: null,
                ParentKind: null));
        }
        foreach (var c in cabinets)
        {
            rows.Add(new NoteRow(
                Id: c.Locator,
                Type: "Cabinet",
                SubType: null,
                Name: c.Name,
                Description: c.Description,
                Icon: c.Icon,
                DateCreated: AsUtc(c.CreatedAtUtc),
                DateUpdated: AsUtc(c.UpdatedAtUtc),
                CreatedBy: DisplayOrNull(displayByUser, c.CreatedBy),
                UpdatedBy: DisplayOrNull(displayByUser, c.UpdatedBy),
                IsArchived: c.IsArchived,
                EntityId: c.Id,
                Kind: ContentKinds.Cabinet,
                ParentEntityId: c.ProjectId,
                ParentKind: ContentKinds.Project));
        }
        foreach (var n in notebooks)
        {
            rows.Add(new NoteRow(
                Id: n.Locator,
                Type: "Notebook",
                SubType: null,
                Name: n.Name,
                Description: n.Description,
                Icon: n.Icon,
                DateCreated: AsUtc(n.CreatedAtUtc),
                DateUpdated: AsUtc(n.UpdatedAtUtc),
                CreatedBy: DisplayOrNull(displayByUser, n.CreatedBy),
                UpdatedBy: DisplayOrNull(displayByUser, n.UpdatedBy),
                IsArchived: n.IsArchived,
                EntityId: n.Id,
                Kind: ContentKinds.Notebook,
                ParentEntityId: n.CabinetId,
                ParentKind: ContentKinds.Cabinet));
        }
        foreach (var p in pages)
        {
            rows.Add(new NoteRow(
                Id: p.Locator,
                Type: "Page",
                // A page nested under another page (sub-page) gets a "SubPage"
                // subtype so users can distinguish top-level pages from
                // nested ones without joining ParentPageId.
                SubType: p.ParentPageId is null ? null : "SubPage",
                Name: p.Title,
                Description: null,
                Icon: null,
                DateCreated: AsUtc(p.CreatedAtUtc),
                DateUpdated: AsUtc(p.UpdatedAtUtc),
                CreatedBy: DisplayOrNull(displayByUser, p.CreatedBy),
                UpdatedBy: DisplayOrNull(displayByUser, p.UpdatedBy),
                IsArchived: p.IsArchived,
                EntityId: p.Id,
                Kind: ContentKinds.Page,
                ParentEntityId: p.ParentPageId ?? p.NotebookId,
                ParentKind: p.ParentPageId is null ? ContentKinds.Notebook : ContentKinds.Page));
        }
        foreach (var n in notes)
        {
            rows.Add(new NoteRow(
                Id: n.Locator,
                Type: "Note",
                SubType: n.NoteKind,
                Name: n.Title,
                Description: null,
                Icon: null,
                DateCreated: AsUtc(n.CreatedAtUtc),
                DateUpdated: AsUtc(n.UpdatedAtUtc),
                CreatedBy: DisplayOrNull(displayByUser, n.CreatedBy),
                UpdatedBy: DisplayOrNull(displayByUser, n.UpdatedBy),
                IsArchived: n.IsArchived,
                EntityId: n.Id,
                Kind: "note",
                ParentEntityId: n.PageId,
                ParentKind: ContentKinds.Page));
        }

        // Indexes used by PARENT(), ISDESCENDENTOF(), COUNTCHILDREN(),
        // COUNTDESCENDENTS(), and FullPath. All keyed by (kind, entity-Guid).
        var indexes = NoteRowIndexes.Build(rows, ancestors);

        // Apply WHERE.
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

    // Build one output row per distinct combination of GROUP() column values.
    // Aggregates in COLUMNS/ORDER BY are evaluated against each group's
    // member rows. ORDER BY may reference grouped columns directly or
    // aggregates; we evaluate either against the group.
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

        // Project each group up front so ORDER BY (when it references a
        // projected column) can reuse the value; ORDER BY items that aren't
        // in COLUMNS get evaluated on demand.
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
                // Non-aggregate columns must be grouped columns (validator
                // enforces this) — all rows in the group share the value,
                // so read from any member.
                dict[p.DisplayName] = ReadFieldRaw(p.Source.Field!, group.Rows[0], indexes);
            }
        }
        return dict;
    }

    // ORDER BY key for a grouped row: prefer the projected value when the
    // item is already in COLUMNS (matches by alias / canonical name), else
    // evaluate it directly against the group.
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

    private GroupKey BuildGroupKey(NoteRow row, IReadOnlyList<string> groupFields, NoteRowIndexes indexes)
    {
        var values = new object?[groupFields.Count];
        for (var i = 0; i < groupFields.Count; i++)
        {
            values[i] = ReadFieldRaw(groupFields[i], row, indexes);
        }
        return new GroupKey(values);
    }

    // ---- Helpers ---------------------------------------------------------

    // Apply a per-kind visibility filter from IContentAuthorizer to an EF
    // queryable. Unrestricted callers (super-admin) get the full set; empty
    // access shortcuts to an empty list without a round trip.
    private static async Task<List<T>> LoadVisible<T>(
        IQueryable<T> source,
        ContentAccessSet access,
        Func<IQueryable<T>, IReadOnlySet<Guid>, IQueryable<T>> applyFilter,
        CancellationToken ct) where T : class
    {
        if (access.Unrestricted)
        {
            return await source.ToListAsync(ct);
        }
        if (access.AllowedIds.Count == 0)
        {
            return new List<T>();
        }
        return await applyFilter(source, access.AllowedIds).ToListAsync(ct);
    }

    private static DateTime AsUtc(DateTime dt) =>
        dt.Kind == DateTimeKind.Utc ? dt : DateTime.SpecifyKind(dt, DateTimeKind.Utc);

    private static string? DisplayOrNull(Dictionary<Guid, string> map, Guid id) =>
        map.TryGetValue(id, out var name) ? name : null;

    private static string FormatDisplayName(string firstName, string lastName, string username)
    {
        var combined = $"{firstName} {lastName}".Trim();
        return string.IsNullOrEmpty(combined) ? username : combined;
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
            // Entity-specific row functions (COUNTCHILDREN, COUNTDESCENDENTS):
            // per-row scalar, type comes from the entity declaration.
            if (Entity.RowFunctions.Any(f => string.Equals(f, fn, StringComparison.OrdinalIgnoreCase)))
            {
                return new ProjItem(item.DisplayName, Entity.RowFunctionDataType(fn), item);
            }
            // Standard aggregates: COUNT → Number; MIN/MAX/AVG/MEDIAN inherit
            // the underlying column type (validator already restricted these
            // to numeric or date columns).
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

    private object? ReadProjection(NoteRow row, ProjItem proj, NoteRowIndexes idx)
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

        // Entity row functions inside a GROUP query are evaluated per row and
        // summed across the group — the only meaning that survives grouping
        // for an integer count is the total.
        if (Entity.RowFunctions.Any(f => string.Equals(f, fn, StringComparison.OrdinalIgnoreCase)))
        {
            long total = 0;
            foreach (var r in groupRows)
            {
                if (EvalRowFunction(fn, r, idx) is int i) total += i;
                else if (EvalRowFunction(fn, r, idx) is long l) total += l;
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
        // Numeric (validator constrains MIN/MAX/AVG/MEDIAN to Number or Date).
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

    private bool EvalWhere(AqlWhere where, NoteRow row, NoteRowIndexes idx) => where switch
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

    private bool EvalCompare(AqlCompare c, NoteRow row, NoteRowIndexes idx)
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
                if (!idx.TryLocatorOf(row.ParentKind, row.ParentEntityId.Value, out var parentLocator))
                {
                    return false;
                }
                return parentLocator == pLocator;
            }
            case "ISDESCENDENTOF":
            {
                if (fc.Args.Count != 1 || ToLongOrNull(ResolveValue(fc.Args[0])) is not long aLocator)
                {
                    return false;
                }
                if (!idx.TryEntityByLocator(aLocator, out var ancestorKind, out var ancestorId))
                {
                    return false;
                }
                return idx.IsDescendentOf(row, ancestorKind, ancestorId);
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
        return fcmp.Op switch
        {
            "="  => actual == expected,
            "!=" => actual != expected,
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
            return op switch
            {
                "="  => adv == edv,
                "!=" => adv != edv,
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
        double d when d == Math.Floor(d) && !double.IsInfinity(d) => (long)d,
        _ => null
    };

    private Func<NoteRow, IComparable?> MakeKeySelector(AqlSelectItem item, NoteRowIndexes idx)
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

// A group bucket: the key (one entry per GROUP() column) plus the rows that
// fell into it. Aggregates over the bucket are computed by the executor.
internal sealed record NoteGroup(GroupKey Key, List<NoteRow> Rows);

// Equality-comparable group key over a sequence of column values. Uses
// case-insensitive comparison for strings so `Type = "Project"` and
// `Type = "project"` collapse into the same bucket.
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

// One row in the unified Notes surface, normalized across Projects/Cabinets/
// Notebooks/Pages/Notes. EntityId/Kind point back to the source row so the
// indexes can answer hierarchy questions; ParentEntityId/ParentKind are the
// immediate parent (null only for Projects).
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
    string? ParentKind);

// Pre-computed lookups that back the hierarchy functions. Built once per
// query — the dataset is configuration-scale, so the cost is negligible.
internal sealed class NoteRowIndexes
{
    // (kind, entity-Guid) -> row
    private readonly Dictionary<(string Kind, Guid Id), NoteRow> _byEntity;
    // (kind, entity-Guid) -> locator
    private readonly Dictionary<(string Kind, Guid Id), long> _locatorByEntity;
    // locator -> (kind, entity-Guid)
    private readonly Dictionary<long, (string Kind, Guid Id)> _entityByLocator;
    // (parent-kind, parent-Guid) -> immediate children
    private readonly Dictionary<(string Kind, Guid Id), List<NoteRow>> _childrenByParent;
    // (descendant-kind, descendant-Guid) -> set of (ancestor-kind, ancestor-Guid)
    private readonly Dictionary<(string Kind, Guid Id), HashSet<(string, Guid)>> _ancestorsOf;

    private NoteRowIndexes(
        Dictionary<(string, Guid), NoteRow> byEntity,
        Dictionary<(string, Guid), long> locatorByEntity,
        Dictionary<long, (string, Guid)> entityByLocator,
        Dictionary<(string, Guid), List<NoteRow>> childrenByParent,
        Dictionary<(string, Guid), HashSet<(string, Guid)>> ancestorsOf)
    {
        _byEntity = byEntity;
        _locatorByEntity = locatorByEntity;
        _entityByLocator = entityByLocator;
        _childrenByParent = childrenByParent;
        _ancestorsOf = ancestorsOf;
    }

    public static NoteRowIndexes Build(
        IReadOnlyList<NoteRow> rows,
        IReadOnlyList<ContentAncestor> ancestors)
    {
        var byEntity = new Dictionary<(string, Guid), NoteRow>();
        var locatorByEntity = new Dictionary<(string, Guid), long>();
        var entityByLocator = new Dictionary<long, (string, Guid)>();
        var children = new Dictionary<(string, Guid), List<NoteRow>>();
        var ancestorsOf = new Dictionary<(string, Guid), HashSet<(string, Guid)>>();

        foreach (var r in rows)
        {
            var key = (r.Kind, r.EntityId);
            byEntity[key] = r;
            locatorByEntity[key] = r.Id;
            // Locator is unique across kinds — overwrite is fine but won't happen.
            entityByLocator[r.Id] = key;
            if (r.ParentEntityId is { } pid && r.ParentKind is { } pk)
            {
                var pkey = (pk, pid);
                if (!children.TryGetValue(pkey, out var list))
                {
                    list = new List<NoteRow>();
                    children[pkey] = list;
                }
                list.Add(r);
            }
        }

        // Seed ancestor sets from content_ancestors (covers project/cabinet/
        // notebook/page including depth-0 self rows).
        foreach (var a in ancestors)
        {
            var dkey = (a.DescendantKind, a.DescendantId);
            if (!ancestorsOf.TryGetValue(dkey, out var set))
            {
                set = new HashSet<(string, Guid)>();
                ancestorsOf[dkey] = set;
            }
            // Skip depth-0 self rows — "descendant of self" is false.
            if (a.Depth == 0) continue;
            set.Add((a.AncestorKind, a.AncestorId));
        }

        // Notes are not in content_ancestors. Synthesize: a note's ancestors
        // are its page + all of that page's ancestors.
        foreach (var r in rows)
        {
            if (r.Kind != "note") continue;
            var nkey = (r.Kind, r.EntityId);
            if (!ancestorsOf.TryGetValue(nkey, out var set))
            {
                set = new HashSet<(string, Guid)>();
                ancestorsOf[nkey] = set;
            }
            if (r.ParentEntityId is { } pid && r.ParentKind is { } pk)
            {
                set.Add((pk, pid));
                var pageKey = (pk, pid);
                if (ancestorsOf.TryGetValue(pageKey, out var pageAncestors))
                {
                    foreach (var pa in pageAncestors) set.Add(pa);
                }
            }
        }

        return new NoteRowIndexes(byEntity, locatorByEntity, entityByLocator, children, ancestorsOf);
    }

    public bool TryLocatorOf(string kind, Guid id, out long locator) =>
        _locatorByEntity.TryGetValue((kind, id), out locator);

    public bool TryEntityByLocator(long locator, out string kind, out Guid id)
    {
        if (_entityByLocator.TryGetValue(locator, out var pair))
        {
            kind = pair.Kind;
            id = pair.Id;
            return true;
        }
        kind = string.Empty;
        id = Guid.Empty;
        return false;
    }

    public int CountChildren(NoteRow row) =>
        _childrenByParent.TryGetValue((row.Kind, row.EntityId), out var list) ? list.Count : 0;

    public int CountDescendents(NoteRow row)
    {
        var count = 0;
        var stack = new Stack<(string Kind, Guid Id)>();
        stack.Push((row.Kind, row.EntityId));
        while (stack.Count > 0)
        {
            var cur = stack.Pop();
            if (!_childrenByParent.TryGetValue(cur, out var list)) continue;
            foreach (var child in list)
            {
                count++;
                stack.Push((child.Kind, child.EntityId));
            }
        }
        return count;
    }

    public bool IsDescendentOf(NoteRow row, string ancestorKind, Guid ancestorId)
    {
        var key = (row.Kind, row.EntityId);
        return _ancestorsOf.TryGetValue(key, out var set)
            && set.Contains((ancestorKind, ancestorId));
    }

    public string FullPathFor(NoteRow row)
    {
        // Walk from the row up to its top-level ancestor, collecting names.
        var chain = new List<string>();
        var current = row;
        var safety = 0;
        while (current is not null && safety++ < 1024)
        {
            chain.Add(current.Name ?? string.Empty);
            if (current.ParentEntityId is { } pid && current.ParentKind is { } pk
                && _byEntity.TryGetValue((pk, pid), out var parent))
            {
                current = parent;
            }
            else
            {
                current = null;
            }
        }
        chain.Reverse();
        return string.Join(" / ", chain);
    }
}
