using System.Net;
using System.Net.Http.Json;
using AutoNate.Web.Endpoints;
using AutoNate.Web.Models;
using Xunit;

namespace AutoNate.Web.Tests;

[Trait("Category", "Integration")]
public sealed class ExecutionEndpointsTests
{
    [Fact]
    public async Task ListExecutions_ReturnsStubbedEmptyList()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();

        var executions = await client.GetFromJsonAsync<WorkflowExecutionSummary[]>(
            "/api/executions/");

        Assert.NotNull(executions);
        Assert.Empty(executions);
        Assert.Contains("ListExecutions", factory.FlowableStub.Calls);
    }

    [Fact]
    public async Task GetDiagram_DelegatesToFlowableClient()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/executions/inst-123/diagram");
        response.EnsureSuccessStatusCode();

        Assert.Contains("Diagram:inst-123", factory.FlowableStub.Calls);
    }

    [Fact]
    public async Task GetHistory_DelegatesToFlowableClient()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        factory.FlowableStub.HistoryByInstance["inst-hist"] = new List<WorkflowExecutionHistoryEvent>
        {
            new() { ActivityId = "startEvent_1", ActivityType = "startEvent", StartedAtUtc = DateTimeOffset.UtcNow },
            new() { ActivityId = "userTask_review", ActivityName = "Review", ActivityType = "userTask", Assignee = "alice" }
        };
        var client = factory.CreateClient();

        var events = await client.GetFromJsonAsync<WorkflowExecutionHistoryEvent[]>(
            "/api/executions/inst-hist/history");

        Assert.NotNull(events);
        Assert.Equal(2, events!.Length);
        Assert.Equal("startEvent_1", events[0].ActivityId);
        Assert.Equal("alice", events[1].Assignee);
        Assert.Contains("History:inst-hist", factory.FlowableStub.Calls);
    }

    [Fact]
    public async Task GetLog_DelegatesToFlowableClient()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        factory.FlowableStub.LogByInstance["inst-log"] = new List<WorkflowExecutionLogEntry>
        {
            new()
            {
                Kind = "variable-update",
                OccurredAtUtc = DateTimeOffset.UtcNow,
                VariableUpdate = new WorkflowExecutionLogVariableUpdate { Name = "amount", Value = "200", Revision = 2 }
            },
            new()
            {
                Kind = "task-completed",
                OccurredAtUtc = DateTimeOffset.UtcNow.AddMinutes(1),
                Task = new WorkflowExecutionLogTask { TaskId = "t-1", Name = "Approve", Assignee = "alice" }
            }
        };
        var client = factory.CreateClient();

        var entries = await client.GetFromJsonAsync<WorkflowExecutionLogEntry[]>(
            "/api/executions/inst-log/log");

        Assert.NotNull(entries);
        Assert.Equal(2, entries!.Length);
        Assert.Equal("variable-update", entries[0].Kind);
        Assert.Equal("amount", entries[0].VariableUpdate?.Name);
        Assert.Equal("task-completed", entries[1].Kind);
        Assert.Equal("alice", entries[1].Task?.Assignee);
        Assert.Contains("Log:inst-log", factory.FlowableStub.Calls);
    }

    [Fact]
    public async Task GetTasksByInstance_DelegatesToFlowableClient()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();

        var tasks = await client.GetFromJsonAsync<FlowableTaskSummary[]>(
            "/api/executions/inst-456/tasks");

        Assert.NotNull(tasks);
        Assert.Contains("TasksByInstance:inst-456", factory.FlowableStub.Calls);
    }

    [Fact]
    public async Task DeleteExecution_Returns204AndCallsClient()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();

        // Prime auth so the DELETE goes through.
        (await client.GetAsync("/api/executions/")).EnsureSuccessStatusCode();

        var response = await client.DeleteAsync("/api/executions/inst-789");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Contains("DeleteExecution:inst-789", factory.FlowableStub.Calls);
    }

    [Fact]
    public async Task CancelExecution_Returns204AndCallsClient()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        (await client.GetAsync("/api/executions/")).EnsureSuccessStatusCode();

        var response = await client.PostAsync("/api/executions/inst-cancel/cancel", content: null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Contains("CancelExecution:inst-cancel", factory.FlowableStub.Calls);
    }

    [Fact]
    public async Task DeleteAllExecutions_Returns200WithCount_AndCallsClient()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        factory.FlowableStub.DeleteAllWorkflowExecutionsResult = 7;
        var client = factory.CreateClient();
        (await client.GetAsync("/api/executions/")).EnsureSuccessStatusCode();

        var response = await client.PostAsync("/api/executions/delete-all", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<DeleteAllResponse>();
        Assert.NotNull(body);
        Assert.Equal(7, body!.Deleted);
        Assert.Contains("DeleteAllExecutions", factory.FlowableStub.Calls);
    }

    private sealed record DeleteAllResponse(int Deleted);

    [Fact]
    public async Task TasksAssignedToMe_PassesActorIdToClient()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();

        var tasks = await client.GetFromJsonAsync<FlowableTaskSummary[]>(
            "/api/tasks/assigned-to-me");

        Assert.NotNull(tasks);
        Assert.Contains(factory.FlowableStub.Calls, c => c.StartsWith("TasksForUser:"));
    }

    [Fact]
    public async Task CompleteTask_Returns204AndCallsClient()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        (await client.GetAsync("/api/tasks/assigned-to-me")).EnsureSuccessStatusCode();

        var response = await client.PostAsJsonAsync(
            "/api/tasks/task-1/complete",
            new ExecutionEndpoints.CompleteTaskRequest(null));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Contains("CompleteTask:task-1", factory.FlowableStub.Calls);
    }

    [Fact]
    public async Task UpdateProcessVariables_Returns204AndCallsClient()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        (await client.GetAsync("/api/executions/")).EnsureSuccessStatusCode();

        var response = await client.PutAsJsonAsync(
            "/api/executions/inst-vars/variables",
            new ExecutionEndpoints.UpdateProcessVariablesRequest(new[]
            {
                new ProcessVariableUpdate { Name = "amount", Value = 42, Type = "integer" },
                new ProcessVariableUpdate { Name = "label", Value = "ok" }
            }));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Contains("UpdateVariables:inst-vars", factory.FlowableStub.Calls);
        Assert.True(factory.FlowableStub.VariableUpdatesByInstance.TryGetValue("inst-vars", out var captured));
        Assert.Equal(2, captured!.Count);
        Assert.Equal("amount", captured[0].Name);
        Assert.Equal("integer", captured[0].Type);
    }

    [Fact]
    public async Task ForceCompleteTask_Returns204AndCallsClient()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        (await client.GetAsync("/api/executions/")).EnsureSuccessStatusCode();

        var response = await client.PostAsJsonAsync(
            "/api/executions/inst-fc/tasks/task-fc/force-complete",
            new ExecutionEndpoints.CompleteTaskRequest(null));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Contains("CompleteTask:task-fc", factory.FlowableStub.Calls);
    }

    [Fact]
    public async Task GetCompletedAssignees_DelegatesToFlowableClient()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        factory.FlowableStub.CompletedAssigneesByActivity[("inst-q", "userTask_review")]
            = new List<string> { "alice", "bob" };
        var client = factory.CreateClient();

        var assignees = await client.GetFromJsonAsync<string[]>(
            "/api/executions/inst-q/activities/userTask_review/completed-assignees");

        Assert.NotNull(assignees);
        Assert.Equal(new[] { "alice", "bob" }, assignees);
        Assert.Contains("CompletedAssignees:inst-q:userTask_review", factory.FlowableStub.Calls);
    }
}
