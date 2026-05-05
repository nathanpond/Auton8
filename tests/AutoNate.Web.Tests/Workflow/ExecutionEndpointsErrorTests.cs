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
