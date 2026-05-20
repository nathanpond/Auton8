using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.EndpointFilters;
using AutoNate.Web.Persistence;
using AutoNate.Web.Persistence.Scaffolded;
using AutoNate.Web.Services.Content;
using AutoNate.Web.Services.Events;
using Microsoft.EntityFrameworkCore;

namespace AutoNate.Web.Endpoints;

public static class CabinetEndpoints
{
    public static IEndpointRouteBuilder MapCabinetEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/content/cabinets").RequireAuthorization();

        group.MapGet("/page", async (
            Guid? projectId,
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
                http.User, ContentKinds.Cabinet, Actions.View, ct);

            var query = db.Cabinets.AsNoTracking().AsQueryable();
            if (projectId is { } pid) query = query.Where(c => c.ProjectId == pid);
            if (!access.Unrestricted)
            {
                var ids = access.AllowedIds;
                query = query.Where(c => ids.Contains(c.Id));
            }
            if (!string.IsNullOrWhiteSpace(q))
            {
                var needle = q.Trim();
                query = query.Where(c =>
                    EF.Functions.ILike(c.Name, "%" + needle + "%") ||
                    (c.Description != null && EF.Functions.ILike(c.Description, "%" + needle + "%")));
            }

            var totalCount = await query.CountAsync(ct);
            var pg = page.GetValueOrDefault(0);
            var ps = Math.Clamp(pageSize.GetValueOrDefault(50), 1, 200);
            var items = await query
                .OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
                .Skip(pg * ps).Take(ps)
                .ToListAsync(ct);

            await auditPublisher.PublishAsync(
                ContentEventTopic.TopicName,
                ContentEventTypes.CabinetListViewed,
                ContentResourceKinds.Cabinet,
                resource: projectId is { } ? new { projectId = projectId.Value } : null,
                details: new { resultCount = items.Count, totalCount, page = pg, pageSize = ps },
                ct);

            return Results.Ok(new CabinetPageResponse(items.Select(MapDto).ToList(), totalCount));
        }).AuthorizedInHandler(
            "Result set filtered by GetAllowedIdsAsync(Cabinet.View); " +
            "unauthorized cabinets never enter the response.");

        group.MapGet("/{id:guid}", async (
            Guid id,
            IDbContextFactory<AutoNateDbContext> dbFactory,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var cabinet = await db.Cabinets.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
            if (cabinet is null) return Results.NotFound();
            await auditPublisher.PublishAsync(
                ContentEventTopic.TopicName,
                ContentEventTypes.CabinetViewed,
                ContentResourceKinds.Cabinet,
                resource: new { id = cabinet.Id, name = cabinet.Name },
                details: null,
                ct);
            return Results.Ok(MapDto(cabinet));
        }).RequirePermission(EntityKinds.Cabinet, Actions.View);

        group.MapPost("/", async (
            CreateCabinetRequest request,
            HttpContext http,
            IDbContextFactory<AutoNateDbContext> dbFactory,
            IContentAuthorizer authorizer,
            IContentTreeService treeService,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return Results.BadRequest(new { error = "Cabinet name is required." });
            }
            // Edit on the parent project gates creating children inside it —
            // composes the kind-level Create with per-resource Edit per design D9.
            var decision = await authorizer.AuthorizeAsync(
                http.User, ContentKinds.Project, request.ProjectId, Actions.Edit, ct);
            if (!decision.IsAllowed) return Results.StatusCode(StatusCodes.Status403Forbidden);

            var actorId = http.GetActorId();
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var projectExists = await db.Projects.AsNoTracking()
                .AnyAsync(p => p.Id == request.ProjectId, ct);
            if (!projectExists) return Results.BadRequest(new { error = "Project not found." });

            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var now = DateTime.UtcNow;
            var cabinet = new Cabinet
            {
                Id = Guid.NewGuid(),
                ProjectId = request.ProjectId,
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
            db.Cabinets.Add(cabinet);
            await db.SaveChangesAsync(ct);
            await treeService.InsertSelfWithAncestorsAsync(db, ContentKinds.Cabinet, cabinet.Id, ct);
            await tx.CommitAsync(ct);

            await auditPublisher.PublishAsync(
                ContentEventTopic.TopicName,
                ContentEventTypes.CabinetCreated,
                ContentResourceKinds.Cabinet,
                resource: new { id = cabinet.Id, projectId = cabinet.ProjectId, name = cabinet.Name },
                details: null,
                ct);

            return Results.Created($"/api/content/cabinets/{cabinet.Id}", MapDto(cabinet));
        }).DisableAntiforgery()
          .AuthorizedInHandler(
              "AuthorizeAsync(Project.Edit) on the parent project gates " +
              "child creation (composes kind-level Create with per-resource " +
              "Edit per design D9).");

        group.MapPatch("/{id:guid}", async (
            Guid id,
            UpdateCabinetRequest request,
            HttpContext http,
            IDbContextFactory<AutoNateDbContext> dbFactory,
            IContentAuthorizer authorizer,
            IContentTreeService treeService,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            var actorId = http.GetActorId();
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var cabinet = await db.Cabinets.FirstOrDefaultAsync(c => c.Id == id, ct);
            if (cabinet is null) return Results.NotFound();

            var fields = new List<string>();
            string? archiveEventType = null;
            Guid? previousProjectId = null;

            if (request.Name is not null)
            {
                if (string.IsNullOrWhiteSpace(request.Name))
                    return Results.BadRequest(new { error = "Cabinet name cannot be empty." });
                if (cabinet.Name != request.Name.Trim()) { cabinet.Name = request.Name.Trim(); fields.Add("name"); }
            }
            if (request.Description is not null)
            {
                var nd = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
                if (cabinet.Description != nd) { cabinet.Description = nd; fields.Add("description"); }
            }
            if (request.Icon is not null)
            {
                var ni = string.IsNullOrWhiteSpace(request.Icon) ? null : request.Icon.Trim();
                if (cabinet.Icon != ni) { cabinet.Icon = ni; fields.Add("icon"); }
            }
            if (request.SortOrder is { } so && cabinet.SortOrder != so) { cabinet.SortOrder = so; fields.Add("sortOrder"); }
            if (request.IsArchived is { } archived && archived != cabinet.IsArchived)
            {
                cabinet.IsArchived = archived;
                fields.Add("isArchived");
                archiveEventType = archived
                    ? ContentEventTypes.CabinetArchived
                    : ContentEventTypes.CabinetRestored;
            }

            if (request.ProjectId is { } newProjectId && newProjectId != cabinet.ProjectId)
            {
                // Edit on the new project is required to receive the cabinet.
                var receive = await authorizer.AuthorizeAsync(
                    http.User, ContentKinds.Project, newProjectId, Actions.Edit, ct);
                if (!receive.IsAllowed) return Results.StatusCode(StatusCodes.Status403Forbidden);

                previousProjectId = cabinet.ProjectId;
                cabinet.ProjectId = newProjectId;
                fields.Add("projectId");
            }

            if (fields.Count == 0)
            {
                return Results.Ok(MapDto(cabinet));
            }

            cabinet.UpdatedAtUtc = DateTime.UtcNow;
            cabinet.UpdatedBy = actorId;
            await db.SaveChangesAsync(ct);
            if (previousProjectId is not null)
            {
                // Move: closure rows for this subtree must be recomputed.
                await treeService.RebuildAncestorsForSubtreeAsync(db, ContentKinds.Cabinet, cabinet.Id, ct);
            }

            if (previousProjectId is not null)
            {
                await auditPublisher.PublishAsync(
                    ContentEventTopic.TopicName,
                    ContentEventTypes.CabinetMoved,
                    ContentResourceKinds.Cabinet,
                    resource: new { id = cabinet.Id },
                    details: new { previousProjectId, newProjectId = cabinet.ProjectId },
                    ct);
            }
            await auditPublisher.PublishAsync(
                ContentEventTopic.TopicName,
                archiveEventType ?? ContentEventTypes.CabinetUpdated,
                ContentResourceKinds.Cabinet,
                resource: new { id = cabinet.Id, name = cabinet.Name },
                details: new { fields },
                ct);

            return Results.Ok(MapDto(cabinet));
        }).DisableAntiforgery()
          .RequirePermission(EntityKinds.Cabinet, Actions.Edit);

        group.MapDelete("/{id:guid}", async (
            Guid id,
            IDbContextFactory<AutoNateDbContext> dbFactory,
            IContentTreeService treeService,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var cabinet = await db.Cabinets.FirstOrDefaultAsync(c => c.Id == id, ct);
            if (cabinet is null) return Results.NotFound();
            db.Cabinets.Remove(cabinet);
            await db.SaveChangesAsync(ct);
            await treeService.DeleteEntityAsync(db, ContentKinds.Cabinet, id, ct);
            await tx.CommitAsync(ct);

            await auditPublisher.PublishAsync(
                ContentEventTopic.TopicName,
                ContentEventTypes.CabinetDeleted,
                ContentResourceKinds.Cabinet,
                resource: new { id, name = cabinet.Name },
                details: null,
                ct);

            return Results.NoContent();
        }).DisableAntiforgery()
          .RequirePermission(EntityKinds.Cabinet, Actions.Delete);

        return app;
    }

    internal static CabinetDto MapDto(Cabinet c) => new(
        c.Id, c.Locator, c.ProjectId, c.Name, c.Description, c.Icon,
        c.SortOrder, c.IsArchived,
        c.CreatedAtUtc, c.UpdatedAtUtc, c.CreatedBy, c.UpdatedBy);

    public sealed record CreateCabinetRequest(
        Guid ProjectId, string Name, string? Description, string? Icon, int? SortOrder);

    public sealed record UpdateCabinetRequest(
        Guid? ProjectId, string? Name, string? Description, string? Icon,
        int? SortOrder, bool? IsArchived);

    public sealed record CabinetDto(
        Guid Id, long Locator, Guid ProjectId, string Name, string? Description,
        string? Icon, int SortOrder, bool IsArchived,
        DateTime CreatedAtUtc, DateTime UpdatedAtUtc, Guid CreatedBy, Guid UpdatedBy);

    public sealed record CabinetPageResponse(List<CabinetDto> Items, int TotalCount);
}
