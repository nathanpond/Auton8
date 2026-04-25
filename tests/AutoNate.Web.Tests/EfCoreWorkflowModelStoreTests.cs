using AutoNate.Web.Models;
using Xunit;

namespace AutoNate.Web.Tests;

[Trait("Category", "Integration")]
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
        Assert.True(saved.IsDraft);
        Assert.Equal(1, saved.DraftVersionNumber);
        Assert.Null(saved.PublishedVersionNumber);

        var loaded = await store.GetAsync(saved.Id);
        Assert.NotNull(loaded);
        Assert.Equal("Approval Flow", loaded.Name);
        Assert.True(loaded.IsDraft);
        Assert.Equal(1, loaded.DraftVersionNumber);
        Assert.Null(loaded.PublishedVersionNumber);
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
    public async Task PublishAsync_CreatesPublishedVersionSnapshot()
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
        var published = await store.PublishAsync(original, new WorkflowDeploymentInfo
        {
            DeploymentId = "deployment-1",
            ProcessDefinitionId = "definition-1",
            ProcessDefinitionKey = "deployment_flow",
            ProcessDefinitionVersion = 4,
            DeployedAtUtc = deploymentTime
        });

        Assert.NotNull(published.LastDeployment);
        Assert.Equal("deployment-1", published.LastDeployment.DeploymentId);
        Assert.Equal(4, published.LastDeployment.ProcessDefinitionVersion);
        Assert.False(published.IsDraft);
        Assert.Equal(1, published.DraftVersionNumber);
        Assert.Equal(1, published.PublishedVersionNumber);

        var versions = await store.ListVersionsAsync(published.Id);

        Assert.Collection(
            versions,
            version =>
            {
                Assert.Equal(1, version.VersionNumber);
                Assert.Equal(published.Id, version.WorkflowModelId);
                Assert.Equal("<xml />", version.BpmnXml);
                Assert.Equal("deployment-1", version.Deployment.DeploymentId);
            });
    }

    [Fact]
    public async Task SaveAsync_AfterPublishCreatesNextDraftVersion()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = database.CreateWorkflowStore();
        var original = await store.SaveAsync(new WorkflowModel
        {
            Name = "Draft Flow",
            ProcessKey = "draft_flow",
            BpmnXml = "<xml />"
        });

        var published = await store.PublishAsync(original, new WorkflowDeploymentInfo
        {
            DeploymentId = "deployment-1",
            ProcessDefinitionId = "definition-1",
            ProcessDefinitionKey = "draft_flow",
            ProcessDefinitionVersion = 1,
            DeployedAtUtc = DateTimeOffset.UtcNow
        });

        var draft = await store.SaveAsync(published with
        {
            BpmnXml = "<xml>updated</xml>",
            ActiveProcessInstanceId = "process-instance-42"
        });

        Assert.True(draft.IsDraft);
        Assert.Equal(2, draft.DraftVersionNumber);
        Assert.Equal(1, draft.PublishedVersionNumber);
        Assert.NotNull(draft.LastDeployment);
        Assert.Equal("deployment-1", draft.LastDeployment.DeploymentId);
        Assert.Equal("process-instance-42", draft.ActiveProcessInstanceId);

        var versions = await store.ListVersionsAsync(draft.Id);
        Assert.Single(versions);
        Assert.Equal(1, versions[0].VersionNumber);
    }

    [Fact]
    public async Task PublishAsync_FromDraftPromotesDraftVersionAndRetainsHistory()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = database.CreateWorkflowStore();
        var original = await store.SaveAsync(new WorkflowModel
        {
            Name = "Versioned Flow",
            ProcessKey = "versioned_flow",
            BpmnXml = "<xml />"
        });

        var publishedV1 = await store.PublishAsync(original, new WorkflowDeploymentInfo
        {
            DeploymentId = "deployment-1",
            ProcessDefinitionId = "definition-1",
            ProcessDefinitionKey = "versioned_flow",
            ProcessDefinitionVersion = 1,
            DeployedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5)
        });

        var draftV2 = await store.SaveAsync(publishedV1 with
        {
            BpmnXml = "<xml>v2</xml>"
        });

        var publishedV2 = await store.PublishAsync(draftV2, new WorkflowDeploymentInfo
        {
            DeploymentId = "deployment-2",
            ProcessDefinitionId = "definition-2",
            ProcessDefinitionKey = "versioned_flow",
            ProcessDefinitionVersion = 2,
            DeployedAtUtc = DateTimeOffset.UtcNow
        });

        Assert.False(publishedV2.IsDraft);
        Assert.Equal(2, publishedV2.DraftVersionNumber);
        Assert.Equal(2, publishedV2.PublishedVersionNumber);

        var versions = await store.ListVersionsAsync(publishedV2.Id);

        Assert.Collection(
            versions,
            version => Assert.Equal(2, version.VersionNumber),
            version => Assert.Equal(1, version.VersionNumber));
    }

    [Fact]
    public async Task SaveAsync_RuntimeOnlyUpdateDoesNotMarkPublishedWorkflowAsDraft()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = database.CreateWorkflowStore();

        var original = await store.SaveAsync(new WorkflowModel
        {
            Name = "Runtime Flow",
            ProcessKey = "runtime_flow",
            BpmnXml = "<xml />"
        });

        var published = await store.PublishAsync(original, new WorkflowDeploymentInfo
        {
            DeploymentId = "deployment-1",
            ProcessDefinitionId = "definition-1",
            ProcessDefinitionKey = "runtime_flow",
            ProcessDefinitionVersion = 1,
            DeployedAtUtc = DateTimeOffset.UtcNow
        });

        var runtimeUpdated = await store.SaveAsync(published with
        {
            ActiveProcessInstanceId = "process-instance-42"
        });

        Assert.False(runtimeUpdated.IsDraft);
        Assert.Equal("process-instance-42", runtimeUpdated.ActiveProcessInstanceId);
    }
}
