using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.EndpointFilters;
using AutoNate.Web.Persistence;
using AutoNate.Web.Services.Authorization;
using AutoNate.Web.Services.Content;
using AutoNate.Web.Services.Events;
using Microsoft.EntityFrameworkCore;

namespace AutoNate.Web.Endpoints;

public static class ProjectMemberEndpoints
{
    public static IEndpointRouteBuilder MapProjectMemberEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/content/projects/{projectId:guid}/members")
            .RequireAuthorization();

        // Listing memberships requires View on the project — anyone who can
        // see the project can see who else has access to it.
        group.MapGet("/", async (
            Guid projectId,
            HttpContext http,
            IDbContextFactory<AutoNateDbContext> dbFactory,
            IProjectMembershipService memberships,
            IContentAuthorizer authorizer,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var rows = await memberships.ListMembersAsync(db, projectId, ct);
            var derived = await authorizer.GetDerivedAccessAsync(projectId, ct);
            // The viewer's effective owner status — accounts for SuperAdmin
            // and wildcard grants (same logic the mutation endpoints gate on).
            // SPA needs this to enable the manage-members/revoke controls
            // since it only sees literal project_members rows otherwise.
            var viewerCanManage = await authorizer.IsProjectOwnerAsync(http.User, projectId, ct);

            // Batched name + locator lookup for every resource referenced by
            // Grant rows.
            var resourceInfo = await ResolveResourceInfoAsync(db, derived, ct);
            // Batched name lookup for group/role principals so the SPA can
            // render them without separate fetches.
            var principalNames = await ResolvePrincipalNamesAsync(db, derived, ct);

            var dtos = rows
                .Select(m => new ProjectMemberDto(
                    m.ProjectId,
                    EntityKinds.User,
                    m.UserId,
                    PrincipalName: null,
                    m.Role,
                    m.AddedAtUtc, m.AddedBy, m.UpdatedAtUtc, m.UpdatedBy,
                    ProjectMemberSources.Member,
                    GrantId: null,
                    Action: null,
                    Revokable: null,
                    Resources: null))
                .ToList();
            foreach (var d in derived)
            {
                var source = d.Source switch
                {
                    DerivedAccessSource.SuperAdmin => ProjectMemberSources.SuperAdmin,
                    DerivedAccessSource.Wildcard => ProjectMemberSources.Wildcard,
                    _ => ProjectMemberSources.Grant
                };
                var resources = d.Source == DerivedAccessSource.Grant
                    ? d.Resources
                        .Select(r =>
                        {
                            resourceInfo.TryGetValue((r.Kind, r.Id), out var info);
                            return new DerivedResourceDto(
                                r.Kind, r.Id, info.Name, info.Locator);
                        })
                        .OrderBy(r => kindRank(r.Kind))
                        .ThenBy(r => r.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                        .ToList()
                    : null;
                principalNames.TryGetValue((d.PrincipalKind, d.PrincipalId), out var pname);
                dtos.Add(new ProjectMemberDto(
                    projectId,
                    d.PrincipalKind,
                    d.PrincipalId,
                    PrincipalName: pname,
                    ProjectRoleNames.ToWire(ProjectRole.Owner),
                    DateTime.MinValue, Guid.Empty, DateTime.MinValue, Guid.Empty,
                    source,
                    GrantId: d.GrantId,
                    Action: d.Action,
                    Revokable: d.Revokable,
                    Resources: resources));
            }

            static int kindRank(string kind) => kind switch
            {
                ContentKinds.Project => 0,
                ContentKinds.Cabinet => 1,
                ContentKinds.Notebook => 2,
                ContentKinds.Page => 3,
                _ => 4
            };

            await auditPublisher.PublishAsync(
                ContentEventTopic.TopicName,
                ContentEventTypes.ProjectMemberListViewed,
                ContentResourceKinds.ProjectMember,
                resource: new { projectId },
                details: new { resultCount = dtos.Count },
                ct);
            return Results.Ok(new ProjectMembersResponse(dtos, viewerCanManage));
        }).RequirePermission(EntityKinds.Project, Actions.View, "projectId");

        // Owner-only: upsert another user's role on the project.
        group.MapPut("/{userId:guid}", async (
            Guid projectId,
            Guid userId,
            SetMemberRoleRequest request,
            HttpContext http,
            IContentAuthorizer authorizer,
            IDbContextFactory<AutoNateDbContext> dbFactory,
            IProjectMembershipService memberships,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            if (!await authorizer.IsProjectOwnerAsync(http.User, projectId, ct))
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }
            var role = ProjectRoleNames.TryParse(request.Role);
            if (role is null)
            {
                return Results.BadRequest(new { error = "Role must be owner | contributor | viewer." });
            }
            var actorId = http.GetActorId();
            await using var db = await dbFactory.CreateDbContextAsync(ct);

            var prior = await db.ProjectMembers.AsNoTracking()
                .Where(m => m.ProjectId == projectId && m.UserId == userId)
                .Select(m => (string?)m.Role)
                .FirstOrDefaultAsync(ct);

            try
            {
                await memberships.SetRoleAsync(db, projectId, userId, role.Value,
                    actorId, DateTime.UtcNow, ct);
                await db.SaveChangesAsync(ct);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }

            var eventType = prior is null
                ? ContentEventTypes.ProjectMemberAdded
                : ContentEventTypes.ProjectMemberRoleChanged;
            await auditPublisher.PublishAsync(
                ContentEventTopic.TopicName,
                eventType,
                ContentResourceKinds.ProjectMember,
                resource: new { projectId, userId, role = ProjectRoleNames.ToWire(role.Value) },
                details: prior is null ? null : new { previousRole = prior },
                ct);

            return Results.Ok(new { projectId, userId, role = ProjectRoleNames.ToWire(role.Value) });
        }).DisableAntiforgery()
          .AuthorizedInHandler(
              "IsProjectOwnerAsync gates the upsert; owner-only by design.");

        // Owner-only: remove a member. Refused if it would remove the last
        // owner.
        group.MapDelete("/{userId:guid}", async (
            Guid projectId,
            Guid userId,
            HttpContext http,
            IContentAuthorizer authorizer,
            IDbContextFactory<AutoNateDbContext> dbFactory,
            IProjectMembershipService memberships,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            if (!await authorizer.IsProjectOwnerAsync(http.User, projectId, ct))
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var prior = await db.ProjectMembers.AsNoTracking()
                .Where(m => m.ProjectId == projectId && m.UserId == userId)
                .Select(m => (string?)m.Role)
                .FirstOrDefaultAsync(ct);
            if (prior is null) return Results.NotFound();

            try
            {
                await memberships.RemoveMemberAsync(db, projectId, userId, ct);
                await db.SaveChangesAsync(ct);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }

            await auditPublisher.PublishAsync(
                ContentEventTopic.TopicName,
                ContentEventTypes.ProjectMemberRemoved,
                ContentResourceKinds.ProjectMember,
                resource: new { projectId, userId },
                details: new { previousRole = prior },
                ct);

            return Results.NoContent();
        }).DisableAntiforgery()
          .AuthorizedInHandler(
              "IsProjectOwnerAsync gates the remove; service refuses to drop " +
              "the last Owner.");

        // Owner-only: revoke a derived-access permission grant whose targets
        // are entirely inside this project. Used by the "× revoke" affordance
        // on grant-source rows in the project permissions modal.
        app.MapDelete(
            "/api/content/projects/{projectId:guid}/derived-grants/{grantId:guid}",
            async (
                Guid projectId,
                Guid grantId,
                HttpContext http,
                IContentAuthorizer authorizer,
                IDbContextFactory<AutoNateDbContext> dbFactory,
                IPermissionGrantStore grantStore,
                IAuditEventPublisher auditPublisher,
                CancellationToken ct) =>
            {
                if (!await authorizer.IsProjectOwnerAsync(http.User, projectId, ct))
                {
                    return Results.StatusCode(StatusCodes.Status403Forbidden);
                }
                await using var db = await dbFactory.CreateDbContextAsync(ct);
                var grant = await db.PermissionGrants.AsNoTracking()
                    .FirstOrDefaultAsync(pg => pg.Id == grantId, ct);
                if (grant is null) return Results.NotFound();
                if (!string.Equals(grant.Effect, "allow", StringComparison.OrdinalIgnoreCase))
                {
                    return Results.BadRequest(new
                    {
                        error = "Only allow grants can be revoked from this page."
                    });
                }

                // Re-derive the project subtree and confirm the grant's
                // selector targets only resources inside it. Reject anything
                // we can't fully scope (set selectors, wildcards, predicates).
                var subtree = await db.ContentAncestors.AsNoTracking()
                    .Where(ca => ca.AncestorKind == ContentKinds.Project
                                 && ca.AncestorId == projectId)
                    .Select(ca => new { ca.DescendantKind, ca.DescendantId })
                    .ToListAsync(ct);
                var scope = new HashSet<(string Kind, Guid Id)>();
                foreach (var r in subtree) scope.Add((r.DescendantKind, r.DescendantId));
                scope.Add((ContentKinds.Project, projectId));

                if (!TryGetSafelyRevokableTarget(grant.SelectorString, scope,
                    out var targetKind, out var targetId))
                {
                    return Results.BadRequest(new
                    {
                        error = "Grant targets resources outside this project; revoke it from Admin → Permissions."
                    });
                }

                var deleted = await grantStore.DeleteAsync(grantId, ct);
                if (!deleted) return Results.NotFound();

                await auditPublisher.PublishAsync(
                    IamEventTopic.TopicName,
                    IamEventTypes.PermissionGrantDeleted,
                    IamResourceKinds.PermissionGrant,
                    resource: new { id = grantId, projectId, targetKind, targetId },
                    details: new { source = "project-derived-revoke" },
                    ct);
                return Results.NoContent();
            }).RequireAuthorization().DisableAntiforgery()
              .AuthorizedInHandler(
                  "IsProjectOwnerAsync gates the revoke; grant's selector " +
                  "must resolve entirely inside this project's subtree.");

        return app;
    }

    private static bool TryGetSafelyRevokableTarget(
        string selectorString,
        HashSet<(string Kind, Guid Id)> projectScope,
        out string targetKind,
        out Guid targetId)
    {
        targetKind = string.Empty;
        targetId = Guid.Empty;
        Authorization.Selectors.SelectorAst ast;
        try { ast = Authorization.Selectors.SelectorParser.Parse(selectorString); }
        catch { return false; }
        if (ast.Predicate is not null) return false;
        if (ast.Path.KindsAreWildcard) return false;
        if (ast.Path.Kinds.Count != 1) return false;
        var kind = ast.Path.Kinds[0];
        if (!ContentKinds.IsContentKind(kind)) return false;
        if (ast.Path.Ids is null || ast.Path.Ids.Count == 0) return false;
        if (ast.Path.IdsAreWildcard) return false;
        if (ast.Path.Ids.Count != 1) return false;
        if (!Guid.TryParse(ast.Path.Ids[0], out var id)) return false;
        if (!projectScope.Contains((kind, id))) return false;
        targetKind = kind;
        targetId = id;
        return true;
    }

    public sealed record SetMemberRoleRequest(string Role);

    // Envelope for the members listing. `ViewerCanManage` mirrors
    // IContentAuthorizer.IsProjectOwnerAsync, so the SPA can light up
    // manage/revoke controls for wildcard-grant and SuperAdmin viewers
    // who aren't literal project_members rows.
    public sealed record ProjectMembersResponse(
        IReadOnlyList<ProjectMemberDto> Members,
        bool ViewerCanManage);

    public sealed record ProjectMemberDto(
        Guid ProjectId,
        string PrincipalKind,
        Guid PrincipalId,
        string? PrincipalName,
        string Role,
        DateTime AddedAtUtc, Guid AddedBy, DateTime UpdatedAtUtc, Guid UpdatedBy,
        string Source,
        // GrantId/Action/Revokable are populated for derived rows backed by a
        // permission_grants row (sources `wildcard` and `grant`). They are
        // null for project-members rows and for SuperAdmin-source rows
        // (whose backing record lives in role_assignments).
        Guid? GrantId,
        string? Action,
        bool? Revokable,
        IReadOnlyList<DerivedResourceDto>? Resources);

    public sealed record DerivedResourceDto(
        string Kind, Guid Id, string? Name, long? Locator);

    public static class ProjectMemberSources
    {
        public const string Member = "member";
        public const string SuperAdmin = "super-admin";
        public const string Wildcard = "wildcard";
        public const string Grant = "grant";
    }

    // Resolve principal display names for group/role principals so the SPA
    // can render them without extra fetches. User principals fall through to
    // null — the SPA already has a user lookup keyed by userId.
    private static async Task<IReadOnlyDictionary<(string Kind, Guid Id), string>>
        ResolvePrincipalNamesAsync(
            AutoNateDbContext db,
            IReadOnlyList<DerivedAccess> derived,
            CancellationToken ct)
    {
        var result = new Dictionary<(string Kind, Guid Id), string>();
        var groupIds = derived
            .Where(d => d.PrincipalKind == EntityKinds.Group)
            .Select(d => d.PrincipalId)
            .ToHashSet();
        var roleIds = derived
            .Where(d => d.PrincipalKind == EntityKinds.Role)
            .Select(d => d.PrincipalId)
            .ToHashSet();
        if (groupIds.Count > 0)
        {
            var rows = await db.Groups.AsNoTracking()
                .Where(g => groupIds.Contains(g.Id))
                .Select(g => new { g.Id, g.Name })
                .ToListAsync(ct);
            foreach (var r in rows) result[(EntityKinds.Group, r.Id)] = r.Name;
        }
        if (roleIds.Count > 0)
        {
            var rows = await db.Roles.AsNoTracking()
                .Where(r => roleIds.Contains(r.Id))
                .Select(r => new { r.Id, r.Name })
                .ToListAsync(ct);
            foreach (var r in rows) result[(EntityKinds.Role, r.Id)] = r.Name;
        }
        return result;
    }

    private static async Task<IReadOnlyDictionary<(string Kind, Guid Id), (string Name, long Locator)>>
        ResolveResourceInfoAsync(
            AutoNateDbContext db,
            IReadOnlyList<DerivedAccess> derived,
            CancellationToken ct)
    {
        var idsByKind = derived
            .Where(d => d.Source == DerivedAccessSource.Grant)
            .SelectMany(d => d.Resources)
            .GroupBy(r => r.Kind)
            .ToDictionary(g => g.Key, g => g.Select(r => r.Id).ToHashSet());
        var result = new Dictionary<(string Kind, Guid Id), (string Name, long Locator)>();
        if (idsByKind.Count == 0) return result;

        if (idsByKind.TryGetValue(ContentKinds.Project, out var projectIds))
        {
            var rows = await db.Projects.AsNoTracking()
                .Where(p => projectIds.Contains(p.Id))
                .Select(p => new { p.Id, p.Name, p.Locator })
                .ToListAsync(ct);
            foreach (var r in rows) result[(ContentKinds.Project, r.Id)] = (r.Name, r.Locator);
        }
        if (idsByKind.TryGetValue(ContentKinds.Cabinet, out var cabinetIds))
        {
            var rows = await db.Cabinets.AsNoTracking()
                .Where(c => cabinetIds.Contains(c.Id))
                .Select(c => new { c.Id, c.Name, c.Locator })
                .ToListAsync(ct);
            foreach (var r in rows) result[(ContentKinds.Cabinet, r.Id)] = (r.Name, r.Locator);
        }
        if (idsByKind.TryGetValue(ContentKinds.Notebook, out var notebookIds))
        {
            var rows = await db.Notebooks.AsNoTracking()
                .Where(n => notebookIds.Contains(n.Id))
                .Select(n => new { n.Id, n.Name, n.Locator })
                .ToListAsync(ct);
            foreach (var r in rows) result[(ContentKinds.Notebook, r.Id)] = (r.Name, r.Locator);
        }
        if (idsByKind.TryGetValue(ContentKinds.Page, out var pageIds))
        {
            var rows = await db.Pages.AsNoTracking()
                .Where(p => pageIds.Contains(p.Id))
                .Select(p => new { p.Id, p.Title, p.Locator })
                .ToListAsync(ct);
            foreach (var r in rows) result[(ContentKinds.Page, r.Id)] = (r.Title, r.Locator);
        }
        return result;
    }
}
