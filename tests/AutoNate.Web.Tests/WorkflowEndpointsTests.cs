using System.Net;
using System.Net.Http.Json;
using AutoNate.Web.Endpoints;
using AutoNate.Web.Models;
using AutoNate.Web.Services.Workflow;
using Xunit;

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
    public async Task StartProcessInstance_DelegatesToFlowableStub()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await PrimeAuthAsync(client);

        var response = await client.PostAsJsonAsync(
            "/api/workflows/my_flow/start",
            new WorkflowEndpoints.StartInstanceRequest(null));
        response.EnsureSuccessStatusCode();

        Assert.Contains("Start:my_flow", factory.FlowableStub.Calls);
    }

    private static async Task PrimeAuthAsync(HttpClient client)
    {
        (await client.GetAsync("/api/workflows/")).EnsureSuccessStatusCode();
    }
}
