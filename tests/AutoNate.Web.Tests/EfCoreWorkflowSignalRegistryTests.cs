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
}
