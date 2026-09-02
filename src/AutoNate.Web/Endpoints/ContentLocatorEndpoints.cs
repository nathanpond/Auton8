using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.EndpointFilters;
using AutoNate.Web.Persistence;
using AutoNate.Web.Services.Content;
using Microsoft.EntityFrameworkCore;

namespace AutoNate.Web.Endpoints;

// Resolves a numeric locator (e.g. /notes/42) into the underlying entity kind,
// id, and full ancestor chain. The SPA hits this on cold-load whenever it has
// a locator in the URL and needs to populate project/cabinet/page state in
// one round-trip.
//
// Permission posture: any authenticated user may resolve any locator. The
// response contains identifiers only (no content), and the subsequent fetch
// for the entity itself is still gated by IContentAuthorizer. Locators are
// sequential and trivially guessable, so authenticated mapping doesn't leak
// anything meaningful.
public static class ContentLocatorEndpoints
{
    public static IEndpointRouteBuilder MapContentLocatorEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/content/locator").RequireAuthorization();

        // Lightweight project-tree dump used by the SPA Move/Copy modal.
        // Returns every cabinet / notebook / page in the project the caller
        // can view, with a per-entity flag indicating whether the caller can
        // also edit it (Contributor or above). The modal uses CanEdit to
        // mark valid destinations. Filtered by IContentAuthorizer so a
        // viewer never sees resources outside their reach.
        var treeGroup = app.MapGroup("/api/content/projects/{projectId:guid}/tree")
            .RequireAuthorization();
        treeGroup.MapGet("/", async (
            Guid projectId,
            HttpContext http,
            IDbContextFactory<AutoNateDbContext> dbFactory,
            IContentAuthorizer authorizer,
            CancellationToken ct) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var projectExists = await db.Projects.AsNoTracking()
                .AnyAsync(p => p.Id == projectId, ct);
            if (!projectExists) return Results.NotFound();

            // View-decision gate: caller must be able to see *something* in
            // the project, otherwise return 404 to avoid revealing existence.
            var projectViewDecision = await authorizer.AuthorizeAsync(
                http.User, ContentKinds.Project, projectId, Actions.View, ct);
            if (!projectViewDecision.IsAllowed)
            {
                return Results.NotFound();
            }

            var cabinetView = await authorizer.GetAllowedIdsAsync(
                http.User, ContentKinds.Cabinet, Actions.View, ct);
            var cabinetEdit = await authorizer.GetAllowedIdsAsync(
                http.User, ContentKinds.Cabinet, Actions.Edit, ct);
            var notebookView = await authorizer.GetAllowedIdsAsync(
                http.User, ContentKinds.Notebook, Actions.View, ct);
            var notebookEdit = await authorizer.GetAllowedIdsAsync(
                http.User, ContentKinds.Notebook, Actions.Edit, ct);
            var pageView = await authorizer.GetAllowedIdsAsync(
                http.User, ContentKinds.Page, Actions.View, ct);
            var pageEdit = await authorizer.GetAllowedIdsAsync(
                http.User, ContentKinds.Page, Actions.Edit, ct);

            bool Allows(ContentAccessSet set, Guid id) =>
                set.Unrestricted || set.AllowedIds.Contains(id);

            // Push the view-access filter into SQL so unauthorized rows never
            // leave Postgres (canonical pattern, see NotebookEndpoints). Edit
            // sets stay in-memory: they only drive the per-row CanEdit flag.
            var cabinetQuery = db.Cabinets.AsNoTracking()
                .Where(c => c.ProjectId == projectId && !c.IsArchived);
            if (!cabinetView.Unrestricted)
            {
                var cabinetViewIds = cabinetView.AllowedIds;
                cabinetQuery = cabinetQuery.Where(c => cabinetViewIds.Contains(c.Id));
            }
            var cabinets = await cabinetQuery
                .OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
                .Select(c => new
                {
                    c.Id, c.Locator, c.Name, c.Icon
                })
                .ToListAsync(ct);
            var cabinetIds = cabinets.Select(c => c.Id).ToList();

            var notebookQuery = db.Notebooks.AsNoTracking()
                .Where(n => cabinetIds.Contains(n.CabinetId) && !n.IsArchived);
            if (!notebookView.Unrestricted)
            {
                var notebookViewIds = notebookView.AllowedIds;
                notebookQuery = notebookQuery.Where(n => notebookViewIds.Contains(n.Id));
            }
            var notebooks = await notebookQuery
                .OrderBy(n => n.SortOrder).ThenBy(n => n.Name)
                .Select(n => new
                {
                    n.Id, n.Locator, n.Name, n.Icon, n.CabinetId
                })
                .ToListAsync(ct);
            var notebookIds = notebooks.Select(n => n.Id).ToList();

            var pageQuery = db.Pages.AsNoTracking()
                .Where(p => notebookIds.Contains(p.NotebookId) && !p.IsArchived);
            if (!pageView.Unrestricted)
            {
                var pageViewIds = pageView.AllowedIds;
                pageQuery = pageQuery.Where(p => pageViewIds.Contains(p.Id));
            }
            var pages = await pageQuery
                .OrderBy(p => p.SortOrder).ThenBy(p => p.Title)
                .Select(p => new
                {
                    p.Id, p.Locator, p.Title, p.NotebookId, p.ParentPageId
                })
                .ToListAsync(ct);

            var cabinetDtos = cabinets
                .Select(c => new ProjectTreeCabinet(
                    c.Id, c.Locator, c.Name, c.Icon, Allows(cabinetEdit, c.Id),
                    notebooks
                        .Where(n => n.CabinetId == c.Id)
                        .Select(n => new ProjectTreeNotebook(
                            n.Id, n.Locator, n.Name, n.Icon, Allows(notebookEdit, n.Id),
                            pages
                                .Where(p => p.NotebookId == n.Id)
                                .Select(p => new ProjectTreePage(
                                    p.Id, p.Locator, p.Title, p.ParentPageId,
                                    Allows(pageEdit, p.Id)))
                                .ToList()))
                        .ToList()))
                .ToList();

            return Results.Ok(new ProjectTreeResponse(projectId, cabinetDtos));
        }).AuthorizedInHandler(
            "Project.View via AuthorizeAsync (project gate); per-resource " +
            "cabinet/notebook/page filtering via GetAllowedIdsAsync.");

        group.MapGet("/{locator:long}", async (
            long locator,
            HttpContext http,
            IDbContextFactory<AutoNateDbContext> dbFactory,
            IContentAuthorizer authorizer,
            CancellationToken ct) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);

            // Locators are sequential longs, so an unguarded lookup is a
            // complete map of the tenant's content tree — kind, GUID and
            // ancestor chain for every project, cabinet, notebook, page and
            // note — to any signed-in user, handing them the ids to feed
            // other endpoints (archived-21). Every hit is authorized before it is
            // returned, and a denial is the same NotFound an unknown locator
            // gets, so the endpoint reveals neither content nor existence.
            async Task<bool> CanViewAsync(string kind, Guid id) =>
                (await authorizer.AuthorizeAsync(http.User, kind, id, Actions.View, ct)).IsAllowed;

            var notFound = Results.NotFound(new { error = $"Locator {locator} not found." });

            // Five indexed lookups — locator unique index per table — find at
            // most one hit. Cheap because each query short-circuits to a
            // single-row index scan.
            var projectHit = await db.Projects.AsNoTracking()
                .Where(p => p.Locator == locator)
                .Select(p => new { p.Id })
                .FirstOrDefaultAsync(ct);
            if (projectHit is not null)
            {
                if (!await CanViewAsync(ContentKinds.Project, projectHit.Id)) return notFound;
                return Results.Ok(BuildResponseSelfOnly(
                    locator, ContentKinds.Project, projectHit.Id, db, ct));
            }

            var cabinetHit = await db.Cabinets.AsNoTracking()
                .Where(c => c.Locator == locator)
                .Select(c => new { c.Id })
                .FirstOrDefaultAsync(ct);
            if (cabinetHit is not null)
            {
                if (!await CanViewAsync(ContentKinds.Cabinet, cabinetHit.Id)) return notFound;
                return Results.Ok(await BuildResponseAsync(
                    db, locator, ContentKinds.Cabinet, cabinetHit.Id, ct));
            }

            var notebookHit = await db.Notebooks.AsNoTracking()
                .Where(n => n.Locator == locator)
                .Select(n => new { n.Id })
                .FirstOrDefaultAsync(ct);
            if (notebookHit is not null)
            {
                if (!await CanViewAsync(ContentKinds.Notebook, notebookHit.Id)) return notFound;
                return Results.Ok(await BuildResponseAsync(
                    db, locator, ContentKinds.Notebook, notebookHit.Id, ct));
            }

            var pageHit = await db.Pages.AsNoTracking()
                .Where(p => p.Locator == locator)
                .Select(p => new { p.Id })
                .FirstOrDefaultAsync(ct);
            if (pageHit is not null)
            {
                if (!await CanViewAsync(ContentKinds.Page, pageHit.Id)) return notFound;
                return Results.Ok(await BuildResponseAsync(
                    db, locator, ContentKinds.Page, pageHit.Id, ct));
            }

            var noteHit = await db.Notes.AsNoTracking()
                .Where(n => n.Locator == locator)
                .Select(n => new { n.Id, n.PageId, n.Locator })
                .FirstOrDefaultAsync(ct);
            if (noteHit is not null)
            {
                // Notes inherit their parent page's permissions (design D10),
                // so the page is what gets authorized.
                if (!await CanViewAsync(ContentKinds.Page, noteHit.PageId)) return notFound;
                // Notes aren't in content_ancestors (they aren't a perm-
                // issionable kind), so resolve the parent page's chain and
                // tack the note on top.
                var pageAncestors = await BuildResponseAsync(
                    db, 0, ContentKinds.Page, noteHit.PageId, ct);
                return Results.Ok(new LocatorResponse(
                    Locator: locator,
                    // Note isn't in ContentKinds — it's intentionally not a
                    // permissionable kind. The literal string matches what
                    // ContentResourceKinds uses on the audit-event side.
                    Kind: "note",
                    Id: noteHit.Id,
                    Ancestors: pageAncestors.Ancestors with
                    {
                        Note = new LocatorRef(noteHit.Id, noteHit.Locator)
                    }));
            }

            return notFound;
        }).AuthorizedInHandler(
            "Locator → (kind, id, ancestor chain) lookup. Every resolved hit " +
            "is checked with IContentAuthorizer for View (notes via their " +
            "parent page) before anything is returned, and a denial is the " +
            "same NotFound an unknown locator gets — locators are sequential, " +
            "so an unguarded lookup would enumerate the whole content tree.");

        return app;
    }

    // For project: there are no ancestors above it, so just self.
    private static LocatorResponse BuildResponseSelfOnly(
        long locator, string kind, Guid id,
        AutoNateDbContext db, CancellationToken ct)
    {
        return new LocatorResponse(
            Locator: locator,
            Kind: kind,
            Id: id,
            Ancestors: new LocatorAncestors(
                Project: new LocatorRef(id, locator),
                Cabinet: null,
                Notebook: null,
                Page: null,
                Note: null));
    }

    private static async Task<LocatorResponse> BuildResponseAsync(
        AutoNateDbContext db, long locator, string kind, Guid id, CancellationToken ct)
    {
        // Pull every ancestor of (kind, id) in one query. Each row is one
        // ancestor — we then look up that ancestor's locator from the
        // appropriate entity table. The closure for these kinds already
        // includes a depth-0 self-row, so the result covers `self` too.
        var ancestorRows = await db.ContentAncestors.AsNoTracking()
            .Where(ca => ca.DescendantKind == kind && ca.DescendantId == id)
            .Select(ca => new { ca.AncestorKind, ca.AncestorId })
            .ToListAsync(ct);

        var projectIds = ancestorRows
            .Where(a => a.AncestorKind == ContentKinds.Project)
            .Select(a => a.AncestorId)
            .ToList();
        var cabinetIds = ancestorRows
            .Where(a => a.AncestorKind == ContentKinds.Cabinet)
            .Select(a => a.AncestorId)
            .ToList();
        var notebookIds = ancestorRows
            .Where(a => a.AncestorKind == ContentKinds.Notebook)
            .Select(a => a.AncestorId)
            .ToList();
        var pageIds = ancestorRows
            .Where(a => a.AncestorKind == ContentKinds.Page)
            .Select(a => a.AncestorId)
            .ToList();

        // Locator lookups — one query per kind, indexed.
        var projectLoc = projectIds.Count == 0
            ? null
            : await db.Projects.AsNoTracking()
                .Where(p => projectIds.Contains(p.Id))
                .Select(p => new { p.Id, p.Locator })
                .FirstOrDefaultAsync(ct);
        var cabinetLoc = cabinetIds.Count == 0
            ? null
            : await db.Cabinets.AsNoTracking()
                .Where(c => cabinetIds.Contains(c.Id))
                .Select(c => new { c.Id, c.Locator })
                .FirstOrDefaultAsync(ct);
        var notebookLoc = notebookIds.Count == 0
            ? null
            : await db.Notebooks.AsNoTracking()
                .Where(n => notebookIds.Contains(n.Id))
                .Select(n => new { n.Id, n.Locator })
                .FirstOrDefaultAsync(ct);

        // For pages there can be multiple ancestors in the chain (parent
        // page lineage). We want the *bottom-most* page (the one being
        // resolved if kind=="page", or null otherwise).
        LocatorRef? pageRef = null;
        if (kind == ContentKinds.Page)
        {
            var bottomPage = await db.Pages.AsNoTracking()
                .Where(p => p.Id == id)
                .Select(p => new { p.Locator })
                .FirstOrDefaultAsync(ct);
            if (bottomPage is not null)
            {
                pageRef = new LocatorRef(id, bottomPage.Locator);
            }
        }
        else if (pageIds.Count > 0)
        {
            // The thing being resolved isn't a page, but its ancestor chain
            // somehow contains pages (it won't for cabinet/notebook). Nothing
            // to do here; left for completeness.
        }

        return new LocatorResponse(
            Locator: locator,
            Kind: kind,
            Id: id,
            Ancestors: new LocatorAncestors(
                Project: projectLoc is null
                    ? null
                    : new LocatorRef(projectLoc.Id, projectLoc.Locator),
                Cabinet: cabinetLoc is null
                    ? null
                    : new LocatorRef(cabinetLoc.Id, cabinetLoc.Locator),
                Notebook: notebookLoc is null
                    ? null
                    : new LocatorRef(notebookLoc.Id, notebookLoc.Locator),
                Page: pageRef,
                Note: null));
    }

    // Used by the SPA Move/Copy destination picker. Pages are returned as a
    // flat list per notebook with their ParentPageId so the SPA can rebuild
    // the hierarchy without an extra round trip.
    public sealed record ProjectTreeResponse(Guid ProjectId, List<ProjectTreeCabinet> Cabinets);
    public sealed record ProjectTreeCabinet(
        Guid Id, long Locator, string Name, string? Icon, bool CanEdit,
        List<ProjectTreeNotebook> Notebooks);
    public sealed record ProjectTreeNotebook(
        Guid Id, long Locator, string Name, string? Icon, bool CanEdit,
        List<ProjectTreePage> Pages);
    public sealed record ProjectTreePage(
        Guid Id, long Locator, string Title, Guid? ParentPageId, bool CanEdit);

    public sealed record LocatorRef(Guid Id, long Locator);

    public sealed record LocatorAncestors(
        LocatorRef? Project,
        LocatorRef? Cabinet,
        LocatorRef? Notebook,
        LocatorRef? Page,
        LocatorRef? Note);

    public sealed record LocatorResponse(
        long Locator,
        string Kind,
        Guid Id,
        LocatorAncestors Ancestors);
}
