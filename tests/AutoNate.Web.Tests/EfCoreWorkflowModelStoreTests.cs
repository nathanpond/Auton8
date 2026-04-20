using AutoNate.Web.Models;
using Xunit;

namespace AutoNate.Web.Tests;

public sealed class EfCoreWorkflowModelStoreTests
{
    [Fact]
    public async Task SaveAsync_CreatesAndLoadsWorkflow()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = database.CreateWorkflowStore();

        var saved = await store.SaveAsync(new WorkflowModel
        {
            Name = "Approval Flow",
            ProcessKey = "approval_flow",
            BpmnXml = "<xml />"
        });

        Assert.NotEqual(Guid.Empty, saved.Id);
        Assert.True(saved.CreatedAtUtc <= saved.UpdatedAtUtc);

        var loaded = await store.GetAsync(saved.Id);
        Assert.NotNull(loaded);
        Assert.Equal("Approval Flow", loaded.Name);
    }

    [Fact]
    public async Task ListAsync_OrdersByUpdatedAtDescendingThenName()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = database.CreateWorkflowStore();

        var first = await store.SaveAsync(new WorkflowModel
        {
            Name = "First Flow",
            ProcessKey = "first_flow",
            BpmnXml = "<xml />"
        });

        await Task.Delay(25);

        var second = await store.SaveAsync(new WorkflowModel
        {
            Name = "Second Flow",
            ProcessKey = "second_flow",
            BpmnXml = "<xml />"
        });

        var listed = await store.ListAsync();

        Assert.Collection(
            listed,
            workflow => Assert.Equal(second.Id, workflow.Id),
            workflow => Assert.Equal(first.Id, workflow.Id));
    }

    [Fact]
    public async Task GetMostRecentAsync_ReturnsNewestWorkflow()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = database.CreateWorkflowStore();

        await store.SaveAsync(new WorkflowModel
        {
            Name = "Older Flow",
            ProcessKey = "older_flow",
            BpmnXml = "<xml />"
        });

        await Task.Delay(25);

        var latest = await store.SaveAsync(new WorkflowModel
        {
            Name = "Latest Flow",
            ProcessKey = "latest_flow",
            BpmnXml = "<xml />"
        });

        var mostRecent = await store.GetMostRecentAsync();

        Assert.NotNull(mostRecent);
        Assert.Equal(latest.Id, mostRecent.Id);
    }

    [Fact]
    public async Task SaveAsync_UpdatePreservesDeploymentMetadata()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = database.CreateWorkflowStore();
        var original = await store.SaveAsync(new WorkflowModel
        {
            Name = "Deployment Flow",
            ProcessKey = "deployment_flow",
            BpmnXml = "<xml />"
        });

        var deploymentTime = DateTimeOffset.UtcNow;
        var updated = await store.SaveAsync(original with
        {
            BpmnXml = "<xml>updated</xml>",
            LastDeployment = new WorkflowDeploymentInfo
            {
                DeploymentId = "deployment-1",
                ProcessDefinitionId = "definition-1",
                ProcessDefinitionKey = "deployment_flow",
                ProcessDefinitionVersion = 4,
                DeployedAtUtc = deploymentTime
            },
            ActiveProcessInstanceId = "process-instance-42"
        });

        Assert.NotNull(updated.LastDeployment);
        Assert.Equal("deployment-1", updated.LastDeployment.DeploymentId);
        Assert.Equal(4, updated.LastDeployment.ProcessDefinitionVersion);
        Assert.Equal("process-instance-42", updated.ActiveProcessInstanceId);
    }
}
