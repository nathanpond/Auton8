using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using AutoNate.Web.Authorization;
using AutoNate.Web.Models;
using AutoNate.Web.Persistence;
using AutoNate.Web.Services.Authorization;
using AutoNate.Web.Services.Flowable.Cache;
using AutoNate.Web.Services.Projections;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AutoNate.Web.Tests;

/// <summary>
/// The executions view says how current it is, and whether it is still
/// updating (#594).
/// </summary>
/// <remarks>
/// Those are two conditions, not one. <c>last_sync_at</c> advances only when a
/// tick <b>succeeds</b>, so a stalled feed and a quiet system look identical from
/// the rows alone — which is the confusion #109's third criterion forbids.
/// </remarks>
[Trait("Category", "Integration")]
public sealed class ExecutionFreshnessTests
{
    private static readonly Dictionary<string, string?> Enforcing = new()
    {
        ["Authorization:Enabled"] = "true",
        ["Authorization:Enforcement"] = "full",
    };

    private sealed record Freshness(
        [property: JsonPropertyName("asOfUtc")] DateTimeOffset? AsOfUtc,
        [property: JsonPropertyName("lastPolledAtUtc")] DateTimeOffset? LastPolledAtUtc,
        [property: JsonPropertyName("isUpdating")] bool IsUpdating,
        [property: JsonPropertyName("freshnessTargetSeconds")] int FreshnessTargetSeconds,
        [property: JsonPropertyName("pollIntervalSeconds")] int PollIntervalSeconds);

    private sealed record MeResponse(
        [property: JsonPropertyName("authenticated")] bool Authenticated,
        [property: JsonPropertyName("userId")] string? UserId);

    /// <summary>
    /// A quiet system reports UPDATING, not stalled (#594).
    /// </summary>
    /// <remarks>
    /// The complement that stops the signal degenerating into "nothing changed
    /// recently", which is the opposite of what it means. Polls are succeeding and
    /// no execution has moved — that is a healthy idle system, and saying it has
    /// stopped updating would be the cry-wolf that gets the indicator ignored.
    /// </remarks>
    [Fact]
    public async Task A_quiet_system_with_a_recent_poll_reports_updating()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(extraConfig: Enforcing);
        var client = factory.CreateClient();
        await SeedAsync(factory, count: 2);
        await HeartbeatAsync(factory, DateTimeOffset.UtcNow);
        await SignInAndGrantAsync(factory, client, "/workflowexecution/*");

        var freshness = await GetAsync(client);

        Assert.True(freshness.IsUpdating);
        Assert.NotNull(freshness.AsOfUtc);
        Assert.NotNull(freshness.LastPolledAtUtc);
    }

    /// <summary>
    /// A stalled feed reports NOT updating, even though every row looks fine (#594).
    /// </summary>
    /// <remarks>
    /// The case the rows cannot express, and the reason this story exists. The
    /// `last_sync_at` values here are untouched and entirely plausible — only the
    /// heartbeat is old. Without it the view would report itself current while the
    /// feed had stopped.
    /// </remarks>
    [Fact]
    public async Task A_stalled_feed_reports_not_updating_while_the_rows_look_fine()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(extraConfig: Enforcing);
        var client = factory.CreateClient();
        await SeedAsync(factory, count: 2);

        // Older than multiplier x poll interval (3 x 60s by default).
        await HeartbeatAsync(factory, DateTimeOffset.UtcNow.AddMinutes(-30));
        await SignInAndGrantAsync(factory, client, "/workflowexecution/*");

        var freshness = await GetAsync(client);

        Assert.False(freshness.IsUpdating);

        // And the rows themselves still look current -- which is exactly why the
        // heartbeat is needed to tell the two conditions apart.
        Assert.NotNull(freshness.AsOfUtc);
        Assert.True(
            DateTimeOffset.UtcNow - freshness.AsOfUtc!.Value < TimeSpan.FromMinutes(5),
            "The rows are fresh; only the feed has stalled. If this fails the test is "
            + "no longer distinguishing the two conditions.");
    }

    /// <summary>
    /// No heartbeat at all reads as NOT updating (#594).
    /// </summary>
    /// <remarks>
    /// A process that has never completed a sweep — a fresh deployment, or a feed
    /// failing every tick since boot. Defaulting to "updating" would make the worst
    /// case look like the best one.
    /// </remarks>
    [Fact]
    public async Task A_feed_that_has_never_completed_a_sweep_reports_not_updating()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(extraConfig: Enforcing);
        var client = factory.CreateClient();
        await SeedAsync(factory, count: 1);
        await SignInAndGrantAsync(factory, client, "/workflowexecution/*");

        var freshness = await GetAsync(client);

        Assert.Null(freshness.LastPolledAtUtc);
        Assert.False(freshness.IsUpdating);
    }

    /// <summary>
    /// `asOfUtc` is the OLDEST sync among the rows the actor may see (#594).
    /// </summary>
    /// <remarks>
    /// The oldest bounds how stale anything on screen could be. The newest would
    /// describe one lucky row and overstate how current the view is — and would
    /// pass a test that only checked the field was populated.
    /// </remarks>
    [Fact]
    public async Task AsOf_is_the_oldest_sync_among_the_visible_rows()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(extraConfig: Enforcing);
        var client = factory.CreateClient();
        await SeedAsync(factory, count: 2);

        var dbFactory = factory.Services.GetRequiredService<IDbContextFactory<AutoNateDbContext>>();
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE workflow_execution_cache SET last_sync_at = NOW() - INTERVAL '2 hours' "
                + "WHERE flowable_instance_id = 'inst-0'");
        }

        await HeartbeatAsync(factory, DateTimeOffset.UtcNow);
        await SignInAndGrantAsync(factory, client, "/workflowexecution/*");

        var freshness = await GetAsync(client);

        Assert.NotNull(freshness.AsOfUtc);
        var age = DateTimeOffset.UtcNow - freshness.AsOfUtc!.Value;
        Assert.True(age > TimeSpan.FromMinutes(90),
            $"Expected the OLDEST row (~2h) to set asOf; got one aged {age}.");
    }

    [Fact]
    public async Task The_endpoint_carries_an_authorization_decision()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(extraConfig: Enforcing);
        var client = factory.CreateClient();
        await SeedAsync(factory, count: 1);

        // Signed in, but with no grant on workflowexecution.
        (await client.GetAsync("/api/auth/me")).EnsureSuccessStatusCode();

        var response = await client.GetAsync("/api/executions/freshness");

        Assert.True(
            response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized,
            $"Expected the kind gate to refuse an ungranted actor; got {response.StatusCode}.");
    }

    // ---- helpers ----

    private static async Task<Freshness> GetAsync(HttpClient client)
    {
        var freshness = await client.GetFromJsonAsync<Freshness>("/api/executions/freshness");
        Assert.NotNull(freshness);
        return freshness!;
    }

    private static async Task HeartbeatAsync(AutoNateWebApplicationFactory factory, DateTimeOffset at)
    {
        using var scope = factory.Services.CreateScope();
        var watermarks = scope.ServiceProvider.GetRequiredService<IProjectionWatermarkStore>();
        await watermarks.SetAsync("flowable.exec.poll", at, CancellationToken.None);
    }

    private static async Task SignInAndGrantAsync(
        AutoNateWebApplicationFactory factory, HttpClient client, string selector)
    {
        var me = await client.GetFromJsonAsync<MeResponse>("/api/auth/me");
        Assert.NotNull(me);
        Assert.True(me!.Authenticated);
        var actor = Guid.Parse(me.UserId!);

        using var scope = factory.Services.CreateScope();
        var grants = scope.ServiceProvider.GetRequiredService<IPermissionGrantStore>();
        await grants.CreateAsync(new CreatePermissionGrantInput(
            EntityKinds.User, actor.ToString(), Actions.View, selector, "allow", 0), actor);
    }

    private static async Task SeedAsync(AutoNateWebApplicationFactory factory, int count)
    {
        using var scope = factory.Services.CreateScope();
        var projection = scope.ServiceProvider.GetRequiredService<FlowableExecutionProjection>();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AutoNateDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();

        await projection.ApplyAsync(
            Enumerable.Range(0, count).Select(i => new ChangeEvent<WorkflowExecutionSummary>(
                ChangeOp.Upsert, $"inst-{i}",
                new WorkflowExecutionSummary
                {
                    Id = $"inst-{i}",
                    Name = $"Run {i}",
                    ProcessDefinitionId = "invoice:1:1",
                    Status = "running",
                    StartUserId = "alice",
                    StartedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10),
                    LastActivityAtUtc = DateTimeOffset.UtcNow
                },
                DateTimeOffset.UtcNow)).ToArray(),
            db, CancellationToken.None);
    }
}
