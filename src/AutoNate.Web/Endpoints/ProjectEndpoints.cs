using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.EndpointFilters;
using AutoNate.Web.Persistence;
using AutoNate.Web.Persistence.Scaffolded;
using AutoNate.Web.Services.Content;
using AutoNate.Web.Services.Events;
using Microsoft.EntityFrameworkCore;

namespace AutoNate.Web.Endpoints;

public static class ProjectEndpoints
{
    public static IEndpointRouteBuilder MapProjectEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/content/projects").RequireAuthorization();

        // List projects the actor can see. Filtered through IContentAuthorizer
        // which combines membership baseline + per-resource overrides.
        group.MapGet("/page", async (
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
                http.User, ContentKinds.Project, Actions.View, ct);

            var query = db.Projects.AsNoTracking().AsQueryable();
            if (!access.Unrestricted)
            {
                var ids = access.AllowedIds;
                query = query.Where(p => ids.Contains(p.Id));
            }
            if (!string.IsNullOrWhiteSpace(q))
            {
                var needle = q.Trim();
                query = query.Where(p =>
                    EF.Functions.ILike(p.Name, "%" + needle + "%") ||
                    (p.Description != null && EF.Functions.ILike(p.Description, "%" + needle + "%")));
            }

            var totalCount = await query.CountAsync(ct);
            var pg = page.GetValueOrDefault(0);
            var ps = Math.Clamp(pageSize.GetValueOrDefault(25), 1, 200);
            var items = await query
                .OrderByDescending(p => p.UpdatedAtUtc)
                .Skip(pg * ps).Take(ps)
                .ToListAsync(ct);

            await auditPublisher.PublishAsync(
                ContentEventTopic.TopicName,
                ContentEventTypes.ProjectListViewed,
                ContentResourceKinds.Project,
                resource: null,
                details: new { resultCount = items.Count, totalCount, page = pg, pageSize = ps, search = q },
                ct);

            return Results.Ok(new ProjectPageResponse(items.Select(MapDto).ToList(), totalCount));
        });

        group.MapGet("/{id:guid}", async (
            Guid id,
            IDbContextFactory<AutoNateDbContext> dbFactory,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var project = await db.Projects.AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == id, ct);
            if (project is null) return Results.NotFound();
            await auditPublisher.PublishAsync(
                ContentEventTopic.TopicName,
                ContentEventTypes.ProjectViewed,
                ContentResourceKinds.Project,
                resource: new { id = project.Id, name = project.Name },
                details: null,
                ct);
            return Results.Ok(MapDto(project));
        }).RequirePermission(EntityKinds.Project, Actions.View);

        // Any authenticated user may create a project. The creator becomes
        // Owner inside the same transaction so the project is immediately
        // usable.
        group.MapPost("/", async (
            CreateProjectRequest request,
            HttpContext http,
            IDbContextFactory<AutoNateDbContext> dbFactory,
            IProjectMembershipService memberships,
            IContentTreeService treeService,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            var actorId = http.GetActorId();
            if (actorId == Guid.Empty) return Results.Unauthorized();
            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return Results.BadRequest(new { error = "Project name is required." });
            }
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var now = DateTime.UtcNow;
            var project = new Project
            {
                Id = Guid.NewGuid(),
                Name = request.Name.Trim(),
                Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
                DeletionsLocked = false,
                IsArchived = false,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                CreatedBy = actorId,
                UpdatedBy = actorId
            };
            db.Projects.Add(project);
            await memberships.AddOwnerOnCreateAsync(db, project.Id, actorId, now, ct);
            await db.SaveChangesAsync(ct);
            await treeService.InsertSelfWithAncestorsAsync(db, ContentKinds.Project, project.Id, ct);
            await tx.CommitAsync(ct);

            await auditPublisher.PublishAsync(
                ContentEventTopic.TopicName,
                ContentEventTypes.ProjectCreated,
                ContentResourceKinds.Project,
                resource: new { id = project.Id, name = project.Name },
                details: null,
                ct);

            return Results.Created($"/api/content/projects/{project.Id}", MapDto(project));
        }).DisableAntiforgery();

        group.MapPatch("/{id:guid}", async (
            Guid id,
            UpdateProjectRequest request,
            HttpContext http,
            IDbContextFactory<AutoNateDbContext> dbFactory,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            var actorId = http.GetActorId();
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == id, ct);
            if (project is null) return Results.NotFound();

            var fields = new List<string>();
            if (request.Name is not null)
            {
                if (string.IsNullOrWhiteSpace(request.Name))
                    return Results.BadRequest(new { error = "Project name cannot be empty." });
                if (project.Name != request.Name.Trim())
                {
                    project.Name = request.Name.Trim();
                    fields.Add("name");
                }
            }
            if (request.Description is not null)
            {
                var newDescription = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
                if (project.Description != newDescription)
                {
                    project.Description = newDescription;
                    fields.Add("description");
                }
            }
            string? archiveEventType = null;
            if (request.IsArchived is { } archived && archived != project.IsArchived)
            {
                project.IsArchived = archived;
                fields.Add("isArchived");
                archiveEventType = archived
                    ? ContentEventTypes.ProjectArchived
                    : ContentEventTypes.ProjectRestored;
            }

            if (fields.Count == 0)
            {
                return Results.Ok(MapDto(project));
            }
            project.UpdatedAtUtc = DateTime.UtcNow;
            project.UpdatedBy = actorId;
            await db.SaveChangesAsync(ct);

            await auditPublisher.PublishAsync(
                ContentEventTopic.TopicName,
                archiveEventType ?? ContentEventTypes.ProjectUpdated,
                ContentResourceKinds.Project,
                resource: new { id = project.Id, name = project.Name },
                details: new { fields },
                ct);

            return Results.Ok(MapDto(project));
        }).DisableAntiforgery()
          .RequirePermission(EntityKinds.Project, Actions.Edit);

        // Owner-only: toggle the deletion lock. Bypasses the override system
        // by design (owner-only operations live in project_members alone).
        group.MapPatch("/{id:guid}/deletions-lock", async (
            Guid id,
            DeletionsLockRequest request,
            HttpContext http,
            IDbContextFactory<AutoNateDbContext> dbFactory,
            IContentAuthorizer authorizer,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            if (!await authorizer.IsProjectOwnerAsync(http.User, id, ct))
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }
            var actorId = http.GetActorId();
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == id, ct);
            if (project is null) return Results.NotFound();
            if (project.DeletionsLocked == request.Locked)
            {
                return Results.Ok(MapDto(project));
            }
            project.DeletionsLocked = request.Locked;
            project.UpdatedAtUtc = DateTime.UtcNow;
            project.UpdatedBy = actorId;
            await db.SaveChangesAsync(ct);

            await auditPublisher.PublishAsync(
                ContentEventTopic.TopicName,
                ContentEventTypes.ProjectDeletionsLockToggled,
                ContentResourceKinds.Project,
                resource: new { id = project.Id },
                details: new { locked = project.DeletionsLocked },
                ct);

            return Results.Ok(MapDto(project));
        }).DisableAntiforgery();

        group.MapDelete("/{id:guid}", async (
            Guid id,
            IDbContextFactory<AutoNateDbContext> dbFactory,
            IContentTreeService treeService,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == id, ct);
            if (project is null) return Results.NotFound();
            var name = project.Name;
            db.Projects.Remove(project);
            await db.SaveChangesAsync(ct);
            // Tree closure is keyed by descendant_id which cascades with the
            // entity tables, so this is just a defensive sweep.
            await treeService.DeleteEntityAsync(db, ContentKinds.Project, id, ct);
            await tx.CommitAsync(ct);

            await auditPublisher.PublishAsync(
                ContentEventTopic.TopicName,
                ContentEventTypes.ProjectDeleted,
                ContentResourceKinds.Project,
                resource: new { id, name },
                details: null,
                ct);

            return Results.NoContent();
        }).DisableAntiforgery()
          .RequirePermission(EntityKinds.Project, Actions.Delete);

        return app;
    }

    internal static ProjectDto MapDto(Project p) => new(
        p.Id, p.Locator, p.Name, p.Description, p.DeletionsLocked, p.IsArchived,
        p.CreatedAtUtc, p.UpdatedAtUtc, p.CreatedBy, p.UpdatedBy);

    public sealed record CreateProjectRequest(string Name, string? Description);

    public sealed record UpdateProjectRequest(string? Name, string? Description, bool? IsArchived);

    public sealed record DeletionsLockRequest(bool Locked);

    public sealed record ProjectDto(
        Guid Id, long Locator, string Name, string? Description,
        bool DeletionsLocked, bool IsArchived,
        DateTime CreatedAtUtc, DateTime UpdatedAtUtc, Guid CreatedBy, Guid UpdatedBy);

    public sealed record ProjectPageResponse(List<ProjectDto> Items, int TotalCount);
}
