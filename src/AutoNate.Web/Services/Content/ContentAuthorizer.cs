using System.Security.Claims;
using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.Selectors;
using AutoNate.Web.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AutoNate.Web.Services.Content;

public sealed class ContentAuthorizer : IContentAuthorizer
{
    private readonly IDbContextFactory<AutoNateDbContext> _dbFactory;
    private readonly ILogger<ContentAuthorizer> _log;

    public ContentAuthorizer(
        IDbContextFactory<AutoNateDbContext> dbFactory,
        ILogger<ContentAuthorizer> log)
    {
        _dbFactory = dbFactory;
        _log = log;
    }

    public async Task<AuthDecision> AuthorizeAsync(
        ClaimsPrincipal actor,
        string kind,
        Guid resourceId,
        string action,
        CancellationToken ct)
    {
        if (!ContentKinds.IsContentKind(kind))
        {
            return AuthDecision.Deny($"kind '{kind}' is not a content kind");
        }

        var userId = TryGetUserId(actor);
        if (userId is null)
        {
            return AuthDecision.Deny("no user identity");
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var ctx = await LoadActorAsync(db, userId.Value, ct);

        if (ctx.IsSuperAdmin)
        {
            return AuthDecision.Allow("super admin");
        }

        // Resolve the resource's project + the lock state in one query so
        // every Delete decision can consult deletions_locked without a second
        // round trip.
        var project = await db.ContentAncestors.AsNoTracking()
            .Where(ca => ca.DescendantKind == kind
                         && ca.DescendantId == resourceId
                         && ca.AncestorKind == ContentKinds.Project)
            .Join(db.Projects, ca => ca.AncestorId, p => p.Id,
                (ca, p) => new { p.Id, p.DeletionsLocked })
            .FirstOrDefaultAsync(ct);

        if (project is null)
        {
            // The resource doesn't have a project ancestor yet — either it
            // doesn't exist or its closure rows haven't been written. Fail
            // closed; the create handler is responsible for the closure.
            return AuthDecision.Deny("no project ancestor for resource");
        }

        // Pull every override grant that applies to this resource through
        // any ancestor (depth 0 = self). One join across permission_grants
        // and content_ancestors; the depth + effect tiebreak is done in code
        // to keep the SQL portable.
        var overrides = await LoadOverrideGrantsAsync(db, ctx, kind, resourceId, action, ct);

        if (overrides.Count > 0)
        {
            var bestDepth = overrides.Min(g => g.Depth);
            var atBestDepth = overrides.Where(g => g.Depth == bestDepth).ToList();
            // Deny beats allow when both exist at the same depth.
            var hasDeny = atBestDepth.Any(g => g.Effect == AuthEffect.Deny);
            if (hasDeny)
            {
                return EnforceDeletionLock(action, kind, project.DeletionsLocked,
                    AuthDecision.Deny($"override deny at depth {bestDepth}"));
            }
            return EnforceDeletionLock(action, kind, project.DeletionsLocked,
                AuthDecision.Allow($"override allow at depth {bestDepth}"));
        }

        // No override → role baseline.
        var role = await GetRoleStringAsync(db, project.Id, ctx.UserId, ct);
        var baselineAllowed = role is not null && RoleAllowsAction(role, action);
        if (!baselineAllowed)
        {
            return AuthDecision.Deny(role is null
                ? "no project membership"
                : $"role '{role}' does not grant '{action}'");
        }
        return EnforceDeletionLock(action, kind, project.DeletionsLocked,
            AuthDecision.Allow($"role '{role}' baseline"));
    }

    public async Task<ContentAccessSet> GetAllowedIdsAsync(
        ClaimsPrincipal actor,
        string kind,
        string action,
        CancellationToken ct)
    {
        if (!ContentKinds.IsContentKind(kind))
        {
            return ContentAccessSet.Empty;
        }

        var userId = TryGetUserId(actor);
        if (userId is null)
        {
            return ContentAccessSet.Empty;
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var ctx = await LoadActorAsync(db, userId.Value, ct);
        if (ctx.IsSuperAdmin)
        {
            return ContentAccessSet.UnrestrictedAccess();
        }

        // Collect every resource id of this kind whose project ancestor is
        // one the actor is a member of. Then merge with overrides. Final
        // decision per id is computed in code, mirroring AuthorizeAsync.
        var lockedProjectIds = await db.Projects.AsNoTracking()
            .Where(p => p.DeletionsLocked)
            .Select(p => p.Id)
            .ToListAsync(ct);
        var lockedSet = lockedProjectIds.ToHashSet();

        // (resourceId, projectId) pairs for every resource of this kind.
        var resources = await db.ContentAncestors.AsNoTracking()
            .Where(ca => ca.DescendantKind == kind
                         && ca.AncestorKind == ContentKinds.Project)
            .Select(ca => new { ResourceId = ca.DescendantId, ProjectId = ca.AncestorId })
            .ToListAsync(ct);

        // Role baseline by project.
        var memberships = await db.ProjectMembers.AsNoTracking()
            .Where(m => m.UserId == ctx.UserId)
            .Select(m => new { m.ProjectId, m.Role })
            .ToListAsync(ct);
        var rolesByProject = memberships.ToDictionary(m => m.ProjectId, m => m.Role);

        // Override grants for this kind+action across every resource the
        // actor's principals reach. Loaded in one shot then bucketed per
        // resource so the closest-depth tiebreak is in-memory.
        var overrideRows = await LoadOverrideGrantsForListingAsync(db, ctx, kind, action, ct);
        var overridesByResource = overrideRows
            .GroupBy(r => r.ResourceId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var allowed = new HashSet<Guid>();
        foreach (var r in resources)
        {
            var locked = lockedSet.Contains(r.ProjectId);
            if (overridesByResource.TryGetValue(r.ResourceId, out var rowOverrides))
            {
                var bestDepth = rowOverrides.Min(g => g.Depth);
                var atBest = rowOverrides.Where(g => g.Depth == bestDepth).ToList();
                var hasDeny = atBest.Any(g => g.Effect == AuthEffect.Deny);
                if (hasDeny)
                {
                    continue;
                }
                if (action == Actions.Delete && locked && kind != ContentKinds.Project &&
                    !KindIsAlwaysDeletable(kind))
                {
                    // Lock blocks Delete on content kinds (notes are not in
                    // this map — they aren't content kinds — and attachments
                    // are gated through Page.Delete which we want blocked).
                    continue;
                }
                if (action == Actions.Delete && locked)
                {
                    continue;
                }
                allowed.Add(r.ResourceId);
                continue;
            }

            // Baseline path.
            if (rolesByProject.TryGetValue(r.ProjectId, out var role) &&
                RoleAllowsAction(role, action))
            {
                if (action == Actions.Delete && locked)
                {
                    continue;
                }
                allowed.Add(r.ResourceId);
            }
        }

        return ContentAccessSet.From(allowed);
    }

    public async Task<ProjectRole?> GetProjectRoleAsync(
        ClaimsPrincipal actor, Guid projectId, CancellationToken ct)
    {
        var userId = TryGetUserId(actor);
        if (userId is null) return null;
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var ctx = await LoadActorAsync(db, userId.Value, ct);
        if (ctx.IsSuperAdmin) return ProjectRole.Owner;
        var roleString = await GetRoleStringAsync(db, projectId, ctx.UserId, ct);
        return ProjectRoleNames.TryParse(roleString);
    }

    public async Task<bool> IsProjectOwnerAsync(
        ClaimsPrincipal actor, Guid projectId, CancellationToken ct)
    {
        var role = await GetProjectRoleAsync(actor, projectId, ct);
        return role == ProjectRole.Owner;
    }

    // ---- private helpers ----

    private static Guid? TryGetUserId(ClaimsPrincipal actor)
    {
        var raw = actor.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(raw, out var id) ? id : null;
    }

    private static AuthDecision EnforceDeletionLock(
        string action, string kind, bool locked, AuthDecision baseline)
    {
        if (!baseline.IsAllowed) return baseline;
        if (action != Actions.Delete) return baseline;
        if (!locked) return baseline;
        if (KindIsAlwaysDeletable(kind)) return baseline;
        return AuthDecision.Deny("project deletions are locked");
    }

    // Notes aren't a content kind so they never reach this authorizer's
    // Delete path, but attachments and pages do — and they are subject to
    // the lock. Keep the helper here so the rule stays explicit.
    private static bool KindIsAlwaysDeletable(string kind) => false;

    private static bool RoleAllowsAction(string roleString, string action)
    {
        return ProjectRoleNames.TryParse(roleString) switch
        {
            ProjectRole.Owner => true,
            ProjectRole.Contributor => action is Actions.View or Actions.Create
                or Actions.Edit or Actions.Delete or Actions.Archive,
            ProjectRole.Viewer => action == Actions.View,
            _ => false
        };
    }

    private static async Task<ActorPrincipals> LoadActorAsync(
        AutoNateDbContext db, Guid userId, CancellationToken ct)
    {
        var groupIds = await db.GroupMembers.AsNoTracking()
            .Where(m => m.UserId == userId)
            .Select(m => m.GroupId)
            .ToListAsync(ct);
        var userIdString = userId.ToString();
        var groupIdStrings = groupIds.Select(g => g.ToString()).ToList();

        var roleIds = await db.RoleAssignments.AsNoTracking()
            .Where(a =>
                (a.PrincipalKind == EntityKinds.User && a.PrincipalId == userIdString)
                || (a.PrincipalKind == EntityKinds.Group && groupIdStrings.Contains(a.PrincipalId)))
            .Select(a => a.RoleId)
            .Distinct()
            .ToListAsync(ct);

        var isSuperAdmin = roleIds.Contains(SystemRoles.SuperAdminId);
        return new ActorPrincipals(userId, groupIds, roleIds, isSuperAdmin);
    }

    private static Task<string?> GetRoleStringAsync(
        AutoNateDbContext db, Guid projectId, Guid userId, CancellationToken ct) =>
        db.ProjectMembers.AsNoTracking()
            .Where(m => m.ProjectId == projectId && m.UserId == userId)
            .Select(m => (string?)m.Role)
            .FirstOrDefaultAsync(ct);

    private async Task<List<OverrideRow>> LoadOverrideGrantsAsync(
        AutoNateDbContext db, ActorPrincipals ctx, string descendantKind, Guid descendantId,
        string action, CancellationToken ct)
    {
        // Ancestor chain for this resource: (ancestorKind, ancestorId, depth).
        var chain = await db.ContentAncestors.AsNoTracking()
            .Where(ca => ca.DescendantKind == descendantKind && ca.DescendantId == descendantId)
            .Select(ca => new { ca.AncestorKind, ca.AncestorId, ca.Depth })
            .ToListAsync(ct);
        if (chain.Count == 0) return new List<OverrideRow>();

        // Pull every grant matching the actor's principals + action; filter
        // to content kinds and parse the selector path in-process so we don't
        // need JSONB-specific predicates in LINQ.
        var grants = await LoadActorGrantsAsync(db, ctx, action, ct);
        if (grants.Count == 0) return new List<OverrideRow>();

        var chainSet = chain.ToDictionary(c => (c.AncestorKind, c.AncestorId), c => c.Depth);
        var result = new List<OverrideRow>();
        foreach (var g in grants)
        {
            if (!TryGetPathTarget(g.Ast, out var pathKind, out var pathId))
            {
                continue;
            }
            if (!ContentKinds.IsContentKind(pathKind)) continue;
            // Wildcard ids would mean "every entity of this kind" — out of
            // scope for the inheritance phase; treat as kind-only and skip.
            if (pathId is null) continue;
            if (chainSet.TryGetValue((pathKind, pathId.Value), out var depth))
            {
                result.Add(new OverrideRow(descendantId, depth, g.Effect));
            }
        }
        return result;
    }

    private async Task<List<OverrideListingRow>> LoadOverrideGrantsForListingAsync(
        AutoNateDbContext db, ActorPrincipals ctx, string kind, string action,
        CancellationToken ct)
    {
        var grants = await LoadActorGrantsAsync(db, ctx, action, ct);
        if (grants.Count == 0) return new List<OverrideListingRow>();

        // Collect each grant's (kind, id) target, then a single closure
        // query gives us every descendant of those targets within `kind`.
        var targets = new List<(string Kind, Guid Id, AuthEffect Effect)>();
        foreach (var g in grants)
        {
            if (!TryGetPathTarget(g.Ast, out var pathKind, out var pathId)) continue;
            if (!ContentKinds.IsContentKind(pathKind)) continue;
            if (pathId is null) continue;
            targets.Add((pathKind, pathId.Value, g.Effect));
        }
        if (targets.Count == 0) return new List<OverrideListingRow>();

        // For each target, find every descendant of `kind` plus the depth
        // from descendant to that specific target ancestor.
        var rows = new List<OverrideListingRow>();
        foreach (var t in targets)
        {
            var matches = await db.ContentAncestors.AsNoTracking()
                .Where(ca => ca.DescendantKind == kind
                             && ca.AncestorKind == t.Kind
                             && ca.AncestorId == t.Id)
                .Select(ca => new { ca.DescendantId, ca.Depth })
                .ToListAsync(ct);
            foreach (var m in matches)
            {
                rows.Add(new OverrideListingRow(m.DescendantId, m.Depth, t.Effect));
            }
        }
        return rows;
    }

    private async Task<List<ParsedGrant>> LoadActorGrantsAsync(
        AutoNateDbContext db, ActorPrincipals ctx, string action, CancellationToken ct)
    {
        var userIdString = ctx.UserId.ToString();
        var groupIdStrings = ctx.GroupIds.Select(g => g.ToString()).ToList();
        var roleIdStrings = ctx.RoleIds.Select(r => r.ToString()).ToList();

        var raw = await db.PermissionGrants.AsNoTracking()
            .Where(pg =>
                (pg.Action == action || pg.Action == Actions.Wildcard)
                && (
                    (pg.PrincipalKind == EntityKinds.User && pg.PrincipalId == userIdString) ||
                    (pg.PrincipalKind == EntityKinds.Group && groupIdStrings.Contains(pg.PrincipalId)) ||
                    (pg.PrincipalKind == EntityKinds.Role && roleIdStrings.Contains(pg.PrincipalId))
                ))
            .Select(pg => new { pg.SelectorString, pg.Effect })
            .ToListAsync(ct);

        var result = new List<ParsedGrant>(raw.Count);
        foreach (var r in raw)
        {
            SelectorAst ast;
            try
            {
                ast = SelectorParser.Parse(r.SelectorString);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex,
                    "Skipping unparseable content grant '{Selector}'",
                    r.SelectorString);
                continue;
            }
            var effect = string.Equals(r.Effect, "deny", StringComparison.OrdinalIgnoreCase)
                ? AuthEffect.Deny
                : AuthEffect.Allow;
            result.Add(new ParsedGrant(ast, effect));
        }
        return result;
    }

    // Selectors for content grants are path-only of the form /<kind>/<id>.
    // Returns the (kind, id) the grant targets, or (null, null) if the
    // selector doesn't fit the supported shape.
    private static bool TryGetPathTarget(SelectorAst ast, out string pathKind, out Guid? pathId)
    {
        pathKind = string.Empty;
        pathId = null;
        if (ast.Path.Kinds.Count != 1) return false;
        if (ast.Path.KindsAreWildcard) return false;
        pathKind = ast.Path.Kinds[0];
        if (ast.Path.Ids is null || ast.Path.Ids.Count == 0)
        {
            return false; // kind-only grants don't apply through the closure
        }
        if (ast.Path.IdsAreWildcard) return false;
        if (ast.Path.Ids.Count != 1) return false;
        if (!Guid.TryParse(ast.Path.Ids[0], out var parsed)) return false;
        pathId = parsed;
        return true;
    }

    private sealed record class ActorPrincipals(
        Guid UserId,
        IReadOnlyList<Guid> GroupIds,
        IReadOnlyList<Guid> RoleIds,
        bool IsSuperAdmin);

    private readonly record struct ParsedGrant(SelectorAst Ast, AuthEffect Effect);

    private readonly record struct OverrideRow(Guid ResourceId, int Depth, AuthEffect Effect);

    private readonly record struct OverrideListingRow(Guid ResourceId, int Depth, AuthEffect Effect);
}
