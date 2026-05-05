using AutoNate.Web.Services.BusWatcher;
using AutoNate.Web.Services.Workflow;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AutoNate.Web.Tests.Workflow;

[Trait("Category", "Integration")]
public sealed class WorkflowExecutionErrorRecorderTests
{
    [Fact]
    public async Task HandleAsync_PersistsErrorMessageAndStackTrace_WhenPayloadCarriesBoth()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();

        var processId = $"proc-{Guid.NewGuid():N}";
        var activityId = "scriptTask_1";
        var payload = $$"""
        {
          "eventType": "job.execution.failed",
          "processInstanceId": "{{processId}}",
          "activityId": "{{activityId}}",
          "activityName": "Eval Script",
          "errorMessage": "ReferenceError: x is not defined",
          "errorStackTrace": "ReferenceError: x is not defined\n  at line 7\n  at engine.run",
          "rawFlowableEventType": "JOB_EXECUTION_FAILURE",
          "occurredAtUtc": "2026-05-05T12:00:00Z"
        }
        """;

        await InvokeRecorderHandleAsync(db, payload);

        await using var read = await db.CreateDbContextFactory().CreateDbContextAsync();
        var row = await read.WorkflowExecutionErrors
            .AsNoTracking()
            .Where(e => e.ProcessInstanceId == processId)
            .SingleAsync();

        Assert.Equal(activityId, row.ActivityId);
        Assert.Equal("Eval Script", row.ActivityName);
        Assert.Equal("ReferenceError: x is not defined", row.ErrorMessage);
        Assert.Contains("at line 7", row.ErrorStackTrace);
    }

    [Fact]
    public async Task HandleAsync_PersistsRow_WhenErrorFieldsAreAbsent()
    {
        await using var db = await PostgresTestDatabase.CreateAsync();

        var processId = $"proc-{Guid.NewGuid():N}";
        var payload = $$"""
        {
          "eventType": "job.execution.failed",
          "processInstanceId": "{{processId}}",
          "activityId": "scriptTask_2",
          "rawFlowableEventType": "JOB_EXECUTION_FAILURE",
          "occurredAtUtc": "2026-05-05T12:01:00Z"
        }
        """;

        await InvokeRecorderHandleAsync(db, payload);

        await using var read = await db.CreateDbContextFactory().CreateDbContextAsync();
        var row = await read.WorkflowExecutionErrors
            .AsNoTracking()
            .Where(e => e.ProcessInstanceId == processId)
            .SingleAsync();

        Assert.Null(row.ErrorMessage);
        Assert.Null(row.ErrorStackTrace);
    }

    private static async Task InvokeRecorderHandleAsync(PostgresTestDatabase db, string payload)
    {
        var busWatcher = new BusWatcherStreamService(NullLogger<BusWatcherStreamService>.Instance);
        var recorder = new WorkflowExecutionErrorRecorder(
            busWatcher,
            db.CreateDbContextFactory(),
            NullLogger<WorkflowExecutionErrorRecorder>.Instance);

        var message = new BusWatcherStreamService.BusWatcherMessage(
            DateTimeOffset.UtcNow,
            BusWatcherStreamService.TopicName,
            "application/json",
            new Dictionary<string, string>(),
            payload);

        await recorder.HandleAsync(message);
    }
}
