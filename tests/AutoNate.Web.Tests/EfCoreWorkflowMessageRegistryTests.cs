using AutoNate.Web.Models;
using AutoNate.Web.Services.Workflow;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AutoNate.Web.Tests;

/// <summary>
/// The message registry reads what was PUBLISHED (#553).
/// </summary>
/// <remarks>
/// <para>
/// This file exists because the signal registry had one test and the message
/// registry had none, and they carried the identical defect: both filtered on
/// <c>PublishedVersionNumber</c> and then read <c>model.BpmnXml</c>, the working
/// copy. Fixing one and guarding one would have left the other exactly as it
/// was — and "the sibling next door does it right" is the reasoning that
/// produced this bug in the first place (#544 cited both registries as the
/// correct precedent; they got the filter right and the xml wrong).
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
public sealed class EfCoreWorkflowMessageRegistryTests
{
    private const string Topic = "record.events";

    private static string Xml(string messageName) =>
        $"""
        <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL"
                     xmlns:flowable="http://flowable.org/bpmn">
          <message id="M" name="{messageName}" flowable:topic="{Topic}"/>
          <process id="OrderFlow">
            <startEvent id="SE"><messageEventDefinition messageRef="M"/></startEvent>
          </process>
        </definitions>
        """;

    /// <summary>
    /// A draft edit does not change what a published workflow listens for (#553).
    /// </summary>
    /// <remarks>
    /// Both directions are asserted. The published name must survive — that is
    /// the direction that leaves a queue message unable to start the workflow
    /// that genuinely declares it. And the draft-only name must not appear —
    /// that is the phantom subscription, a topic listening for a name no
    /// deployed definition catches.
    /// </remarks>
    [Fact]
    public async Task A_draft_edit_does_not_change_what_a_published_workflow_listens_for()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = database.CreateWorkflowStore();

        var draft = await store.SaveAsync(new WorkflowModel
        {
            Name = "Order Flow",
            ProcessKey = "OrderFlow",
            BpmnXml = Xml("order.placed")
        });

        var published = await store.PublishAsync(draft, new WorkflowDeploymentInfo
        {
            DeploymentId = "deployment-1",
            ProcessDefinitionId = "definition-1",
            ProcessDefinitionKey = "OrderFlow",
            ProcessDefinitionVersion = 1,
            DeployedAtUtc = DateTimeOffset.UtcNow
        });

        // Edited, NOT published. Flowable is still running `order.placed`.
        await store.SaveAsync(published with { BpmnXml = Xml("order.renamed") });

        var registry = new EfCoreWorkflowMessageRegistry(
            database.CreateDbContextFactory(),
            NullLogger<EfCoreWorkflowMessageRegistry>.Instance);
        await registry.RefreshAsync();

        var names = registry.GetMessageNamesForTopic(Topic);

        Assert.Contains("order.placed", names);
        Assert.DoesNotContain("order.renamed", names);
    }

    /// <summary>A never-published draft registers nothing at all (#553).</summary>
    /// <remarks>
    /// The filter half, kept beside the join half so one cannot be reverted
    /// without the other being visible. Asserting only the join would pass
    /// against a registry that also subscribed every draft in the database.
    /// </remarks>
    [Fact]
    public async Task A_never_published_draft_registers_nothing()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = database.CreateWorkflowStore();

        await store.SaveAsync(new WorkflowModel
        {
            Name = "Never Published",
            ProcessKey = "NeverPublished",
            BpmnXml = Xml("never.published")
        });

        var registry = new EfCoreWorkflowMessageRegistry(
            database.CreateDbContextFactory(),
            NullLogger<EfCoreWorkflowMessageRegistry>.Instance);
        await registry.RefreshAsync();

        Assert.DoesNotContain(Topic, registry.GetSubscribedTopics());
    }
}
