using System.Net.Http.Json;
using AutoNate.Web.Endpoints;
using AutoNate.Web.Models;
using AutoNate.Web.Services.Workflow;
using Xunit;

namespace AutoNate.Web.Tests;

[Trait("Category", "Integration")]
public sealed class WorkflowAdminEventPublishingTests
{
    [Fact]
    public async Task SaveWorkflowModel_publishes_model_saved()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await Prime(client);
        factory.RecordedAuditEvents.Clear();

        var response = await client.PostAsJsonAsync("/api/workflows", new WorkflowModel
        {
            Id = Guid.NewGuid(),
            Name = "Test Workflow",
            ProcessKey = "test-workflow",
            BpmnXml = "<?xml version=\"1.0\"?><definitions/>"
        });
        response.EnsureSuccessStatusCode();

        Assert.Contains(factory.RecordedAuditEvents.Events,
            e => e.EventType == WorkflowAdminEventTypes.ModelSaved);
    }

    [Fact]
    public async Task StartWorkflow_publishes_start_invoked()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await Prime(client);
        factory.RecordedAuditEvents.Clear();

        var response = await client.PostAsJsonAsync(
            "/api/workflows/some-process-key/start",
            new WorkflowEndpoints.StartInstanceRequest("Smoke run", null));
        response.EnsureSuccessStatusCode();

        Assert.Contains(factory.RecordedAuditEvents.Events,
            e => e.EventType == WorkflowAdminEventTypes.ModelStarted);
    }

    [Fact]
    public async Task CancelExecution_publishes_execution_cancelled()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await Prime(client);
        factory.RecordedAuditEvents.Clear();

        (await client.PostAsync("/api/executions/proc-1/cancel", null)).EnsureSuccessStatusCode();

        Assert.Contains(factory.RecordedAuditEvents.Events,
            e => e.EventType == WorkflowAdminEventTypes.ExecutionCancelled);
    }

    [Fact]
    public async Task DeleteExecution_publishes_execution_deleted()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await Prime(client);
        factory.RecordedAuditEvents.Clear();

        (await client.DeleteAsync("/api/executions/proc-1")).EnsureSuccessStatusCode();

        Assert.Contains(factory.RecordedAuditEvents.Events,
            e => e.EventType == WorkflowAdminEventTypes.ExecutionDeleted);
    }

    [Fact]
    public async Task DeleteAllExecutions_publishes_bulk_deleted()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await Prime(client);
        factory.RecordedAuditEvents.Clear();

        (await client.PostAsync("/api/executions/delete-all", null)).EnsureSuccessStatusCode();

        Assert.Contains(factory.RecordedAuditEvents.Events,
            e => e.EventType == WorkflowAdminEventTypes.ExecutionsBulkDeleted);
    }

    [Fact]
    public async Task ReassignTask_publishes_task_reassigned()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await Prime(client);
        factory.RecordedAuditEvents.Clear();

        (await client.PostAsJsonAsync(
            "/api/executions/proc-1/tasks/task-1/reassign",
            new ExecutionEndpoints.ReassignTaskRequest("alice"))).EnsureSuccessStatusCode();

        Assert.Contains(factory.RecordedAuditEvents.Events,
            e => e.EventType == WorkflowAdminEventTypes.TaskReassigned);
    }

    [Fact]
    public async Task ForceCompleteTask_publishes_force_completed()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await Prime(client);
        factory.RecordedAuditEvents.Clear();

        (await client.PostAsJsonAsync(
            "/api/executions/proc-1/tasks/task-1/force-complete",
            new ExecutionEndpoints.CompleteTaskRequest(null))).EnsureSuccessStatusCode();

        Assert.Contains(factory.RecordedAuditEvents.Events,
            e => e.EventType == WorkflowAdminEventTypes.TaskForceCompleted);
    }

    [Fact]
    public async Task CompleteTask_publishes_task_completed()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await Prime(client);
        factory.RecordedAuditEvents.Clear();

        (await client.PostAsJsonAsync(
            "/api/tasks/task-1/complete",
            new ExecutionEndpoints.CompleteTaskRequest(null))).EnsureSuccessStatusCode();

        Assert.Contains(factory.RecordedAuditEvents.Events,
            e => e.EventType == WorkflowAdminEventTypes.TaskCompleted);
    }

    private static async Task Prime(HttpClient client) =>
        (await client.GetAsync("/api/workflows")).EnsureSuccessStatusCode();
}
