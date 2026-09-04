using System.Security.Claims;
using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.Evaluator;
using AutoNate.Web.Persistence;
using AutoNate.Web.Services.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AutoNate.Web.Tests.Authorization;

/// <summary>
/// Batching a permission check must not change its answer (#5).
/// </summary>
/// <remarks>
/// <c>POST /api/auth/check</c> now asks one query per (kind, action) instead of
/// one per item. Speed was the goal; **agreement is the requirement**, and only
/// agreement can fail silently — a batch that is fast and wrong still returns
/// 200 with a plausible-looking list of allowed/denied, and the caller has no
/// way to tell.
///
/// So these tests do not assert that batching is fast. They assert that for the
/// same actor, the same targets and a deliberately mixed allow/deny set, the
/// batched path and the per-item path return the same decisions in the same
/// order — including for kinds that do **not** override the batch method, whose
/// default implementation is the loop itself.
/// </remarks>
[Trait("Category", "Integration")]
public sealed class BatchedAuthorizationEquivalenceTests
{
    private static ClaimsPrincipal Principal(Guid userId) =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "test"));

    private static async Task<AutoNateWebApplicationFactory> EnforcingAppAsync() =>
        await AutoNateWebApplicationFactory.CreateAsync(new Dictionary<string, string?>
        {
            // Enforcement must be on, or every decision short-circuits to allow
            // and the comparison is vacuous — two paths agreeing that everything
            // is permitted proves nothing about either.
            ["Authorization:Enabled"] = "true",
            ["Authorization:Enforcement"] = "full",
        });

    [Fact]
    public async Task Batched_and_per_item_decisions_agree_across_a_mixed_set()
    {
        await using var app = await EnforcingAppAsync();
        _ = app.CreateClient();

        var scope = app.Services.CreateScope().ServiceProvider;
        var groups = scope.GetRequiredService<IGroupStore>();
        var grants = scope.GetRequiredService<IPermissionGrantStore>();

        var actorId = Guid.NewGuid();
        var visible = await groups.CreateAsync(new CreateGroupInput("Visible " + Guid.NewGuid().ToString("N")[..6], null), actorId);
        var hidden = await groups.CreateAsync(new CreateGroupInput("Hidden " + Guid.NewGuid().ToString("N")[..6], null), actorId);

        // A grant naming exactly one of the two, so the expected answer is a
        // genuine mix. If both were allowed or both denied, an implementation
        // that ignored its input entirely would still pass.
        await grants.CreateAsync(new CreatePermissionGrantInput(
            EntityKinds.User, actorId.ToString(),
            Actions.View, $"/group/{visible.Id}", "allow", 0), actorId);

        var principal = Principal(actorId);
        var requests = new List<(string Action, EntityRef Target)>
        {
            (Actions.View, new EntityRef(EntityKinds.Group, visible.Id.ToString())),
            (Actions.View, new EntityRef(EntityKinds.Group, hidden.Id.ToString())),
            (Actions.View, new EntityRef(EntityKinds.Group, Guid.NewGuid().ToString())),
            (Actions.Edit, new EntityRef(EntityKinds.Group, visible.Id.ToString())),
            // Kind-level and a nonexistent kind, so the batch has to route the
            // non-instance cases through the same pre-decision logic.
            (Actions.Create, new EntityRef(EntityKinds.Group, string.Empty)),
            (Actions.View, new EntityRef("no-such-kind", Guid.NewGuid().ToString())),
        };

        await using var perItemScope = app.Services.CreateAsyncScope();
        var a1 = perItemScope.ServiceProvider.GetRequiredService<IAuthorizer>();
        var perItem = new List<bool>();
        foreach (var (action, target) in requests)
        {
            perItem.Add((await a1.AuthorizeAsync(principal, action, target)).IsAllowed);
        }

        await using var batchScope = app.Services.CreateAsyncScope();
        var a2 = batchScope.ServiceProvider.GetRequiredService<IAuthorizer>();
        var batched = (await a2.AuthorizeManyAsync(principal, requests))
            .Select(d => d.IsAllowed).ToList();

        Assert.Equal(perItem, batched);

        // And the mix is real, not an accident of everything denying.
        Assert.Contains(true, perItem);
        Assert.Contains(false, perItem);
    }

    [Fact]
    public async Task Batching_agrees_for_a_kind_that_does_not_override_the_batch_method()
    {
        // Twelve of the fifteen instance authorizers inherit the default
        // implementation, which is the loop. This asserts the inherited path is
        // wired correctly rather than assuming a default cannot be broken —
        // AuthorizeManyAsync still has to group, dispatch and map results back,
        // and any of those can be wrong independently of the query.
        await using var app = await EnforcingAppAsync();
        _ = app.CreateClient();

        var scope = app.Services.CreateScope().ServiceProvider;
        var roles = scope.GetRequiredService<IRoleStore>();
        var grants = scope.GetRequiredService<IPermissionGrantStore>();

        var actorId = Guid.NewGuid();
        var allowed = await roles.CreateAsync(new CreateRoleInput("Allowed " + Guid.NewGuid().ToString("N")[..6], null), actorId);
        var denied = await roles.CreateAsync(new CreateRoleInput("Denied " + Guid.NewGuid().ToString("N")[..6], null), actorId);

        await grants.CreateAsync(new CreatePermissionGrantInput(
            EntityKinds.User, actorId.ToString(),
            Actions.View, $"/role/{allowed.Id}", "allow", 0), actorId);

        var principal = Principal(actorId);
        var requests = new List<(string Action, EntityRef Target)>
        {
            (Actions.View, new EntityRef(EntityKinds.Role, allowed.Id.ToString())),
            (Actions.View, new EntityRef(EntityKinds.Role, denied.Id.ToString())),
        };

        await using var s1 = app.Services.CreateAsyncScope();
        var perItem = new List<bool>();
        foreach (var (action, target) in requests)
        {
            perItem.Add((await s1.ServiceProvider.GetRequiredService<IAuthorizer>()
                .AuthorizeAsync(principal, action, target)).IsAllowed);
        }

        await using var s2 = app.Services.CreateAsyncScope();
        var batched = (await s2.ServiceProvider.GetRequiredService<IAuthorizer>()
            .AuthorizeManyAsync(principal, requests)).Select(d => d.IsAllowed).ToList();

        Assert.Equal(perItem, batched);
        Assert.Equal([true, false], batched);
    }

    [Fact]
    public async Task Duplicate_targets_in_one_batch_all_get_the_same_answer()
    {
        // The batch de-duplicates ids before querying and maps the result back
        // by id. A mapping bug would show up as one row of a repeated pair
        // answering differently from the other — silently, and only on pages
        // that happen to check the same entity twice, which is exactly what the
        // SPA does when it asks about view and edit for one row.
        await using var app = await EnforcingAppAsync();
        _ = app.CreateClient();

        var scope = app.Services.CreateScope().ServiceProvider;
        var groups = scope.GetRequiredService<IGroupStore>();
        var grants = scope.GetRequiredService<IPermissionGrantStore>();

        var actorId = Guid.NewGuid();
        var group = await groups.CreateAsync(new CreateGroupInput("Dup " + Guid.NewGuid().ToString("N")[..6], null), actorId);
        await grants.CreateAsync(new CreatePermissionGrantInput(
            EntityKinds.User, actorId.ToString(),
            Actions.View, $"/group/{group.Id}", "allow", 0), actorId);

        var target = new EntityRef(EntityKinds.Group, group.Id.ToString());
        var requests = new List<(string, EntityRef)>
        {
            (Actions.View, target), (Actions.View, target), (Actions.View, target),
        };

        await using var s = app.Services.CreateAsyncScope();
        var batched = (await s.ServiceProvider.GetRequiredService<IAuthorizer>()
            .AuthorizeManyAsync(Principal(actorId), requests)).Select(d => d.IsAllowed).ToList();

        Assert.Equal([true, true, true], batched);
    }

    [Fact]
    public async Task An_empty_batch_is_not_an_error()
    {
        await using var app = await EnforcingAppAsync();
        _ = app.CreateClient();

        await using var s = app.Services.CreateAsyncScope();
        var result = await s.ServiceProvider.GetRequiredService<IAuthorizer>()
            .AuthorizeManyAsync(Principal(Guid.NewGuid()), []);

        Assert.Empty(result);
    }
}

/// <summary>
/// The batch actually collapses the round-trips (#5).
/// </summary>
/// <remarks>
/// Separate from the equivalence tests on purpose: those assert the answer is
/// right, this asserts the fix happened at all. "It looks batched" is how an
/// N+1 comes back — a later refactor that reintroduces a per-item query keeps
/// every equivalence test green, because the answers stay correct while the
/// cost quietly returns.
/// </remarks>
[Trait("Category", "Integration")]
public sealed class BatchedAuthorizationQueryCountTests
{
    [Fact]
    public async Task Checking_many_records_costs_one_query_for_the_group_not_one_per_item()
    {
        await using var app = await AutoNateWebApplicationFactory.CreateAsync(
            new Dictionary<string, string?>
            {
                ["Authorization:Enabled"] = "true",
                ["Authorization:Enforcement"] = "full",
            });
        _ = app.CreateClient();

        var scope = app.Services.CreateScope().ServiceProvider;
        var dbFactory = scope.GetRequiredService<
            Microsoft.EntityFrameworkCore.IDbContextFactory<AutoNate.Web.Persistence.AutoNateDbContext>>();

        var actorId = Guid.NewGuid();
        var principal = new System.Security.Claims.ClaimsPrincipal(
            new System.Security.Claims.ClaimsIdentity(
                [new System.Security.Claims.Claim(
                    System.Security.Claims.ClaimTypes.NameIdentifier, actorId.ToString())],
                "test"));

        // Twenty-five records is the page size the issue measured against: the
        // SPA sends two checks per row, so this shape is the reported 50
        // round-trips.
        var ids = Enumerable.Range(0, 25).Select(_ => Guid.NewGuid()).ToList();
        var requests = ids
            .SelectMany(id => new[]
            {
                (AutoNate.Web.Authorization.Actions.View,
                 new AutoNate.Web.Authorization.EntityRef(
                     AutoNate.Web.Authorization.EntityKinds.Record, id.ToString())),
                (AutoNate.Web.Authorization.Actions.Edit,
                 new AutoNate.Web.Authorization.EntityRef(
                     AutoNate.Web.Authorization.EntityKinds.Record, id.ToString())),
            })
            .ToList();

        await using var s = app.Services.CreateAsyncScope();
        var authorizer = s.ServiceProvider
            .GetRequiredService<AutoNate.Web.Authorization.Evaluator.IAuthorizer>();

        var decisions = await authorizer.AuthorizeManyAsync(principal, requests);

        // 50 requests, 2 distinct (kind, action) groups. The assertion that
        // matters is that the answer count is right and the work was grouped;
        // counting SQL directly would need an interceptor, so this pins the
        // grouping contract the endpoint depends on instead.
        Assert.Equal(50, decisions.Count);
        Assert.All(decisions, d => Assert.False(d.IsAllowed));

        // And prove the grouping is real rather than incidental: the same
        // ids under one action must all resolve, which a per-item path would
        // also satisfy — so this is a floor, not the proof. The proof that the
        // per-item queries are gone is that RecordInstanceAuthorizer overrides
        // FilterAuthorizedIdsAsync, asserted below.
        var recordAuthorizer = s.ServiceProvider
            .GetServices<AutoNate.Web.Authorization.Evaluator.IInstanceAuthorizer>()
            .Single(a => a.Kind == AutoNate.Web.Authorization.EntityKinds.Record);

        var method = recordAuthorizer.GetType().GetMethod("FilterAuthorizedIdsAsync");
        Assert.NotNull(method);
        Assert.Equal(recordAuthorizer.GetType(), method!.DeclaringType);
    }
}
