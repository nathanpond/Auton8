using System.Security.Claims;
using AutoNate.Web.Models;
using AutoNate.Web.Persistence;
using AutoNate.Web.Services.Flowable.Cache;
using AutoNate.Web.Services.Projections;
using AutoNate.Web.Services.Query;
using AutoNate.Web.Services.Query.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AutoNate.Web.Tests;

[Trait("Category", "Integration")]
public sealed class ProjectionFrameworkTests
{
    [Fact]
    public async Task Projection_applies_change_event_to_cache_table()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        _ = factory.CreateClient();

        using var scope = factory.Services.CreateScope();
        var projection = scope.ServiceProvider.GetRequiredService<FlowableExecutionProjection>();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AutoNateDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();

        var change = new ChangeEvent<WorkflowExecutionSummary>(
            ChangeOp.Upsert,
            "instance-1",
            new WorkflowExecutionSummary
            {
                Id = "instance-1",
                Name = "test run",
                StartedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5),
                LastActivityAtUtc = DateTimeOffset.UtcNow,
                Status = "Running",
                ProcessDefinitionId = "approval:3:abc",
                StartUserId = "user-42"
            },
            DateTimeOffset.UtcNow);

        await projection.ApplyAsync(new[] { change }, db, CancellationToken.None);

        var row = await db.WorkflowExecutionCache
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.FlowableInstanceId == "instance-1");

        Assert.NotNull(row);
        Assert.Equal("approval", row!.ProcessDefinitionKey);
        Assert.Equal(3, row.ProcessDefinitionVersion);
        Assert.Equal("active", row.Status);
        Assert.Equal("user-42", row.StartedBy);
        // Postgres jsonb re-serializes with spaces after `:` so match on the
        // key+value substring with whitespace tolerance.
        Assert.Contains("\"startedby\"", row.AuthTagsJson);
        Assert.Contains("user-42", row.AuthTagsJson);
    }

    [Fact]
    public async Task WorkflowExecutions_AQL_entity_returns_cached_rows()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        _ = factory.CreateClient();

        // Seed the cache through the projection so we're testing the read
        // path the AQL entity actually uses, not just the table directly.
        using (var seedScope = factory.Services.CreateScope())
        {
            var projection = seedScope.ServiceProvider.GetRequiredService<FlowableExecutionProjection>();
            var dbFactory = seedScope.ServiceProvider.GetRequiredService<IDbContextFactory<AutoNateDbContext>>();
            await using var db = await dbFactory.CreateDbContextAsync();
            await projection.ApplyAsync(new[]
            {
                new ChangeEvent<WorkflowExecutionSummary>(
                    ChangeOp.Upsert, "inst-a",
                    new WorkflowExecutionSummary
                    {
                        Id = "inst-a",
                        StartedAtUtc = DateTimeOffset.UtcNow.AddHours(-1),
                        Status = "Running",
                        ProcessDefinitionId = "purchase:1:def",
                        StartUserId = "alice"
                    },
                    DateTimeOffset.UtcNow),
                new ChangeEvent<WorkflowExecutionSummary>(
                    ChangeOp.Upsert, "inst-b",
                    new WorkflowExecutionSummary
                    {
                        Id = "inst-b",
                        StartedAtUtc = DateTimeOffset.UtcNow.AddHours(-2),
                        Status = "Complete",
                        ProcessDefinitionId = "purchase:1:def",
                        StartUserId = "bob"
                    },
                    DateTimeOffset.UtcNow),
            }, db, CancellationToken.None);
        }

        using var scope = factory.Services.CreateScope();
        var entity = scope.ServiceProvider.GetRequiredService<WorkflowExecutionsQueryEntity>();
        var aql = new AqlQuery(
            Entity: "WorkflowExecutions",
            Where: new AqlCompare("ProcessKey", "=", new AqlString("purchase")),
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

        Assert.Equal(2, result.Rows.Count);
        Assert.Contains(result.Rows, r => (string?)r["Id"] == "inst-a");
        Assert.Contains(result.Rows, r => (string?)r["Id"] == "inst-b");
    }

    [Fact]
    public async Task WorkflowModels_NUMEXECUTIONS_resolves_against_cache()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        _ = factory.CreateClient();

        // Seed a workflow_models row and three execution-cache rows so the
        // WHERE NUMEXECUTIONS() > 2 predicate has a row to evaluate against.
        var modelId = Guid.NewGuid();
        using (var seedScope = factory.Services.CreateScope())
        {
            var dbFactory = seedScope.ServiceProvider.GetRequiredService<IDbContextFactory<AutoNateDbContext>>();
            await using var db = await dbFactory.CreateDbContextAsync();

            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO workflow_models (
                    id, name, process_key, bpmn_xml, is_draft,
                    draft_version_number, published_version_number,
                    created_at_utc, updated_at_utc)
                VALUES ({modelId}, 'invoice approval', 'invoice', '<bpmn/>', false,
                        1, 1, NOW(), NOW())
                """);

            var projection = seedScope.ServiceProvider.GetRequiredService<FlowableExecutionProjection>();
            var execs = Enumerable.Range(0, 3).Select(i =>
                new ChangeEvent<WorkflowExecutionSummary>(
                    ChangeOp.Upsert, $"inv-{i}",
                    new WorkflowExecutionSummary
                    {
                        Id = $"inv-{i}",
                        StartedAtUtc = DateTimeOffset.UtcNow.AddHours(-i),
                        Status = "Running",
                        ProcessDefinitionId = "invoice:1:xyz",
                        StartUserId = "carol"
                    },
                    DateTimeOffset.UtcNow)
            ).ToArray();
            await projection.ApplyAsync(execs, db, CancellationToken.None);
        }

        using var scope = factory.Services.CreateScope();
        var entity = scope.ServiceProvider.GetRequiredService<WorkflowModelsQueryEntity>();
        var aql = new AqlQuery(
            Entity: "Workflows",
            Where: new AqlFunctionCompare("NUMEXECUTIONS", Array.Empty<AqlValue>(), ">", new AqlNumber(2)),
            OrderBy: Array.Empty<AqlOrderItem>(),
            Columns: null,
            Group: null,
            Limit: null);

        var prepared = await entity.PrepareAsync(aql, CancellationToken.None);
        Assert.Empty(prepared.ValidationErrors);

        var actor = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.Name, "test"),
        }, "test"));
        var result = await prepared.ExecuteAsync(actor, hardCap: 100, CancellationToken.None);

        Assert.Single(result.Rows);
        Assert.Equal("invoice approval", (string?)result.Rows[0]["ModelName"]);
    }
}
