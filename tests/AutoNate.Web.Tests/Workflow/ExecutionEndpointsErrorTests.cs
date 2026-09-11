using System.Net.Http.Json;
using AutoNate.Web.Models;
using AutoNate.Web.Persistence.Scaffolded;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AutoNate.Web.Tests.Workflow;

[Trait("Category", "Integration")]
public sealed class ExecutionEndpointsErrorTests
{
    [Fact]
    public async Task DiagramEndpoint_ReturnsLatestErrorMessagePerActivity()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();

        var processId = $"proc-{Guid.NewGuid():N}";
        // The stub's GetWorkflowExecutionDiagramDetailAsync always returns a
        // valid (non-null) WorkflowExecutionDiagramDetail, so the endpoint
        // never short-circuits — no stub configuration is needed here.
        await SeedErrorsAsync(factory, processId,
            ("scriptTask_1", "older message", "older trace", "2026-05-05T10:00:00Z"),
            ("scriptTask_1", "newer message", "newer trace", "2026-05-05T11:00:00Z"),
            ("scriptTask_2", "another",       "trace2",      "2026-05-05T10:30:00Z"));

        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me"); // prime auto-login

        var detail = await client.GetFromJsonAsync<WorkflowExecutionDiagramDetail>(
            $"/api/executions/{processId}/diagram");

        Assert.NotNull(detail);
        Assert.Equal("newer message", detail!.ErrorMessagesByActivityId["scriptTask_1"]);
        Assert.Equal("another", detail.ErrorMessagesByActivityId["scriptTask_2"]);
    }

    [Fact]
    public async Task HistoryEndpoint_ReturnsErrorStackTrace()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();

        var processId = $"proc-{Guid.NewGuid():N}";
        // The stub's GetWorkflowExecutionHistoryAsync returns an empty list by
        // default.  The endpoint synthesizes a history row for each errored
        // activityId that isn't already in the Flowable history list, so the
        // seeded DB row surfaces without any additional stub wiring.
        await SeedErrorsAsync(factory, processId,
            ("scriptTask_1", "boom", "Caused by: ReferenceError\n  at line 7",
             "2026-05-05T11:00:00Z"));

        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me"); // prime auto-login

        var rows = await client.GetFromJsonAsync<WorkflowExecutionHistoryEvent[]>(
            $"/api/executions/{processId}/history");

        Assert.NotNull(rows);
        var row = Assert.Single(rows!, r => r.ActivityId == "scriptTask_1");
        Assert.True(row.IsErrored);
        Assert.Equal("boom", row.ErrorMessage);
        Assert.Contains("ReferenceError", row.ErrorStackTrace ?? "");
    }

    [Fact]
    public async Task HistoryEndpoint_PairsErrorMessageAndStackFromSameRetry()
    {
        // Pin the consistency invariant from commit c085fc1f: when an activity
        // has multiple retries with mixed presence/absence of message and stack,
        // the surfaced (ErrorMessage, ErrorStackTrace) MUST come from the same
        // retry row — not from independently-picked "latest non-empty" rows in
        // each column. The synthesized-row branch and the errorsByActivity
        // dictionary both share this rule.
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();

        var processId = $"proc-{Guid.NewGuid():N}";
        await SeedErrorsAsync(factory, processId,
            ("scriptTask_1", "msg-A", null,        "2026-05-05T10:00:00Z"),
            ("scriptTask_1", null,    "trace-B",   "2026-05-05T10:30:00Z"),
            ("scriptTask_1", "msg-C", "trace-C",   "2026-05-05T11:00:00Z"));

        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        var rows = await client.GetFromJsonAsync<WorkflowExecutionHistoryEvent[]>(
            $"/api/executions/{processId}/history");

        Assert.NotNull(rows);
        var row = Assert.Single(rows!, r => r.ActivityId == "scriptTask_1");
        Assert.True(row.IsErrored);
        Assert.Equal("msg-C", row.ErrorMessage);
        Assert.Contains("trace-C", row.ErrorStackTrace ?? "");
        Assert.Equal(3, row.ErrorCount);
    }


    /// <summary>
    /// The gateway's generated ids are mapped back on EVERY id-bearing surface (#294).
    /// </summary>
    /// <remarks>
    /// <para>
    /// #218's AC names five: completed, cancelled, failed, the error map, and
    /// history. The code does all five. Until now the only assertion anywhere was
    /// on <c>completedActivityIds</c>, in one E2E test —
    /// <c>StubFlowableClient.ExpansionSourceMap</c> existed for exactly this and
    /// was never set by any test in any file, so deleting the history mapping, or
    /// the error-map mapping, or the failed-ids mapping broke nothing.
    /// </para>
    /// <para>
    /// The failed and error-map surfaces are the ones an operator reads when a
    /// routing script has just gone wrong — precisely the moment the raw
    /// <c>cg__autonateRoute</c> id is least useful and most likely to appear.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task DiagramEndpoint_MapsGeneratedIdsBackOnEverySurface()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();

        var processId = $"proc-{Guid.NewGuid():N}";
        const string generated = "cg__autonateRoute";
        const string authored = "cg";

        factory.FlowableStub.ExpansionSourceMap =
            new Dictionary<string, string>(StringComparer.Ordinal) { [generated] = authored };

        factory.FlowableStub.DiagramDetail = new WorkflowExecutionDiagramDetail
        {
            ExecutionId = processId,
            CompletedActivityIds = [generated, "userTask_1"],
            CurrentActivityIds = [generated],
            CancelledActivityIds = [generated],
            // The DIAGRAM endpoint maps from this property on the detail; the
            // HISTORY endpoint maps from IFlowableClient.GetExpansionSourceMapAsync.
            // Two sources for one mapping, which is why a test that set only the
            // client-side one (as the first version of this test did) sees the
            // history mapping work and the diagram mapping not.
            ExpansionSourceIds = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [generated] = authored
            },
        };

        // The failed ids and the error map are built from this table, not the stub.
        await SeedErrorsAsync(factory, processId,
            (generated, "the routing script blew up", "trace", "2026-05-05T10:00:00Z"));

        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        var detail = await client.GetFromJsonAsync<WorkflowExecutionDiagramDetail>(
            $"/api/executions/{processId}/diagram");

        Assert.NotNull(detail);

        // All five, each asserted both ways: the author's id present AND the
        // generated one absent. Presence alone would pass on a surface that
        // emitted both, which is a diagram highlighting a node the author cannot
        // see beside one they can.
        foreach (var (surface, ids) in new (string, IReadOnlyList<string>)[]
        {
            ("completed", detail!.CompletedActivityIds),
            ("current", detail.CurrentActivityIds),
            ("cancelled", detail.CancelledActivityIds),
            ("failed", detail.FailedActivityIds),
        })
        {
            Assert.True(ids.Contains(authored),
                $"The '{surface}' surface does not carry the author's gateway id '{authored}'. " +
                $"It has: {string.Join(", ", ids)}");
            Assert.True(!ids.Contains(generated),
                $"The '{surface}' surface still carries the generated id '{generated}', which " +
                "names a node the author never drew and cannot find in their diagram.");
        }

        Assert.True(detail.ErrorMessagesByActivityId.ContainsKey(authored),
            "The error map is keyed by the generated id, so an operator reading why the " +
            "gateway failed finds the message filed under a node that is not in the diagram.");
        Assert.False(detail.ErrorMessagesByActivityId.ContainsKey(generated),
            "The error map carries the generated id as well as the author's.");

        // The untouched id must survive, or a mapping that rewrote everything to
        // the same value would pass every assertion above.
        Assert.Contains("userTask_1", detail.CompletedActivityIds);
    }

    [Fact]
    public async Task HistoryEndpoint_MapsGeneratedIdsBackToTheAuthoredGateway()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();

        var processId = $"proc-{Guid.NewGuid():N}";
        const string generated = "cg__autonateRoute";

        factory.FlowableStub.ExpansionSourceMap =
            new Dictionary<string, string>(StringComparer.Ordinal) { [generated] = "cg" };

        factory.FlowableStub.HistoryByInstance[processId] =
        [
            new WorkflowExecutionHistoryEvent
            {
                ActivityId = generated,
                ActivityName = "Route (script)",
                ActivityType = "scriptTask",
                StartedAtUtc = DateTimeOffset.UtcNow
            },
            new WorkflowExecutionHistoryEvent
            {
                ActivityId = "userTask_1",
                ActivityName = "Approve",
                ActivityType = "userTask",
                StartedAtUtc = DateTimeOffset.UtcNow
            },
        ];

        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        var history = await client.GetFromJsonAsync<List<WorkflowExecutionHistoryEvent>>(
            $"/api/executions/{processId}/history");

        Assert.NotNull(history);
        var ids = history!.Select(e => e.ActivityId).ToList();

        Assert.Contains("cg", ids);
        Assert.DoesNotContain(generated, ids);
        // Untouched rows survive, so a mapping that rewrote every id fails here.
        Assert.Contains("userTask_1", ids);
    }

    private static async Task SeedErrorsAsync(
        AutoNateWebApplicationFactory factory,
        string processId,
        params (string ActivityId, string? Message, string? Trace, string OccurredAtUtc)[] rows)
    {
        var dbFactory = factory.Services.GetRequiredService<
            IDbContextFactory<AutoNate.Web.Persistence.AutoNateDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();
        foreach (var row in rows)
        {
            db.WorkflowExecutionErrors.Add(new WorkflowExecutionError
            {
                Id = Guid.NewGuid(),
                ProcessInstanceId = processId,
                ActivityId = row.ActivityId,
                ActivityName = null,
                ErrorMessage = row.Message,
                ErrorStackTrace = row.Trace,
                RawFlowableEventType = "JOB_EXECUTION_FAILURE",
                OccurredAtUtc = DateTime.Parse(row.OccurredAtUtc).ToUniversalTime()
            });
        }
        await db.SaveChangesAsync();
    }
}
