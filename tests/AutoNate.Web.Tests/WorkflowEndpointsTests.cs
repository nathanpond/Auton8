using System.Net;
using System.Net.Http.Json;
using AutoNate.Web.Endpoints;
using AutoNate.Web.Models;
using AutoNate.Web.Services.Workflow;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using RecordTypeEntity = AutoNate.Web.Persistence.Scaffolded.RecordType;

namespace AutoNate.Web.Tests;

[Trait("Category", "Integration")]
public sealed class WorkflowEndpointsTests
{
    private const string SimpleBpmn = """
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI"
                          xmlns:dc="http://www.omg.org/spec/DD/20100524/DC"
                          xmlns:di="http://www.omg.org/spec/DD/20100524/DI"
                          id="Definitions_1"
                          targetNamespace="http://autonate.dev/workflows">
          <bpmn:process id="simple_flow" name="Simple Flow" isExecutable="true">
            <bpmn:startEvent id="StartEvent_1" />
          </bpmn:process>
        </bpmn:definitions>
        """;

    [Fact]
    public async Task ListWorkflows_OnEmptyDatabase_ReturnsEmpty()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();

        var models = await client.GetFromJsonAsync<WorkflowModel[]>("/api/workflows/");

        Assert.NotNull(models);
        Assert.Empty(models);
    }

    [Fact]
    public async Task GetLatestWorkflow_OnEmptyDatabase_Returns404()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/workflows/latest");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetWorkflow_NotFound_Returns404()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();

        var response = await client.GetAsync($"/api/workflows/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetWorkflowVersions_OnUnknownId_ReturnsEmpty()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();

        var versions = await client.GetFromJsonAsync<WorkflowModelVersion[]>(
            $"/api/workflows/{Guid.NewGuid()}/versions");

        Assert.NotNull(versions);
        Assert.Empty(versions);
    }

    [Fact]
    public async Task SaveWorkflow_RoundTrips()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await PrimeAuthAsync(client);

        var model = new WorkflowModel
        {
            Id = Guid.NewGuid(),
            Name = "My Flow",
            ProcessKey = "my_flow",
            BpmnXml = SimpleBpmn
        };

        var response = await client.PostAsJsonAsync("/api/workflows/", model);
        response.EnsureSuccessStatusCode();
        var saved = await response.Content.ReadFromJsonAsync<WorkflowModel>();
        Assert.NotNull(saved);
        Assert.Equal(model.Id, saved.Id);
        Assert.Equal("My Flow", saved.Name);

        var listed = await client.GetFromJsonAsync<WorkflowModel[]>("/api/workflows/");
        Assert.NotNull(listed);
        Assert.Single(listed);
    }

    [Fact]
    public async Task PrepareWorkflow_NormalizesNameAndProcessKey()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await PrimeAuthAsync(client);

        var request = new PrepareWorkflowRequest(
            new WorkflowModel
            {
                Id = Guid.NewGuid(),
                Name = "  Spaced Name  ",
                ProcessKey = string.Empty,
                BpmnXml = SimpleBpmn
            },
            Array.Empty<WorkflowElementSnapshot>());

        var response = await client.PostAsJsonAsync("/api/workflows/prepare", request);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<PrepareWorkflowResponse>();

        Assert.NotNull(result);
        Assert.Equal("Spaced Name", result.Model.Name);
        Assert.False(string.IsNullOrWhiteSpace(result.Model.ProcessKey));
    }

    [Fact]
    public async Task PrepareWorkflow_ReturnsWarning_WhenSignalFilterReferencesUnknownShortCode()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await PrimeAuthAsync(client);

        // Seed a known record type so we can verify the warning is selective.
        await SeedRecordTypeAsync(factory, "asset");

        const string xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                              xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI"
                              xmlns:flowable="http://flowable.org/bpmn"
                              id="Definitions_1"
                              targetNamespace="http://autonate.dev/workflows">
              <bpmn:signal id="Signal_record_created" name="record.created" flowable:topic="record.events" />
              <bpmn:process id="filter_flow" name="Filter Flow" isExecutable="true">
                <bpmn:startEvent id="StartEvent_1">
                  <bpmn:signalEventDefinition signalRef="Signal_record_created" flowable:recordTypeShortCodes="asset,unknownType" />
                </bpmn:startEvent>
              </bpmn:process>
            </bpmn:definitions>
            """;

        var request = new PrepareWorkflowRequest(
            new WorkflowModel
            {
                Id = Guid.NewGuid(),
                Name = "Filter Flow",
                ProcessKey = "filter_flow",
                BpmnXml = xml
            },
            Array.Empty<WorkflowElementSnapshot>());

        var response = await client.PostAsJsonAsync("/api/workflows/prepare", request);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<PrepareWorkflowResponse>();

        Assert.NotNull(result);
        // Publish proceeds — no errors from this rule.
        Assert.DoesNotContain(result.Errors,
            e => e.Contains("recordTypeShortCodes", StringComparison.OrdinalIgnoreCase));
        // The unknown shortcode is named in the warning.
        Assert.Contains(result.Warnings,
            w => w.Contains("unknownType", StringComparison.OrdinalIgnoreCase));
        // The known shortcode is NOT named — only unknowns.
        Assert.DoesNotContain(result.Warnings,
            w => w.Contains("asset", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task PrepareWorkflow_NoRecordTypeWarning_WhenAllShortCodesKnown()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await PrimeAuthAsync(client);

        await SeedRecordTypeAsync(factory, "asset");

        const string xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                              xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI"
                              xmlns:flowable="http://flowable.org/bpmn"
                              id="Definitions_1"
                              targetNamespace="http://autonate.dev/workflows">
              <bpmn:signal id="Signal_record_created" name="record.created" flowable:topic="record.events" />
              <bpmn:process id="known_flow" name="Known Flow" isExecutable="true">
                <bpmn:startEvent id="StartEvent_1">
                  <bpmn:signalEventDefinition signalRef="Signal_record_created" flowable:recordTypeShortCodes="asset" />
                </bpmn:startEvent>
              </bpmn:process>
            </bpmn:definitions>
            """;

        var request = new PrepareWorkflowRequest(
            new WorkflowModel
            {
                Id = Guid.NewGuid(),
                Name = "Known Flow",
                ProcessKey = "known_flow",
                BpmnXml = xml
            },
            Array.Empty<WorkflowElementSnapshot>());

        var response = await client.PostAsJsonAsync("/api/workflows/prepare", request);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<PrepareWorkflowResponse>();

        Assert.NotNull(result);
        Assert.DoesNotContain(result.Warnings,
            w => w.Contains("not found in this environment", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task SeedRecordTypeAsync(AutoNateWebApplicationFactory factory, string shortCode)
    {
        var dbFactory = factory.Database.CreateDbContextFactory();
        await using var dbContext = await dbFactory.CreateDbContextAsync();

        var now = DateTime.UtcNow;
        dbContext.RecordTypes.Add(new RecordTypeEntity
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
        });
        await dbContext.SaveChangesAsync();
    }

    [Fact]
    public async Task PublishWorkflow_DelegatesToFlowableStub()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await PrimeAuthAsync(client);

        var id = Guid.NewGuid();
        var model = new WorkflowModel
        {
            Id = id,
            Name = "Publish Me",
            ProcessKey = "publish_me",
            BpmnXml = SimpleBpmn
        };
        // Save first so the store has a row to publish.
        (await client.PostAsJsonAsync("/api/workflows/", model)).EnsureSuccessStatusCode();

        var response = await client.PostAsJsonAsync(
            $"/api/workflows/{id}/publish",
            model);
        response.EnsureSuccessStatusCode();

        Assert.Contains("Deploy:publish_me", factory.FlowableStub.Calls);
    }

    [Fact]
    public async Task PublishWorkflow_MismatchedId_Returns400()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await PrimeAuthAsync(client);

        var response = await client.PostAsJsonAsync(
            $"/api/workflows/{Guid.NewGuid()}/publish",
            new WorkflowModel
            {
                Id = Guid.NewGuid(),
                Name = "x",
                ProcessKey = "x",
                BpmnXml = SimpleBpmn
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task StartProcessInstance_AutoNamesUsingModelNameAndCount_WhenRequestNameIsMissing()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await PrimeAuthAsync(client);

        // Seed a workflow model so the auto-name lookup finds a label.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IWorkflowModelStore>();
            await store.SaveAsync(new WorkflowModel
            {
                Id = Guid.NewGuid(),
                Name = "Lead Qualification",
                ProcessKey = "my_flow",
                BpmnXml = string.Empty
            });
        }

        // Three existing runs → next auto-name should be "(4)".
        factory.FlowableStub.InstanceCountsByDefinitionKey["my_flow"] = 3;

        var response = await client.PostAsJsonAsync(
            "/api/workflows/my_flow/start",
            new WorkflowEndpoints.StartInstanceRequest(null, null));
        response.EnsureSuccessStatusCode();

        Assert.Contains("Start:my_flow:Lead Qualification (4)", factory.FlowableStub.Calls);
    }

    [Fact]
    public async Task StartProcessInstance_PassesExplicitName_VerbatimToFlowable()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await PrimeAuthAsync(client);

        var response = await client.PostAsJsonAsync(
            "/api/workflows/my_flow/start",
            new WorkflowEndpoints.StartInstanceRequest("Custom Run Label", null));
        response.EnsureSuccessStatusCode();

        Assert.Contains("Start:my_flow:Custom Run Label", factory.FlowableStub.Calls);
        // No count lookup when caller supplied a name.
        Assert.DoesNotContain(factory.FlowableStub.Calls, c => c.StartsWith("CountByDefinitionKey:"));
    }

    [Fact]
    public async Task PauseWorkflow_OnUnpublished_Returns400()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await PrimeAuthAsync(client);

        var id = Guid.NewGuid();
        (await client.PostAsJsonAsync("/api/workflows/", new WorkflowModel
        {
            Id = id,
            Name = "Unpublished",
            ProcessKey = "unpublished",
            BpmnXml = SimpleBpmn
        })).EnsureSuccessStatusCode();

        var response = await client.PostAsync($"/api/workflows/{id}/pause", null);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain(factory.FlowableStub.Calls, c => c.StartsWith("SuspendDefinition:"));
    }

    [Fact]
    public async Task PauseWorkflow_OnMissing_Returns404()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await PrimeAuthAsync(client);

        var response = await client.PostAsync($"/api/workflows/{Guid.NewGuid()}/pause", null);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PauseAndResumeWorkflow_ToggleFlowableSuspendedState()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await PrimeAuthAsync(client);

        var id = Guid.NewGuid();
        var model = new WorkflowModel
        {
            Id = id,
            Name = "Pausable",
            ProcessKey = "pausable",
            BpmnXml = SimpleBpmn
        };
        (await client.PostAsJsonAsync("/api/workflows/", model)).EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync($"/api/workflows/{id}/publish", model)).EnsureSuccessStatusCode();

        // Seed the stub so GetLatestProcessDefinitionAsync (used by pause/resume
        // and the IsSuspended augmentation) returns a real definition.
        factory.FlowableStub.ProcessDefinitionsByKey["pausable"] = new Models.FlowableProcessDefinitionSummary
        {
            Id = "pd-pausable",
            Key = "pausable",
            Version = 1,
            DeploymentId = "dep-pausable",
            Suspended = false
        };

        var pauseResponse = await client.PostAsync($"/api/workflows/{id}/pause", null);
        pauseResponse.EnsureSuccessStatusCode();
        var paused = await pauseResponse.Content.ReadFromJsonAsync<WorkflowModel>();
        Assert.NotNull(paused);
        Assert.True(paused.IsSuspended);
        Assert.Contains("SuspendDefinition:pausable", factory.FlowableStub.Calls);

        var resumeResponse = await client.PostAsync($"/api/workflows/{id}/resume", null);
        resumeResponse.EnsureSuccessStatusCode();
        var resumed = await resumeResponse.Content.ReadFromJsonAsync<WorkflowModel>();
        Assert.NotNull(resumed);
        Assert.False(resumed.IsSuspended);
        Assert.Contains("ActivateDefinition:pausable", factory.FlowableStub.Calls);
    }

    [Fact]
    public async Task ListWorkflows_PopulatesIsSuspendedFromFlowable()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await PrimeAuthAsync(client);

        var id = Guid.NewGuid();
        var model = new WorkflowModel
        {
            Id = id,
            Name = "Listed",
            ProcessKey = "listed",
            BpmnXml = SimpleBpmn
        };
        (await client.PostAsJsonAsync("/api/workflows/", model)).EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync($"/api/workflows/{id}/publish", model)).EnsureSuccessStatusCode();

        factory.FlowableStub.ProcessDefinitionsByKey["listed"] = new Models.FlowableProcessDefinitionSummary
        {
            Id = "pd-listed",
            Key = "listed",
            Version = 1,
            DeploymentId = "dep-listed",
            Suspended = true
        };

        var listed = await client.GetFromJsonAsync<WorkflowModel[]>("/api/workflows/");
        Assert.NotNull(listed);
        var found = Assert.Single(listed);
        Assert.True(found.IsSuspended);
    }

    private static async Task PrimeAuthAsync(HttpClient client)
    {
        (await client.GetAsync("/api/workflows/")).EnsureSuccessStatusCode();
    }
}
