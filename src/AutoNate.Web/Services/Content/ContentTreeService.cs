using AutoNate.Web.Persistence;
using AutoNate.Web.Persistence.Scaffolded;
using Microsoft.EntityFrameworkCore;

namespace AutoNate.Web.Services.Content;

public sealed class ContentTreeService : IContentTreeService
{
    public async Task InsertSelfWithAncestorsAsync(
        AutoNateDbContext db, string kind, Guid id, CancellationToken ct)
    {
        var chain = await BuildAncestorChainViaDbAsync(db, kind, id, ct);
        foreach (var row in chain)
        {
            db.ContentAncestors.Add(row);
        }
        await db.SaveChangesAsync(ct);
    }

    // Recomputes content_ancestors for the moved root and every descendant.
    // Previous shape ran one query per node to collect, one DELETE per
    // descendant, and one parent walk (1+ queries per level) per descendant —
    // ~280 round trips on a modest cabinet subtree. The replacement is bounded
    // at ~10 round trips regardless of subtree size:
    //   1) one recursive CTE enumerates every (kind, id) descendant;
    //   2) at most 4 batched DELETEs (one per descendant kind) wipe the
    //      existing closure rows;
    //   3) one short walk above the root + one batched parent-lookup per
    //      descendant kind populates an in-memory (kind, id) → parent map,
    //      so each chain walk runs against memory not the DB;
    //   4) a single SaveChangesAsync batches every INSERT.
    public async Task RebuildAncestorsForSubtreeAsync(
        AutoNateDbContext db, string kind, Guid rootId, CancellationToken ct)
    {
        var descendants = await CollectSubtreeAsync(db, kind, rootId, ct);
        if (descendants.Count == 0)
        {
            return;
        }

        var idsByKind = descendants
            .GroupBy(d => d.Kind, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(d => d.Id).ToList(), StringComparer.Ordinal);

        // Wipe in bulk, one DELETE per kind.
        foreach (var (descendantKind, ids) in idsByKind)
        {
            var k = descendantKind;
            await db.ContentAncestors
                .Where(ca => ca.DescendantKind == k && ids.Contains(ca.DescendantId))
                .ExecuteDeleteAsync(ct);
        }

        // Build (kind, id) → (parent_kind, parent_id) for every node we'll
        // need to walk. Two sources:
        //   - The chain ABOVE the root (root's parent, grandparent, …, project).
        //     Walked once via ResolveParentAsync — bounded by tree depth (≤4).
        //   - The subtree nodes themselves, fetched per-kind in bulk.
        var parentMap = new Dictionary<(string Kind, Guid Id), (string Kind, Guid Id)>(
            ContentNodeComparer.Instance);
        await SeedAboveRootAsync(db, kind, rootId, parentMap, ct);
        await SeedSubtreeAsync(db, idsByKind, parentMap, ct);

        foreach (var (descendantKind, descendantId) in descendants)
        {
            foreach (var row in BuildAncestorChainInMemory(parentMap, descendantKind, descendantId))
            {
                db.ContentAncestors.Add(row);
            }
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteEntityAsync(
        AutoNateDbContext db, string kind, Guid id, CancellationToken ct)
    {
        await db.ContentAncestors
            .Where(ca =>
                (ca.DescendantKind == kind && ca.DescendantId == id) ||
                (ca.AncestorKind == kind && ca.AncestorId == id))
            .ExecuteDeleteAsync(ct);
    }

    // Walks parent FKs in code (kept simple and DB-agnostic) and returns a
    // list of ContentAncestor rows for the entity itself plus every ancestor
    // (depth 0 = self, depth 1 = direct parent, …, up to the project). Used
    // by InsertSelfWithAncestorsAsync which only needs the chain for a single
    // new entity — the per-level DB queries are bounded by tree depth and
    // amortized over a single create.
    private static async Task<List<ContentAncestor>> BuildAncestorChainViaDbAsync(
        AutoNateDbContext db, string startKind, Guid startId, CancellationToken ct)
    {
        var rows = new List<ContentAncestor>();
        var depth = 0;
        var currentKind = startKind;
        var currentId = startId;
        const int maxDepth = 256;
        while (true)
        {
            rows.Add(new ContentAncestor
            {
                DescendantKind = startKind,
                DescendantId = startId,
                AncestorKind = currentKind,
                AncestorId = currentId,
                Depth = depth
            });

            if (depth >= maxDepth)
            {
                break;
            }

            var (parentKind, parentId) = await ResolveParentAsync(db, currentKind, currentId, ct);
            if (parentKind is null || parentId is null)
            {
                break;
            }
            currentKind = parentKind;
            currentId = parentId.Value;
            depth++;
        }
        return rows;
    }

    private static async Task<(string? Kind, Guid? Id)> ResolveParentAsync(
        AutoNateDbContext db, string kind, Guid id, CancellationToken ct)
    {
        switch (kind)
        {
            case ContentKinds.Project:
                return (null, null);
            case ContentKinds.Cabinet:
            {
                var pid = await db.Cabinets.AsNoTracking()
                    .Where(c => c.Id == id)
                    .Select(c => (Guid?)c.ProjectId)
                    .FirstOrDefaultAsync(ct);
                return pid is null ? (null, null) : (ContentKinds.Project, pid);
            }
            case ContentKinds.Notebook:
            {
                var cid = await db.Notebooks.AsNoTracking()
                    .Where(n => n.Id == id)
                    .Select(n => (Guid?)n.CabinetId)
                    .FirstOrDefaultAsync(ct);
                return cid is null ? (null, null) : (ContentKinds.Cabinet, cid);
            }
            case ContentKinds.Page:
            {
                var page = await db.Pages.AsNoTracking()
                    .Where(p => p.Id == id)
                    .Select(p => new { p.NotebookId, p.ParentPageId })
                    .FirstOrDefaultAsync(ct);
                if (page is null) return (null, null);
                if (page.ParentPageId is { } parent)
                {
                    return (ContentKinds.Page, parent);
                }
                return (ContentKinds.Notebook, page.NotebookId);
            }
            case ContentKinds.Folder:
            {
                var folder = await db.Folders.AsNoTracking()
                    .Where(f => f.Id == id)
                    .Select(f => new { f.ProjectId, f.ParentFolderId })
                    .FirstOrDefaultAsync(ct);
                if (folder is null) return (null, null);
                if (folder.ParentFolderId is { } parent)
                {
                    return (ContentKinds.Folder, parent);
                }
                return (ContentKinds.Project, folder.ProjectId);
            }
            case ContentKinds.Document:
            {
                var doc = await db.Documents.AsNoTracking()
                    .Where(d => d.Id == id)
                    .Select(d => new { d.ProjectId, d.FolderId })
                    .FirstOrDefaultAsync(ct);
                if (doc is null) return (null, null);
                if (doc.FolderId is { } parent)
                {
                    return (ContentKinds.Folder, parent);
                }
                return (ContentKinds.Project, doc.ProjectId);
            }
            default:
                return (null, null);
        }
    }

    // Single recursive CTE enumerates every descendant under the root in one
    // round trip. The inner SELECT joins parent links from cabinets +
    // notebooks + pages; pages' parent toggles between 'page' (when nested)
    // and 'notebook'. Projects are roots and only appear if rootKind=project,
    // in which case they're the seed row of the recursion.
    private static async Task<List<(string Kind, Guid Id)>> CollectSubtreeAsync(
        AutoNateDbContext db, string rootKind, Guid rootId, CancellationToken ct)
    {
        const string sql = """
            WITH RECURSIVE subtree (kind, id) AS (
                SELECT {0}::text AS kind, {1}::uuid AS id
                UNION ALL
                SELECT child.kind, child.id
                FROM subtree s
                JOIN (
                    SELECT 'cabinet'::text AS kind, id, 'project'::text AS parent_kind, project_id AS parent_id
                    FROM cabinets
                    UNION ALL
                    SELECT 'notebook'::text, id, 'cabinet'::text, cabinet_id
                    FROM notebooks
                    UNION ALL
                    SELECT 'page'::text, id,
                           CASE WHEN parent_page_id IS NOT NULL THEN 'page'::text ELSE 'notebook'::text END,
                           COALESCE(parent_page_id, notebook_id)
                    FROM pages
                    UNION ALL
                    SELECT 'folder'::text, id,
                           CASE WHEN parent_folder_id IS NOT NULL THEN 'folder'::text ELSE 'project'::text END,
                           COALESCE(parent_folder_id, project_id)
                    FROM folders
                    UNION ALL
                    SELECT 'document'::text, id,
                           CASE WHEN folder_id IS NOT NULL THEN 'folder'::text ELSE 'project'::text END,
                           COALESCE(folder_id, project_id)
                    FROM documents
                ) AS child ON child.parent_kind = s.kind AND child.parent_id = s.id
            )
            SELECT kind AS "Kind", id AS "Id" FROM subtree
            """;

        var rows = await db.Database
            .SqlQueryRaw<SubtreeNode>(sql, rootKind, rootId)
            .ToListAsync(ct);
        return rows.Select(r => (r.Kind, r.Id)).ToList();
    }

    private static async Task SeedAboveRootAsync(
        AutoNateDbContext db, string rootKind, Guid rootId,
        Dictionary<(string Kind, Guid Id), (string Kind, Guid Id)> parentMap,
        CancellationToken ct)
    {
        var currentKind = rootKind;
        var currentId = rootId;
        const int maxDepth = 256;
        for (var d = 0; d < maxDepth; d++)
        {
            var (parentKind, parentId) = await ResolveParentAsync(db, currentKind, currentId, ct);
            if (parentKind is null || parentId is null) break;
            parentMap[(currentKind, currentId)] = (parentKind, parentId.Value);
            currentKind = parentKind;
            currentId = parentId.Value;
        }
    }

    private static async Task SeedSubtreeAsync(
        AutoNateDbContext db,
        Dictionary<string, List<Guid>> idsByKind,
        Dictionary<(string Kind, Guid Id), (string Kind, Guid Id)> parentMap,
        CancellationToken ct)
    {
        if (idsByKind.TryGetValue(ContentKinds.Cabinet, out var cabinetIds) && cabinetIds.Count > 0)
        {
            var rows = await db.Cabinets.AsNoTracking()
                .Where(c => cabinetIds.Contains(c.Id))
                .Select(c => new { c.Id, c.ProjectId })
                .ToListAsync(ct);
            foreach (var r in rows)
            {
                parentMap[(ContentKinds.Cabinet, r.Id)] = (ContentKinds.Project, r.ProjectId);
            }
        }
        if (idsByKind.TryGetValue(ContentKinds.Notebook, out var notebookIds) && notebookIds.Count > 0)
        {
            var rows = await db.Notebooks.AsNoTracking()
                .Where(n => notebookIds.Contains(n.Id))
                .Select(n => new { n.Id, n.CabinetId })
                .ToListAsync(ct);
            foreach (var r in rows)
            {
                parentMap[(ContentKinds.Notebook, r.Id)] = (ContentKinds.Cabinet, r.CabinetId);
            }
        }
        if (idsByKind.TryGetValue(ContentKinds.Page, out var pageIds) && pageIds.Count > 0)
        {
            var rows = await db.Pages.AsNoTracking()
                .Where(p => pageIds.Contains(p.Id))
                .Select(p => new { p.Id, p.NotebookId, p.ParentPageId })
                .ToListAsync(ct);
            foreach (var r in rows)
            {
                parentMap[(ContentKinds.Page, r.Id)] = r.ParentPageId is { } pp
                    ? (ContentKinds.Page, pp)
                    : (ContentKinds.Notebook, r.NotebookId);
            }
        }
        if (idsByKind.TryGetValue(ContentKinds.Folder, out var folderIds) && folderIds.Count > 0)
        {
            var rows = await db.Folders.AsNoTracking()
                .Where(f => folderIds.Contains(f.Id))
                .Select(f => new { f.Id, f.ProjectId, f.ParentFolderId })
                .ToListAsync(ct);
            foreach (var r in rows)
            {
                parentMap[(ContentKinds.Folder, r.Id)] = r.ParentFolderId is { } pf
                    ? (ContentKinds.Folder, pf)
                    : (ContentKinds.Project, r.ProjectId);
            }
        }
        if (idsByKind.TryGetValue(ContentKinds.Document, out var documentIds) && documentIds.Count > 0)
        {
            var rows = await db.Documents.AsNoTracking()
                .Where(d => documentIds.Contains(d.Id))
                .Select(d => new { d.Id, d.ProjectId, d.FolderId })
                .ToListAsync(ct);
            foreach (var r in rows)
            {
                parentMap[(ContentKinds.Document, r.Id)] = r.FolderId is { } fid
                    ? (ContentKinds.Folder, fid)
                    : (ContentKinds.Project, r.ProjectId);
            }
        }
        // Projects are roots — they never have a parent entry, which makes
        // BuildAncestorChainInMemory terminate when it walks into one.
    }

    private static List<ContentAncestor> BuildAncestorChainInMemory(
        Dictionary<(string Kind, Guid Id), (string Kind, Guid Id)> parentMap,
        string startKind, Guid startId)
    {
        var rows = new List<ContentAncestor>();
        var depth = 0;
        var currentKind = startKind;
        var currentId = startId;
        const int maxDepth = 256;
        while (true)
        {
            rows.Add(new ContentAncestor
            {
                DescendantKind = startKind,
                DescendantId = startId,
                AncestorKind = currentKind,
                AncestorId = currentId,
                Depth = depth
            });
            if (depth >= maxDepth) break;
            if (!parentMap.TryGetValue((currentKind, currentId), out var parent))
            {
                break;
            }
            currentKind = parent.Kind;
            currentId = parent.Id;
            depth++;
        }
        return rows;
    }

    private sealed class ContentNodeComparer : IEqualityComparer<(string Kind, Guid Id)>
    {
        public static readonly ContentNodeComparer Instance = new();
        public bool Equals((string Kind, Guid Id) x, (string Kind, Guid Id) y) =>
            string.Equals(x.Kind, y.Kind, StringComparison.Ordinal) && x.Id == y.Id;
        public int GetHashCode((string Kind, Guid Id) obj) =>
            HashCode.Combine(obj.Kind, obj.Id);
    }

    // Result row for the recursive-CTE SqlQueryRaw — column aliases in the
    // SQL match these property names. Public to satisfy EF Core's reflection.
    public sealed record SubtreeNode(string Kind, Guid Id);
}
