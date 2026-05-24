using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using AutoNate.Web.Models;
using AutoNate.Web.Persistence;
using AutoNate.Web.Persistence.Scaffolded;
using AutoNate.Web.Services.Flowable.Cache;
using AutoNate.Web.Services.Projections;
using AutoNate.Web.Services.Query;
using AutoNate.Web.Services.Query.Entities;
using AutoNate.Web.Services.Records.Rollups;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AutoNate.Web.Tests;

[Trait("Category", "Integration")]
public sealed class ProjectionFrameworkPhase4Tests
{
    [Fact]
    public async Task Health_service_records_apply_and_failure()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        _ = factory.CreateClient();

        using var scope = factory.Services.CreateScope();
        var health = scope.ServiceProvider.GetRequiredService<IProjectionHealthService>();
        var registry = scope.ServiceProvider.GetRequiredService<IProjectionRegistry>();

        // Pretend the execution projection just applied 5 events from the
        // poll feed, then immediately hit a transient failure.
        health.RecordApply("flowable.workflow_execution_cache", "flowable.exec.poll", 5);
        health.RecordFailure("flowable.workflow_execution_cache", "flowable.exec.poll", "test failure");

        var exec = registry.TryGet("flowable.workflow_execution_cache");
        Assert.NotNull(exec);
        var snap = health.Snapshot(exec!);
        Assert.NotNull(snap);
        Assert.Equal(5, snap!.EventsAppliedTotal);
        Assert.Equal(1, snap.ApplyFailuresTotal);
        Assert.Equal("test failure", snap.LastFailureMessage);
        Assert.NotNull(snap.LastAppliedAtUtc);
        Assert.NotNull(snap.LastFailureAtUtc);
    }

    [Fact]
    public async Task Pause_then_resume_round_trips_through_health()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        _ = factory.CreateClient();

        using var scope = factory.Services.CreateScope();
        var health = scope.ServiceProvider.GetRequiredService<IProjectionHealthService>();

        Assert.False(health.IsPaused("flowable.workflow_task_cache"));
        health.Pause("flowable.workflow_task_cache");
        Assert.True(health.IsPaused("flowable.workflow_task_cache"));
        health.Resume("flowable.workflow_task_cache");
        Assert.False(health.IsPaused("flowable.workflow_task_cache"));
    }

    [Fact]
    public async Task Admin_endpoint_lists_projections_and_supports_pause()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();

        // Touch /api/auth/me first so the dev auto-login cookie is set.
        await client.GetAsync("/api/auth/me");

        var listResp = await client.GetAsync("/api/admin/projections/");
        Assert.Equal(HttpStatusCode.OK, listResp.StatusCode);
        var snapshots = await listResp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(snapshots.GetArrayLength() >= 1);

        // Every snapshot has the standard shape — `name`, `version`, `paused`.
        var first = snapshots.EnumerateArray().First();
        Assert.True(first.TryGetProperty("name", out _));
        Assert.True(first.TryGetProperty("paused", out _));

        // Pause one and read back via the singular endpoint.
        var name = first.GetProperty("name").GetString()!;
        var pauseResp = await client.PostAsync($"/api/admin/projections/{Uri.EscapeDataString(name)}/pause", content: null);
        Assert.Equal(HttpStatusCode.OK, pauseResp.StatusCode);

        var singleResp = await client.GetAsync($"/api/admin/projections/{Uri.EscapeDataString(name)}");
        Assert.Equal(HttpStatusCode.OK, singleResp.StatusCode);
        var single = await singleResp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(single.GetProperty("paused").GetBoolean());

        // Resume and verify.
        var resumeResp = await client.PostAsync($"/api/admin/projections/{Uri.EscapeDataString(name)}/resume", content: null);
        Assert.Equal(HttpStatusCode.OK, resumeResp.StatusCode);
        var afterResume = await client.GetFromJsonAsync<JsonElement>($"/api/admin/projections/{Uri.EscapeDataString(name)}");
        Assert.False(afterResume.GetProperty("paused").GetBoolean());
    }

    [Fact]
    public async Task Admin_rebuild_returns_404_for_unknown_projection()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        var resp = await client.PostAsync("/api/admin/projections/no-such-projection/rebuild", content: null);
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Record_activity_rollup_projection_writes_per_bucket_counts()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        _ = factory.CreateClient();

        using var scope = factory.Services.CreateScope();
        var projection = scope.ServiceProvider.GetRequiredService<RecordActivityRollupProjection>();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AutoNateDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();

        var typeId = Guid.NewGuid();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        await projection.ApplyAsync(new[]
        {
            new ChangeEvent<RecordActivityRollupSnapshot>(
                ChangeOp.Upsert,
                RecordActivityRollupProjection.BuildSourceId(typeId, today),
                new RecordActivityRollupSnapshot(typeId, today,
                    RecordsCreated: 7, RecordsUpdated: 3, RecordsArchived: 1),
                DateTimeOffset.UtcNow)
        }, db, CancellationToken.None);

        var row = await db.RecordActivityRollupCache.AsNoTracking()
            .FirstOrDefaultAsync(r => r.RecordTypeId == typeId && r.BucketDay == today);
        Assert.NotNull(row);
        Assert.Equal(7, row!.RecordsCreated);
        Assert.Equal(3, row.RecordsUpdated);
        Assert.Equal(1, row.RecordsArchived);

        // Re-apply with new counts — overwrite, not accumulate.
        await projection.ApplyAsync(new[]
        {
            new ChangeEvent<RecordActivityRollupSnapshot>(
                ChangeOp.Upsert,
                RecordActivityRollupProjection.BuildSourceId(typeId, today),
                new RecordActivityRollupSnapshot(typeId, today,
                    RecordsCreated: 9, RecordsUpdated: 4, RecordsArchived: 2),
                DateTimeOffset.UtcNow)
        }, db, CancellationToken.None);

        var updated = await db.RecordActivityRollupCache.AsNoTracking()
            .FirstOrDefaultAsync(r => r.RecordTypeId == typeId && r.BucketDay == today);
        Assert.Equal(9, updated!.RecordsCreated);
        Assert.Equal(4, updated.RecordsUpdated);
        Assert.Equal(2, updated.RecordsArchived);
    }

    [Fact]
    public async Task Plugin_scheduled_job_registry_dedupes_by_name()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        _ = factory.CreateClient();

        using var scope = factory.Services.CreateScope();
        var registry = scope.ServiceProvider.GetRequiredService<AutoNate.Web.Plugins.PluginScheduledJobRegistry>();
        var pluginId = Guid.NewGuid();

        registry.Register(pluginId, "plugin.job.test-a", TimeSpan.FromMinutes(1), _ => Task.CompletedTask);

        Assert.Throws<InvalidOperationException>(() =>
            registry.Register(Guid.NewGuid(), "plugin.job.test-a", TimeSpan.FromMinutes(1), _ => Task.CompletedTask));

        var snap = registry.Snapshot();
        Assert.Single(snap.Where(j => j.Name == "plugin.job.test-a"));

        var removed = registry.RemoveForPlugin(pluginId);
        Assert.Equal(1, removed);
        Assert.DoesNotContain(registry.Snapshot(), j => j.Name == "plugin.job.test-a");
    }
}
