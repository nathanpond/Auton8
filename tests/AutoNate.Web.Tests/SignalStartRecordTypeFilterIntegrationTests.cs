using AutoNate.Web.Models;
using AutoNate.Web.Services.BusWatcher;
using AutoNate.Web.Services.Records;
using AutoNate.Web.Services.Signals;
using AutoNate.Web.Services.Workflow;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using RecordTypeEntity = AutoNate.Web.Persistence.Scaffolded.RecordType;

namespace AutoNate.Web.Tests;

// End-to-end integration test for the signal-start record-type filter feature.
// Wires the real EfCoreWorkflowSignalRegistry, RecordTypeShortCodeCache, and
// WorkflowSignalDispatcher together against a real Postgres test DB, with a
// stub Flowable client capturing process starts. Asserts that filtered
// workflows fire only when the payload's recordTypeId resolves to a shortcode
// in the filter set, while unfiltered workflows always fire on signal match.
[Trait("Category", "Integration")]
public sealed class SignalStartRecordTypeFilterIntegrationTests
{
    [Fact]
    public async Task SignalDispatch_StartsBothFilteredAndUnfilteredWorkflows_OnMatchingRecordType()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();

        var assetTypeId = await SeedRecordTypeAsync(database, shortCode: "asset");
        var vehicleTypeId = await SeedRecordTypeAsync(database, shortCode: "vehicle");

        await PublishSignalStartWorkflowAsync(
            database,
            processKey: "AssetFlow",
            workflowName: "Asset Flow",
            recordTypeShortCodes: new[] { "asset" });
        await PublishSignalStartWorkflowAsync(
            database,
            processKey: "AnyRecordFlow",
            workflowName: "Any Record Flow",
            recordTypeShortCodes: Array.Empty<string>());

        var registry = new EfCoreWorkflowSignalRegistry(
            database.CreateDbContextFactory(),
            NullLogger<EfCoreWorkflowSignalRegistry>.Instance);
        await registry.RefreshAsync();

        var cache = new RecordTypeShortCodeCache(
            database.CreateDbContextFactory(),
            NullLogger<RecordTypeShortCodeCache>.Instance);
        await cache.RefreshAsync();

        var stub = new StubFlowableClient();
        var dispatcher = new WorkflowSignalDispatcher(
            registry,
            stub,
            cache,
            NullLogger<WorkflowSignalDispatcher>.Instance);

        // Asset payload — both AssetFlow (filter matches) and AnyRecordFlow
        // (unfiltered) should start.
        await dispatcher.HandleAsync(BuildMessage(assetTypeId));
        Assert.Equal(
            new[] { "AnyRecordFlow", "AssetFlow" },
            stub.StartedProcesses
                .Select(s => s.ProcessDefinitionKey)
                .OrderBy(k => k, StringComparer.Ordinal)
                .ToArray());

        stub.StartedProcesses.Clear();

        // Vehicle payload — AssetFlow's filter excludes it, only AnyRecordFlow
        // starts.
        await dispatcher.HandleAsync(BuildMessage(vehicleTypeId));
        var only = Assert.Single(stub.StartedProcesses);
        Assert.Equal("AnyRecordFlow", only.ProcessDefinitionKey);
    }

    [Fact]
    public async Task SignalDispatch_SkipsFilteredWorkflow_WhenPayloadRecordTypeIsUnknown()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();

        // Seed only 'asset' — the payload below references a record type id
        // that doesn't exist in the DB, so the cache won't resolve it.
        await SeedRecordTypeAsync(database, shortCode: "asset");

        await PublishSignalStartWorkflowAsync(
            database,
            processKey: "AssetFlow",
            workflowName: "Asset Flow",
            recordTypeShortCodes: new[] { "asset" });
        await PublishSignalStartWorkflowAsync(
            database,
            processKey: "AnyRecordFlow",
            workflowName: "Any Record Flow",
            recordTypeShortCodes: Array.Empty<string>());

        var registry = new EfCoreWorkflowSignalRegistry(
            database.CreateDbContextFactory(),
            NullLogger<EfCoreWorkflowSignalRegistry>.Instance);
        await registry.RefreshAsync();

        var cache = new RecordTypeShortCodeCache(
            database.CreateDbContextFactory(),
            NullLogger<RecordTypeShortCodeCache>.Instance);
        await cache.RefreshAsync();

        var stub = new StubFlowableClient();
        var dispatcher = new WorkflowSignalDispatcher(
            registry,
            stub,
            cache,
            NullLogger<WorkflowSignalDispatcher>.Instance);

        await dispatcher.HandleAsync(BuildMessage(Guid.NewGuid()));

        var only = Assert.Single(stub.StartedProcesses);
        Assert.Equal("AnyRecordFlow", only.ProcessDefinitionKey);
    }

    private static async Task<Guid> SeedRecordTypeAsync(
        PostgresTestDatabase database,
        string shortCode)
    {
        var factory = database.CreateDbContextFactory();
        await using var dbContext = await factory.CreateDbContextAsync();

        var now = DateTime.UtcNow;
        var entity = new RecordTypeEntity
        {
            Id = Guid.NewGuid(),
            ShortCode = shortCode,
            Name = shortCode,
            Description = null,
            Icon = null,
            Color = null,
            IsSystem = false,
            IsArchived = false,
            NextKeyNumber = 1,
            CreatedAtUtc = now,
            CreatedBy = Guid.Empty,
            UpdatedAtUtc = now,
            UpdatedBy = Guid.Empty
        };
        dbContext.RecordTypes.Add(entity);
        await dbContext.SaveChangesAsync();
        return entity.Id;
    }

    // Saves a draft + publishes a workflow whose signal start event listens on
    // `record.events` for `record.created` and (optionally) filters on the
    // given record-type shortcodes. Uses the real EfCoreWorkflowModelStore so
    // the published BPMN and version row are written exactly as production
    // would write them — the registry's RefreshAsync then re-reads them.
    private static async Task PublishSignalStartWorkflowAsync(
        PostgresTestDatabase database,
        string processKey,
        string workflowName,
        IReadOnlyList<string> recordTypeShortCodes)
    {
        var store = database.CreateWorkflowStore();

        var shortCodesAttribute = recordTypeShortCodes.Count == 0
            ? string.Empty
            : $" flowable:recordTypeShortCodes=\"{string.Join(",", recordTypeShortCodes)}\"";

        var bpmnXml =
            $"""
             <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          xmlns:flowable="http://flowable.org/bpmn">
               <signal id="Signal_record_created" name="record.created" flowable:topic="record.events"/>
               <process id="{processKey}">
                 <startEvent id="StartEvent_1">
                   <signalEventDefinition signalRef="Signal_record_created"{shortCodesAttribute}/>
                 </startEvent>
               </process>
             </definitions>
             """;

        var draft = await store.SaveAsync(new WorkflowModel
        {
            Name = workflowName,
            ProcessKey = processKey,
            BpmnXml = bpmnXml
        });

        await store.PublishAsync(draft, new WorkflowDeploymentInfo
        {
            DeploymentId = $"deployment-{processKey}",
            ProcessDefinitionId = $"definition-{processKey}",
            ProcessDefinitionKey = processKey,
            ProcessDefinitionVersion = 1,
            DeployedAtUtc = DateTimeOffset.UtcNow
        });
    }

    private static BusWatcherStreamService.BusWatcherMessage BuildMessage(Guid recordTypeId)
    {
        var payload = $$"""
            {"eventType":"record.created","recordTypeId":"{{recordTypeId}}"}
            """;

        return new BusWatcherStreamService.BusWatcherMessage(
            DateTimeOffset.UtcNow,
            "record.events",
            "application/json",
            new Dictionary<string, string>(),
            payload);
    }
}
