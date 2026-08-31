using System.Security.Claims;
using AutoNate.Web.Models;
using AutoNate.Web.Persistence;
using AutoNate.Web.Services.Flowable.Cache;
using AutoNate.Web.Services.Flowable.Cache.ColdTier;
using AutoNate.Web.Services.Projections;
using AutoNate.Web.Services.Query;
using AutoNate.Web.Services.Query.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AutoNate.Web.Tests;

[Trait("Category", "Integration")]
public sealed class ProjectionColdTierArchiveTests : IDisposable
{
    // Each test gets its own cold-tier directory so parallel runs can't
    // see each other's parquet files. Cleaned in Dispose so we don't
    // accumulate gigabytes of dev cruft over many runs.
    private readonly string _coldRoot = Path.Combine(
        Path.GetTempPath(),
        "autonate-cold-" + Guid.NewGuid().ToString("n"));

    public void Dispose()
    {
        try { if (Directory.Exists(_coldRoot)) Directory.Delete(_coldRoot, recursive: true); }
        catch { /* best-effort cleanup */ }
    }

    private Dictionary<string, string?> ColdTierConfig() => new()
    {
        ["FlowableCache:ColdTier:Enabled"] = "true",
        ["FlowableCache:ColdTier:ArchiveAfterDays"] = "30",
        ["FlowableCache:ColdTier:Root"] = _coldRoot,
        ["FlowableCache:ColdTier:MinimumRowAge"] = "00:00:00",
    };

    [Fact]
    public async Task Archiver_writes_parquet_and_deletes_aged_rows()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(ColdTierConfig());
        _ = factory.CreateClient();

        using var scope = factory.Services.CreateScope();
        var execProjection = scope.ServiceProvider.GetRequiredService<FlowableExecutionProjection>();
        var historyProjection = scope.ServiceProvider.GetRequiredService<FlowableHistoryProjection>();
        var archiver = scope.ServiceProvider.GetRequiredService<ColdTierArchiverService>();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AutoNateDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();

        await execProjection.ApplyAsync(new[]
        {
            new ChangeEvent<WorkflowExecutionSummary>(
                ChangeOp.Upsert, "inst-archive",
                new WorkflowExecutionSummary
                {
                    Id = "inst-archive",
                    StartedAtUtc = DateTimeOffset.UtcNow.AddDays(-180),
                    Status = "Complete",
                    ProcessDefinitionId = "purchase:1:abc",
                    StartUserId = "iris"
                },
                DateTimeOffset.UtcNow)
        }, db, CancellationToken.None);

        // Three "old" events (~60 days old, past the 30d archive cutoff) and
        // one fresh event the archiver must leave alone.
        var oldEvents = Enumerable.Range(0, 3).Select(i => new FlowableHistoricActivityEvent
        {
            ProcessInstanceId = "inst-archive",
            ProcessDefinitionId = "purchase:1:abc",
            ActivityId = $"step-{i}",
            ActivityName = $"Step {i}",
            ActivityType = "serviceTask",
            StartTime = DateTimeOffset.UtcNow.AddDays(-60 - i)
        }).ToArray();
        var freshEvent = new FlowableHistoricActivityEvent
        {
            ProcessInstanceId = "inst-archive",
            ProcessDefinitionId = "purchase:1:abc",
            ActivityId = "fresh-step",
            ActivityName = "Fresh",
            ActivityType = "userTask",
            StartTime = DateTimeOffset.UtcNow.AddMinutes(-30)
        };

        await historyProjection.ApplyAsync(
            oldEvents.Concat(new[] { freshEvent })
                .Select((e, i) => new ChangeEvent<FlowableHistoricActivityEvent>(
                    ChangeOp.Upsert, $"src-{i}", e, DateTimeOffset.UtcNow))
                .ToArray(),
            db, CancellationToken.None);

        var beforeArchive = await db.WorkflowEventLogCache.AsNoTracking()
            .Where(e => e.FlowableInstanceId == "inst-archive")
            .CountAsync();
        // 3 old events + 1 fresh event, all without EndTime => 4 started rows.
        Assert.Equal(4, beforeArchive);

        var report = await archiver.RunOnceAsync(CancellationToken.None);
        Assert.Equal(3, report.RowsArchived);
        Assert.True(report.MonthsTouched > 0);

        var afterArchive = await db.WorkflowEventLogCache.AsNoTracking()
            .Where(e => e.FlowableInstanceId == "inst-archive")
            .CountAsync();
        Assert.Equal(1, afterArchive);

        // Parquet file is on disk and non-empty.
        var coldDir = Path.Combine(_coldRoot, "workflow_event_log");
        Assert.True(Directory.Exists(coldDir));
        var files = Directory.GetFiles(coldDir, "*.parquet");
        Assert.NotEmpty(files);
        Assert.True(new FileInfo(files[0]).Length > 0);
    }

    [Fact]
    public async Task Analytics_entity_counts_events_across_hot_and_cold()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(ColdTierConfig());
        _ = factory.CreateClient();

        using (var seedScope = factory.Services.CreateScope())
        {
            var execProjection = seedScope.ServiceProvider.GetRequiredService<FlowableExecutionProjection>();
            var historyProjection = seedScope.ServiceProvider.GetRequiredService<FlowableHistoryProjection>();
            var archiver = seedScope.ServiceProvider.GetRequiredService<ColdTierArchiverService>();
            var dbFactory = seedScope.ServiceProvider.GetRequiredService<IDbContextFactory<AutoNateDbContext>>();
            await using var db = await dbFactory.CreateDbContextAsync();

            await execProjection.ApplyAsync(new[]
            {
                new ChangeEvent<WorkflowExecutionSummary>(
                    ChangeOp.Upsert, "inst-analytics",
                    new WorkflowExecutionSummary
                    {
                        Id = "inst-analytics",
                        StartedAtUtc = DateTimeOffset.UtcNow.AddDays(-200),
                        Status = "Complete",
                        ProcessDefinitionId = "report:1:abc",
                        StartUserId = "jack"
                    },
                    DateTimeOffset.UtcNow)
            }, db, CancellationToken.None);

            // 5 old events (will end up in cold) + 2 fresh events (stay hot).
            var oldEvents = Enumerable.Range(0, 5).Select(i => new FlowableHistoricActivityEvent
            {
                ProcessInstanceId = "inst-analytics",
                ProcessDefinitionId = "report:1:abc",
                ActivityId = $"old-{i}",
                ActivityType = "serviceTask",
                StartTime = DateTimeOffset.UtcNow.AddDays(-90 - i)
            });
            var freshEvents = Enumerable.Range(0, 2).Select(i => new FlowableHistoricActivityEvent
            {
                ProcessInstanceId = "inst-analytics",
                ProcessDefinitionId = "report:1:abc",
                ActivityId = $"fresh-{i}",
                ActivityType = "userTask",
                StartTime = DateTimeOffset.UtcNow.AddMinutes(-30 - i)
            });

            await historyProjection.ApplyAsync(
                oldEvents.Concat(freshEvents)
                    .Select((e, i) => new ChangeEvent<FlowableHistoricActivityEvent>(
                        ChangeOp.Upsert, $"src-an-{i}", e, DateTimeOffset.UtcNow))
                    .ToArray(),
                db, CancellationToken.None);

            await archiver.RunOnceAsync(CancellationToken.None);
        }

        using var scope = factory.Services.CreateScope();
        var entity = scope.ServiceProvider.GetRequiredService<WorkflowAnalyticsQueryEntity>();
        var aql = new AqlQuery(
            Entity: "WorkflowAnalytics",
            Where: new AqlCompare("ProcessKey", "=", new AqlString("report")),
            OrderBy: Array.Empty<AqlOrderItem>(),
            Columns: new[]
            {
                new AqlSelectItem(Field: "ProcessKey", AggregateFn: null, AggregateField: null),
                new AqlSelectItem(Field: null, AggregateFn: "COUNT", AggregateField: null, Alias: "EventCount")
            },
            Group: new[] { "ProcessKey" },
            Limit: null);

        var prepared = await entity.PrepareAsync(aql, CancellationToken.None);
        var actor = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.Name, "test"),
        }, "test"));
        var result = await prepared.ExecuteAsync(actor, hardCap: 100, CancellationToken.None);

        Assert.Single(result.Rows);
        var row = result.Rows[0];
        Assert.Equal("report", (string?)row["ProcessKey"]);
        // 5 cold + 2 hot = 7 events all in the "report" bucket.
        Assert.Equal(7.0, (double?)row["EventCount"]);
    }
}
