using AutoNate.Web.Models;
using AutoNate.Web.Services.Workflow;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AutoNate.Web.Tests;

[Trait("Category", "Integration")]
public sealed class EfCoreWorkflowSignalRegistryTests
{
    [Fact]
    public async Task GetRegistrationsForTopic_ReturnsRegistrationsExtractedFromPublishedWorkflows()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = database.CreateWorkflowStore();

        var draft = await store.SaveAsync(new WorkflowModel
        {
            Name = "Order Flow",
            ProcessKey = "OrderFlow",
            BpmnXml = """
                <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL"
                             xmlns:flowable="http://flowable.org/bpmn">
                  <signal id="S" name="record.created" flowable:topic="record.events"/>
                  <process id="OrderFlow">
                    <startEvent id="SE"><signalEventDefinition signalRef="S"/></startEvent>
                  </process>
                </definitions>
                """
        });

        await store.PublishAsync(draft, new WorkflowDeploymentInfo
        {
            DeploymentId = "deployment-1",
            ProcessDefinitionId = "definition-1",
            ProcessDefinitionKey = "OrderFlow",
            ProcessDefinitionVersion = 1,
            DeployedAtUtc = DateTimeOffset.UtcNow
        });

        var registry = new EfCoreWorkflowSignalRegistry(
            database.CreateDbContextFactory(),
            NullLogger<EfCoreWorkflowSignalRegistry>.Instance);
        await registry.RefreshAsync();

        var registrations = registry.GetRegistrationsForTopic("record.events");
        var registration = Assert.Single(registrations);
        Assert.Equal("record.created", registration.SignalName);
        Assert.Equal("record.events", registration.Topic);
        Assert.Equal("OrderFlow", registration.ProcessDefinitionKey);
        Assert.Empty(registration.RecordTypeShortCodes);
    }
    /// <summary>
    /// A draft edit does not change what the registry subscribes to (#553).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The registry filtered on <c>PublishedVersionNumber</c> and then read
    /// <c>model.BpmnXml</c> — the WORKING copy. So for a published workflow,
    /// whatever the author last typed into the draft decided which signal names
    /// the running system listened for, while Flowable went on executing the
    /// published definition. Editing a draft could drop a subscription out from
    /// under parked instances, or add one nothing deployed catches.
    /// </para>
    /// <para>
    /// #544 fixed this shape in the signal broadcast endpoint and cited this
    /// registry as the precedent that got it right. It got the FILTER right and
    /// the xml wrong, which is the half that bit — and nothing here noticed,
    /// because the one test above never edits a draft after publishing.
    /// </para>
    /// <para>
    /// Both directions are asserted: the published name survives, and the
    /// draft-only name never appears. The second is what fails if the join is
    /// reverted to <c>.Select(model =&gt; new { model.Id, model.BpmnXml })</c>.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_draft_edit_does_not_change_what_a_published_workflow_subscribes_to()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = database.CreateWorkflowStore();

        var draft = await store.SaveAsync(new WorkflowModel
        {
            Name = "Order Flow",
            ProcessKey = "OrderFlow",
            BpmnXml = """
                <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL"
                             xmlns:flowable="http://flowable.org/bpmn">
                  <signal id="S" name="record.created" flowable:topic="record.events"/>
                  <process id="OrderFlow">
                    <startEvent id="SE"><signalEventDefinition signalRef="S"/></startEvent>
                  </process>
                </definitions>
                """
        });

        var published = await store.PublishAsync(draft, new WorkflowDeploymentInfo
        {
            DeploymentId = "deployment-1",
            ProcessDefinitionId = "definition-1",
            ProcessDefinitionKey = "OrderFlow",
            ProcessDefinitionVersion = 1,
            DeployedAtUtc = DateTimeOffset.UtcNow
        });

        // The edit that used to move the subscription. Not published.
        await store.SaveAsync(published with
        {
            BpmnXml = """
                <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL"
                             xmlns:flowable="http://flowable.org/bpmn">
                  <signal id="S" name="record.renamed" flowable:topic="record.events"/>
                  <process id="OrderFlow">
                    <startEvent id="SE"><signalEventDefinition signalRef="S"/></startEvent>
                  </process>
                </definitions>
                """
        });

        var registry = new EfCoreWorkflowSignalRegistry(
            database.CreateDbContextFactory(),
            NullLogger<EfCoreWorkflowSignalRegistry>.Instance);
        await registry.RefreshAsync();

        var names = registry.GetSignalNamesForTopic("record.events");

        // The published name still listens -- instances are parked on it.
        Assert.Contains("record.created", names);

        // AND THE DRAFT'S NAME DOES NOT. Asserting only the first would pass
        // against a registry that read BOTH, which is what "the draft added a
        // subscription nothing deployed catches" looks like.
        Assert.DoesNotContain("record.renamed", names);
    }
}
