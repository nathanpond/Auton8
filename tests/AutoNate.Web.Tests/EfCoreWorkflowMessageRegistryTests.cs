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

    private static string Xml(string messageName, string processKey = "OrderFlow") =>
        $"""
        <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL"
                     xmlns:flowable="http://flowable.org/bpmn">
          <message id="M" name="{messageName}" flowable:topic="{Topic}"/>
          <process id="{processKey}">
            <startEvent id="SE"><messageEventDefinition messageRef="M"/></startEvent>
          </process>
        </definitions>
        """;

    /// <summary>
    /// A second published model, so a WRONG JOIN KEY cannot pass (#557).
    /// </summary>
    /// <remarks>
    /// With one model holding one version, a join on model id alone — or on
    /// version number alone — still returns the right row, so the fixture could
    /// not tell a correct composite key from either broken one. Two models, each
    /// with its own published version, make id-only and version-only joins
    /// produce the wrong xml or duplicate rows.
    /// </remarks>
    private static WorkflowDeploymentInfo Deployment(string processKey, int version) => new()
    {
        DeploymentId = $"deployment-{processKey}-{version}",
        ProcessDefinitionId = $"definition-{processKey}-{version}",
        ProcessDefinitionKey = processKey,
        ProcessDefinitionVersion = version,
        DeployedAtUtc = DateTimeOffset.UtcNow
    };

    private static async Task PublishAsync(
        IWorkflowModelStore store, string name, string processKey, string messageName)
    {
        var draft = await store.SaveAsync(new WorkflowModel
        {
            Name = name,
            ProcessKey = processKey,
            BpmnXml = Xml(messageName, processKey)
        });

        await store.PublishAsync(draft, Deployment(processKey, 1));
    }

    /// <summary>
    /// A draft edit does not change what a published workflow listens for (#553),
    /// and neither does a SUPERSEDED version (#557).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The registry filtered on <c>PublishedVersionNumber</c> and then read
    /// <c>model.BpmnXml</c> — the WORKING copy. So for a published workflow,
    /// whatever the author last typed into the draft decided which message names
    /// the running system listened for, while Flowable went on executing the
    /// published definition.
    /// </para>
    /// <para>
    /// <b>Three names, three different states, one assertion set (#557).</b> The
    /// first fixture for this had one model with one version, which could not
    /// tell a correct composite join from an id-only or version-only one — both
    /// broken forms return the same single row when there is only one. So:
    /// </para>
    /// <list type="bullet">
    ///   <item><c>order.v1</c> — a SUPERSEDED published version. An id-only join
    ///   picks it up alongside the current one.</item>
    ///   <item><c>order.placed</c> — the current published version. The only one
    ///   that may appear for this model.</item>
    ///   <item><c>order.renamed</c> — an unpublished draft edit. The #553
    ///   defect.</item>
    /// </list>
    /// <para>
    /// A second model published on the same topic catches a version-only join,
    /// which would cross models at the same version number.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Only_the_current_published_version_of_each_model_is_registered()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = database.CreateWorkflowStore();

        // Version 1, published and then superseded.
        var draft = await store.SaveAsync(new WorkflowModel
        {
            Name = "Order Flow",
            ProcessKey = "OrderFlow",
            BpmnXml = Xml("order.v1")
        });
        var v1 = await store.PublishAsync(draft, Deployment("OrderFlow", 1));

        // Version 2, the current published one.
        var v2Draft = await store.SaveAsync(v1 with { BpmnXml = Xml("order.placed") });
        var published = await store.PublishAsync(v2Draft, Deployment("OrderFlow", 2));

        // Edited, NOT published. Flowable is still running `order.placed`.
        await store.SaveAsync(published with { BpmnXml = Xml("order.renamed") });

        // A second model on the same topic, so a version-only join crosses.
        await PublishAsync(store, "Shipping Flow", "ShippingFlow", "shipment.booked");

        var registry = new EfCoreWorkflowMessageRegistry(
            database.CreateDbContextFactory(),
            NullLogger<EfCoreWorkflowMessageRegistry>.Instance);
        await registry.RefreshAsync();

        var names = registry.GetMessageNamesForTopic(Topic);

        // EXACTLY these two. Set equality rather than three separate assertions,
        // because each broken join fails it in its own way: reading the draft
        // adds `order.renamed`, an id-only join adds `order.v1`, and a
        // version-only join crosses the two models.
        Assert.Equal<IEnumerable<string>>(
            new[] { "order.placed", "shipment.booked" },
            names.OrderBy(n => n, StringComparer.Ordinal).ToArray());
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
