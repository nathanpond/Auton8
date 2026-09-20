using System.Net;
using System.Net.Http.Json;
using AutoNate.Web.Models;
using AutoNate.Web.Persistence;
using AutoNate.Web.Persistence.Scaffolded;
using AutoNate.Web.Services.Flowable;
using AutoNate.Web.Services.Flowable.Cache;
using AutoNate.Web.Services.Projections;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AutoNate.Web.Tests;

/// <summary>
/// Completing a task is visible to the caller that did it (#604).
/// </summary>
/// <remarks>
/// <para>
/// <c>GET /api/executions/{id}/tasks</c> serves from <c>workflow_task_cache</c>
/// and nothing on the completion path wrote to it, so the finished task kept
/// being offered as current until a background pass caught up —
/// <c>TaskPollInterval</c> is a minute. The cost was 24 of 493 full-local E2E
/// specs, every one of them a spec that completes a task and waits for the next.
/// </para>
/// <para>
/// Those 24 are the acceptance evidence and they need a live engine. These run in
/// slim, over the real routes with the engine stubbed, so the regression has a
/// guard on the merge gate GitHub actually runs — which is the half that was
/// missing when this shipped.
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
public sealed class WorkflowTaskCacheFreshnessTests
{
    [Fact]
    public async Task Completing_a_task_shows_the_successor_in_the_very_next_read()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        (await client.GetAsync("/api/workflows/")).EnsureSuccessStatusCode();

        const string Instance = "inst-604";
        await SeedActiveTaskAsync(factory, "task-first", Instance);
        SeedInstance(factory, Instance);

        // What the engine reports AFTER the completion: the first task is gone
        // from the runtime and its successor is open. That is the state the cache
        // has to reach without waiting for a poll.
        factory.FlowableStub.TasksByProcess[Instance] =
        [
            new FlowableTaskSummary
            {
                Id = "task-second",
                Name = "Second",
                ProcessInstanceId = Instance,
                ProcessDefinitionId = "invoice:1:1",
                CreatedAtUtc = DateTimeOffset.UtcNow
            }
        ];

        var completed = await client.PostAsJsonAsync("/api/tasks/task-first/complete", new { });
        Assert.Equal(HttpStatusCode.NoContent, completed.StatusCode);

        // THE VERY NEXT READ, with no delay and no second poll. Before the fix
        // this returned `task-first` for up to a minute.
        var listed = await client.GetFromJsonAsync<List<TaskRow>>(
            $"/api/executions/{Instance}/tasks");

        Assert.NotNull(listed);
        Assert.Contains(listed!, t => t.Id == "task-second");

        // AND THE COMPLETED ONE IS GONE FROM IT. This is the load-bearing half,
        // measured rather than assumed: with the refresher removed, `task-second`
        // STILL APPEARS -- the read-through's first-seen coalesce projects the
        // instance's tasks on the way past -- so the assertion above passes
        // against the broken code. What never happens without the fix is the
        // finished task leaving the list, and a list still offering it is the
        // same stale id the caller would complete again, which is how this
        // arrived as a 500.
        Assert.DoesNotContain(listed!, t => t.Id == "task-first");

        var row = await ReadTaskAsync(factory, "task-first");
        Assert.Equal("completed", row.Status);
        Assert.NotNull(row.CompletedTime);
    }

    /// <summary>
    /// A task the engine has already finished is a 409, not a 500 (#604).
    /// </summary>
    /// <remarks>
    /// Flowable answers <c>404 Could not find a task with id '…'</c>, and uncaught
    /// that reached the caller as a 500 — which tells a polling client nothing it
    /// can act on. The route exists and the caller was entitled to it; what
    /// changed is the state underneath them, so 409 and "re-read the list".
    /// </remarks>
    [Fact]
    public async Task Completing_a_task_the_engine_has_finished_is_a_conflict_not_a_server_error()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        (await client.GetAsync("/api/workflows/")).EnsureSuccessStatusCode();

        await SeedActiveTaskAsync(factory, "task-stale", "inst-604b");

        factory.FlowableStub.CompleteThrows = new FlowableRequestException(
            HttpStatusCode.NotFound,
            "complete the user task",
            """{"message":"Not found","exception":"Could not find a task with id 'task-stale'."}""");

        var response = await client.PostAsJsonAsync("/api/tasks/task-stale/complete", new { });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("task-stale", body, StringComparison.Ordinal);

        // AND THE ENGINE'S OWN BODY NEVER REACHES THE CALLER (#350). The raw text
        // carries JDBC URLs, hostnames and Java frames; it goes to the log.
        Assert.DoesNotContain("Could not find a task with id", body, StringComparison.Ordinal);
    }

    private sealed record TaskRow(string Id, string Name);

    private static void SeedInstance(AutoNateWebApplicationFactory factory, string instanceId)
    {
        factory.FlowableStub.InstancesById[instanceId] = new FlowableProcessInstanceSummary
        {
            Id = instanceId,
            ProcessDefinitionId = "invoice:1:1"
        };
    }

    private static async Task SeedActiveTaskAsync(
        AutoNateWebApplicationFactory factory, string taskId, string instanceId)
    {
        using var scope = factory.Services.CreateScope();
        var projection = scope.ServiceProvider.GetRequiredService<FlowableTaskProjection>();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AutoNateDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();

        await projection.ApplyAsync(
            [new ChangeEvent<FlowableTaskSummary>(
                ChangeOp.Upsert, taskId,
                new FlowableTaskSummary
                {
                    Id = taskId,
                    Name = taskId,
                    ProcessInstanceId = instanceId,
                    ProcessDefinitionId = "invoice:1:1",
                    CreatedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10)
                },
                DateTimeOffset.UtcNow)],
            db, CancellationToken.None);
    }

    private static async Task<WorkflowTaskCache> ReadTaskAsync(
        AutoNateWebApplicationFactory factory, string taskId)
    {
        using var scope = factory.Services.CreateScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AutoNateDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.WorkflowTaskCache.AsNoTracking()
            .SingleAsync(t => t.FlowableTaskId == taskId);
    }
}
