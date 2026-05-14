using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.EndpointFilters;
using AutoNate.Web.Persistence;
using AutoNate.Web.Persistence.Scaffolded;
using AutoNate.Web.Services.Content;
using AutoNate.Web.Services.Events;
using Microsoft.EntityFrameworkCore;

namespace AutoNate.Web.Endpoints;

public static class NotebookEndpoints
{
    public static IEndpointRouteBuilder MapNotebookEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/content/notebooks").RequireAuthorization();

        group.MapGet("/page", async (
            Guid? cabinetId,
            int? page,
            int? pageSize,
            string? q,
            HttpContext http,
            IDbContextFactory<AutoNateDbContext> dbFactory,
            IContentAuthorizer authorizer,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var access = await authorizer.GetAllowedIdsAsync(
                http.User, ContentKinds.Notebook, Actions.View, ct);

            var query = db.Notebooks.AsNoTracking().AsQueryable();
            if (cabinetId is { } cid) query = query.Where(n => n.CabinetId == cid);
            if (!access.Unrestricted)
            {
                var ids = access.AllowedIds;
                query = query.Where(n => ids.Contains(n.Id));
            }
            if (!string.IsNullOrWhiteSpace(q))
            {
                var needle = q.Trim();
                query = query.Where(n =>
                    EF.Functions.ILike(n.Name, "%" + needle + "%") ||
                    (n.Description != null && EF.Functions.ILike(n.Description, "%" + needle + "%")));
            }

            var totalCount = await query.CountAsync(ct);
            var pg = page.GetValueOrDefault(0);
            var ps = Math.Clamp(pageSize.GetValueOrDefault(50), 1, 200);
            var items = await query
                .OrderBy(n => n.SortOrder).ThenBy(n => n.Name)
                .Skip(pg * ps).Take(ps)
                .ToListAsync(ct);

            await auditPublisher.PublishAsync(
                ContentEventTopic.TopicName,
                ContentEventTypes.NotebookListViewed,
                ContentResourceKinds.Notebook,
                resource: cabinetId is { } ? new { cabinetId = cabinetId.Value } : null,
                details: new { resultCount = items.Count, totalCount, page = pg, pageSize = ps },
                ct);

            return Results.Ok(new NotebookPageResponse(items.Select(MapDto).ToList(), totalCount));
        });

        group.MapGet("/{id:guid}", async (
            Guid id,
            IDbContextFactory<AutoNateDbContext> dbFactory,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var notebook = await db.Notebooks.AsNoTracking().FirstOrDefaultAsync(n => n.Id == id, ct);
            if (notebook is null) return Results.NotFound();
            await auditPublisher.PublishAsync(
                ContentEventTopic.TopicName,
                ContentEventTypes.NotebookViewed,
                ContentResourceKinds.Notebook,
                resource: new { id = notebook.Id, name = notebook.Name },
                details: null,
                ct);
            return Results.Ok(MapDto(notebook));
        }).RequirePermission(EntityKinds.Notebook, Actions.View);

        group.MapPost("/", async (
            CreateNotebookRequest request,
            HttpContext http,
            IDbContextFactory<AutoNateDbContext> dbFactory,
            IContentAuthorizer authorizer,
            IContentTreeService treeService,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name))
                return Results.BadRequest(new { error = "Notebook name is required." });

            var decision = await authorizer.AuthorizeAsync(
                http.User, ContentKinds.Cabinet, request.CabinetId, Actions.Edit, ct);
            if (!decision.IsAllowed) return Results.StatusCode(StatusCodes.Status403Forbidden);

            var actorId = http.GetActorId();
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var cabinetExists = await db.Cabinets.AsNoTracking()
                .AnyAsync(c => c.Id == request.CabinetId, ct);
            if (!cabinetExists) return Results.BadRequest(new { error = "Cabinet not found." });

            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var now = DateTime.UtcNow;
            var notebook = new Notebook
            {
                Id = Guid.NewGuid(),
                CabinetId = request.CabinetId,
                Name = request.Name.Trim(),
                Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
                Icon = string.IsNullOrWhiteSpace(request.Icon) ? null : request.Icon.Trim(),
                SortOrder = request.SortOrder ?? 0,
                IsArchived = false,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                CreatedBy = actorId,
                UpdatedBy = actorId
            };
            db.Notebooks.Add(notebook);
            await db.SaveChangesAsync(ct);
            await treeService.InsertSelfWithAncestorsAsync(db, ContentKinds.Notebook, notebook.Id, ct);
            await tx.CommitAsync(ct);

            await auditPublisher.PublishAsync(
                ContentEventTopic.TopicName,
                ContentEventTypes.NotebookCreated,
                ContentResourceKinds.Notebook,
                resource: new { id = notebook.Id, cabinetId = notebook.CabinetId, name = notebook.Name },
                details: null,
                ct);

            return Results.Created($"/api/content/notebooks/{notebook.Id}", MapDto(notebook));
        }).DisableAntiforgery();

        group.MapPatch("/{id:guid}", async (
            Guid id,
            UpdateNotebookRequest request,
            HttpContext http,
            IDbContextFactory<AutoNateDbContext> dbFactory,
            IContentAuthorizer authorizer,
            IContentTreeService treeService,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            var actorId = http.GetActorId();
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var notebook = await db.Notebooks.FirstOrDefaultAsync(n => n.Id == id, ct);
            if (notebook is null) return Results.NotFound();

            var fields = new List<string>();
            string? archiveEventType = null;
            Guid? previousCabinetId = null;

            if (request.Name is not null)
            {
                if (string.IsNullOrWhiteSpace(request.Name))
                    return Results.BadRequest(new { error = "Notebook name cannot be empty." });
                if (notebook.Name != request.Name.Trim()) { notebook.Name = request.Name.Trim(); fields.Add("name"); }
            }
            if (request.Description is not null)
            {
                var nd = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
                if (notebook.Description != nd) { notebook.Description = nd; fields.Add("description"); }
            }
            if (request.Icon is not null)
            {
                var ni = string.IsNullOrWhiteSpace(request.Icon) ? null : request.Icon.Trim();
                if (notebook.Icon != ni) { notebook.Icon = ni; fields.Add("icon"); }
            }
            if (request.SortOrder is { } so && notebook.SortOrder != so) { notebook.SortOrder = so; fields.Add("sortOrder"); }
            if (request.IsArchived is { } archived && archived != notebook.IsArchived)
            {
                notebook.IsArchived = archived;
                fields.Add("isArchived");
                archiveEventType = archived
                    ? ContentEventTypes.NotebookArchived
                    : ContentEventTypes.NotebookRestored;
            }
            if (request.CabinetId is { } newCabinetId && newCabinetId != notebook.CabinetId)
            {
                var receive = await authorizer.AuthorizeAsync(
                    http.User, ContentKinds.Cabinet, newCabinetId, Actions.Edit, ct);
                if (!receive.IsAllowed) return Results.StatusCode(StatusCodes.Status403Forbidden);

                previousCabinetId = notebook.CabinetId;
                notebook.CabinetId = newCabinetId;
                fields.Add("cabinetId");
            }

            if (fields.Count == 0) return Results.Ok(MapDto(notebook));

            notebook.UpdatedAtUtc = DateTime.UtcNow;
            notebook.UpdatedBy = actorId;
            await db.SaveChangesAsync(ct);
            if (previousCabinetId is not null)
            {
                await treeService.RebuildAncestorsForSubtreeAsync(db, ContentKinds.Notebook, notebook.Id, ct);
            }

            if (previousCabinetId is not null)
            {
                await auditPublisher.PublishAsync(
                    ContentEventTopic.TopicName,
                    ContentEventTypes.NotebookMoved,
                    ContentResourceKinds.Notebook,
                    resource: new { id = notebook.Id },
                    details: new { previousCabinetId, newCabinetId = notebook.CabinetId },
                    ct);
            }
            await auditPublisher.PublishAsync(
                ContentEventTopic.TopicName,
                archiveEventType ?? ContentEventTypes.NotebookUpdated,
                ContentResourceKinds.Notebook,
                resource: new { id = notebook.Id, name = notebook.Name },
                details: new { fields },
                ct);

            return Results.Ok(MapDto(notebook));
        }).DisableAntiforgery()
          .RequirePermission(EntityKinds.Notebook, Actions.Edit);

        group.MapDelete("/{id:guid}", async (
            Guid id,
            IDbContextFactory<AutoNateDbContext> dbFactory,
            IContentTreeService treeService,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var notebook = await db.Notebooks.FirstOrDefaultAsync(n => n.Id == id, ct);
            if (notebook is null) return Results.NotFound();
            db.Notebooks.Remove(notebook);
            await db.SaveChangesAsync(ct);
            await treeService.DeleteEntityAsync(db, ContentKinds.Notebook, id, ct);
            await tx.CommitAsync(ct);

            await auditPublisher.PublishAsync(
                ContentEventTopic.TopicName,
                ContentEventTypes.NotebookDeleted,
                ContentResourceKinds.Notebook,
                resource: new { id, name = notebook.Name },
                details: null,
                ct);

            return Results.NoContent();
        }).DisableAntiforgery()
          .RequirePermission(EntityKinds.Notebook, Actions.Delete);

        return app;
    }

    internal static NotebookDto MapDto(Notebook n) => new(
        n.Id, n.Locator, n.CabinetId, n.Name, n.Description, n.Icon,
        n.SortOrder, n.IsArchived,
        n.CreatedAtUtc, n.UpdatedAtUtc, n.CreatedBy, n.UpdatedBy);

    public sealed record CreateNotebookRequest(
        Guid CabinetId, string Name, string? Description, string? Icon, int? SortOrder);

    public sealed record UpdateNotebookRequest(
        Guid? CabinetId, string? Name, string? Description, string? Icon,
        int? SortOrder, bool? IsArchived);

    public sealed record NotebookDto(
        Guid Id, long Locator, Guid CabinetId, string Name, string? Description,
        string? Icon, int SortOrder, bool IsArchived,
        DateTime CreatedAtUtc, DateTime UpdatedAtUtc, Guid CreatedBy, Guid UpdatedBy);

    public sealed record NotebookPageResponse(List<NotebookDto> Items, int TotalCount);
}
