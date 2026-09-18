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
    private const string Topic = "record.events";

    private static string Xml(string signalName, string processKey = "OrderFlow") =>
        $"""
        <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL"
                     xmlns:flowable="http://flowable.org/bpmn">
          <signal id="S" name="{signalName}" flowable:topic="{Topic}"/>
          <process id="{processKey}">
            <startEvent id="SE"><signalEventDefinition signalRef="S"/></startEvent>
          </process>
        </definitions>
        """;

    private static WorkflowDeploymentInfo Deployment(string processKey, int version) => new()
    {
        DeploymentId = $"deployment-{processKey}-{version}",
        ProcessDefinitionId = $"definition-{processKey}-{version}",
        ProcessDefinitionKey = processKey,
        ProcessDefinitionVersion = version,
        DeployedAtUtc = DateTimeOffset.UtcNow
    };

    /// <summary>
    /// A draft edit does not change what the registry subscribes to (#553), and
    /// neither does a SUPERSEDED version (#557).
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
    /// the xml wrong, which is the half that bit.
    /// </para>
    /// <para>
    /// <b>Three names, three states (#557).</b> The first fixture for this had
    /// one model with one version, which cannot tell a correct composite join
    /// from an id-only or version-only one — every broken form returns the same
    /// single row when there is only one. <c>record.v1</c> is a superseded
    /// published version (an id-only join picks it up), <c>record.created</c> is
    /// the current one, <c>record.renamed</c> is the unpublished draft edit; and
    /// a second model published on the same topic catches a version-only join
    /// crossing models.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Only_the_current_published_version_of_each_model_is_subscribed()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = database.CreateWorkflowStore();

        var draft = await store.SaveAsync(new WorkflowModel
        {
            Name = "Order Flow",
            ProcessKey = "OrderFlow",
            BpmnXml = Xml("record.v1")
        });
        var v1 = await store.PublishAsync(draft, Deployment("OrderFlow", 1));

        var v2Draft = await store.SaveAsync(v1 with { BpmnXml = Xml("record.created") });
        var published = await store.PublishAsync(v2Draft, Deployment("OrderFlow", 2));

        // The edit that used to move the subscription. Not published.
        await store.SaveAsync(published with { BpmnXml = Xml("record.renamed") });

        // A second model on the same topic, so a version-only join crosses.
        var other = await store.SaveAsync(new WorkflowModel
        {
            Name = "Shipping Flow",
            ProcessKey = "ShippingFlow",
            BpmnXml = Xml("shipment.booked", "ShippingFlow")
        });
        await store.PublishAsync(other, Deployment("ShippingFlow", 1));

        var registry = new EfCoreWorkflowSignalRegistry(
            database.CreateDbContextFactory(),
            NullLogger<EfCoreWorkflowSignalRegistry>.Instance);
        await registry.RefreshAsync();

        var names = registry.GetSignalNamesForTopic(Topic);

        // EXACTLY these two. Each broken join fails it differently: reading the
        // draft adds `record.renamed`, an id-only join adds `record.v1`, and a
        // version-only join crosses the two models.
        Assert.Equal<IEnumerable<string>>(
            new[] { "record.created", "shipment.booked" },
            names.OrderBy(n => n, StringComparer.Ordinal).ToArray());
    }
}
