using AutoNate.Web.Persistence;
using AutoNate.Web.Persistence.Scaffolded;
using Microsoft.EntityFrameworkCore;

namespace AutoNate.Web.Services.Content;

public sealed class ContentTreeService : IContentTreeService
{
    public async Task InsertSelfWithAncestorsAsync(
        AutoNateDbContext db, string kind, Guid id, CancellationToken ct)
    {
        var chain = await BuildAncestorChainAsync(db, kind, id, ct);
        foreach (var row in chain)
        {
            db.ContentAncestors.Add(row);
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task RebuildAncestorsForSubtreeAsync(
        AutoNateDbContext db, string kind, Guid rootId, CancellationToken ct)
    {
        // Collect every descendant in the subtree (kind+id pairs).
        var descendants = await CollectSubtreeAsync(db, kind, rootId, ct);

        // Wipe their existing ancestor rows in bulk.
        foreach (var (descendantKind, descendantId) in descendants)
        {
            var k = descendantKind;
            var i = descendantId;
            await db.ContentAncestors
                .Where(ca => ca.DescendantKind == k && ca.DescendantId == i)
                .ExecuteDeleteAsync(ct);
        }

        // Recompute each row from the current entity state.
        foreach (var (descendantKind, descendantId) in descendants)
        {
            var chain = await BuildAncestorChainAsync(db, descendantKind, descendantId, ct);
            foreach (var row in chain)
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
    // (depth 0 = self, depth 1 = direct parent, …, up to the project).
    private static async Task<List<ContentAncestor>> BuildAncestorChainAsync(
        AutoNateDbContext db, string startKind, Guid startId, CancellationToken ct)
    {
        var rows = new List<ContentAncestor>();
        var depth = 0;
        var currentKind = startKind;
        var currentId = startId;
        // Guardrail: refuses to walk past a generous depth in case the page
        // graph ever has a cycle (the CHECK constraint on pages prevents the
        // direct self-cycle case, but defence in depth).
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
                // Pages nest: prefer parent page when present, fall back to
                // the notebook so the chain always reaches the project.
                if (page.ParentPageId is { } parent)
                {
                    return (ContentKinds.Page, parent);
                }
                return (ContentKinds.Notebook, page.NotebookId);
            }
            default:
                return (null, null);
        }
    }

    private static async Task<List<(string Kind, Guid Id)>> CollectSubtreeAsync(
        AutoNateDbContext db, string rootKind, Guid rootId, CancellationToken ct)
    {
        var result = new List<(string, Guid)> { (rootKind, rootId) };
        switch (rootKind)
        {
            case ContentKinds.Project:
            {
                var cabinetIds = await db.Cabinets.AsNoTracking()
                    .Where(c => c.ProjectId == rootId)
                    .Select(c => c.Id)
                    .ToListAsync(ct);
                foreach (var cid in cabinetIds)
                {
                    result.AddRange(await CollectSubtreeAsync(db, ContentKinds.Cabinet, cid, ct));
                }
                break;
            }
            case ContentKinds.Cabinet:
            {
                var notebookIds = await db.Notebooks.AsNoTracking()
                    .Where(n => n.CabinetId == rootId)
                    .Select(n => n.Id)
                    .ToListAsync(ct);
                foreach (var nid in notebookIds)
                {
                    result.AddRange(await CollectSubtreeAsync(db, ContentKinds.Notebook, nid, ct));
                }
                break;
            }
            case ContentKinds.Notebook:
            {
                // Root pages of the notebook only — child pages are reached
                // recursively through the Page branch below.
                var rootPageIds = await db.Pages.AsNoTracking()
                    .Where(p => p.NotebookId == rootId && p.ParentPageId == null)
                    .Select(p => p.Id)
                    .ToListAsync(ct);
                foreach (var pid in rootPageIds)
                {
                    result.AddRange(await CollectSubtreeAsync(db, ContentKinds.Page, pid, ct));
                }
                break;
            }
            case ContentKinds.Page:
            {
                var childIds = await db.Pages.AsNoTracking()
                    .Where(p => p.ParentPageId == rootId)
                    .Select(p => p.Id)
                    .ToListAsync(ct);
                foreach (var cpid in childIds)
                {
                    result.AddRange(await CollectSubtreeAsync(db, ContentKinds.Page, cpid, ct));
                }
                break;
            }
        }
        return result;
    }
}
