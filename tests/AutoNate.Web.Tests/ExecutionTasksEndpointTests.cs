using System.Net;
using System.Net.Http.Json;
using AutoNate.Web.Models;
using AutoNate.Web.Persistence;
using AutoNate.Web.Services.Flowable.Cache;
using AutoNate.Web.Services.Projections;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AutoNate.Web.Tests;

/// <summary>
/// What `/{id}/tasks` answers, and where the answer comes from (#104, #604).
/// </summary>
/// <remarks>
/// <para>
/// <b>RENAMED from <c>ExecutionTasksFromCacheTests</c> by #604</b>, rather than
/// left with a name that had stopped being true. #104 moved this route onto the
/// cache — possible only once the cache had a `name` column for
/// `ProcessInstanceName` (#583) and had learned that a task finishes (#586).
/// </para>
/// <para>
/// #604 then measured what that cost: the cache is a minute stale, and this is
/// the one view a caller watches for change — a timer firing, a boundary event,
/// an ad-hoc activity starting, the successor to a task they just completed. It
/// was 24 of 493 full-local specs, and a person completing a task in the UI
/// seeing it still listed for up to a minute. So the route now READS THROUGH on
/// every call and the cache supplies the shape, which is why the old name would
/// mislead the next reader rather than merely age.
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
public sealed class ExecutionTasksEndpointTests
{
    [Fact]
    public async Task It_serves_open_tasks_from_the_cache_with_the_instance_name()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();

        await SeedAsync(factory, "inst-1", instanceName: "Invoice for Acme", taskIds: ["task-a", "task-b"]);

        // What the engine says is open right now. Under #604 this is the
        // authority for WHICH tasks are returned; the cache still supplies what
        // each one looks like.
        factory.FlowableStub.TasksByProcess["inst-1"] =
        [
            new FlowableTaskSummary { Id = "task-a", Name = "task-a", ProcessInstanceId = "inst-1", ProcessDefinitionId = "invoice:1:1" },
            new FlowableTaskSummary { Id = "task-b", Name = "task-b", ProcessInstanceId = "inst-1", ProcessDefinitionId = "invoice:1:1" }
        ];

        var tasks = await client.GetFromJsonAsync<FlowableTaskSummary[]>("/api/executions/inst-1/tasks");

        Assert.NotNull(tasks);
        Assert.Equal(["task-a", "task-b"], tasks!.Select(t => t.Id));

        // The CACHE is still what supplies the instance name -- the live task
        // list carries none, which is why #583 had to land before #104 and is
        // still the reason both sources are in play.
        Assert.All(tasks, t => Assert.Equal("Invoice for Acme", t.ProcessInstanceName));
        Assert.All(tasks, t => Assert.Equal("inst-1", t.ProcessInstanceId));

        // It DID ask Flowable. This assertion was inverted by #604 rather than
        // deleted: "served without asking the engine" was #104's guarantee and is
        // now precisely the defect, so the inversion is the change, visible here.
        Assert.Contains(
            factory.FlowableStub.Calls,
            c => c.StartsWith("TasksByInstance:inst-1", StringComparison.Ordinal));
    }

    /// <summary>
    /// A completed task is not returned (#104, assertable because of #586).
    /// </summary>
    /// <remarks>
    /// <para>The complement, and the reason this route could not move earlier. The
    /// live endpoint queries `service/runtime/tasks`, which is open tasks only.
    /// Served from a cache that never learned about completion, this route would
    /// have returned the instance's entire task history as though all of it were
    /// outstanding — a worse answer than the one it replaced.</para>
    /// </remarks>
    [Fact]
    public async Task A_completed_task_is_not_returned()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();

        await SeedAsync(factory, "inst-2", instanceName: "Invoice for Beta", taskIds: ["task-done", "task-open"]);

        // Flowable's history says the first one finished; the sweep marks it.
        factory.FlowableStub.FinishedTasks.Add(new FlowableFinishedTask
        {
            Id = "task-done", EndedAtUtc = DateTimeOffset.UtcNow, ProcessInstanceId = "inst-2"
        });
        using (var scope = factory.Services.CreateScope())
        {
            var sweep = scope.ServiceProvider.GetRequiredService<WorkflowTaskCompletionSweep>();
            Assert.Equal(1, await sweep.RunOnceAsync(CancellationToken.None));
        }

        // The engine lists only the open one, which is what `runtime/tasks` means.
        factory.FlowableStub.TasksByProcess["inst-2"] =
        [
            new FlowableTaskSummary { Id = "task-open", Name = "task-open", ProcessInstanceId = "inst-2", ProcessDefinitionId = "invoice:1:1" }
        ];

        var tasks = await client.GetFromJsonAsync<FlowableTaskSummary[]>("/api/executions/inst-2/tasks");

        Assert.NotNull(tasks);
        Assert.Equal(["task-open"], tasks!.Select(t => t.Id));
        Assert.DoesNotContain(tasks, t => t.Id == "task-done");
    }

    /// <summary>
    /// With Flowable unreachable the route still serves (#104).
    /// </summary>
    /// <remarks>
    /// The read-through returns the cached row when the live call throws, so the
    /// engine being down is no longer a read outage for this page. The row is aged
    /// past `ReadThroughFreshness` first, so the live attempt genuinely happens —
    /// a fresh row would be served from cache without ever reaching the stub, and
    /// the test would prove nothing.
    /// </remarks>
    [Fact]
    public async Task It_still_serves_with_Flowable_unreachable()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();

        await SeedAsync(factory, "inst-3", instanceName: "Invoice for Gamma", taskIds: ["task-x"], stale: true);
        factory.FlowableStub.GetProcessInstanceThrows = new HttpRequestException("connection refused");

        // BOTH calls, because #604 made this route ask the engine for the task
        // list too. Left answering normally, the stub would be modelling an
        // engine that is UP and reports no tasks -- and this test would pass or
        // fail for a reason that has nothing to do with the engine being down.
        factory.FlowableStub.GetTasksByProcessInstanceThrows =
            new HttpRequestException("connection refused");

        var response = await client.GetAsync("/api/executions/inst-3/tasks");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var tasks = await response.Content.ReadFromJsonAsync<FlowableTaskSummary[]>();
        Assert.Equal(["task-x"], tasks!.Select(t => t.Id));
    }

    [Fact]
    public async Task An_unknown_instance_is_not_found()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/executions/never-heard-of-it/tasks");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---- helpers ----

    private static async Task SeedAsync(
        AutoNateWebApplicationFactory factory,
        string instanceId,
        string instanceName,
        string[] taskIds,
        bool stale = false)
    {
        using var scope = factory.Services.CreateScope();
        var executions = scope.ServiceProvider.GetRequiredService<FlowableExecutionProjection>();
        var tasks = scope.ServiceProvider.GetRequiredService<FlowableTaskProjection>();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AutoNateDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();

        await executions.ApplyAsync(
            [new ChangeEvent<WorkflowExecutionSummary>(
                ChangeOp.Upsert, instanceId,
                new WorkflowExecutionSummary
                {
                    Id = instanceId,
                    Name = instanceName,
                    ProcessDefinitionId = "invoice:1:1",
                    Status = "running",
                    StartUserId = "alice",
                    StartedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10),
                    LastActivityAtUtc = DateTimeOffset.UtcNow
                },
                DateTimeOffset.UtcNow)],
            db, CancellationToken.None);

        for (var i = 0; i < taskIds.Length; i++)
        {
            await tasks.ApplyAsync(
                [new ChangeEvent<FlowableTaskSummary>(
                    ChangeOp.Upsert, taskIds[i],
                    new FlowableTaskSummary
                    {
                        Id = taskIds[i],
                        Name = taskIds[i],
                        ProcessInstanceId = instanceId,
                        ProcessDefinitionId = "invoice:1:1",
                        CreatedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10 + i)
                    },
                    DateTimeOffset.UtcNow)],
                db, CancellationToken.None);
        }

        if (stale)
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE workflow_execution_cache SET last_sync_at = NOW() - INTERVAL '1 hour'
                WHERE flowable_instance_id = {instanceId}
                """);
        }
    }
}
