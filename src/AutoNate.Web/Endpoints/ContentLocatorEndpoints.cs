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

        group.MapGet("/{locator:long}", async (
            long locator,
            IDbContextFactory<AutoNateDbContext> dbFactory,
            CancellationToken ct) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);

            // Five indexed lookups — locator unique index per table — find at
            // most one hit. Cheap because each query short-circuits to a
            // single-row index scan.
            var projectHit = await db.Projects.AsNoTracking()
                .Where(p => p.Locator == locator)
                .Select(p => new { p.Id })
                .FirstOrDefaultAsync(ct);
            if (projectHit is not null)
            {
                return Results.Ok(BuildResponseSelfOnly(
                    locator, ContentKinds.Project, projectHit.Id, db, ct));
            }

            var cabinetHit = await db.Cabinets.AsNoTracking()
                .Where(c => c.Locator == locator)
                .Select(c => new { c.Id })
                .FirstOrDefaultAsync(ct);
            if (cabinetHit is not null)
            {
                return Results.Ok(await BuildResponseAsync(
                    db, locator, ContentKinds.Cabinet, cabinetHit.Id, ct));
            }

            var notebookHit = await db.Notebooks.AsNoTracking()
                .Where(n => n.Locator == locator)
                .Select(n => new { n.Id })
                .FirstOrDefaultAsync(ct);
            if (notebookHit is not null)
            {
                return Results.Ok(await BuildResponseAsync(
                    db, locator, ContentKinds.Notebook, notebookHit.Id, ct));
            }

            var pageHit = await db.Pages.AsNoTracking()
                .Where(p => p.Locator == locator)
                .Select(p => new { p.Id })
                .FirstOrDefaultAsync(ct);
            if (pageHit is not null)
            {
                return Results.Ok(await BuildResponseAsync(
                    db, locator, ContentKinds.Page, pageHit.Id, ct));
            }

            var noteHit = await db.Notes.AsNoTracking()
                .Where(n => n.Locator == locator)
                .Select(n => new { n.Id, n.PageId, n.Locator })
                .FirstOrDefaultAsync(ct);
            if (noteHit is not null)
            {
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

            return Results.NotFound(new { error = $"Locator {locator} not found." });
        });

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
