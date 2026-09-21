using System.Net;
using System.Net.Http.Json;
using AutoNate.Web.Models;
using AutoNate.Web.Persistence;
using AutoNate.Web.Services.Flowable;
using AutoNate.Web.Services.Flowable.Cache;
using AutoNate.Web.Services.Workflow;
using AutoNate.Web.Services.Projections;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AutoNate.Web.Tests;

/// <summary>
/// A step that fails with no job behind it still reaches the execution (#222).
/// </summary>
/// <remarks>
/// <para>
/// <c>WorkflowExecutionErrorRecorder</c> listens for one event type,
/// <c>job.execution.failed</c>, and that is the only thing feeding the red node on
/// the diagram and <c>isErrored</c> in the history. A synchronous failure produces
/// no job, so it could never arrive there however long anyone waited.
/// </para>
/// <para>
/// The live proof is <c>ExecutionErrorSurfaceTests</c>, which drives a real
/// gateway condition that throws — and needs an engine, so GitHub never runs it.
/// This runs on every merge.
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
public sealed class SynchronousStepFailureTests
{
    /// <summary>
    /// The recorded failure carries no engine internals to /history (#626).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The sibling above asserts the engine's words are absent from the
    /// <c>/complete</c> RESPONSE. That is not where they leaked. The recorded row
    /// carried <c>exception.Message</c> — which
    /// <c>FlowableClient.EnsureSuccessAsync</c> builds as
    /// <c>"Flowable could not {op}. HTTP {code} {reason}. {rawResponseBody}"</c> —
    /// and <c>GET /api/executions/{id}/history</c> serves it to any caller with
    /// <c>WorkflowExecution:View</c>.
    /// </para>
    /// <para>
    /// So the assertion belongs on the endpoint that serves it, not the one that
    /// refuses. <c>NoEndpointReturnsARawEngineMessageTests</c> could not see it
    /// either: it scans for <c>.Message</c> used inside a
    /// <c>FlowableRequestException</c> catch block, and this use was in a helper
    /// two hops away, exfiltrating through a different route.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task The_recorded_failure_carries_no_engine_internals_to_the_history()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        (await client.GetAsync("/api/workflows/")).EnsureSuccessStatusCode();

        const string Instance = "inst-626";
        await SeedTaskAsync(factory, "task-626", Instance, "approve");

        // Shaped like a real Flowable 500: a stack frame, a container id and a
        // connection string with a password, which is what the #350 guard's own
        // docstring records that body having carried.
        factory.FlowableStub.CompleteThrows = new FlowableRequestException(
            HttpStatusCode.InternalServerError,
            "complete the user task",
            """{"exception":"org.flowable.common.engine.api.FlowableException: jdbc:postgresql://db:5432/AutoNate?user=autonate&password=hunter2 at org.flowable.engine.impl.bpmn.behavior.ExclusiveGatewayActivityBehavior.leave(ExclusiveGatewayActivityBehavior.java:82) [container 9f3c1a]"}""");

        var response = await client.PostAsJsonAsync("/api/tasks/task-626/complete", new { });
        Assert.False(response.IsSuccessStatusCode);

        var history = await client.GetStringAsync($"/api/executions/{Instance}/history");

        // The row exists -- otherwise the absences below are vacuous.
        Assert.Contains("\"isErrored\":true", history, StringComparison.Ordinal);

        // AND IT CARRIES NONE OF THE ENGINE'S INTERNALS.
        Assert.DoesNotContain("password=hunter2", history, StringComparison.Ordinal);
        Assert.DoesNotContain("jdbc:postgresql", history, StringComparison.Ordinal);
        Assert.DoesNotContain("org.flowable", history, StringComparison.Ordinal);
        Assert.DoesNotContain("container 9f3c1a", history, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_failed_completion_is_recorded_against_the_execution()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        (await client.GetAsync("/api/workflows/")).EnsureSuccessStatusCode();

        const string Instance = "inst-222";
        await SeedTaskAsync(factory, "task-222", Instance, "approve");

        factory.FlowableStub.CompleteThrows = new FlowableRequestException(
            HttpStatusCode.InternalServerError,
            "complete the user task",
            """{"message":"Internal server error","exception":"Unknown property used in expression: ${nosuchvar.length() > 1} ... activity 'split'"}""");

        var response = await client.PostAsJsonAsync("/api/tasks/task-222/complete", new { });

        Assert.False(response.IsSuccessStatusCode);

        // THE ENGINE'S TEXT IS NOT IN THE ANSWER. The route had no catch at all,
        // so what a caller saw depended on the environment rather than on us.
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Unknown property used in expression", body, StringComparison.Ordinal);
        Assert.DoesNotContain("nosuchvar", body, StringComparison.Ordinal);

        // AND THE EXECUTION SAYS SO. This is the row that could not exist before,
        // because there was no job for the recorder's one event type to hear.
        using var scope = factory.Services.CreateScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AutoNateDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();

        var row = await db.WorkflowExecutionErrors.AsNoTracking()
            .SingleOrDefaultAsync(e => e.ProcessInstanceId == Instance);

        Assert.NotNull(row);

        // Attributed to the activity the OPERATOR acted on, not one parsed out of
        // the engine's message. The message above contains `activity 'split'`
        // precisely so this test fails if anyone starts reading it: an author
        // controls the expression text, so a parsed id is an author's choice.
        Assert.Equal("approve", row!.ActivityId);
        Assert.Equal(WorkflowExecutionErrorRecorder.SynchronousEventType, row.RawFlowableEventType);
    }

    /// <summary>
    /// A task the engine no longer has is still a 409, not a recorded failure (#604, #222).
    /// </summary>
    /// <remarks>
    /// The complement. #604's stale-id path returns 409 and records nothing,
    /// because "someone else got there first" is not a failure of the process —
    /// and a catch-all that recorded it would put a red node on every execution
    /// two people touched at once.
    /// </remarks>
    [Fact]
    public async Task A_stale_task_id_is_not_recorded_as_an_execution_failure()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        (await client.GetAsync("/api/workflows/")).EnsureSuccessStatusCode();

        const string Instance = "inst-222b";
        await SeedTaskAsync(factory, "task-222b", Instance, "approve");

        factory.FlowableStub.CompleteThrows = new FlowableRequestException(
            HttpStatusCode.NotFound,
            "complete the user task",
            """{"message":"Not found","exception":"Could not find a task with id 'task-222b'."}""");

        var response = await client.PostAsJsonAsync("/api/tasks/task-222b/complete", new { });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        using var scope = factory.Services.CreateScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AutoNateDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();

        Assert.False(
            await db.WorkflowExecutionErrors.AnyAsync(e => e.ProcessInstanceId == Instance),
            "A stale task id is an ordinary race, not a failure of the process.");
    }

    private static async Task SeedTaskAsync(
        AutoNateWebApplicationFactory factory, string taskId, string instanceId, string definitionKey)
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
                    TaskDefinitionKey = definitionKey,
                    ProcessInstanceId = instanceId,
                    ProcessDefinitionId = "invoice:1:1",
                    CreatedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5)
                },
                DateTimeOffset.UtcNow)],
            db, CancellationToken.None);
    }
}
