using System.Linq.Expressions;
using System.Security.Claims;
using AutoNate.Plugins.Abstractions;
using AutoNate.Web.Authorization.Selectors;
using AutoNate.Web.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AutoNate.Web.Authorization.Evaluator;

// Phase 4 evaluator: real grant materialization and read-path enforcement.
//
// AuthorizeAsync remains permissive while Enforcement is "read-only" — it only
// hardens to deny in Phase 5 ("full"). FilterQueryAsync compiles each grant
// selector into a LINQ predicate and combines them as `OR(allows) AND NOT
// OR(denies)` before applying to the source queryable.
//
// SuperAdmin short-circuits in every flow. When Enabled is false, all reads
// pass through unchanged so production behavior is preserved until the flag
// is flipped.
public sealed class Authorizer : IAuthorizer
{
    private readonly IDbContextFactory<AutoNateDbContext> _dbFactory;
    private readonly IOptions<AuthorizationOptions> _options;
    private readonly IEntityRegistry _registry;
    private readonly ISelectorCompilerRegistry _compilers;
    private readonly IReadOnlyDictionary<string, IInstanceAuthorizer> _instanceAuthorizers;
    private readonly IFilterHub _filterHub;
    private readonly ILogger<Authorizer> _log;

    private ActorContext? _actorContext;

    // Per-request grant cache. Authorizer is registered scoped, so this lives
    // exactly as long as _actorContext above. Per-row loops (e.g.
    // ExecutionEndpoints.FilterVisibleExecutionsAsync, FlowableInstance
    // authorizers) call IsAuthorizedAsync with the same (kind, action) for
    // every row — without this cache each iteration would re-query
    // permission_grants and re-parse every selector returned. Cache key
    // includes the actor's UserId so a stray "different principal" call
    // doesn't return a stale set.
    private readonly Dictionary<(Guid UserId, string Kind, string Action),
        IReadOnlyList<EffectiveGrant>> _grantsCache = new();

    public Authorizer(
        IDbContextFactory<AutoNateDbContext> dbFactory,
        IOptions<AuthorizationOptions> options,
        IEntityRegistry registry,
        ISelectorCompilerRegistry compilers,
        IEnumerable<IInstanceAuthorizer> instanceAuthorizers,
        IFilterHub filterHub,
        ILogger<Authorizer> log)
    {
        _dbFactory = dbFactory;
        _options = options;
        _registry = registry;
        _compilers = compilers;
        _instanceAuthorizers = instanceAuthorizers.ToDictionary(h => h.Kind, StringComparer.Ordinal);
        _filterHub = filterHub;
        _log = log;
    }

    public async Task<AuthDecision> AuthorizeAsync(
        ClaimsPrincipal actor,
        string action,
        EntityRef target,
        CancellationToken cancellationToken = default)
    {
        var raw = await ComputeDecisionAsync(actor, action, target, cancellationToken);
        return await ApplyAuthorizeFilterAsync(actor, action, target, raw, cancellationToken);
    }

    private async Task<AuthDecision> ComputeDecisionAsync(
        ClaimsPrincipal actor,
        string action,
        EntityRef target,
        CancellationToken cancellationToken)
    {
        if (!_options.Value.Enabled)
        {
            return AuthDecision.Allow("authorization disabled");
        }

        var userId = GetUserId(actor);
        if (userId is null)
        {
            return MaybeDryRun(AuthDecision.Deny("no user identity"), action, target);
        }

        var ctx = await GetActorContextAsync(userId.Value, cancellationToken);
        if (ctx.IsSuperAdmin)
        {
            return AuthDecision.Allow("super admin");
        }

        // In read-only enforcement, list filtering still happens via FilterQueryAsync
        // but instance writes pass — that's the deliberate "filter reads first,
        // then enforce writes" rollout.
        if (_options.Value.Enforcement != AuthorizationEnforcement.Full)
        {
            return AuthDecision.Allow("write enforcement disabled");
        }

        // Kind-level check (e.g. "create"): no specific entity exists yet, so
        // approve if any allow grant for the kind+action exists without a
        // blanket deny.
        if (string.IsNullOrEmpty(target.Id) || target.Id == Actions.Wildcard)
        {
            return await AuthorizeKindLevelAsync(ctx, action, target.Kind, cancellationToken);
        }

        if (!_instanceAuthorizers.TryGetValue(target.Kind, out var handler))
        {
            return MaybeDryRun(
                AuthDecision.Deny($"no instance handler for kind '{target.Kind}'"),
                action, target);
        }

        var allowed = await handler.ExistsAndAuthorizedAsync(
            this, actor, action, target.Id, cancellationToken);

        return allowed
            ? AuthDecision.Allow("matched grant")
            : MaybeDryRun(AuthDecision.Deny("no matching grant"), action, target);
    }

    private async Task<AuthDecision> ApplyAuthorizeFilterAsync(
        ClaimsPrincipal actor,
        string action,
        EntityRef target,
        AuthDecision raw,
        CancellationToken cancellationToken)
    {
        if (!_filterHub.HasFilter(HookPoints.AuthorizeAuthorize))
        {
            return raw;
        }

        var ctx = new AuthorizeFilterContext
        {
            Actor = actor,
            Action = action,
            Target = new EntityRefDto(target.Kind, target.Id),
            CurrentDecision = ToDto(raw),
        };

        AuthorizeFilterContext filtered;
        try
        {
            filtered = await _filterHub.ApplyAsync(HookPoints.AuthorizeAuthorize, ctx, cancellationToken);
        }
        catch (Exception ex)
        {
            _log.LogError(ex,
                "auth filter threw; fail-secure deny for action={Action} target={Kind}:{Id}",
                action, target.Kind, target.Id);
            return AuthDecision.Deny("filter threw");
        }

        return FromDto(filtered.CurrentDecision);
    }

    private static AuthDecisionDto ToDto(AuthDecision d) => new()
    {
        Effect = d.Effect == AuthEffect.Allow ? AuthEffectDto.Allow : AuthEffectDto.Deny,
        Reason = d.Reason,
    };

    private static AuthDecision FromDto(AuthDecisionDto d) =>
        d.Effect == AuthEffectDto.Allow ? AuthDecision.Allow(d.Reason) : AuthDecision.Deny(d.Reason);

    private async Task<AuthDecision> AuthorizeKindLevelAsync(
        ActorContext actor,
        string action,
        string kind,
        CancellationToken cancellationToken)
    {
        var grants = await LoadGrantsAsync(actor, kind, action, cancellationToken);
        var hasAllow = grants.Any(g => g.Effect == AuthEffect.Allow);
        if (!hasAllow)
        {
            return MaybeDryRun(
                AuthDecision.Deny("no allow grant for kind"),
                action, new EntityRef(kind, Actions.Wildcard));
        }

        // A blanket deny — one whose path ids are absent or wildcard — blocks
        // even kind-level operations. Targeted denies only matter at the
        // instance level.
        var blanketDeny = grants.Any(g =>
            g.Effect == AuthEffect.Deny
            && (g.Ast.Path.Ids is null || g.Ast.Path.IdsAreWildcard));

        if (blanketDeny)
        {
            return MaybeDryRun(
                AuthDecision.Deny("blanket deny for kind"),
                action, new EntityRef(kind, Actions.Wildcard));
        }

        return AuthDecision.Allow("kind-level allow");
    }

    private AuthDecision MaybeDryRun(AuthDecision decision, string action, EntityRef target)
    {
        if (decision.IsAllowed || !_options.Value.DryRun)
        {
            return decision;
        }

        _log.LogWarning(
            "auth.deny.dryrun=true action={Action} target={Target} reason={Reason}",
            action, target, decision.Reason);
        return AuthDecision.Allow($"dry-run override of: {decision.Reason}");
    }

    public async Task<IQueryable<T>> FilterQueryAsync<T>(
        AutoNateDbContext db,
        ClaimsPrincipal actor,
        string kind,
        string action,
        IQueryable<T> source,
        CancellationToken cancellationToken = default) where T : class
    {
        ArgumentNullException.ThrowIfNull(source);

        if (!_options.Value.Enabled
            || _options.Value.Enforcement == AuthorizationEnforcement.Off)
        {
            return source;
        }

        ArgumentNullException.ThrowIfNull(db);

        var userId = GetUserId(actor);
        if (userId is null)
        {
            return source.Where(_ => false);
        }

        var ctx = await GetActorContextAsync(userId.Value, cancellationToken);
        if (ctx.IsSuperAdmin)
        {
            return source;
        }

        var grants = await LoadGrantsAsync(ctx, kind, action, cancellationToken);
        if (grants.Count == 0)
        {
            return source.Where(_ => false);
        }

        var compiler = _compilers.TryGetFor<T>(kind);
        if (compiler is null)
        {
            // Nothing in the registry can speak this kind+CLR pair. Be safe.
            _log.LogWarning(
                "No selector compiler registered for kind '{Kind}' targeting {Type}; denying by default.",
                kind, typeof(T).FullName);
            return source.Where(_ => false);
        }

        var compilationCtx = new CompilationContext(db, userId.Value);

        var allows = new List<Expression<Func<T, bool>>>();
        var denies = new List<Expression<Func<T, bool>>>();

        foreach (var grant in grants)
        {
            try
            {
                var predicate = compiler.Compile(grant.Ast, compilationCtx);
                if (grant.Effect == AuthEffect.Allow)
                {
                    allows.Add(predicate);
                }
                else
                {
                    denies.Add(predicate);
                }
            }
            catch (SelectorCompilationException ex)
            {
                _log.LogWarning(ex,
                    "Skipping grant '{Selector}' for kind '{Kind}': compilation failed.",
                    grant.SelectorString, kind);
            }
        }

        if (allows.Count == 0)
        {
            return source.Where(_ => false);
        }

        var allowExpr = allows.Aggregate(ExpressionUtilities.OrElse);
        if (denies.Count == 0)
        {
            return source.Where(allowExpr);
        }

        var denyExpr = denies.Aggregate(ExpressionUtilities.OrElse);
        var combined = ExpressionUtilities.AndAlso(allowExpr, ExpressionUtilities.Not(denyExpr));
        return source.Where(combined);
    }

    public async Task<bool> IsAuthorizedAsync(
        ClaimsPrincipal actor,
        string kind,
        string action,
        Func<SelectorAst, bool> selectorMatcher,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(selectorMatcher);

        if (!_options.Value.Enabled
            || _options.Value.Enforcement == AuthorizationEnforcement.Off)
        {
            return true;
        }

        var userId = GetUserId(actor);
        if (userId is null)
        {
            return false;
        }

        var ctx = await GetActorContextAsync(userId.Value, cancellationToken);
        if (ctx.IsSuperAdmin)
        {
            return true;
        }

        var grants = await LoadGrantsAsync(ctx, kind, action, cancellationToken);
        if (grants.Count == 0)
        {
            return false;
        }

        var matchedAllow = false;
        foreach (var grant in grants)
        {
            if (!selectorMatcher(grant.Ast))
            {
                continue;
            }

            if (grant.Effect == AuthEffect.Deny)
            {
                return false;
            }

            matchedAllow = true;
        }

        return matchedAllow;
    }

    public async Task<RecordSqlFilter> BuildRecordSqlFilterAsync(
        ClaimsPrincipal actor,
        string action,
        int parameterOffset,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Value.Enabled
            || _options.Value.Enforcement == AuthorizationEnforcement.Off)
        {
            return RecordSqlFilter.Open;
        }

        var userId = GetUserId(actor);
        if (userId is null)
        {
            return RecordSqlFilter.Closed;
        }

        var ctx = await GetActorContextAsync(userId.Value, cancellationToken);
        if (ctx.IsSuperAdmin)
        {
            return RecordSqlFilter.Open;
        }

        var grants = await LoadGrantsAsync(ctx, EntityKinds.Record, action, cancellationToken);
        if (grants.Count == 0)
        {
            return RecordSqlFilter.Closed;
        }

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var shortCodes = await db.RecordTypes.AsNoTracking()
            .Select(t => new { t.Id, t.ShortCode })
            .ToDictionaryAsync(t => t.ShortCode, t => t.Id, StringComparer.Ordinal, cancellationToken);

        var compiler = new RecordSelectorSqlCompiler();
        var build = new RecordSqlBuildContext(userId.Value, parameterOffset, shortCodes);
        var allows = new List<string>();
        var denies = new List<string>();

        foreach (var grant in grants)
        {
            try
            {
                var sql = compiler.Compile(grant.Ast, build);
                if (grant.Effect == AuthEffect.Allow) allows.Add(sql);
                else denies.Add(sql);
            }
            catch (SelectorCompilationException ex)
            {
                _log.LogWarning(ex,
                    "Skipping grant '{Selector}' for kind 'record' (SQL): compilation failed.",
                    grant.SelectorString);
            }
        }

        if (allows.Count == 0)
        {
            return RecordSqlFilter.Closed;
        }

        var allowSql = "(" + string.Join(" OR ", allows) + ")";
        var combinedSql = denies.Count == 0
            ? allowSql
            : $"({allowSql} AND NOT (" + string.Join(" OR ", denies) + "))";

        return new RecordSqlFilter
        {
            Sql = combinedSql,
            Parameters = build.Parameters
        };
    }

    public async Task<AuthExplanation> ExplainAsync(
        Guid asUserId,
        string action,
        EntityRef target,
        CancellationToken cancellationToken = default)
    {
        var ctx = await GetActorContextAsync(asUserId, cancellationToken);
        if (ctx.IsSuperAdmin)
        {
            return new AuthExplanation
            {
                Effect = AuthEffect.Allow,
                Reason = "super admin bypass — all actions allowed",
                AsUserId = asUserId,
                IsSuperAdmin = true,
                GroupIds = ctx.GroupIds,
                RoleIds = ctx.RoleIds,
                Grants = Array.Empty<GrantConsideration>()
            };
        }

        var grants = await LoadGrantsWithSourceAsync(ctx, action, cancellationToken);

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var considered = new List<GrantConsideration>();
        var matchedAllow = false;
        var matchedDeny = false;

        foreach (var grant in grants)
        {
            // Kind mismatch — record but skip evaluation.
            if (!MatchesKind(grant.Ast.Path, target.Kind))
            {
                considered.Add(new GrantConsideration
                {
                    PrincipalKind = grant.PrincipalKind,
                    PrincipalId = grant.PrincipalId,
                    PrincipalName = grant.PrincipalName,
                    Action = grant.Action,
                    SelectorString = grant.SelectorString,
                    Effect = grant.Effect,
                    Matched = false,
                    Error = null
                });
                continue;
            }

            bool? matched;
            string? error = null;
            try
            {
                matched = await EvaluateGrantAgainstTargetAsync(
                    db, ctx, grant.Ast, target, cancellationToken);
            }
            catch (SelectorCompilationException ex)
            {
                matched = null;
                error = ex.Message;
            }

            if (matched == true)
            {
                if (grant.Effect == AuthEffect.Deny) matchedDeny = true;
                else matchedAllow = true;
            }

            considered.Add(new GrantConsideration
            {
                PrincipalKind = grant.PrincipalKind,
                PrincipalId = grant.PrincipalId,
                PrincipalName = grant.PrincipalName,
                Action = grant.Action,
                SelectorString = grant.SelectorString,
                Effect = grant.Effect,
                Matched = matched,
                Error = error
            });
        }

        AuthEffect finalEffect;
        string reason;
        if (matchedDeny)
        {
            finalEffect = AuthEffect.Deny;
            reason = "matched deny grant";
        }
        else if (matchedAllow)
        {
            finalEffect = AuthEffect.Allow;
            reason = "matched allow grant";
        }
        else
        {
            finalEffect = AuthEffect.Deny;
            reason = grants.Count == 0
                ? "no grants for this action on the user / its groups / its roles"
                : "no grant matched the target";
        }

        return new AuthExplanation
        {
            Effect = finalEffect,
            Reason = reason,
            AsUserId = asUserId,
            IsSuperAdmin = false,
            GroupIds = ctx.GroupIds,
            RoleIds = ctx.RoleIds,
            Grants = considered
        };
    }

    // Per-grant evaluation against a single target. Returns:
    //   true  — selector matches the target.
    //   false — selector doesn't match.
    //   null  — kind isn't supported by the debugger (Flowable kinds, etc.).
    // Throws SelectorCompilationException for grants that look valid but blow
    // up at compile time — the caller surfaces those as per-grant `Error`.
    private async Task<bool?> EvaluateGrantAgainstTargetAsync(
        AutoNateDbContext db,
        ActorContext ctx,
        SelectorAst ast,
        EntityRef target,
        CancellationToken ct)
    {
        // Path id filter is a quick reject regardless of kind.
        if (ast.Path.Ids is { } ids && !ast.Path.IdsAreWildcard)
        {
            if (!ids.Contains(target.Id, StringComparer.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        // No predicates → path-level match alone is enough.
        if (ast.Predicate is null || ast.Predicate.Expressions.Count == 0)
        {
            return true;
        }

        switch (target.Kind)
        {
            case EntityKinds.Record:
                return await EvaluateRecordGrantAsync(db, ctx, ast, target.Id, ct);
            case EntityKinds.RecordType:
            case EntityKinds.WorkflowModel:
            case EntityKinds.Role:
            case EntityKinds.Group:
            case EntityKinds.User:
                // Path-only kinds: predicates aren't modeled, so a predicate
                // here can't match. Surface the situation as a compilation
                // failure so the user sees a clear error rather than a silent
                // false.
                throw new SelectorCompilationException(
                    $"Predicates aren't supported on '{target.Kind}' selectors yet.");
            case EntityKinds.WorkflowExecution:
            case EntityKinds.WorkflowTask:
                // The debugger doesn't reach into Flowable to load facts.
                return null;
            default:
                return null;
        }
    }

    private async Task<bool> EvaluateRecordGrantAsync(
        AutoNateDbContext db,
        ActorContext ctx,
        SelectorAst ast,
        string recordId,
        CancellationToken ct)
    {
        if (!Guid.TryParse(recordId, out var rid)) return false;

        var compiler = _compilers.TryGetFor<Persistence.Scaffolded.Record>(EntityKinds.Record);
        if (compiler is null) return false;

        var compilationCtx = new CompilationContext(db, ctx.UserId);
        var predicate = compiler.Compile(ast, compilationCtx);

        return await db.Records.AsNoTracking()
            .Where(r => r.Id == rid)
            .AnyAsync(predicate, ct);
    }

    public Task<CapabilitySummary> GetCapabilitiesAsync(
        ClaimsPrincipal actor,
        CancellationToken cancellationToken = default)
    {
        var userId = GetUserId(actor) ?? Guid.Empty;
        if (!_options.Value.Enabled)
        {
            return Task.FromResult(new CapabilitySummary
            {
                UserId = userId,
                IsSuperAdmin = false,
                Capabilities = BuildCapabilityMap(_registry, allowed: true)
            });
        }

        return GetCapabilitiesAsyncCore(userId, cancellationToken);
    }

    private async Task<CapabilitySummary> GetCapabilitiesAsyncCore(Guid userId, CancellationToken ct)
    {
        var ctx = await GetActorContextAsync(userId, ct);
        var capabilities = ctx.IsSuperAdmin
            ? BuildCapabilityMap(_registry, allowed: true)
            : BuildCapabilityMap(_registry, allowed: false);

        return new CapabilitySummary
        {
            UserId = userId,
            IsSuperAdmin = ctx.IsSuperAdmin,
            Capabilities = capabilities
        };
    }

    private async Task<ActorContext> GetActorContextAsync(Guid userId, CancellationToken ct)
    {
        if (_actorContext is not null && _actorContext.UserId == userId)
        {
            return _actorContext;
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

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

        _actorContext = new ActorContext(userId, groupIds, roleIds, isSuperAdmin);
        return _actorContext;
    }

    private async Task<IReadOnlyList<EffectiveGrant>> LoadGrantsAsync(
        ActorContext actor,
        string kind,
        string action,
        CancellationToken ct)
    {
        var cacheKey = (actor.UserId, kind, action);
        if (_grantsCache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var userIdString = actor.UserId.ToString();
        var groupIdStrings = actor.GroupIds.Select(g => g.ToString()).ToList();
        var roleIdStrings = actor.RoleIds.Select(r => r.ToString()).ToList();

        // Single source of truth: permission_grants holds every grant, keyed
        // by polymorphic principal (user|group|role). Role permissions used to
        // live in their own table — they're now grants where principal_kind='role'.
        var grants = await db.PermissionGrants.AsNoTracking()
            .Where(pg =>
                (pg.Action == action || pg.Action == Actions.Wildcard)
                && (
                    (pg.PrincipalKind == EntityKinds.User && pg.PrincipalId == userIdString) ||
                    (pg.PrincipalKind == EntityKinds.Group && groupIdStrings.Contains(pg.PrincipalId)) ||
                    (pg.PrincipalKind == EntityKinds.Role && roleIdStrings.Contains(pg.PrincipalId))
                ))
            .Select(pg => new RawGrant(pg.SelectorString, pg.Effect))
            .ToListAsync(ct);

        var result = new List<EffectiveGrant>();
        foreach (var raw in grants)
        {
            SelectorAst ast;
            try
            {
                ast = SelectorParser.Parse(raw.Selector);
            }
            catch (SelectorParseException ex)
            {
                _log.LogWarning(ex,
                    "Stored grant has unparseable selector '{Selector}'; skipping.",
                    raw.Selector);
                continue;
            }

            if (!MatchesKind(ast.Path, kind))
            {
                continue;
            }

            var effect = string.Equals(raw.Effect, "deny", StringComparison.OrdinalIgnoreCase)
                ? AuthEffect.Deny
                : AuthEffect.Allow;
            result.Add(new EffectiveGrant(ast, effect, raw.Selector));
        }

        _grantsCache[cacheKey] = result;
        return result;
    }

    // Same shape as LoadGrantsAsync but preserves principal information so
    // the explain endpoint can show which role/group/user contributed each
    // grant. Skips the kind filter — the explain layer wants every grant the
    // user has for the action so kind-mismatches show up with Matched=false
    // rather than silently disappear.
    private async Task<IReadOnlyList<SourcedGrant>> LoadGrantsWithSourceAsync(
        ActorContext actor,
        string action,
        CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var userIdString = actor.UserId.ToString();
        var groupIdStrings = actor.GroupIds.Select(g => g.ToString()).ToList();
        var roleIdStrings = actor.RoleIds.Select(r => r.ToString()).ToList();

        var raw = await db.PermissionGrants.AsNoTracking()
            .Where(pg =>
                (pg.Action == action || pg.Action == Actions.Wildcard)
                && (
                    (pg.PrincipalKind == EntityKinds.User && pg.PrincipalId == userIdString) ||
                    (pg.PrincipalKind == EntityKinds.Group && groupIdStrings.Contains(pg.PrincipalId)) ||
                    (pg.PrincipalKind == EntityKinds.Role && roleIdStrings.Contains(pg.PrincipalId))
                ))
            .Select(pg => new
            {
                pg.PrincipalKind,
                pg.PrincipalId,
                pg.Action,
                pg.SelectorString,
                pg.Effect
            })
            .ToListAsync(ct);

        // Resolve principal display names in one pass per kind.
        var roleGuids = raw.Where(g => g.PrincipalKind == EntityKinds.Role)
            .Select(g => Guid.TryParse(g.PrincipalId, out var x) ? x : (Guid?)null)
            .Where(x => x.HasValue).Select(x => x!.Value).Distinct().ToList();
        var groupGuids = raw.Where(g => g.PrincipalKind == EntityKinds.Group)
            .Select(g => Guid.TryParse(g.PrincipalId, out var x) ? x : (Guid?)null)
            .Where(x => x.HasValue).Select(x => x!.Value).Distinct().ToList();
        var userGuids = raw.Where(g => g.PrincipalKind == EntityKinds.User)
            .Select(g => Guid.TryParse(g.PrincipalId, out var x) ? x : (Guid?)null)
            .Where(x => x.HasValue).Select(x => x!.Value).Distinct().ToList();

        var roleNames = await db.Roles.AsNoTracking()
            .Where(r => roleGuids.Contains(r.Id))
            .ToDictionaryAsync(r => r.Id, r => r.Name, ct);
        var groupNames = await db.Groups.AsNoTracking()
            .Where(g => groupGuids.Contains(g.Id))
            .ToDictionaryAsync(g => g.Id, g => g.Name, ct);
        var userNames = await db.LocalUsers.AsNoTracking()
            .Where(u => userGuids.Contains(u.UserId))
            .ToDictionaryAsync(u => u.UserId, u => u.Username, ct);

        var result = new List<SourcedGrant>(raw.Count);
        foreach (var g in raw)
        {
            SelectorAst ast;
            try
            {
                ast = SelectorParser.Parse(g.SelectorString);
            }
            catch (SelectorParseException ex)
            {
                _log.LogWarning(ex,
                    "Stored grant has unparseable selector '{Selector}'; skipping.",
                    g.SelectorString);
                continue;
            }

            string? name = null;
            if (Guid.TryParse(g.PrincipalId, out var pid))
            {
                name = g.PrincipalKind switch
                {
                    EntityKinds.Role  => roleNames.TryGetValue(pid, out var n) ? n : null,
                    EntityKinds.Group => groupNames.TryGetValue(pid, out var n) ? n : null,
                    EntityKinds.User  => userNames.TryGetValue(pid, out var n) ? n : null,
                    _ => null
                };
            }

            var effect = string.Equals(g.Effect, "deny", StringComparison.OrdinalIgnoreCase)
                ? AuthEffect.Deny
                : AuthEffect.Allow;

            result.Add(new SourcedGrant(
                g.PrincipalKind, g.PrincipalId, name, g.Action,
                g.SelectorString, ast, effect));
        }

        return result;
    }

    private static bool MatchesKind(PathNode path, string kind) =>
        path.KindsAreWildcard || path.Kinds.Contains(kind);

    private static Guid? GetUserId(ClaimsPrincipal actor)
    {
        var raw = actor.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(raw, out var id) ? id : null;
    }

    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, bool>> BuildCapabilityMap(
        IEntityRegistry registry, bool allowed)
    {
        var map = new Dictionary<string, IReadOnlyDictionary<string, bool>>(StringComparer.Ordinal);
        foreach (var entityType in registry.All)
        {
            var actions = new Dictionary<string, bool>(StringComparer.Ordinal);
            foreach (var action in entityType.Actions)
            {
                actions[action] = allowed;
            }

            map[entityType.Kind] = actions;
        }

        return map;
    }

    private sealed record class ActorContext(
        Guid UserId,
        IReadOnlyList<Guid> GroupIds,
        IReadOnlyList<Guid> RoleIds,
        bool IsSuperAdmin);

    private readonly record struct RawGrant(string Selector, string Effect);

    private readonly record struct EffectiveGrant(SelectorAst Ast, AuthEffect Effect, string SelectorString);

    private sealed record class SourcedGrant(
        string PrincipalKind,
        string PrincipalId,
        string? PrincipalName,
        string Action,
        string SelectorString,
        SelectorAst Ast,
        AuthEffect Effect);
}
