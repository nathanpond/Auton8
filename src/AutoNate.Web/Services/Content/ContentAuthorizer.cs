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

    public async Task<IReadOnlyDictionary<Guid, AuthDecision>> AuthorizeManyAsync(
        IReadOnlyCollection<Guid> userIds,
        string kind,
        Guid resourceId,
        string action,
        CancellationToken ct)
    {
        var distinct = userIds.Distinct().ToList();
        var result = new Dictionary<Guid, AuthDecision>(distinct.Count);
        if (distinct.Count == 0) return result;

        if (!ContentKinds.IsContentKind(kind))
        {
            foreach (var u in distinct) result[u] = AuthDecision.Deny($"kind '{kind}' is not a content kind");
            return result;
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        // Resource shape is shared across all callers — one query each for
        // project + ancestor chain.
        var project = await db.ContentAncestors.AsNoTracking()
            .Where(ca => ca.DescendantKind == kind
                         && ca.DescendantId == resourceId
                         && ca.AncestorKind == ContentKinds.Project)
            .Join(db.Projects, ca => ca.AncestorId, p => p.Id,
                (ca, p) => new { p.Id, p.DeletionsLocked })
            .FirstOrDefaultAsync(ct);
        if (project is null)
        {
            foreach (var u in distinct) result[u] = AuthDecision.Deny("no project ancestor for resource");
            return result;
        }
        var chainRows = await db.ContentAncestors.AsNoTracking()
            .Where(ca => ca.DescendantKind == kind && ca.DescendantId == resourceId)
            .Select(ca => new { ca.AncestorKind, ca.AncestorId, ca.Depth })
            .ToListAsync(ct);
        var chainSet = chainRows.ToDictionary(c => (c.AncestorKind, c.AncestorId), c => c.Depth);

        // Per-user direct group memberships.
        var groupRows = await db.GroupMembers.AsNoTracking()
            .Where(m => distinct.Contains(m.UserId))
            .Select(m => new { m.UserId, m.GroupId })
            .ToListAsync(ct);
        var userGroups = distinct.ToDictionary(u => u, _ => new List<Guid>());
        var allGroupIds = new HashSet<Guid>();
        foreach (var g in groupRows)
        {
            userGroups[g.UserId].Add(g.GroupId);
            allGroupIds.Add(g.GroupId);
        }

        // Role assignments for all (user, group) principals in one query.
        var userIdStrings = distinct.Select(u => u.ToString()).ToList();
        var allGroupIdStrings = allGroupIds.Select(g => g.ToString()).ToList();
        var roleRows = await db.RoleAssignments.AsNoTracking()
            .Where(a =>
                (a.PrincipalKind == EntityKinds.User && userIdStrings.Contains(a.PrincipalId))
                || (a.PrincipalKind == EntityKinds.Group && allGroupIdStrings.Contains(a.PrincipalId)))
            .Select(a => new { a.PrincipalKind, a.PrincipalId, a.RoleId })
            .ToListAsync(ct);
        var userRoles = distinct.ToDictionary(u => u, _ => new HashSet<Guid>());
        var superAdmins = new HashSet<Guid>();
        foreach (var r in roleRows)
        {
            if (r.PrincipalKind == EntityKinds.User)
            {
                if (!Guid.TryParse(r.PrincipalId, out var uid)) continue;
                if (!userRoles.TryGetValue(uid, out var rs)) continue;
                rs.Add(r.RoleId);
                if (r.RoleId == SystemRoles.SuperAdminId) superAdmins.Add(uid);
            }
            else if (r.PrincipalKind == EntityKinds.Group)
            {
                if (!Guid.TryParse(r.PrincipalId, out var gid)) continue;
                foreach (var (u, gs) in userGroups)
                {
                    if (!gs.Contains(gid)) continue;
                    userRoles[u].Add(r.RoleId);
                    if (r.RoleId == SystemRoles.SuperAdminId) superAdmins.Add(u);
                }
            }
        }
        foreach (var u in superAdmins) result[u] = AuthDecision.Allow("super admin");

        var remaining = distinct.Where(u => !superAdmins.Contains(u)).ToList();
        if (remaining.Count == 0) return result;

        // Single permission_grants load covering every relevant principal —
        // direct user, every group the users are in, every role attached.
        var remainingUserStrings = remaining.Select(u => u.ToString()).ToList();
        var allRoleIdStrings = userRoles.Values.SelectMany(s => s).Distinct()
            .Select(r => r.ToString()).ToList();
        var grantRows = await db.PermissionGrants.AsNoTracking()
            .Where(pg =>
                (pg.Action == action || pg.Action == Actions.Wildcard)
                && (
                    (pg.PrincipalKind == EntityKinds.User && remainingUserStrings.Contains(pg.PrincipalId))
                    || (pg.PrincipalKind == EntityKinds.Group && allGroupIdStrings.Contains(pg.PrincipalId))
                    || (pg.PrincipalKind == EntityKinds.Role && allRoleIdStrings.Contains(pg.PrincipalId))
                ))
            .Select(pg => new { pg.PrincipalKind, pg.PrincipalId, pg.SelectorString, pg.Effect })
            .ToListAsync(ct);

        var grantsByUser = new Dictionary<Guid, List<ParsedGrant>>();
        var grantsByGroup = new Dictionary<Guid, List<ParsedGrant>>();
        var grantsByRole = new Dictionary<Guid, List<ParsedGrant>>();
        foreach (var g in grantRows)
        {
            SelectorAst ast;
            try { ast = SelectorParser.Parse(g.SelectorString); }
            catch (Exception ex)
            {
                _log.LogWarning(ex,
                    "Skipping unparseable content grant '{Selector}'", g.SelectorString);
                continue;
            }
            var effect = string.Equals(g.Effect, "deny", StringComparison.OrdinalIgnoreCase)
                ? AuthEffect.Deny : AuthEffect.Allow;
            var parsed = new ParsedGrant(ast, effect);
            if (!Guid.TryParse(g.PrincipalId, out var pid)) continue;
            var bucket = g.PrincipalKind switch
            {
                EntityKinds.User => grantsByUser,
                EntityKinds.Group => grantsByGroup,
                EntityKinds.Role => grantsByRole,
                _ => null
            };
            if (bucket is null) continue;
            if (!bucket.TryGetValue(pid, out var list))
            {
                list = new List<ParsedGrant>();
                bucket[pid] = list;
            }
            list.Add(parsed);
        }

        // Single project_members lookup for the role baseline.
        var memberRows = await db.ProjectMembers.AsNoTracking()
            .Where(m => m.ProjectId == project.Id && remaining.Contains(m.UserId))
            .Select(m => new { m.UserId, m.Role })
            .ToListAsync(ct);
        var rolesByUser = memberRows.ToDictionary(m => m.UserId, m => (string?)m.Role);

        foreach (var u in remaining)
        {
            var userGrants = new List<ParsedGrant>();
            if (grantsByUser.TryGetValue(u, out var ug)) userGrants.AddRange(ug);
            foreach (var gid in userGroups[u])
            {
                if (grantsByGroup.TryGetValue(gid, out var gg)) userGrants.AddRange(gg);
            }
            foreach (var rid in userRoles[u])
            {
                if (grantsByRole.TryGetValue(rid, out var rg)) userGrants.AddRange(rg);
            }

            var hasWildcardAllow = HasGlobalContentAllow(userGrants);
            var overrides = new List<OverrideRow>();
            foreach (var g in userGrants)
            {
                if (!TryGetPathTarget(g.Ast, out var pathKind, out var pathId)) continue;
                if (!ContentKinds.IsContentKind(pathKind)) continue;
                if (pathId is null) continue;
                if (chainSet.TryGetValue((pathKind, pathId.Value), out var depth))
                {
                    overrides.Add(new OverrideRow(resourceId, depth, g.Effect));
                }
            }

            AuthDecision decision;
            if (overrides.Count > 0)
            {
                var bestDepth = overrides.Min(g => g.Depth);
                var hasDeny = overrides.Any(g => g.Depth == bestDepth && g.Effect == AuthEffect.Deny);
                decision = hasDeny
                    ? AuthDecision.Deny($"override deny at depth {bestDepth}")
                    : AuthDecision.Allow($"override allow at depth {bestDepth}");
            }
            else if (hasWildcardAllow)
            {
                decision = AuthDecision.Allow("wildcard grant");
            }
            else
            {
                var role = rolesByUser.TryGetValue(u, out var r) ? r : null;
                var baselineAllowed = role is not null && RoleAllowsAction(role, action);
                decision = !baselineAllowed
                    ? AuthDecision.Deny(role is null
                        ? "no project membership"
                        : $"role '{role}' does not grant '{action}'")
                    : AuthDecision.Allow($"role '{role}' baseline");
            }

            result[u] = EnforceDeletionLock(action, kind, project.DeletionsLocked, decision);
        }

        return result;
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

        // Slow path: wildcard-with-denies or explicit override grants. The
        // entire policy collapses into one SQL query so we never materialise
        // the system-wide resource list in C# and the closest-depth-deny-
        // wins tiebreak runs inside the database where it can use the
        // existing content_ancestors indexes.
        var slowGrantingProjectIds = memberships
            .Where(m => RoleAllowsAction(m.Role, action))
            .Select(m => m.ProjectId)
            .ToList();
        var overrideTargets = ExtractOverrideTargets(grants);
        var allowedIds = await ComputeAllowedIdsViaSqlAsync(
            db, kind, action,
            hasWildcardAllow, slowGrantingProjectIds, overrideTargets, ct);
        return ContentAccessSet.From(allowedIds);
    }

    // Single CTE pipeline that produces the access set:
    //   1. Expand allow/deny override targets through content_ancestors to
    //      get per-resource (descendant_id, depth, effect) rows. Bounded by
    //      override-target subtree sizes, not total resources.
    //   2. Compute the per-resource "winning" effect at the closest depth.
    //      Deny beats allow at the same depth (current policy).
    //   3. Assemble reach = membership-reach ∪ (if wildcard) all-of-kind ∪
    //      override-allow winners.
    //   4. Subtract deny winners and (for Delete) resources whose project
    //      ancestor is in deletions_locked.
    //
    // The wildcard and is-delete branches stay as boolean parameters in the
    // CTEs rather than being composed at C# time, which keeps a single SQL
    // shape Postgres can cache the plan for. Empty override-target arrays
    // are handled cleanly — unnest('{}'::text[]) returns zero rows so the
    // override CTEs degenerate to empty sets.
    private static async Task<List<Guid>> ComputeAllowedIdsViaSqlAsync(
        AutoNateDbContext db,
        string kind,
        string action,
        bool hasWildcardAllow,
        IReadOnlyList<Guid> grantingProjectIds,
        IReadOnlyList<(string TargetKind, Guid TargetId, AuthEffect Effect)> overrideTargets,
        CancellationToken ct)
    {
        var allowKinds = overrideTargets.Where(t => t.Effect == AuthEffect.Allow)
            .Select(t => t.TargetKind).ToArray();
        var allowIds = overrideTargets.Where(t => t.Effect == AuthEffect.Allow)
            .Select(t => t.TargetId).ToArray();
        var denyKinds = overrideTargets.Where(t => t.Effect == AuthEffect.Deny)
            .Select(t => t.TargetKind).ToArray();
        var denyIds = overrideTargets.Where(t => t.Effect == AuthEffect.Deny)
            .Select(t => t.TargetId).ToArray();
        var grantingProjects = grantingProjectIds.ToArray();
        var isDelete = action == Actions.Delete;

        const string sql = """
            WITH
            allow_targets AS (
                SELECT unnest({0}::text[]) AS kind, unnest({1}::uuid[]) AS id
            ),
            deny_targets AS (
                SELECT unnest({2}::text[]) AS kind, unnest({3}::uuid[]) AS id
            ),
            override_expansions AS (
                SELECT ca.descendant_id, ca.depth, 'allow'::text AS effect
                FROM content_ancestors ca
                JOIN allow_targets t
                    ON t.kind = ca.ancestor_kind AND t.id = ca.ancestor_id
                WHERE ca.descendant_kind = {4}
                UNION ALL
                SELECT ca.descendant_id, ca.depth, 'deny'::text AS effect
                FROM content_ancestors ca
                JOIN deny_targets t
                    ON t.kind = ca.ancestor_kind AND t.id = ca.ancestor_id
                WHERE ca.descendant_kind = {4}
            ),
            min_depth_per_resource AS (
                SELECT descendant_id, MIN(depth) AS min_depth
                FROM override_expansions
                GROUP BY descendant_id
            ),
            deny_winners AS (
                SELECT DISTINCT e.descendant_id
                FROM override_expansions e
                JOIN min_depth_per_resource m ON m.descendant_id = e.descendant_id
                WHERE e.depth = m.min_depth AND e.effect = 'deny'
            ),
            allow_winners AS (
                SELECT DISTINCT e.descendant_id
                FROM override_expansions e
                JOIN min_depth_per_resource m ON m.descendant_id = e.descendant_id
                WHERE e.depth = m.min_depth
                  AND e.effect = 'allow'
                  AND NOT EXISTS (
                      SELECT 1 FROM deny_winners d
                      WHERE d.descendant_id = e.descendant_id)
            ),
            membership_reach AS (
                SELECT ca.descendant_id
                FROM content_ancestors ca
                WHERE ca.descendant_kind = {4}
                  AND ca.ancestor_kind = 'project'
                  AND ca.ancestor_id = ANY({5}::uuid[])
            ),
            wildcard_reach AS (
                SELECT ca.descendant_id
                FROM content_ancestors ca
                WHERE {6}::boolean
                  AND ca.descendant_kind = {4}
                  AND ca.ancestor_kind = 'project'
            ),
            reach AS (
                SELECT descendant_id FROM membership_reach
                UNION
                SELECT descendant_id FROM wildcard_reach
                UNION
                SELECT descendant_id FROM allow_winners
            ),
            locked_for_delete AS (
                SELECT ca.descendant_id
                FROM content_ancestors ca
                JOIN projects p
                    ON p.id = ca.ancestor_id AND p.deletions_locked
                WHERE {7}::boolean
                  AND ca.descendant_kind = {4}
                  AND ca.ancestor_kind = 'project'
            )
            SELECT r.descendant_id AS "Id"
            FROM reach r
            WHERE NOT EXISTS (
                    SELECT 1 FROM deny_winners d WHERE d.descendant_id = r.descendant_id)
              AND NOT EXISTS (
                    SELECT 1 FROM locked_for_delete l WHERE l.descendant_id = r.descendant_id)
            """;

        var rows = await db.Database
            .SqlQueryRaw<ResourceIdRow>(
                sql,
                allowKinds, allowIds, denyKinds, denyIds,
                kind, grantingProjects, hasWildcardAllow, isDelete)
            .ToListAsync(ct);
        return rows.Select(r => r.Id).ToList();
    }

    // Flat list of (kind, id, effect) targets pulled from the actor's grants
    // for the SQL slow path. Mirrors the entry condition of HasOverrideTargets
    // but yields the data the SQL needs rather than just a boolean. Multiple
    // grants on the same target — e.g. a user-allow plus a group-deny on the
    // same page — produce multiple tuples, which the SQL collapses via the
    // closest-depth-deny-wins CTE.
    private static List<(string TargetKind, Guid TargetId, AuthEffect Effect)>
        ExtractOverrideTargets(IReadOnlyList<ParsedGrant> grants)
    {
        var result = new List<(string, Guid, AuthEffect)>();
        foreach (var g in grants)
        {
            if (!TryGetPathTarget(g.Ast, out var pathKind, out var pathId)) continue;
            if (pathId is null) continue;
            if (!ContentKinds.IsContentKind(pathKind)) continue;
            result.Add((pathKind, pathId.Value, g.Effect));
        }
        return result;
    }

    // Row shape for the slow-path SqlQueryRaw. Public so EF Core's reflection
    // can bind the "Id" column alias. Plain record — no entity mapping.
    public sealed record class ResourceIdRow(Guid Id);

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

    // Does any grant target a specific content resource? Used to decide
    // whether GetAllowedIdsAsync can take the SQL fast path (no overrides
    // → membership-reach only) or has to fall through to the SQL slow path
    // (closest-depth-deny-wins CTE).
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
}
