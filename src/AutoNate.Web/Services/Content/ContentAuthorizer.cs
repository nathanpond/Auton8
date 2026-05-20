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

    // ContentAuthorizer is scoped — one instance per request. The project-tree
    // endpoint hits GetAllowedIdsAsync six times (cabinet/notebook/page × view/
    // edit) with the same actor; each call otherwise re-runs the grant load,
    // membership query, and full closure scan. Memoize by (user, kind, action)
    // so repeat calls within a request collapse to a single computation.
    // Endpoint flow is sequential await — no Task.WhenAll across this service —
    // so a plain Dictionary is safe.
    private readonly Dictionary<(Guid UserId, string Kind, string Action), ContentAccessSet>
        _accessSetCache = new();

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

        // Load every grant for this principal+action once, then derive both
        // the closest-ancestor overrides and the "global content allow"
        // wildcard from the same list.
        var grants = await LoadActorGrantsAsync(db, ctx, action, ct);
        var hasWildcardAllow = HasGlobalContentAllow(grants);
        var overrides = await BuildOverrideRowsAsync(db, grants, kind, resourceId, ct);

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

        // No specific override: a wildcard allow grant (`*` / `<action>` on
        // `/*`) acts as a baseline pass over every content resource, similar
        // to super-admin but still subject to the project deletion lock.
        if (hasWildcardAllow)
        {
            return EnforceDeletionLock(action, kind, project.DeletionsLocked,
                AuthDecision.Allow("wildcard grant"));
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

        var cacheKey = (userId.Value, kind, action);
        if (_accessSetCache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        var computed = await ComputeAllowedIdsAsync(userId.Value, kind, action, ct);
        _accessSetCache[cacheKey] = computed;
        return computed;
    }

    private async Task<ContentAccessSet> ComputeAllowedIdsAsync(
        Guid userId, string kind, string action, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var ctx = await LoadActorAsync(db, userId, ct);
        if (ctx.IsSuperAdmin)
        {
            return ContentAccessSet.UnrestrictedAccess();
        }

        // Single grant load drives both the per-resource override path and
        // the global wildcard fallback.
        var grants = await LoadActorGrantsAsync(db, ctx, action, ct);
        var hasWildcardAllow = HasGlobalContentAllow(grants);

        // Wildcard-allow with no carve-outs and no lock concerns is the
        // same answer as super-admin — skip resource enumeration entirely.
        if (hasWildcardAllow && !HasAnyContentDeny(grants))
        {
            var lockBlocksAll = action == Actions.Delete
                && await db.Projects.AsNoTracking().AnyAsync(p => p.DeletionsLocked, ct);
            if (!lockBlocksAll)
            {
                return ContentAccessSet.UnrestrictedAccess();
            }
        }

        // Role baseline by project (small — bounded by the actor's project
        // count). Loaded up front because both fast and slow paths need it.
        var memberships = await db.ProjectMembers.AsNoTracking()
            .Where(m => m.UserId == ctx.UserId)
            .Select(m => new { m.ProjectId, m.Role })
            .ToListAsync(ct);

        // Fast path: actor has no wildcard-allow and no content-kind override
        // targets. The answer is exactly "every resource of `kind` whose
        // project ancestor is a membership project whose role grants
        // `action`" — push it into SQL and skip the system-wide closure
        // scan + in-memory merge.
        if (!hasWildcardAllow && !HasOverrideTargets(grants))
        {
            var grantingProjectIds = memberships
                .Where(m => RoleAllowsAction(m.Role, action))
                .Select(m => m.ProjectId)
                .ToList();
            if (grantingProjectIds.Count == 0)
            {
                return ContentAccessSet.Empty;
            }

            var fastQuery = db.ContentAncestors.AsNoTracking()
                .Where(ca => ca.DescendantKind == kind
                             && ca.AncestorKind == ContentKinds.Project
                             && grantingProjectIds.Contains(ca.AncestorId));
            if (action == Actions.Delete)
            {
                // Drop resources whose project is delete-locked. Done with a
                // subquery so we don't materialise the locked set unless this
                // action actually cares about it.
                fastQuery = fastQuery.Where(ca =>
                    !db.Projects.Any(p => p.Id == ca.AncestorId && p.DeletionsLocked));
            }
            var fastIds = await fastQuery
                .Select(ca => ca.DescendantId)
                .ToListAsync(ct);
            return ContentAccessSet.From(fastIds);
        }

        // Slow path: wildcard-with-denies or explicit override grants. We
        // need the full resource list because overrides can reach beyond the
        // actor's membership scope, and per-resource override math doesn't
        // fit cleanly in SQL.
        //
        // Locked-projects only matter for Delete; for any other action the
        // lock checks below short-circuit before consulting `lockedSet`, so
        // skipping the load saves a round trip on every non-Delete call.
        HashSet<Guid> lockedSet;
        if (action == Actions.Delete)
        {
            var lockedProjectIds = await db.Projects.AsNoTracking()
                .Where(p => p.DeletionsLocked)
                .Select(p => p.Id)
                .ToListAsync(ct);
            lockedSet = lockedProjectIds.ToHashSet();
        }
        else
        {
            lockedSet = new HashSet<Guid>();
        }

        // (resourceId, projectId) pairs for every resource of this kind.
        var resources = await db.ContentAncestors.AsNoTracking()
            .Where(ca => ca.DescendantKind == kind
                         && ca.AncestorKind == ContentKinds.Project)
            .Select(ca => new { ResourceId = ca.DescendantId, ProjectId = ca.AncestorId })
            .ToListAsync(ct);

        var rolesByProject = memberships.ToDictionary(m => m.ProjectId, m => m.Role);

        // Override grants for this kind+action across every resource the
        // actor's principals reach. Loaded in one shot then bucketed per
        // resource so the closest-depth tiebreak is in-memory.
        var overrideRows = await BuildOverrideListingRowsAsync(db, grants, kind, ct);
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

            // Wildcard-allow acts as a baseline over every resource when
            // there's no specific override. Project lock still gates Delete.
            if (hasWildcardAllow)
            {
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
        // A wildcard-action allow grant on `/*` confers owner-equivalent
        // standing for member-management and the deletion-lock toggle.
        var wildcardActionGrants = await LoadActorGrantsAsync(db, ctx, Actions.Wildcard, ct);
        if (HasGlobalContentAllow(wildcardActionGrants)) return ProjectRole.Owner;
        var roleString = await GetRoleStringAsync(db, projectId, ctx.UserId, ct);
        return ProjectRoleNames.TryParse(roleString);
    }

    public async Task<bool> IsProjectOwnerAsync(
        ClaimsPrincipal actor, Guid projectId, CancellationToken ct)
    {
        var role = await GetProjectRoleAsync(actor, projectId, ct);
        return role == ProjectRole.Owner;
    }

    public async Task<IReadOnlyList<DerivedAccess>> GetDerivedAccessAsync(
        Guid projectId, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var noResources = Array.Empty<DerivedResource>();
        var result = new List<DerivedAccess>();
        var seenSuperAdmin = new HashSet<(string Kind, Guid Id)>();

        // SuperAdmin role: emit one row per role_assignment principal. The
        // assignment principal is always user|group (never role).
        var superAdminAssignments = await db.RoleAssignments.AsNoTracking()
            .Where(a => a.RoleId == SystemRoles.SuperAdminId)
            .Select(a => new { a.PrincipalKind, a.PrincipalId })
            .ToListAsync(ct);
        foreach (var a in superAdminAssignments)
        {
            if (!Guid.TryParse(a.PrincipalId, out var pid)) continue;
            if (seenSuperAdmin.Add((a.PrincipalKind, pid)))
            {
                result.Add(new DerivedAccess(
                    GrantId: null,
                    a.PrincipalKind, pid,
                    DerivedAccessSource.SuperAdmin,
                    Action: null,
                    Revokable: false,
                    noResources));
            }
        }

        // Project subtree — used both for wildcard de-dup and grant scoping.
        var subtree = await db.ContentAncestors.AsNoTracking()
            .Where(ca => ca.AncestorKind == ContentKinds.Project && ca.AncestorId == projectId)
            .Select(ca => new { ca.DescendantKind, ca.DescendantId })
            .ToListAsync(ct);
        var projectScope = new HashSet<(string Kind, Guid Id)>();
        foreach (var r in subtree) projectScope.Add((r.DescendantKind, r.DescendantId));
        projectScope.Add((ContentKinds.Project, projectId));

        // Walk every allow grant once. Branch on selector shape:
        //  - `/*` (wildcard kind): Wildcard source — emit one row per grant.
        //  - `/<contentKind>/<id>` or `/<contentKind>/{ids}`: explicit content
        //    targets — emit a Grant row if any of those ids are in scope.
        // Anything else (wildcard ids, predicates, non-content kinds) is
        // dropped — those don't fit the "show a specific resource link" model
        // and are managed under Admin → Permissions.
        var grants = await db.PermissionGrants.AsNoTracking()
            .Where(pg => pg.Effect == "allow")
            .Select(pg => new
            {
                pg.Id, pg.PrincipalKind, pg.PrincipalId, pg.Action, pg.SelectorString
            })
            .ToListAsync(ct);
        foreach (var g in grants)
        {
            if (!Guid.TryParse(g.PrincipalId, out var ppid)) continue;
            SelectorAst ast;
            try { ast = SelectorParser.Parse(g.SelectorString); }
            catch { continue; }

            // Wildcard source: `/*` only — predicate/id-wildcard variants are
            // not "global content allow" and don't get owner-equivalent.
            if (IsGlobalContentAllow(ast))
            {
                result.Add(new DerivedAccess(
                    g.Id, g.PrincipalKind, ppid,
                    DerivedAccessSource.Wildcard,
                    g.Action,
                    Revokable: false,
                    noResources));
                continue;
            }

            // Grant source: must have a single content kind and an explicit
            // (non-wildcard, non-predicate) id list.
            if (ast.Predicate is not null) continue;
            if (ast.Path.KindsAreWildcard) continue;
            if (ast.Path.Kinds.Count != 1) continue;
            var pathKind = ast.Path.Kinds[0];
            if (!ContentKinds.IsContentKind(pathKind)) continue;
            if (ast.Path.Ids is null || ast.Path.Ids.Count == 0) continue;
            if (ast.Path.IdsAreWildcard) continue;

            var grantTargets = new List<(string Kind, Guid Id)>(ast.Path.Ids.Count);
            var malformed = false;
            foreach (var idStr in ast.Path.Ids)
            {
                if (!Guid.TryParse(idStr, out var id)) { malformed = true; break; }
                grantTargets.Add((pathKind, id));
            }
            if (malformed) continue;

            var inScope = new List<DerivedResource>();
            foreach (var t in grantTargets)
            {
                if (projectScope.Contains(t))
                {
                    inScope.Add(new DerivedResource(t.Kind, t.Id));
                }
            }
            if (inScope.Count == 0) continue;
            var allInScope = inScope.Count == grantTargets.Count;

            result.Add(new DerivedAccess(
                g.Id, g.PrincipalKind, ppid,
                DerivedAccessSource.Grant,
                g.Action,
                Revokable: allInScope,
                inScope));
        }
        return result;
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

    private static async Task<List<OverrideRow>> BuildOverrideRowsAsync(
        AutoNateDbContext db, IReadOnlyList<ParsedGrant> grants,
        string descendantKind, Guid descendantId, CancellationToken ct)
    {
        if (grants.Count == 0) return new List<OverrideRow>();

        // Ancestor chain for this resource: (ancestorKind, ancestorId, depth).
        var chain = await db.ContentAncestors.AsNoTracking()
            .Where(ca => ca.DescendantKind == descendantKind && ca.DescendantId == descendantId)
            .Select(ca => new { ca.AncestorKind, ca.AncestorId, ca.Depth })
            .ToListAsync(ct);
        if (chain.Count == 0) return new List<OverrideRow>();

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

    private static async Task<List<OverrideListingRow>> BuildOverrideListingRowsAsync(
        AutoNateDbContext db, IReadOnlyList<ParsedGrant> grants, string kind,
        CancellationToken ct)
    {
        if (grants.Count == 0) return new List<OverrideListingRow>();

        // Collect each grant's (kind, id) target. Multiple grants can land on
        // the same (kind, id) with different effects (e.g. user-allow plus
        // group-deny), so retain a list of effects per target rather than
        // collapsing.
        var effectsByTarget = new Dictionary<(string Kind, Guid Id), List<AuthEffect>>();
        foreach (var g in grants)
        {
            if (!TryGetPathTarget(g.Ast, out var pathKind, out var pathId)) continue;
            if (!ContentKinds.IsContentKind(pathKind)) continue;
            if (pathId is null) continue;
            var key = (pathKind, pathId.Value);
            if (!effectsByTarget.TryGetValue(key, out var list))
            {
                list = new List<AuthEffect>();
                effectsByTarget[key] = list;
            }
            list.Add(g.Effect);
        }
        if (effectsByTarget.Count == 0) return new List<OverrideListingRow>();

        // Group targets by ancestor kind so we issue at most one closure
        // query per kind (≤ 4 round trips total, regardless of grant count)
        // instead of one per grant target.
        var idsByAncestorKind = new Dictionary<string, List<Guid>>();
        foreach (var (target, _) in effectsByTarget)
        {
            if (!idsByAncestorKind.TryGetValue(target.Kind, out var ids))
            {
                ids = new List<Guid>();
                idsByAncestorKind[target.Kind] = ids;
            }
            ids.Add(target.Id);
        }

        var rows = new List<OverrideListingRow>();
        foreach (var (ancestorKind, ids) in idsByAncestorKind)
        {
            var matches = await db.ContentAncestors.AsNoTracking()
                .Where(ca => ca.DescendantKind == kind
                             && ca.AncestorKind == ancestorKind
                             && ids.Contains(ca.AncestorId))
                .Select(ca => new { ca.AncestorId, ca.DescendantId, ca.Depth })
                .ToListAsync(ct);
            foreach (var m in matches)
            {
                if (!effectsByTarget.TryGetValue((ancestorKind, m.AncestorId), out var effects))
                {
                    continue;
                }
                foreach (var effect in effects)
                {
                    rows.Add(new OverrideListingRow(m.DescendantId, m.Depth, effect));
                }
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

    // A "global content allow" is a permission_grants row whose selector is
    // `/*` with no ids and no predicate, effect=allow. It mirrors the super-
    // admin short-circuit but stays within the normal grant model so admins
    // can carve out specific-resource denies on top of it.
    private static bool IsGlobalContentAllow(SelectorAst ast) =>
        ast.Path.KindsAreWildcard
        && (ast.Path.Ids is null || ast.Path.Ids.Count == 0)
        && ast.Predicate is null;

    private static bool HasGlobalContentAllow(IReadOnlyList<ParsedGrant> grants)
    {
        foreach (var g in grants)
        {
            if (g.Effect == AuthEffect.Allow && IsGlobalContentAllow(g.Ast))
            {
                return true;
            }
        }
        return false;
    }

    private static bool HasAnyContentDeny(IReadOnlyList<ParsedGrant> grants)
    {
        foreach (var g in grants)
        {
            if (g.Effect != AuthEffect.Deny) continue;
            if (!TryGetPathTarget(g.Ast, out var pathKind, out _)) continue;
            if (ContentKinds.IsContentKind(pathKind)) return true;
        }
        return false;
    }

    // Does any grant target a specific content resource (the shape that
    // BuildOverrideListingRowsAsync turns into override rows)? Used to decide
    // whether GetAllowedIdsAsync can take the SQL fast path or has to fall
    // back to the per-resource merge.
    private static bool HasOverrideTargets(IReadOnlyList<ParsedGrant> grants)
    {
        foreach (var g in grants)
        {
            if (!TryGetPathTarget(g.Ast, out var pathKind, out var pathId)) continue;
            if (pathId is null) continue;
            if (ContentKinds.IsContentKind(pathKind)) return true;
        }
        return false;
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
