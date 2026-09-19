using System.Net.Http.Json;
using System.Text.Json.Serialization;
using AutoNate.Web.Authorization;
using AutoNate.Web.Endpoints;
using AutoNate.Web.Models;
using AutoNate.Web.Persistence;
using AutoNate.Web.Services.Authorization;
using AutoNate.Web.Services.Flowable;
using AutoNate.Web.Services.Flowable.Cache;
using AutoNate.Web.Services.Projections;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AutoNate.Web.Tests;

/// <summary>
/// The executions list is a SQL query over the cache, with authorization pushed
/// into it (#108).
/// </summary>
[Trait("Category", "Integration")]
public sealed class ExecutionListFromCacheTests
{
    private static readonly Dictionary<string, string?> Enforcing = new()
    {
        ["Authorization:Enabled"] = "true",
        ["Authorization:Enforcement"] = "full",
    };

    private sealed record Page(
        [property: JsonPropertyName("items")] WorkflowExecutionSummary[] Items,
        [property: JsonPropertyName("totalCount")] int TotalCount);

    /// <summary>
    /// More than 200 visible executions are returned (#108).
    /// </summary>
    /// <remarks>
    /// The criterion that was <b>unsatisfiable</b> before #588: the fetch filling
    /// this cache was capped at 200, so no query over it could return more
    /// however well written. 250 rows, and the total says 250.
    /// </remarks>
    [Fact]
    public async Task An_actor_with_more_than_200_visible_executions_sees_more_than_200()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(extraConfig: Enforcing);
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });

        await SeedAsync(factory, count: 250);
        await SignInAndGrantAsync(factory, client, "/workflowexecution/*");

        var page = await GetPageAsync(client, "?page=0&pageSize=500");

        Assert.Equal(250, page.TotalCount);
        Assert.Equal(250, page.Items.Length);
    }

    /// <summary>
    /// Paging is over the FILTERED set, not the unfiltered one (#108).
    /// </summary>
    /// <remarks>
    /// This is what "filtering before paging" buys, and it is the difference
    /// between a fast list and a correct one: a page sliced from the unfiltered
    /// set would contain rows the actor may not see, or leave gaps where they were
    /// removed afterwards.
    /// </remarks>
    [Fact]
    public async Task Paging_is_over_the_authorized_set()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(extraConfig: Enforcing);
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });

        await SeedAsync(factory, count: 10, startedBy: "alice");
        await SeedAsync(factory, count: 10, startedBy: "bob", idPrefix: "bobinst");
        await SignInAndGrantAsync(factory, client, "/workflowexecution/*[startedby=alice]");

        var page = await GetPageAsync(client, "?page=0&pageSize=5");

        Assert.Equal(10, page.TotalCount);          // alice's only, not 20
        Assert.Equal(5, page.Items.Length);
        Assert.All(page.Items, i => Assert.Equal("alice", i.StartUserId));
    }

    /// <summary>
    /// The complement: an actor does not see what they are not permitted to (#108).
    /// </summary>
    /// <remarks>
    /// Pushing authorization into SQL must change <b>where</b> the decision is made
    /// and not <b>what</b> it decides. Without this, the test above would pass
    /// against a query that ignored grants entirely and simply returned the first
    /// page of everything.
    /// </remarks>
    [Fact]
    public async Task An_execution_the_actor_may_not_see_is_absent()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(extraConfig: Enforcing);
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });

        await SeedAsync(factory, count: 3, startedBy: "alice");
        await SeedAsync(factory, count: 3, startedBy: "bob", idPrefix: "bobinst");
        await SignInAndGrantAsync(factory, client, "/workflowexecution/*[startedby=alice]");

        var page = await GetPageAsync(client, "?page=0&pageSize=50");

        Assert.Equal(3, page.TotalCount);
        Assert.DoesNotContain(page.Items, i => i.StartUserId == "bob");
        Assert.NotEmpty(page.Items);   // and it is not empty for the wrong reason
    }

    /// <summary>
    /// The API's status vocabulary is preserved (#108).
    /// </summary>
    /// <remarks>
    /// The cache stores `active`; the SPA compares against `"Running"`
    /// (`WorkflowExecutions.tsx:191`). Serving the list from the cache without
    /// translating leaves every status count at zero, silently — nothing errors
    /// and the page just stops meaning anything.
    /// </remarks>
    [Fact]
    public async Task The_status_vocabulary_is_the_one_the_client_speaks()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(extraConfig: Enforcing);
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });

        await SeedAsync(factory, count: 1, status: "running", idPrefix: "run");
        await SeedAsync(factory, count: 1, status: "completed", idPrefix: "done");
        await SeedAsync(factory, count: 1, status: "cancelled", idPrefix: "gone");
        await SignInAndGrantAsync(factory, client, "/workflowexecution/*");

        var page = await GetPageAsync(client, "?page=0&pageSize=50");
        var byStatus = page.Items.ToDictionary(i => i.Id, i => i.Status, StringComparer.Ordinal);

        Assert.Equal("Running", byStatus["run-0"]);
        Assert.Equal("Complete", byStatus["done-0"]);
        Assert.Equal("Cancelled", byStatus["gone-0"]);

        // The cache's own words must not leak through.
        Assert.DoesNotContain(page.Items, i => i.Status is "active" or "completed" or "cancelled");
    }

    /// <summary>
    /// Cancelled beats Errored; Errored beats Running (#108).
    /// </summary>
    /// <remarks>
    /// The live path's precedence, kept deliberately: operator intent supersedes a
    /// stale failure, and a process with a failed job is actionable but no longer
    /// healthy. Asserting only the Errored case would pass against an
    /// implementation that let Errored win over everything.
    /// </remarks>
    [Fact]
    public async Task The_errored_overlay_keeps_the_live_paths_precedence()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(extraConfig: Enforcing);
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });

        await SeedAsync(factory, count: 1, status: "running", idPrefix: "err");
        await SeedAsync(factory, count: 1, status: "cancelled", idPrefix: "cancelled-and-errored");
        await MarkErroredAsync(factory, "err-0");
        await MarkErroredAsync(factory, "cancelled-and-errored-0");
        await SignInAndGrantAsync(factory, client, "/workflowexecution/*");

        var page = await GetPageAsync(client, "?page=0&pageSize=50");
        var byStatus = page.Items.ToDictionary(i => i.Id, i => i.Status, StringComparer.Ordinal);

        Assert.Equal("Errored", byStatus["err-0"]);
        Assert.Equal("Cancelled", byStatus["cancelled-and-errored-0"]);
    }

    /// <summary>
    /// "Last activity" comes from the event log, not from the start time (#108).
    /// </summary>
    /// <remarks>
    /// <c>workflow_execution_cache</c> has no last-activity column. Falling back to
    /// <c>EndTime ?? StartTime</c> would show a RUNNING instance its own start time
    /// in a column labelled "Last activity" — wrong for exactly the rows an
    /// operator watches. The event log carries the signal the live path read from
    /// activity history.
    /// </remarks>
    [Fact]
    public async Task Last_activity_comes_from_the_event_log()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(extraConfig: Enforcing);
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });

        await SeedAsync(factory, count: 1, idPrefix: "active-run");
        var activityAt = DateTime.UtcNow;
        await AddEventAsync(factory, "active-run-0", activityAt);
        await SignInAndGrantAsync(factory, client, "/workflowexecution/*");

        var page = await GetPageAsync(client, "?page=0&pageSize=50");
        var row = Assert.Single(page.Items);

        Assert.NotNull(row.LastActivityAtUtc);
        Assert.True(
            row.LastActivityAtUtc!.Value > row.StartedAtUtc!.Value,
            "Last activity must come from the event log, not fall back to the start time.");
    }

    // ---- helpers ----

    private static async Task<Page> GetPageAsync(HttpClient client, string query)
    {
        var page = await client.GetFromJsonAsync<Page>($"/api/executions/page{query}");
        Assert.NotNull(page);
        return page!;
    }

    /// <summary>
    /// Signs in (dev auto-login attaches the cookie on the first GET) and grants
    /// to whoever that turned out to be.
    /// </summary>
    /// <remarks>
    /// The id is READ BACK from /api/auth/me rather than assumed. The first
    /// version of these tests granted to a hardcoded GUID, so the grant belonged
    /// to nobody the request was made as — every list came back empty, which is
    /// also what a broken query looks like.
    /// </remarks>
    private static async Task<Guid> SignInAndGrantAsync(
        AutoNateWebApplicationFactory factory, HttpClient client, string selector)
    {
        var me = await client.GetFromJsonAsync<MeResponse>("/api/auth/me");
        Assert.NotNull(me);
        Assert.True(me!.Authenticated, "Dev auto-login should have signed the test client in.");
        var actor = Guid.Parse(me.UserId!);

        using var scope = factory.Services.CreateScope();
        var grants = scope.ServiceProvider.GetRequiredService<IPermissionGrantStore>();
        await grants.CreateAsync(new CreatePermissionGrantInput(
            EntityKinds.User, actor.ToString(), Actions.View, selector, "allow", 0), actor);
        return actor;
    }

    private sealed record MeResponse(
        [property: JsonPropertyName("authenticated")] bool Authenticated,
        [property: JsonPropertyName("userId")] string? UserId);

    private static async Task SeedAsync(
        AutoNateWebApplicationFactory factory,
        int count,
        string startedBy = "alice",
        string status = "running",
        string idPrefix = "inst")
    {
        using var scope = factory.Services.CreateScope();
        var projection = scope.ServiceProvider.GetRequiredService<FlowableExecutionProjection>();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AutoNateDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();

        var batch = Enumerable.Range(0, count)
            .Select(i => new ChangeEvent<WorkflowExecutionSummary>(
                ChangeOp.Upsert, $"{idPrefix}-{i}",
                new WorkflowExecutionSummary
                {
                    Id = $"{idPrefix}-{i}",
                    Name = $"Run {i}",
                    WorkflowModelName = "Invoice Approval",
                    ProcessDefinitionId = "invoice:1:1",
                    Status = status,
                    StartUserId = startedBy,
                    StartedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-count + i),
                    LastActivityAtUtc = DateTimeOffset.UtcNow.AddMinutes(-count + i)
                },
                DateTimeOffset.UtcNow))
            .ToArray();

        await projection.ApplyAsync(batch, db, CancellationToken.None);
    }

    private static async Task MarkErroredAsync(AutoNateWebApplicationFactory factory, string instanceId)
    {
        var dbFactory = factory.Services.GetRequiredService<IDbContextFactory<AutoNateDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO workflow_execution_errors
                (id, process_instance_id, activity_id, error_message, occurred_at_utc)
            VALUES (gen_random_uuid(), {instanceId}, 'task-1', 'boom', NOW())
            """);
    }

    private static async Task AddEventAsync(
        AutoNateWebApplicationFactory factory, string instanceId, DateTime at)
    {
        var dbFactory = factory.Services.GetRequiredService<IDbContextFactory<AutoNateDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();
        var payload = "{}";
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO workflow_event_log_cache
                (event_id, flowable_instance_id, process_definition_key, event_time,
                 event_type, payload, projection_version, last_sync_at)
            VALUES ({Guid.NewGuid().ToString()}, {instanceId}, 'invoice', {at},
                    'activity', {payload}::jsonb, 1, NOW())
            """);
    }
}
