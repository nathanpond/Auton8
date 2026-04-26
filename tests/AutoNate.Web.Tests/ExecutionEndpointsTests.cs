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
