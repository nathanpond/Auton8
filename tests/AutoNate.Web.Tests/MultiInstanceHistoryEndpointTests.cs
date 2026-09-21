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

    private const string SequentialBpmn = """
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          xmlns:flowable="http://flowable.org/bpmn"
                          xmlns:autonate="http://autonate.dev/workflows"
                          targetNamespace="http://autonate.dev/workflows">
          <bpmn:process id="review_flow" isExecutable="true">
            <bpmn:userTask id="review" name="Review">
              <bpmn:multiInstanceLoopCharacteristics isSequential="true"
                  flowable:collection="reviewers" flowable:elementVariable="reviewer" />
            </bpmn:userTask>
          </bpmn:process>
        </bpmn:definitions>
        """;

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

    /// <summary>
    /// A sequential loop in flight reports the engine's total, not its rows (#173).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is the case the row count cannot answer, measured on Flowable
    /// 8.0.0.</b> A sequential multi-instance creates ONE instance at a time, so a
    /// loop over five reviewers with the first task open writes exactly one
    /// historic activity row. Counting rows reports "1 of 1" — a finished-looking
    /// activity that has four runs still to go, and the AC asking for "how many
    /// remain" is unanswerable.
    /// </para>
    /// <para>
    /// The engine knows: <c>nrOfInstances</c> is 5 on the multi-instance container
    /// execution. The complement is the row below — with no engine state, the same
    /// request falls back to the rows — so this pair fails in opposite directions
    /// if the precedence is ever inverted.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_sequential_loop_in_flight_reports_the_engine_total()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        (await client.GetAsync("/api/workflows/")).EnsureSuccessStatusCode();

        await SeedDefinitionAsync(factory, SequentialBpmn);

        var now = DateTimeOffset.UtcNow;
        factory.FlowableStub.HistoryByInstance[Instance] =
        [
            Row("review", "Review", now.AddMinutes(-9), endedAt: null)
        ];

        factory.FlowableStub.MultiInstanceStateByInstance[Instance] = new MultiInstanceEngineState(
            new Dictionary<string, MultiInstanceCounts>(StringComparer.Ordinal)
            {
                ["review"] = new MultiInstanceCounts(Total: 5, Completed: 0, Active: 1)
            },
            new Dictionary<string, IReadOnlyDictionary<string, string?>>(StringComparer.Ordinal));

        var rows = await client.GetFromJsonAsync<List<JsonElement>>(
            $"/api/executions/{Instance}/history");

        var one = Assert.Single(rows!.Where(r => r.GetProperty("activityId").GetString() == "review"));
        var progress = one.GetProperty("multiInstance");

        Assert.Equal(5, progress.GetProperty("total").GetInt32());
        Assert.Equal(1, progress.GetProperty("active").GetInt32());
        Assert.True(progress.GetProperty("isSequential").GetBoolean());
    }

    /// <summary>
    /// With no engine state, the rows are the total (#173).
    /// </summary>
    /// <remarks>
    /// The complement of the test above, and the shape a FINISHED process really
    /// returns: the runtime tree is gone, so the counters cannot be attributed to
    /// an activity. Counting rows is exact by then — every instance the loop will
    /// ever create has one. Without this row, "always prefer the engine" would
    /// pass the test above while reporting zero for every completed activity.
    /// </remarks>
    [Fact]
    public async Task With_no_engine_state_the_historic_rows_are_the_total()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        (await client.GetAsync("/api/workflows/")).EnsureSuccessStatusCode();

        await SeedDefinitionAsync(factory, SequentialBpmn);

        var now = DateTimeOffset.UtcNow;
        factory.FlowableStub.HistoryByInstance[Instance] =
        [
            Row("review", "Review", now.AddMinutes(-9), now.AddMinutes(-8)),
            Row("review", "Review", now.AddMinutes(-8), now.AddMinutes(-7)),
            Row("review", "Review", now.AddMinutes(-7), now.AddMinutes(-6))
        ];

        var rows = await client.GetFromJsonAsync<List<JsonElement>>(
            $"/api/executions/{Instance}/history");

        var one = Assert.Single(rows!.Where(r => r.GetProperty("activityId").GetString() == "review"));
        var progress = one.GetProperty("multiInstance");

        Assert.Equal(3, progress.GetProperty("total").GetInt32());
        Assert.Equal(3, progress.GetProperty("completed").GetInt32());
        Assert.Equal(0, progress.GetProperty("active").GetInt32());
    }

    /// <summary>
    /// Each expanded instance says which collection item it has (#173).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The AC is "its element value from the collection so an operator can tell
    /// which item it is". Flowable scopes that variable to each instance's own
    /// execution, so the join is by execution id — asserted with DIFFERENT values
    /// per instance, because a single shared value passes against an
    /// implementation that reads the process-level collection and hands the same
    /// item to everyone.
    /// </para>
    /// <para>
    /// The complement is the third instance: its execution has no such variable,
    /// and it must come back null rather than borrowing a neighbour's.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Each_expanded_instance_carries_its_own_collection_item()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        (await client.GetAsync("/api/workflows/")).EnsureSuccessStatusCode();

        await SeedDefinitionAsync(factory, SequentialBpmn);

        var now = DateTimeOffset.UtcNow;
        factory.FlowableStub.HistoryByInstance[Instance] =
        [
            Row("review", "Review", now.AddMinutes(-9), now.AddMinutes(-8), executionId: "exec-1"),
            Row("review", "Review", now.AddMinutes(-8), endedAt: null, executionId: "exec-2"),
            Row("review", "Review", now.AddMinutes(-7), endedAt: null, executionId: "exec-3")
        ];

        factory.FlowableStub.MultiInstanceStateByInstance[Instance] = new MultiInstanceEngineState(
            new Dictionary<string, MultiInstanceCounts>(StringComparer.Ordinal),
            new Dictionary<string, IReadOnlyDictionary<string, string?>>(StringComparer.Ordinal)
            {
                ["exec-1"] = new Dictionary<string, string?>(StringComparer.Ordinal) { ["reviewer"] = "alice" },
                ["exec-2"] = new Dictionary<string, string?>(StringComparer.Ordinal) { ["reviewer"] = "bob" }
            });

        var instances = await client.GetFromJsonAsync<List<JsonElement>>(
            $"/api/executions/{Instance}/activities/review/instances");

        Assert.Equal(3, instances!.Count);
        Assert.Equal("alice", instances[0].GetProperty("elementValue").GetString());
        Assert.Equal("bob", instances[1].GetProperty("elementValue").GetString());
        Assert.Equal(JsonValueKind.Null, instances[2].GetProperty("elementValue").ValueKind);
    }

    /// <summary>
    /// The collapsed history never carries the element values (#173).
    /// </summary>
    /// <remarks>
    /// The lazy-loading criterion, asserted on the payload rather than on the UI.
    /// Enriching the collapsed row would be invisible in the browser and would
    /// pay the per-instance cost on every history read — which is the regression
    /// #108's paging work exists to prevent.
    /// </remarks>
    [Fact]
    public async Task The_collapsed_history_does_not_pay_for_element_values()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        (await client.GetAsync("/api/workflows/")).EnsureSuccessStatusCode();

        await SeedDefinitionAsync(factory, SequentialBpmn);

        var now = DateTimeOffset.UtcNow;
        factory.FlowableStub.HistoryByInstance[Instance] =
        [
            Row("review", "Review", now.AddMinutes(-9), now.AddMinutes(-8), executionId: "exec-1"),
            Row("review", "Review", now.AddMinutes(-8), endedAt: null, executionId: "exec-2")
        ];

        factory.FlowableStub.MultiInstanceStateByInstance[Instance] = new MultiInstanceEngineState(
            new Dictionary<string, MultiInstanceCounts>(StringComparer.Ordinal),
            new Dictionary<string, IReadOnlyDictionary<string, string?>>(StringComparer.Ordinal)
            {
                ["exec-1"] = new Dictionary<string, string?>(StringComparer.Ordinal) { ["reviewer"] = "alice" }
            });

        var rows = await client.GetFromJsonAsync<List<JsonElement>>(
            $"/api/executions/{Instance}/history");

        var one = Assert.Single(rows!.Where(r => r.GetProperty("activityId").GetString() == "review"));
        Assert.Equal(JsonValueKind.Null, one.GetProperty("elementValue").ValueKind);
    }

    /// <summary>
    /// The collapse survives a cache miss by reading through (#627).
    /// </summary>
    /// <remarks>
    /// <para>
    /// #173's AC says "where the cache cannot answer, it reads through". It did
    /// not: both the collapse and the element-value lookup issued a bare
    /// <c>WorkflowExecutionCache</c> query and returned the rows UNCOLLAPSED on a
    /// miss — so opening a history in the window before the projection lands a
    /// row, or after a rebuild or eviction, rendered a 50-instance activity as 50
    /// rows with no progress and no error. Silent degradation to precisely the
    /// behaviour the story exists to remove.
    /// </para>
    /// <para>
    /// Here the execution-cache row is deleted while the ENGINE still knows the
    /// instance, which is the shape of a miss. The read-through repopulates it and
    /// the collapse happens anyway.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_cache_miss_still_collapses_because_the_read_through_answers()
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

        // The engine still knows it -- this is a cache MISS, not a deletion.
        factory.FlowableStub.InstancesById[Instance] = new FlowableProcessInstanceSummary
        {
            Id = Instance,
            ProcessDefinitionId = DefinitionId,
            Name = "Review flow"
        };

        using (var scope = factory.Services.CreateScope())
        {
            var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AutoNateDbContext>>();
            await using var db = await dbFactory.CreateDbContextAsync();
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                DELETE FROM workflow_execution_cache WHERE flowable_instance_id = {Instance}
                """);
        }

        var rows = await client.GetFromJsonAsync<List<JsonElement>>(
            $"/api/executions/{Instance}/history");

        // ONE row, with progress. Before the fix this came back as three.
        var one = Assert.Single(rows!.Where(r => r.GetProperty("activityId").GetString() == "review"));
        Assert.Equal(JsonValueKind.Object, one.GetProperty("multiInstance").ValueKind);
    }

    private static WorkflowExecutionHistoryEvent Row(
        string activityId,
        string name,
        DateTimeOffset startedAt,
        DateTimeOffset? endedAt,
        string? executionId = null) => new()
    {
        ActivityId = activityId,
        ActivityName = name,
        ActivityType = "userTask",
        StartedAtUtc = startedAt,
        EndedAtUtc = endedAt,
        ExecutionId = executionId
    };

    private static async Task SeedDefinitionAsync(
        AutoNateWebApplicationFactory factory, string? diagram = null)
    {
        var bpmn = diagram ?? Bpmn;
        using var scope = factory.Services.CreateScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AutoNateDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();

        var modelId = Guid.NewGuid();
        db.WorkflowModels.Add(new AutoNate.Web.Persistence.Scaffolded.WorkflowModel
        {
            Id = modelId,
            Name = "Review flow",
            ProcessKey = "review_flow",
            BpmnXml = bpmn,
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
            BpmnXml = bpmn,
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
