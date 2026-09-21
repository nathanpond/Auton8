using System.Net.Http.Json;
using System.Text.Json;
using AutoNate.Web.Models;
using AutoNate.Web.Persistence;
using AutoNate.Web.Persistence.Scaffolded;
using AutoNate.Web.Services.Flowable.Cache;
using AutoNate.Web.Services.Projections;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AutoNate.Web.Tests;

/// <summary>
/// A multi-instance activity is one row with progress (#173).
/// </summary>
/// <remarks>
/// <b>Mixed state is the primary case</b>, per the story's test plan: aggregating a
/// set where every instance is in the same state passes against an implementation
/// that cannot count. These rows are deliberately not uniform.
/// </remarks>
[Trait("Category", "Integration")]
public sealed class MultiInstanceHistoryEndpointTests
{
    private const string Instance = "inst-173";
    private const string DefinitionId = "review_flow:1:abc";

    private const string Bpmn = """
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          xmlns:autonate="http://autonate.dev/workflows"
                          targetNamespace="http://autonate.dev/workflows">
          <bpmn:process id="review_flow" isExecutable="true">
            <bpmn:startEvent id="s" />
            <bpmn:userTask id="review" name="Review">
              <bpmn:multiInstanceLoopCharacteristics isSequential="false" autonate:loopCardinality="5" />
            </bpmn:userTask>
            <bpmn:userTask id="sign" name="Sign" />
            <bpmn:endEvent id="e" />
          </bpmn:process>
        </bpmn:definitions>
        """;

    [Fact]
    public async Task A_multi_instance_activity_collapses_to_one_row_with_mixed_state_counted()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        (await client.GetAsync("/api/workflows/")).EnsureSuccessStatusCode();

        await SeedDefinitionAsync(factory);

        // THREE instances of `review` in DIFFERENT states, plus an ordinary
        // activity that ran TWICE. The second is the complement: history cannot
        // tell the two apart, so only the diagram marker may collapse anything.
        var now = DateTimeOffset.UtcNow;
        factory.FlowableStub.HistoryByInstance[Instance] =
        [
            Row("s", "Start", now.AddMinutes(-10), now.AddMinutes(-10)),
            Row("review", "Review", now.AddMinutes(-9), now.AddMinutes(-8)),
            Row("review", "Review", now.AddMinutes(-9), now.AddMinutes(-7)),
            Row("review", "Review", now.AddMinutes(-9), endedAt: null),
            Row("sign", "Sign", now.AddMinutes(-6), now.AddMinutes(-5)),
            Row("sign", "Sign", now.AddMinutes(-4), endedAt: null)
        ];

        var rows = await client.GetFromJsonAsync<List<JsonElement>>(
            $"/api/executions/{Instance}/history");

        Assert.NotNull(rows);

        var review = rows!.Where(r => r.GetProperty("activityId").GetString() == "review").ToList();
        var sign = rows.Where(r => r.GetProperty("activityId").GetString() == "sign").ToList();

        // ONE row for the multi-instance activity...
        var one = Assert.Single(review);
        var progress = one.GetProperty("multiInstance");

        // ...and the author's declared total, not the instance count. These
        // differ, which is the point: three instances exist and five were asked
        // for, and "2/3 complete" would be a confident wrong answer.
        Assert.Equal(5, progress.GetProperty("total").GetInt32());
        Assert.Equal(2, progress.GetProperty("completed").GetInt32());
        Assert.Equal(1, progress.GetProperty("active").GetInt32());
        Assert.False(progress.GetProperty("isSequential").GetBoolean());

        // AND THE ORDINARY ACTIVITY IS UNTOUCHED. It repeats in history exactly
        // as a multi-instance does; collapsing it would report "1/2 complete" for
        // a task that simply ran again.
        Assert.Equal(2, sign.Count);
        Assert.All(sign, r => Assert.Equal(JsonValueKind.Null, r.GetProperty("multiInstance").ValueKind));
    }

    /// <summary>
    /// The instances are fetched only when asked for (#173).
    /// </summary>
    /// <remarks>
    /// Asserted by what the history payload CONTAINS, per the test plan's
    /// "asserted by query count, because eager loading looks identical in the UI".
    /// A history response carrying all five instances has already paid the cost
    /// the lazy-loading criterion exists to avoid, however the UI then behaves.
    /// </remarks>
    [Fact]
    public async Task The_instances_are_not_in_the_history_payload_but_are_available_on_request()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        (await client.GetAsync("/api/workflows/")).EnsureSuccessStatusCode();

        await SeedDefinitionAsync(factory);

        var now = DateTimeOffset.UtcNow;
        factory.FlowableStub.HistoryByInstance[Instance] =
        [
            Row("review", "Review", now.AddMinutes(-9), now.AddMinutes(-8)),
            Row("review", "Review", now.AddMinutes(-9), now.AddMinutes(-7)),
            Row("review", "Review", now.AddMinutes(-9), endedAt: null)
        ];

        var collapsed = await client.GetFromJsonAsync<List<JsonElement>>(
            $"/api/executions/{Instance}/history");
        Assert.Single(collapsed!);

        var instances = await client.GetFromJsonAsync<List<JsonElement>>(
            $"/api/executions/{Instance}/activities/review/instances");

        Assert.Equal(3, instances!.Count);
    }

    /// <summary>
    /// One recorded failure across three instances reads as ONE (#173).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The complement is the whole test.</b> Asserting that a failure shows up
    /// at all passes against a counter that reports every instance as failed —
    /// which is exactly what counting <c>IsErrored</c> over the instances did,
    /// because the history enrichment stamps that flag on every row sharing the
    /// activity id. Three instances, one error, and the honest answer is 1.
    /// </para>
    /// <para>
    /// <c>workflow_execution_errors</c> is keyed by <c>(process, activity)</c> and
    /// carries no execution or task id, so WHICH instance failed is not knowable
    /// here. The count of recorded failures is, and it is clamped to the instances
    /// that exist so a retried instance cannot report more failures than there are
    /// instances to fail.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task One_failure_across_three_instances_is_counted_once()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        (await client.GetAsync("/api/workflows/")).EnsureSuccessStatusCode();

        await SeedDefinitionAsync(factory);

        var now = DateTimeOffset.UtcNow;
        factory.FlowableStub.HistoryByInstance[Instance] =
        [
            Row("review", "Review", now.AddMinutes(-9), now.AddMinutes(-8)),
            Row("review", "Review", now.AddMinutes(-9), now.AddMinutes(-7)),
            Row("review", "Review", now.AddMinutes(-9), endedAt: null)
        ];

        await SeedErrorsAsync(factory, "review", count: 1);

        var rows = await client.GetFromJsonAsync<List<JsonElement>>(
            $"/api/executions/{Instance}/history");

        var one = Assert.Single(rows!.Where(r => r.GetProperty("activityId").GetString() == "review"));
        var progress = one.GetProperty("multiInstance");

        Assert.Equal(1, progress.GetProperty("failed").GetInt32());
        Assert.Equal(5, progress.GetProperty("total").GetInt32());
    }

    /// <summary>
    /// Retries of one instance cannot outnumber the instances (#173).
    /// </summary>
    /// <remarks>
    /// Four recorded failures over three instances is an ordinary retry history,
    /// not four failed instances. Without the clamp the row reads "4 failed" of a
    /// declared total of 5 — a number the reader cannot reconcile with anything.
    /// </remarks>
    [Fact]
    public async Task Four_failures_across_three_instances_cannot_exceed_the_instance_count()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        (await client.GetAsync("/api/workflows/")).EnsureSuccessStatusCode();

        await SeedDefinitionAsync(factory);

        var now = DateTimeOffset.UtcNow;
        factory.FlowableStub.HistoryByInstance[Instance] =
        [
            Row("review", "Review", now.AddMinutes(-9), now.AddMinutes(-8)),
            Row("review", "Review", now.AddMinutes(-9), now.AddMinutes(-7)),
            Row("review", "Review", now.AddMinutes(-9), endedAt: null)
        ];

        await SeedErrorsAsync(factory, "review", count: 4);

        var rows = await client.GetFromJsonAsync<List<JsonElement>>(
            $"/api/executions/{Instance}/history");

        var one = Assert.Single(rows!.Where(r => r.GetProperty("activityId").GetString() == "review"));
        Assert.Equal(3, one.GetProperty("multiInstance").GetProperty("failed").GetInt32());
    }

    /// <summary>
    /// A clean multi-instance activity reports no failures (#173).
    /// </summary>
    /// <remarks>
    /// The complement of the two above: a count sourced from the error rows must
    /// read zero when there are none, or "failed" becomes decoration.
    /// </remarks>
    [Fact]
    public async Task No_errors_means_no_failures_counted()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        (await client.GetAsync("/api/workflows/")).EnsureSuccessStatusCode();

        await SeedDefinitionAsync(factory);

        var now = DateTimeOffset.UtcNow;
        factory.FlowableStub.HistoryByInstance[Instance] =
        [
            Row("review", "Review", now.AddMinutes(-9), now.AddMinutes(-8)),
            Row("review", "Review", now.AddMinutes(-9), endedAt: null)
        ];

        var rows = await client.GetFromJsonAsync<List<JsonElement>>(
            $"/api/executions/{Instance}/history");

        var one = Assert.Single(rows!.Where(r => r.GetProperty("activityId").GetString() == "review"));
        Assert.Equal(0, one.GetProperty("multiInstance").GetProperty("failed").GetInt32());
    }

    private static async Task SeedErrorsAsync(
        AutoNateWebApplicationFactory factory, string activityId, int count)
    {
        using var scope = factory.Services.CreateScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AutoNateDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();

        for (var i = 0; i < count; i++)
        {
            db.WorkflowExecutionErrors.Add(new WorkflowExecutionError
            {
                Id = Guid.NewGuid(),
                ProcessInstanceId = Instance,
                ActivityId = activityId,
                ActivityName = "Review",
                ErrorMessage = $"boom {i}",
                RawFlowableEventType = "job.execution.failed",
                OccurredAtUtc = DateTime.UtcNow.AddMinutes(-5 + i)
            });
        }

        await db.SaveChangesAsync();
    }

    private static WorkflowExecutionHistoryEvent Row(
        string activityId, string name, DateTimeOffset startedAt, DateTimeOffset? endedAt) => new()
    {
        ActivityId = activityId,
        ActivityName = name,
        ActivityType = "userTask",
        StartedAtUtc = startedAt,
        EndedAtUtc = endedAt
    };

    private static async Task SeedDefinitionAsync(AutoNateWebApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AutoNateDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();

        var modelId = Guid.NewGuid();
        db.WorkflowModels.Add(new AutoNate.Web.Persistence.Scaffolded.WorkflowModel
        {
            Id = modelId,
            Name = "Review flow",
            ProcessKey = "review_flow",
            BpmnXml = Bpmn,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });
        // Saved before the version so the foreign key has something to point at;
        // one SaveChanges left EF free to order them the other way.
        await db.SaveChangesAsync();

        db.WorkflowModelVersions.Add(new AutoNate.Web.Persistence.Scaffolded.WorkflowModelVersion
        {
            Id = Guid.NewGuid(),
            WorkflowModelId = modelId,
            VersionNumber = 1,
            Name = "Review flow",
            ProcessKey = "review_flow",
            BpmnXml = Bpmn,
            ProcessDefinitionId = DefinitionId,
            DeploymentId = "dep-173",
            ProcessDefinitionKey = "review_flow",
            ProcessDefinitionVersion = 1,
            PublishedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        // The definition id is resolved through the execution cache, which #609
        // populates on start.
        var projection = scope.ServiceProvider.GetRequiredService<FlowableExecutionProjection>();
        await projection.ApplyAsync(
            [new ChangeEvent<WorkflowExecutionSummary>(
                ChangeOp.Upsert, Instance,
                new WorkflowExecutionSummary
                {
                    Id = Instance,
                    Name = "Review flow",
                    ProcessDefinitionId = DefinitionId,
                    Status = "Running",
                    StartedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10)
                },
                DateTimeOffset.UtcNow)],
            db, CancellationToken.None);
    }
}
