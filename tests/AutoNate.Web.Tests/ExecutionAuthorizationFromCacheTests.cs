using System.Security.Claims;
using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.Evaluator;
using AutoNate.Web.Endpoints;
using AutoNate.Web.Models;
using AutoNate.Web.Persistence;
using AutoNate.Web.Persistence.Scaffolded;
using AutoNate.Web.Services.Authorization;
using AutoNate.Web.Services.Flowable;
using AutoNate.Web.Services.Flowable.Cache;
using AutoNate.Web.Services.Projections;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AutoNate.Web.Tests;

/// <summary>
/// A single execution is authorized from the cache, not from a live Flowable
/// call (#579).
/// </summary>
/// <remarks>
/// <para>
/// <c>WorkflowExecutionInstanceAuthorizer</c> called
/// <c>IFlowableClient.GetProcessInstanceAsync</c> per instance, so every
/// <c>RequirePermission(…, "processInstanceId")</c> gate carried a hard
/// per-request dependency on the engine — and the list authorized from the
/// cache while single reads authorized from live Flowable, which is two sources
/// of truth for one decision.
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
public sealed class ExecutionAuthorizationFromCacheTests
{
    private static readonly Guid Actor = new("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

    // The factory defaults to authorization OFF, so IsAuthorizedAsync returns
    // true for everything. Without this every test below would pass whatever the
    // authorizer did -- which the complement test is what caught: it failed while
    // its permitted-actor sibling passed vacuously.
    private static readonly Dictionary<string, string?> Enforcing = new()
    {
        ["Authorization:Enabled"] = "true",
        // lower-case exactly: AuthorizationOptions refuses to start otherwise,
        // because the evaluator compares ordinally and "Full" reads as "not full".
        ["Authorization:Enforcement"] = "full",
    };

    private static ClaimsPrincipal Principal(Guid id) =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, id.ToString())], "test"));

    /// <summary>
    /// The two paths build the same facts for the same instance (#579 AC3).
    /// </summary>
    /// <remarks>
    /// Asserted rather than assumed, because "the same grant admits the same rows
    /// through either route" is the entire point of moving this path onto the
    /// cache. If these two dictionaries ever diverge, a grant means two things
    /// again — the defect M5's first four stories existed to close, reappearing
    /// one layer up.
    /// </remarks>
    [Fact]
    public void The_authorizer_and_the_list_path_build_identical_facts()
    {
        var summary = new WorkflowExecutionSummary
        {
            Id = "inst-parity",
            Name = "Invoice for Acme",
            WorkflowModelName = "Invoice Approval",
            ProcessDefinitionId = "invoice:3:abc",
            Status = "running",
            StartUserId = "alice"
        };

        // The row the projection would write for that summary.
        var row = new WorkflowExecutionCache
        {
            FlowableInstanceId = "inst-parity",
            ProcessDefinitionKey = "invoice",
            ProcessDefinitionId = "invoice:3:abc",
            Status = WorkflowExecutionStatuses.Normalize(summary.Status),
            StartedBy = "alice",
            StartTime = DateTime.UtcNow,
            LastSyncAtUtc = DateTime.UtcNow
        };

        var fromAuthorizer = WorkflowExecutionInstanceAuthorizer.BuildFacts(row);
        var fromListPath = ExecutionEndpoints.BuildFacts(summary);

        Assert.Equal(
            fromListPath.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase),
            fromAuthorizer.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase));

        // Named explicitly so a silent narrowing of either side is visible here
        // rather than only as a count mismatch.
        Assert.Equal("invoice", fromAuthorizer["processkey"]);
        Assert.Equal("invoice:3:abc", fromAuthorizer["definitionkey"]);
        Assert.Equal("alice", fromAuthorizer["startedby"]);
        Assert.Equal(WorkflowExecutionStatuses.Active, fromAuthorizer["status"]);
    }

    [Fact]
    public async Task A_permitted_actor_is_authorized_with_Flowable_unreachable()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(
            extraConfig: Enforcing);
        _ = factory.CreateClient();

        await SeedCachedInstanceAsync(factory, "inst-down", startedBy: "alice");
        await GrantAsync(factory, "/workflowexecution/*");

        // Not "returns null" — that means the engine says the instance is gone,
        // which the read-through treats as a deletion. Throwing is "we could not
        // ask", which is the degradation this story is about.
        factory.FlowableStub.GetProcessInstanceThrows =
            new HttpRequestException("connection refused");

        Assert.True(await AuthorizeAsync(factory, "inst-down"));
    }

    /// <summary>
    /// The complement: degrading must not fail open (#579 AC4).
    /// </summary>
    /// <remarks>
    /// A degradation that starts admitting people is worse than the dependency it
    /// removed. Without this, the test above would pass against an implementation
    /// that simply returned true whenever Flowable was unreachable.
    /// </remarks>
    [Fact]
    public async Task An_unpermitted_actor_is_still_refused_with_Flowable_unreachable()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(
            extraConfig: Enforcing);
        _ = factory.CreateClient();

        await SeedCachedInstanceAsync(factory, "inst-refused", startedBy: "bob");

        // A grant that exists but does not cover this instance: the actor may see
        // executions started by alice, and this one was started by bob.
        await GrantAsync(factory, "/workflowexecution/*[startedby=alice]");

        factory.FlowableStub.GetProcessInstanceThrows =
            new HttpRequestException("connection refused");

        Assert.False(await AuthorizeAsync(factory, "inst-refused"));
    }

    /// <summary>
    /// No cached row and no reachable engine refuses (#579 AC5).
    /// </summary>
    /// <remarks>
    /// The story has to say which answer this case gets, and it is refusal. With
    /// no row there are no facts, so no selector can be evaluated — and admitting
    /// on absent evidence is exactly the failure #577 closed on the query path.
    /// </remarks>
    [Fact]
    public async Task A_cache_miss_with_Flowable_unreachable_refuses()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(
            extraConfig: Enforcing);
        _ = factory.CreateClient();

        // Deliberately no cached row for this id.
        await GrantAsync(factory, "/workflowexecution/*");

        factory.FlowableStub.GetProcessInstanceThrows =
            new HttpRequestException("connection refused");

        Assert.False(await AuthorizeAsync(factory, "never-cached"));
    }

    // ---- helpers ----

    private static async Task<bool> AuthorizeAsync(
        AutoNateWebApplicationFactory factory, string instanceId)
    {
        using var scope = factory.Services.CreateScope();
        var authorizer = scope.ServiceProvider.GetRequiredService<IAuthorizer>();
        var instanceAuthorizer = scope.ServiceProvider
            .GetServices<IInstanceAuthorizer>()
            .Single(a => a.Kind == EntityKinds.WorkflowExecution);

        return await instanceAuthorizer.ExistsAndAuthorizedAsync(
            authorizer, Principal(Actor), Actions.View, instanceId, CancellationToken.None);
    }

    private static async Task GrantAsync(
        AutoNateWebApplicationFactory factory, string selector, string effect = "allow")
    {
        using var scope = factory.Services.CreateScope();
        var grants = scope.ServiceProvider.GetRequiredService<IPermissionGrantStore>();
        await grants.CreateAsync(new CreatePermissionGrantInput(
            EntityKinds.User, Actor.ToString(), Actions.View, selector, effect, 0), Actor);
    }

    /// <summary>
    /// A finished run is still authorizable and still cached (#634).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The read-through treated a null live read as "deleted in Flowable" and
    /// removed the row. But <c>GetProcessInstanceAsync</c> asks
    /// <c>service/runtime/process-instances/{id}</c>, and Flowable answers 404
    /// there for every COMPLETED instance — measured against the engine, whose
    /// runtime table holds only live runs.
    /// </para>
    /// <para>
    /// So once a finished run's row aged past the 30s freshness window inside the
    /// 60s poll interval, it was deleted: every
    /// <c>RequirePermission(..., "processInstanceId")</c> route 403'd for
    /// non-super-admins and the executions list lost the run until the next poll.
    /// 4,894 completed/cancelled rows were in scope on the dev database.
    /// </para>
    /// <para>
    /// The stub holds NO instance for this id, which is exactly what the real
    /// client returns for a finished run, and the row is aged so the live read is
    /// genuinely attempted.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_completed_run_is_not_deleted_when_the_engine_stops_listing_it()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        (await client.GetAsync("/api/workflows/")).EnsureSuccessStatusCode();

        const string Instance = "inst-634";
        await SeedCachedInstanceAsync(factory, Instance, "alice");

        // Finished. The runtime endpoint will not list it, which the stub models
        // by simply not holding it.
        using (var scope = factory.Services.CreateScope())
        {
            var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AutoNateDbContext>>();
            await using var db = await dbFactory.CreateDbContextAsync();
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE workflow_execution_cache SET status = 'completed'
                WHERE flowable_instance_id = {Instance}
                """);
        }

        factory.FlowableStub.InstancesById.Remove(Instance);

        using (var scope = factory.Services.CreateScope())
        {
            var readThrough = scope.ServiceProvider.GetRequiredService<IFlowableReadThrough>();

            var served = await readThrough.GetInstanceAsync(Instance, CancellationToken.None);

            Assert.NotNull(served);
            Assert.Equal("completed", served!.Status);
        }

        // AND THE ROW SURVIVED. Returning it while deleting it would satisfy the
        // assertion above and still break the executions list on the next read.
        using (var scope = factory.Services.CreateScope())
        {
            var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AutoNateDbContext>>();
            await using var db = await dbFactory.CreateDbContextAsync();
            var still = await db.WorkflowExecutionCache.AsNoTracking()
                .FirstOrDefaultAsync(c => c.FlowableInstanceId == Instance);

            Assert.NotNull(still);
        }
    }

    /// <summary>
    /// The complement: a run that is genuinely gone IS still removed (#634).
    /// </summary>
    /// <remarks>
    /// Without this, "never delete" passes the test above while turning the cache
    /// into a graveyard — a deleted instance would be served forever. Only a
    /// TERMINAL row is protected; an active one the engine no longer lists really
    /// has been deleted.
    /// </remarks>
    [Fact]
    public async Task An_active_run_the_engine_no_longer_lists_is_still_removed()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        (await client.GetAsync("/api/workflows/")).EnsureSuccessStatusCode();

        const string Instance = "inst-634-gone";
        await SeedCachedInstanceAsync(factory, Instance, "alice");
        factory.FlowableStub.InstancesById.Remove(Instance);

        using (var scope = factory.Services.CreateScope())
        {
            var readThrough = scope.ServiceProvider.GetRequiredService<IFlowableReadThrough>();
            Assert.Null(await readThrough.GetInstanceAsync(Instance, CancellationToken.None));
        }

        using (var scope = factory.Services.CreateScope())
        {
            var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AutoNateDbContext>>();
            await using var db = await dbFactory.CreateDbContextAsync();
            var gone = await db.WorkflowExecutionCache.AsNoTracking()
                .FirstOrDefaultAsync(c => c.FlowableInstanceId == Instance);

            Assert.Null(gone);
        }
    }

    /// <summary>
    /// A deny naming a withdrawn tag refuses, in memory as it does in SQL (#632).
    /// </summary>
    /// <remarks>
    /// <para>
    /// #576 withdrew <c>tenant</c> and #581 withdrew the candidate pair from the
    /// compilers, and #577 made an uncompilable DENY fail the request closed. But
    /// <c>EfCorePermissionGrantStore.CreateAsync</c> only PARSES a selector, so a
    /// stored deny naming a withdrawn tag survives — and on this path it resolved
    /// to <c>actual = null</c>, compared false, and therefore did not deny.
    /// </para>
    /// <para>
    /// The same grant failed closed in the list and GRANTED ACCESS on a single
    /// read. A deny that stops denying is the direction that matters, and it was
    /// introduced by this cluster's own removals.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_deny_naming_a_withdrawn_tag_still_refuses()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(
            extraConfig: Enforcing);
        _ = factory.CreateClient();

        await SeedCachedInstanceAsync(factory, "inst-632", startedBy: "alice");

        // Engine unreachable, as the siblings above do: the seed ages the row past
        // ReadThroughFreshness, so without this the live read returns null, #634
        // concludes the ACTIVE run was deleted, and the refusal below would be
        // about a missing row rather than about the deny.
        factory.FlowableStub.GetProcessInstanceThrows =
            new HttpRequestException("connection refused");

        // A broad allow the actor really holds...
        await GrantAsync(factory, "/workflowexecution/*");
        // ...and a deny naming a tag this kind no longer advertises.
        await GrantAsync(factory, "/workflowexecution/*[tenant=acme]", effect: "deny");

        Assert.False(await AuthorizeAsync(factory, "inst-632"));
    }

    /// <summary>
    /// The complement: a deny naming a LIVE tag still behaves normally (#632).
    /// </summary>
    /// <remarks>
    /// Without this, "refuse whenever a deny exists" passes the test above while
    /// making every deny unconditional — which would refuse far more than it
    /// should and look like the fix working.
    /// </remarks>
    [Fact]
    public async Task A_deny_naming_a_live_tag_that_does_not_match_still_allows()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(
            extraConfig: Enforcing);
        _ = factory.CreateClient();

        await SeedCachedInstanceAsync(factory, "inst-632-ok", startedBy: "alice");

        factory.FlowableStub.GetProcessInstanceThrows =
            new HttpRequestException("connection refused");

        await GrantAsync(factory, "/workflowexecution/*");
        // `startedby` IS advertised, and this run was started by alice, so the
        // deny does not match and must not fire.
        await GrantAsync(factory, "/workflowexecution/*[startedby=bob]", effect: "deny");

        Assert.True(await AuthorizeAsync(factory, "inst-632-ok"));
    }

    private static async Task SeedCachedInstanceAsync(
        AutoNateWebApplicationFactory factory, string instanceId, string startedBy)
    {
        using var scope = factory.Services.CreateScope();
        var projection = scope.ServiceProvider.GetRequiredService<FlowableExecutionProjection>();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AutoNateDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();

        await projection.ApplyAsync(
            [new ChangeEvent<WorkflowExecutionSummary>(
                ChangeOp.Upsert, instanceId,
                new WorkflowExecutionSummary
                {
                    Id = instanceId,
                    Name = "seeded run",
                    ProcessDefinitionId = "invoice:1:1",
                    Status = "running",
                    StartUserId = startedBy,
                    StartedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
                    LastActivityAtUtc = DateTimeOffset.UtcNow
                },
                DateTimeOffset.UtcNow)],
            db, CancellationToken.None);

        // AGE THE ROW PAST ReadThroughFreshness, deliberately.
        //
        // The first version of this helper set last_sync_at = NOW(), which made
        // the read-through serve straight from cache and never call Flowable at
        // all -- so tests named "with Flowable unreachable" never reached the
        // throwing stub and would have passed with the degradation path broken.
        // The mutation that makes FlowableReadThrough rethrow instead of
        // returning the cached row killed only the cache-miss test, which is how
        // that was found.
        //
        // Stale forces the live attempt, the stub throws, and the cached row is
        // what comes back -- which is the behaviour these tests claim.
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE workflow_execution_cache SET last_sync_at = NOW() - INTERVAL '1 hour'
            WHERE flowable_instance_id = {instanceId}
            """);
    }
}
