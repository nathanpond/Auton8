using System.Security.Claims;
using System.Text.Json;
using AutoNate.Web.Models;
using AutoNate.Web.Persistence;
using AutoNate.Web.Persistence.Scaffolded;
using AutoNate.Web.Services.Flowable;
using AutoNate.Web.Services.Flowable.Cache;
using AutoNate.Web.Services.Projections;
using AutoNate.Web.Services.Query;
using AutoNate.Web.Services.Query.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AutoNate.Web.Tests;

[Trait("Category", "Integration")]
public sealed class ProjectionVariableHistoryAndRetentionTests
{
    [Fact]
    public async Task Variable_projection_writes_snapshot_and_replaces_on_re_emit()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        _ = factory.CreateClient();

        using var scope = factory.Services.CreateScope();
        var projection = scope.ServiceProvider.GetRequiredService<FlowableVariableProjection>();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AutoNateDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();

        var first = new Dictionary<string, JsonElement>
        {
            ["amount"] = JsonDocument.Parse("1500").RootElement,
            ["approved"] = JsonDocument.Parse("true").RootElement,
            ["note"] = JsonDocument.Parse("\"first draft\"").RootElement,
        };
        await projection.ApplyAsync(new[]
        {
            new ChangeEvent<FlowableInstanceVariables>(
                ChangeOp.Upsert, "inst-vars-1",
                new FlowableInstanceVariables("inst-vars-1", first),
                DateTimeOffset.UtcNow)
        }, db, CancellationToken.None);

        var rows = await db.WorkflowVariableCache.AsNoTracking()
            .Where(v => v.FlowableInstanceId == "inst-vars-1")
            .OrderBy(v => v.Name)
            .ToListAsync();
        Assert.Equal(3, rows.Count);
        Assert.Equal(1500L, rows.Single(r => r.Name == "amount").ValueLong);
        Assert.True(rows.Single(r => r.Name == "approved").ValueBool);
        Assert.Equal("first draft", rows.Single(r => r.Name == "note").ValueText);

        // Re-emit with a smaller set — the "note" variable should disappear.
        var second = new Dictionary<string, JsonElement>
        {
            ["amount"] = JsonDocument.Parse("2000").RootElement,
            ["approved"] = JsonDocument.Parse("false").RootElement,
        };
        await projection.ApplyAsync(new[]
        {
            new ChangeEvent<FlowableInstanceVariables>(
                ChangeOp.Upsert, "inst-vars-1",
                new FlowableInstanceVariables("inst-vars-1", second),
                DateTimeOffset.UtcNow)
        }, db, CancellationToken.None);

        var after = await db.WorkflowVariableCache.AsNoTracking()
            .Where(v => v.FlowableInstanceId == "inst-vars-1")
            .OrderBy(v => v.Name)
            .ToListAsync();
        Assert.Equal(2, after.Count);
        Assert.Equal(2000L, after.Single(r => r.Name == "amount").ValueLong);
        Assert.False(after.Single(r => r.Name == "approved").ValueBool);
    }

    [Fact]
    public async Task History_projection_inserts_started_and_ended_rows_idempotently()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        _ = factory.CreateClient();

        using var scope = factory.Services.CreateScope();
        var execProjection = scope.ServiceProvider.GetRequiredService<FlowableExecutionProjection>();
        var historyProjection = scope.ServiceProvider.GetRequiredService<FlowableHistoryProjection>();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AutoNateDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();

        // History entity needs an execution row to resolve auth visibility.
        await execProjection.ApplyAsync(new[]
        {
            new ChangeEvent<WorkflowExecutionSummary>(
                ChangeOp.Upsert, "inst-history",
                new WorkflowExecutionSummary
                {
                    Id = "inst-history",
                    StartedAtUtc = DateTimeOffset.UtcNow.AddHours(-1),
                    Status = "Complete",
                    ProcessDefinitionId = "review:1:abc",
                    StartUserId = "dana"
                },
                DateTimeOffset.UtcNow)
        }, db, CancellationToken.None);

        var ev = new FlowableHistoricActivityEvent
        {
            ProcessInstanceId = "inst-history",
            ProcessDefinitionId = "review:1:abc",
            ActivityId = "approval-step",
            ActivityName = "Approve",
            ActivityType = "userTask",
            StartTime = DateTimeOffset.UtcNow.AddMinutes(-30),
            EndTime = DateTimeOffset.UtcNow.AddMinutes(-25),
            DurationMs = 5 * 60 * 1000,
            Assignee = "eve"
        };

        await historyProjection.ApplyAsync(new[]
        {
            new ChangeEvent<FlowableHistoricActivityEvent>(
                ChangeOp.Upsert, "src-1", ev, DateTimeOffset.UtcNow)
        }, db, CancellationToken.None);

        var rows = await db.WorkflowEventLogCache.AsNoTracking()
            .Where(e => e.FlowableInstanceId == "inst-history")
            .OrderBy(e => e.EventTime)
            .ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.Equal("activity_started", rows[0].EventType);
        Assert.Equal("activity_ended", rows[1].EventType);
        Assert.Equal(5L * 60 * 1000, rows[1].DurationMs);

        // Re-applying the same source event must not create duplicates
        // (ON CONFLICT DO NOTHING keeps the original row).
        await historyProjection.ApplyAsync(new[]
        {
            new ChangeEvent<FlowableHistoricActivityEvent>(
                ChangeOp.Upsert, "src-1", ev, DateTimeOffset.UtcNow)
        }, db, CancellationToken.None);
        var afterReplay = await db.WorkflowEventLogCache.AsNoTracking()
            .Where(e => e.FlowableInstanceId == "inst-history")
            .CountAsync();
        Assert.Equal(2, afterReplay);
    }

    [Fact]
    public async Task History_AQL_entity_supports_BETWEEN_time_range()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        _ = factory.CreateClient();

        var now = DateTime.UtcNow;
        using (var seedScope = factory.Services.CreateScope())
        {
            var execProjection = seedScope.ServiceProvider.GetRequiredService<FlowableExecutionProjection>();
            var historyProjection = seedScope.ServiceProvider.GetRequiredService<FlowableHistoryProjection>();
            var dbFactory = seedScope.ServiceProvider.GetRequiredService<IDbContextFactory<AutoNateDbContext>>();
            await using var db = await dbFactory.CreateDbContextAsync();

            await execProjection.ApplyAsync(new[]
            {
                new ChangeEvent<WorkflowExecutionSummary>(
                    ChangeOp.Upsert, "inst-ts",
                    new WorkflowExecutionSummary
                    {
                        Id = "inst-ts",
                        StartedAtUtc = DateTimeOffset.UtcNow.AddHours(-12),
                        Status = "Running",
                        ProcessDefinitionId = "audit:1:abc",
                        StartUserId = "frank"
                    },
                    DateTimeOffset.UtcNow)
            }, db, CancellationToken.None);

            // Three events spaced 1 hour apart — only the middle one should
            // fall inside a "between 3h ago and 1h ago" window.
            var events = new[] { -5, -2, -0.5 }.Select((hours, i) => new FlowableHistoricActivityEvent
            {
                ProcessInstanceId = "inst-ts",
                ProcessDefinitionId = "audit:1:abc",
                ActivityId = $"activity-{i}",
                ActivityName = $"Step {i}",
                ActivityType = "serviceTask",
                StartTime = DateTimeOffset.UtcNow.AddHours(hours),
                EndTime = null
            }).ToArray();

            await historyProjection.ApplyAsync(
                events.Select((e, i) => new ChangeEvent<FlowableHistoricActivityEvent>(
                    ChangeOp.Upsert, $"src-{i}", e, DateTimeOffset.UtcNow)).ToArray(),
                db, CancellationToken.None);
        }

        using var scope = factory.Services.CreateScope();
        var entity = scope.ServiceProvider.GetRequiredService<WorkflowHistoryQueryEntity>();
        // The lo/hi are resolved at AqlRelativeDate.Resolve() time inside the
        // entity — no local variables needed at the test boundary. Keeping a
        // reference to `now` for the assertion narrative below.
        _ = now;
        var aql = new AqlQuery(
            Entity: "WorkflowHistory",
            Where: new AqlBetween("EventTime",
                new AqlRelativeDate(-3, 'h'),
                new AqlRelativeDate(-1, 'h')),
            OrderBy: Array.Empty<AqlOrderItem>(),
            Columns: null,
            Group: null,
            Limit: null);

        var prepared = await entity.PrepareAsync(aql, CancellationToken.None);
        var actor = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.Name, "test"),
        }, "test"));
        var result = await prepared.ExecuteAsync(actor, hardCap: 100, CancellationToken.None);

        Assert.Single(result.Rows);
        Assert.Equal("activity-1", (string?)result.Rows[0]["ActivityId"]);
    }

    [Fact]
    public async Task ReadThrough_falls_back_to_live_Flowable_on_cache_miss()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        _ = factory.CreateClient();

        // Pre-seed the stub so the live fetch on cache miss returns something.
        factory.FlowableStub.InstancesById["live-only"] = new FlowableProcessInstanceSummary
        {
            Id = "live-only",
            Name = "fresh run",
            ProcessDefinitionId = "ad-hoc:1:xyz",
            Suspended = false,
            StartUserId = "grace"
        };

        using var scope = factory.Services.CreateScope();
        var readThrough = scope.ServiceProvider.GetRequiredService<IFlowableReadThrough>();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AutoNateDbContext>>();

        // Cache is empty for this id — read-through must hit Flowable and
        // write the row back so subsequent reads are cache hits.
        var row = await readThrough.GetInstanceAsync("live-only");
        Assert.NotNull(row);
        Assert.Equal("ad-hoc", row!.ProcessDefinitionKey);
        Assert.Equal("grace", row.StartedBy);

        await using var db = await dbFactory.CreateDbContextAsync();
        var cached = await db.WorkflowExecutionCache.AsNoTracking()
            .FirstOrDefaultAsync(c => c.FlowableInstanceId == "live-only");
        Assert.NotNull(cached);
    }

    [Fact]
    public async Task Retention_janitor_drops_executions_past_their_retention()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(
            extraConfig: new Dictionary<string, string?>
            {
                // 7-day default keeps the test arithmetic simple.
                ["FlowableCache:DefaultRetentionDays"] = "7"
            });
        _ = factory.CreateClient();

        using var scope = factory.Services.CreateScope();
        var execProjection = scope.ServiceProvider.GetRequiredService<FlowableExecutionProjection>();
        var janitor = scope.ServiceProvider.GetRequiredService<WorkflowCacheRetentionService>();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AutoNateDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();

        await execProjection.ApplyAsync(new[]
        {
            new ChangeEvent<WorkflowExecutionSummary>(
                ChangeOp.Upsert, "old-inst",
                new WorkflowExecutionSummary
                {
                    Id = "old-inst",
                    StartedAtUtc = DateTimeOffset.UtcNow.AddDays(-30),
                    Status = "Complete",
                    ProcessDefinitionId = "retention-test:1:abc",
                    StartUserId = "henry"
                },
                DateTimeOffset.UtcNow),
            new ChangeEvent<WorkflowExecutionSummary>(
                ChangeOp.Upsert, "new-inst",
                new WorkflowExecutionSummary
                {
                    Id = "new-inst",
                    StartedAtUtc = DateTimeOffset.UtcNow.AddDays(-1),
                    Status = "Complete",
                    ProcessDefinitionId = "retention-test:1:abc",
                    StartUserId = "henry"
                },
                DateTimeOffset.UtcNow),
        }, db, CancellationToken.None);

        var report = await janitor.RunOnceAsync(CancellationToken.None);
        Assert.Equal(1, report.ExecutionsDeleted);

        var remaining = await db.WorkflowExecutionCache.AsNoTracking()
            .Where(c => c.ProcessDefinitionKey == "retention-test")
            .Select(c => c.FlowableInstanceId)
            .ToListAsync();
        Assert.Single(remaining);
        Assert.Equal("new-inst", remaining[0]);
    }
}
