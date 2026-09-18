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

    /// <summary>
    /// The published list carries the PUBLISHED xml, not the draft's (#552).
    /// </summary>
    /// <remarks>
    /// <para>
    /// #544 was two defects and this is the second, which its own fix commit
    /// called the worse one: a workflow published catching a signal, then
    /// draft-edited to drop it, read as no longer declaring it -- while real
    /// instances sat parked on exactly that element in the engine. The fix was
    /// to JOIN <c>WorkflowModelVersions</c> rather than filter
    /// <c>ListAsync</c>, because filtering afterwards still hands back
    /// <c>workflow_models.bpmn_xml</c>, which is the working copy.
    /// </para>
    /// <para>
    /// <b>Nothing tested it.</b> The unit test that carried this defect's name
    /// was a renamed copy of an existing positive path, and no test anywhere
    /// invoked <c>ListPublishedAsync</c> against a store holding a
    /// published-versus-draft divergence -- so replacing the join with
    /// <c>.Where(m =&gt; m.PublishedVersionNumber != null).Select(m =&gt; m.ToModel())</c>
    /// left the entire suite green. That is the mutation this fact exists to
    /// kill, and it is killed on the xml assertion below rather than on a
    /// precondition.
    /// </para>
    /// <para>
    /// A broadcaster-level test cannot do this job at all: the broadcaster sees
    /// only what the store returns, so draft-versus-published is invisible one
    /// layer up. The guard has to sit where the query is.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task ListPublishedAsync_ReturnsThePublishedXmlNotTheDraftEdit()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = database.CreateWorkflowStore();

        const string PublishedXml = "<xml published=\"yes\" />";
        const string DraftXml = "<xml published=\"no\" />";

        var original = await store.SaveAsync(new WorkflowModel
        {
            Name = "Published Flow",
            ProcessKey = "published_flow",
            BpmnXml = PublishedXml
        });

        var published = await store.PublishAsync(original, new WorkflowDeploymentInfo
        {
            DeploymentId = "deployment-1",
            ProcessDefinitionId = "definition-1",
            ProcessDefinitionKey = "published_flow",
            ProcessDefinitionVersion = 1,
            DeployedAtUtc = DateTimeOffset.UtcNow
        });

        // The draft edit that used to un-declare it.
        await store.SaveAsync(published with { BpmnXml = DraftXml });

        // A never-published model, so the absence half is asserted here too.
        //
        // Not an independent guard on the `.Where` clause, and the first version
        // of this comment said it was (#559): the join is INNER and keys on
        // `PublishedVersionNumber`, so SQL `NULL = version_number` never matches
        // and deleting the `.Where` changes nothing observable. The filter is
        // subsumed by the join. What this row does buy is that `Assert.Single`
        // is meaningful rather than vacuous.
        await store.SaveAsync(new WorkflowModel
        {
            Name = "Never Published",
            ProcessKey = "never_published",
            BpmnXml = "<xml drafted=\"only\" />"
        });

        var listed = await store.ListPublishedAsync();

        var row = Assert.Single(listed);
        Assert.Equal("published_flow", row.ProcessKey);

        // THE JOIN. A filter-only implementation returns DraftXml here and
        // passes every other assertion in this fact.
        Assert.Equal(PublishedXml, row.BpmnXml);
    }

    /// <summary>
    /// The single-key lookup carries the published xml too (#557).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>GetPublishedByProcessKeyAsync</c> was written to fix #553 and shipped
    /// with no test at all — only stub implementations in four test doubles and
    /// two tripwire strings. Reverting its body to
    /// <c>GetByProcessKeyAsync</c>'s, which filters nothing and returns the
    /// draft, left the entire suite green.
    /// </para>
    /// <para>
    /// That is #552's finding, recurring inside the fix for the issue #552
    /// blocked on. The caller-level tripwires the same commit added catch a
    /// different property — "the caller asked the wrong method" — and cannot see
    /// this one, because the stub supplies the answer.
    /// </para>
    /// <para>
    /// Both callers are runtime ones — <c>WorkflowMessageCorrelator</c> deciding
    /// which messages a RUNNING instance can be addressed by, and
    /// <c>SendMessageBehavior</c> resolving a send from inside one — so the
    /// diagram they read has to be the one the engine deployed.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task GetPublishedByProcessKeyAsync_ReturnsThePublishedXmlNotTheDraftEdit()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = database.CreateWorkflowStore();

        const string PublishedXml = "<xml published=\"yes\" />";
        const string DraftXml = "<xml published=\"no\" />";

        var original = await store.SaveAsync(new WorkflowModel
        {
            Name = "Published Flow",
            ProcessKey = "published_flow",
            BpmnXml = PublishedXml
        });

        var published = await store.PublishAsync(original, new WorkflowDeploymentInfo
        {
            DeploymentId = "deployment-1",
            ProcessDefinitionId = "definition-1",
            ProcessDefinitionKey = "published_flow",
            ProcessDefinitionVersion = 1,
            DeployedAtUtc = DateTimeOffset.UtcNow
        });

        await store.SaveAsync(published with { BpmnXml = DraftXml });

        var found = await store.GetPublishedByProcessKeyAsync("published_flow");

        Assert.NotNull(found);

        // THE ORACLE. A draft-returning body answers DraftXml here and passes
        // the NotNull above, so the mutation dies on this line.
        Assert.Equal(PublishedXml, found.BpmnXml);
    }

    /// <summary>A never-published draft is not found by process key (#557).</summary>
    /// <remarks>
    /// The complement. Without it the fact above passes against a lookup that
    /// answers for every model regardless of publication — which is the first
    /// half of #544, one method over.
    /// </remarks>
    [Fact]
    public async Task GetPublishedByProcessKeyAsync_DoesNotFindANeverPublishedDraft()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = database.CreateWorkflowStore();

        await store.SaveAsync(new WorkflowModel
        {
            Name = "Never Published",
            ProcessKey = "never_published",
            BpmnXml = "<xml drafted=\"only\" />"
        });

        Assert.Null(await store.GetPublishedByProcessKeyAsync("never_published"));
    }

    /// <summary>
    /// A draft rename does not orphan the published definition (#558).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The lookup matched <c>workflow_models.process_key</c> — the working
    /// copy's — while returning the published version's xml.
    /// <c>SaveAsync</c> re-applies the key from the request on every save, so a
    /// draft rename of a published workflow made the running definition
    /// unfindable: <c>WorkflowMessageCorrelator</c> answered
    /// <c>UnknownProcess</c> and <c>SendMessageBehavior</c> failed
    /// <c>senderNotFound</c>, for an instance running perfectly well.
    /// </para>
    /// <para>
    /// The same "a draft edit changes what a running instance does" shape #553
    /// was filed about, surviving inside the method written to end it.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task GetPublishedByProcessKeyAsync_FindsThePublishedKeyAfterADraftRename()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = database.CreateWorkflowStore();

        var original = await store.SaveAsync(new WorkflowModel
        {
            Name = "Orders",
            ProcessKey = "orders_v1",
            BpmnXml = "<xml published=\"yes\" />"
        });

        var published = await store.PublishAsync(original, new WorkflowDeploymentInfo
        {
            DeploymentId = "deployment-1",
            ProcessDefinitionId = "definition-1",
            ProcessDefinitionKey = "orders_v1",
            ProcessDefinitionVersion = 1,
            DeployedAtUtc = DateTimeOffset.UtcNow
        });

        // Renamed in the draft, NOT published. Flowable is still running
        // `orders_v1` and instances of it are still addressable by that key.
        await store.SaveAsync(published with { ProcessKey = "orders_v2" });

        var found = await store.GetPublishedByProcessKeyAsync("orders_v1");

        Assert.NotNull(found);
        Assert.Equal("orders_v1", found.ProcessKey);

        // AND THE DRAFT'S NEW KEY FINDS NOTHING, because nothing is deployed
        // under it. Asserting only the first would pass against a lookup that
        // matched either key.
        Assert.Null(await store.GetPublishedByProcessKeyAsync("orders_v2"));
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
