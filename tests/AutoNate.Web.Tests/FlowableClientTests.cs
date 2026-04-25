using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AutoNate.Web.Configuration;
using AutoNate.Web.Models;
using AutoNate.Web.Services.Flowable;
using Microsoft.Extensions.Options;
using Xunit;

namespace AutoNate.Web.Tests;

public sealed class FlowableClientTests
{
    private const string BaseAddress = "http://flowable.test/flowable/";

    private const string SimpleBpmn = """
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL">
          <bpmn:process id="p" name="Process" isExecutable="true">
            <bpmn:startEvent id="start" />
          </bpmn:process>
        </bpmn:definitions>
        """;

    private const string BpmnWithScriptTask = """
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL">
          <bpmn:process id="p" name="Process" isExecutable="true">
            <bpmn:startEvent id="start" />
            <bpmn:scriptTask id="t" scriptFormat="javascript">
              <bpmn:script>x = 1;</bpmn:script>
            </bpmn:scriptTask>
          </bpmn:process>
        </bpmn:definitions>
        """;

    private const string BpmnWithDiagram = """
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI">
          <bpmn:process id="p"><bpmn:startEvent id="start" /></bpmn:process>
          <bpmndi:BPMNDiagram id="d" />
        </bpmn:definitions>
        """;

    // --- ConfigureHttpClient -------------------------------------------------

    [Fact]
    public void ConfigureHttpClient_AppendsTrailingSlashToBaseUrl()
    {
        var http = new HttpClient();
        FlowableClient.ConfigureHttpClient(http, new FlowableOptions { BaseUrl = "http://example.com/api" });

        Assert.Equal(new Uri("http://example.com/api/"), http.BaseAddress);
    }

    [Fact]
    public void ConfigureHttpClient_AddsBasicAuthHeader_WhenUsernameProvided()
    {
        var http = new HttpClient();
        FlowableClient.ConfigureHttpClient(http, new FlowableOptions
        {
            BaseUrl = "http://example.com",
            Username = "admin",
            Password = "secret"
        });

        var auth = http.DefaultRequestHeaders.Authorization;
        Assert.NotNull(auth);
        Assert.Equal("Basic", auth.Scheme);
        var decoded = Encoding.ASCII.GetString(Convert.FromBase64String(auth.Parameter!));
        Assert.Equal("admin:secret", decoded);
    }

    [Fact]
    public void ConfigureHttpClient_OmitsAuthHeader_WhenUsernameMissing()
    {
        var http = new HttpClient();
        FlowableClient.ConfigureHttpClient(http, new FlowableOptions { BaseUrl = "http://example.com/" });

        Assert.Null(http.DefaultRequestHeaders.Authorization);
    }

    [Fact]
    public void ConfigureHttpClient_ThrowsWhenBaseUrlMissing()
    {
        var http = new HttpClient();
        Assert.Throws<InvalidOperationException>(() =>
            FlowableClient.ConfigureHttpClient(http, new FlowableOptions()));
    }

    // --- GetLatestProcessDefinitionAsync -------------------------------------

    [Fact]
    public async Task GetLatestProcessDefinitionAsync_ReturnsNull_WhenNoData()
    {
        var (client, stub) = CreateClient();
        stub.WhenJson(HttpMethod.Get, "service/repository/process-definitions",
            new { data = Array.Empty<object>() });

        var result = await client.GetLatestProcessDefinitionAsync("missing_key");

        Assert.Null(result);
        Assert.Contains(stub.Requests, r => r.Url.Contains("key=missing_key") && r.Url.Contains("latest=true"));
    }

    [Fact]
    public async Task GetLatestProcessDefinitionAsync_MapsResponseFields()
    {
        var (client, stub) = CreateClient();
        stub.WhenJson(HttpMethod.Get, "service/repository/process-definitions",
            new
            {
                data = new[]
                {
                    new
                    {
                        id = "pd-1",
                        key = "my_flow",
                        name = "My Flow",
                        version = 3,
                        deploymentId = "dep-1"
                    }
                }
            });

        var result = await client.GetLatestProcessDefinitionAsync("my_flow");

        Assert.NotNull(result);
        Assert.Equal("pd-1", result.Id);
        Assert.Equal("my_flow", result.Key);
        Assert.Equal(3, result.Version);
        Assert.Equal("dep-1", result.DeploymentId);
    }

    [Fact]
    public async Task GetLatestProcessDefinitionAsync_ThrowsOnNon2xx()
    {
        var (client, stub) = CreateClient();
        stub.WhenStatus(HttpMethod.Get, "service/repository/process-definitions",
            HttpStatusCode.InternalServerError, "boom");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.GetLatestProcessDefinitionAsync("k"));
        Assert.Contains("Flowable could not query the latest deployed process definition", ex.Message);
    }

    // --- StartProcessInstanceAsync -------------------------------------------

    [Fact]
    public async Task StartProcessInstanceAsync_PostsProcessKey_AndReturnsSummary()
    {
        var (client, stub) = CreateClient();
        stub.WhenJson(HttpMethod.Post, "service/runtime/process-instances",
            new
            {
                id = "inst-1",
                processDefinitionId = "pd-1",
                activityId = "start",
                suspended = false
            });

        var summary = await client.StartProcessInstanceAsync("my_flow");

        Assert.Equal("inst-1", summary.Id);
        Assert.Equal("pd-1", summary.ProcessDefinitionId);

        var sent = Assert.Single(stub.Requests);
        Assert.Contains("\"processDefinitionKey\":\"my_flow\"", sent.Body);
        // No variables provided — empty variables array.
        Assert.Contains("\"variables\":[]", sent.Body);
    }

    [Fact]
    public async Task StartProcessInstanceAsync_SerializesProvidedVariables()
    {
        var (client, stub) = CreateClient();
        stub.WhenJson(HttpMethod.Post, "service/runtime/process-instances",
            new { id = "i", processDefinitionId = "p", activityId = (string?)null, suspended = false });

        await client.StartProcessInstanceAsync(
            "my_flow",
            new Dictionary<string, object?> { ["foo"] = 42, ["bar"] = "baz" });

        var body = Assert.Single(stub.Requests).Body!;
        Assert.Contains("\"name\":\"foo\"", body);
        Assert.Contains("\"value\":42", body);
        Assert.Contains("\"name\":\"bar\"", body);
        Assert.Contains("\"value\":\"baz\"", body);
    }

    // --- GetProcessInstanceAsync ---------------------------------------------

    [Fact]
    public async Task GetProcessInstanceAsync_Returns200Response()
    {
        var (client, stub) = CreateClient();
        stub.WhenJson(HttpMethod.Get, "service/runtime/process-instances/inst-1",
            new { id = "inst-1", processDefinitionId = "pd-1", activityId = "step", suspended = true });

        var result = await client.GetProcessInstanceAsync("inst-1");

        Assert.NotNull(result);
        Assert.Equal("inst-1", result.Id);
        Assert.True(result.Suspended);
        Assert.Equal("step", result.ActivityId);
    }

    [Fact]
    public async Task GetProcessInstanceAsync_ReturnsNull_OnHttp404()
    {
        var (client, stub) = CreateClient();
        stub.WhenStatus(HttpMethod.Get, "service/runtime/process-instances/missing",
            HttpStatusCode.NotFound);

        var result = await client.GetProcessInstanceAsync("missing");

        Assert.Null(result);
    }

    // --- DeleteWorkflowExecutionAsync ----------------------------------------

    [Fact]
    public async Task DeleteWorkflowExecutionAsync_DeletesRuntimeAndHistoricRows_WhenInstanceIsRunning()
    {
        var (client, stub) = CreateClient();
        stub.WhenJson(HttpMethod.Get, "service/runtime/process-instances/inst-1",
            new { id = "inst-1", processDefinitionId = "pd-1", activityId = (string?)null, suspended = false });
        stub.WhenStatus(HttpMethod.Delete, "service/runtime/process-instances/inst-1", HttpStatusCode.NoContent);
        stub.WhenStatus(HttpMethod.Delete, "service/history/historic-process-instances/inst-1", HttpStatusCode.NoContent);

        await client.DeleteWorkflowExecutionAsync("inst-1");

        Assert.Contains(stub.Requests, r =>
            r.Method == HttpMethod.Delete && r.Url.Contains("service/runtime/process-instances/inst-1"));
        Assert.Contains(stub.Requests, r =>
            r.Method == HttpMethod.Delete && r.Url.Contains("service/history/historic-process-instances/inst-1"));
    }

    [Fact]
    public async Task DeleteWorkflowExecutionAsync_SkipsRuntimeDelete_WhenInstanceAlreadyEnded()
    {
        var (client, stub) = CreateClient();
        // Initial GET returns 404 → instance has finished, only history needs deleting.
        stub.WhenStatus(HttpMethod.Get, "service/runtime/process-instances/inst-2", HttpStatusCode.NotFound);
        stub.WhenStatus(HttpMethod.Delete, "service/history/historic-process-instances/inst-2", HttpStatusCode.NoContent);

        await client.DeleteWorkflowExecutionAsync("inst-2");

        Assert.DoesNotContain(stub.Requests, r =>
            r.Method == HttpMethod.Delete && r.Url.Contains("service/runtime/process-instances/inst-2"));
    }

    [Fact]
    public async Task DeleteWorkflowExecutionAsync_SwallowsHistory404()
    {
        var (client, stub) = CreateClient();
        stub.WhenStatus(HttpMethod.Get, "service/runtime/process-instances/inst-3", HttpStatusCode.NotFound);
        stub.WhenStatus(HttpMethod.Delete, "service/history/historic-process-instances/inst-3", HttpStatusCode.NotFound);

        // Should not throw — both 404s are tolerated.
        await client.DeleteWorkflowExecutionAsync("inst-3");
    }

    // --- GetTasksByProcessInstanceAsync --------------------------------------

    [Fact]
    public async Task GetTasksByProcessInstanceAsync_MapsTaskFields()
    {
        var (client, stub) = CreateClient();
        stub.WhenJson(HttpMethod.Get, "service/runtime/tasks", new
        {
            data = new[]
            {
                new
                {
                    id = "t-1",
                    name = "Approve",
                    taskDefinitionKey = "approve",
                    assignee = "alice",
                    processInstanceId = "inst-1",
                    processDefinitionId = "pd-1",
                    createTime = "2026-04-01T00:00:00Z",
                    dueDate = (string?)null
                }
            }
        });

        var tasks = await client.GetTasksByProcessInstanceAsync("inst-1");

        var task = Assert.Single(tasks);
        Assert.Equal("t-1", task.Id);
        Assert.Equal("Approve", task.Name);
        Assert.Equal("alice", task.Assignee);
        Assert.Equal("inst-1", task.ProcessInstanceId);
    }

    // --- CompleteTaskAsync ---------------------------------------------------

    [Fact]
    public async Task CompleteTaskAsync_PostsCompleteAction()
    {
        var (client, stub) = CreateClient();
        stub.WhenStatus(HttpMethod.Post, "service/runtime/tasks/t-1", HttpStatusCode.OK);

        await client.CompleteTaskAsync("t-1");

        var sent = Assert.Single(stub.Requests);
        Assert.Contains("\"action\":\"complete\"", sent.Body);
        Assert.Contains("\"variables\":[]", sent.Body);
    }

    [Fact]
    public async Task CompleteTaskAsync_IncludesVariables()
    {
        var (client, stub) = CreateClient();
        stub.WhenStatus(HttpMethod.Post, "service/runtime/tasks/t-2", HttpStatusCode.OK);

        await client.CompleteTaskAsync("t-2",
            new Dictionary<string, object?> { ["approved"] = true });

        var body = Assert.Single(stub.Requests).Body!;
        Assert.Contains("\"name\":\"approved\"", body);
        Assert.Contains("\"value\":true", body);
    }

    [Fact]
    public async Task CompleteTaskAsync_ThrowsWithFlowablesErrorBody_OnNon2xx()
    {
        var (client, stub) = CreateClient();
        stub.WhenStatus(HttpMethod.Post, "service/runtime/tasks/t-3",
            HttpStatusCode.BadRequest, "task already completed");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.CompleteTaskAsync("t-3"));
        Assert.Contains("Flowable could not complete the user task", ex.Message);
        Assert.Contains("task already completed", ex.Message);
    }

    // --- GetTasksAssignedToUserAsync -----------------------------------------

    [Fact]
    public async Task GetTasksAssignedToUserAsync_MergesAssigneeAndCandidateTasks_AndDedupesById()
    {
        var (client, stub) = CreateClient();

        // Both assignee and candidate queries hit the same path; route by query.
        stub.When(HttpMethod.Get, "service/runtime/tasks", request =>
        {
            var q = request.RequestUri!.Query;
            return q.Contains("assignee=", StringComparison.Ordinal)
                ? StubHttpMessageHandler.JsonResponse(new
                {
                    data = new[]
                    {
                        new { id = "t-shared", name = "T", taskDefinitionKey = "k", assignee = "u",
                              processInstanceId = "i", processDefinitionId = "pd-1",
                              createTime = "2026-04-01T00:00:00Z", dueDate = (string?)null },
                        new { id = "t-only-assignee", name = "OnlyA", taskDefinitionKey = "k", assignee = "u",
                              processInstanceId = "i", processDefinitionId = "pd-1",
                              createTime = "2026-04-01T00:00:00Z", dueDate = (string?)null }
                    }
                })
                : StubHttpMessageHandler.JsonResponse(new
                {
                    data = new[]
                    {
                        new { id = "t-shared", name = "T", taskDefinitionKey = "k", assignee = (string?)null,
                              processInstanceId = "i", processDefinitionId = "pd-1",
                              createTime = "2026-04-01T00:00:00Z", dueDate = (string?)null },
                        new { id = "t-only-candidate", name = "OnlyC", taskDefinitionKey = "k", assignee = (string?)null,
                              processInstanceId = "i", processDefinitionId = "pd-1",
                              createTime = "2026-04-02T00:00:00Z", dueDate = (string?)null }
                    }
                });
        });
        stub.WhenJson(HttpMethod.Get, "service/repository/process-definitions/pd-1",
            new { id = "pd-1", key = "k", name = "My Process", version = 1 });

        var tasks = await client.GetTasksAssignedToUserAsync("u");

        Assert.Equal(3, tasks.Count); // shared deduped to one
        Assert.Single(tasks, t => t.Id == "t-shared");
        Assert.All(tasks, t => Assert.Equal("My Process", t.ProcessDefinitionName));
    }

    // --- GetWorkflowExecutionsAsync ------------------------------------------

    [Fact]
    public async Task GetWorkflowExecutionsAsync_ClassifiesRunningAndCompletedInstances()
    {
        var (client, stub) = CreateClient();
        stub.WhenJson(HttpMethod.Get, "service/history/historic-process-instances", new
        {
            data = new[]
            {
                new { id = "inst-running", processDefinitionId = "pd-1",
                      startTime = "2026-04-01T00:00:00Z", endTime = (string?)null },
                new { id = "inst-done", processDefinitionId = "pd-1",
                      startTime = "2026-03-01T00:00:00Z", endTime = "2026-03-02T00:00:00Z" }
            }
        });
        stub.When(HttpMethod.Get, "service/runtime/process-instances", _ =>
            StubHttpMessageHandler.JsonResponse(new
            {
                data = new[]
                {
                    new { id = "inst-running", processDefinitionId = "pd-1",
                          activityId = "task-step", suspended = false }
                }
            }));
        stub.WhenJson(HttpMethod.Get, "service/runtime/tasks", new { data = Array.Empty<object>() });
        stub.WhenJson(HttpMethod.Get, "service/history/historic-activity-instances", new { data = Array.Empty<object>() });
        stub.WhenJson(HttpMethod.Get, "service/repository/process-definitions/pd-1",
            new { id = "pd-1", key = "k", name = "My Flow", version = 1 });

        var executions = await client.GetWorkflowExecutionsAsync();

        Assert.Equal(2, executions.Count);
        var running = executions.Single(e => e.Id == "inst-running");
        Assert.Equal("Running", running.Status);
        Assert.Equal("task-step", running.CurrentStep);
        Assert.Equal("My Flow", running.WorkflowModelName);

        var done = executions.Single(e => e.Id == "inst-done");
        Assert.Equal("Complete", done.Status);
        Assert.Null(done.CurrentStep);
    }

    // --- GetWorkflowExecutionDiagramDetailAsync ------------------------------

    [Fact]
    public async Task GetWorkflowExecutionDiagramDetailAsync_ReturnsBpmnAndActivityIds()
    {
        var (client, stub) = CreateClient();

        stub.WhenJson(HttpMethod.Get, "service/history/historic-process-instances/inst-1", new
        {
            id = "inst-1",
            processDefinitionId = "pd-1",
            startTime = "2026-04-01T00:00:00Z",
            endTime = (string?)null
        });
        stub.WhenJson(HttpMethod.Get, "service/repository/process-definitions/pd-1", new
        {
            id = "pd-1",
            key = "k",
            name = "My Flow",
            version = 1,
            graphicalNotationDefined = true
        });
        stub.When(HttpMethod.Get, "service/repository/process-definitions/pd-1/resourcedata", _ =>
            StubHttpMessageHandler.TextResponse(BpmnWithDiagram, mediaType: "application/xml"));
        stub.WhenJson(HttpMethod.Get, "service/history/historic-activity-instances", new
        {
            data = new[]
            {
                new { activityId = "start", processInstanceId = "inst-1",
                      startTime = "2026-04-01T00:00:00Z", endTime = "2026-04-01T00:01:00Z" },
                new { activityId = "task-step", processInstanceId = "inst-1",
                      startTime = "2026-04-01T00:01:00Z", endTime = (string?)null }
            }
        });
        stub.WhenJson(HttpMethod.Get, "service/history/historic-variable-instances", new
        {
            data = new[]
            {
                new
                {
                    variable = new { name = "amount", type = "long", value = 42 },
                    createTime = "2026-04-01T00:00:00Z",
                    lastUpdatedTime = "2026-04-01T00:01:00Z"
                }
            }
        });

        var detail = await client.GetWorkflowExecutionDiagramDetailAsync("inst-1");

        Assert.Equal("inst-1", detail.ExecutionId);
        Assert.Contains("BPMNDiagram", detail.BpmnXml);
        Assert.Contains("start", detail.CompletedActivityIds);
        Assert.Contains("task-step", detail.CurrentActivityIds);
        var amount = Assert.Single(detail.Variables);
        Assert.Equal("amount", amount.Name);
        Assert.Equal("42", amount.Value);
    }

    [Fact]
    public async Task GetWorkflowExecutionDiagramDetailAsync_ThrowsWhenGraphicalNotationMissing()
    {
        var (client, stub) = CreateClient();

        stub.WhenJson(HttpMethod.Get, "service/history/historic-process-instances/inst-1", new
        {
            id = "inst-1", processDefinitionId = "pd-1",
            startTime = "2026-04-01T00:00:00Z", endTime = (string?)null
        });
        stub.WhenJson(HttpMethod.Get, "service/repository/process-definitions/pd-1", new
        {
            id = "pd-1", key = "k", name = "Flow", version = 1,
            graphicalNotationDefined = false
        });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.GetWorkflowExecutionDiagramDetailAsync("inst-1"));
        Assert.Contains("without BPMN diagram notation", ex.Message);
    }

    [Fact]
    public async Task GetWorkflowExecutionDiagramDetailAsync_ThrowsWhenBpmnHasNoDiagramElement()
    {
        var (client, stub) = CreateClient();

        stub.WhenJson(HttpMethod.Get, "service/history/historic-process-instances/inst-1", new
        {
            id = "inst-1", processDefinitionId = "pd-1",
            startTime = "2026-04-01T00:00:00Z", endTime = (string?)null
        });
        stub.WhenJson(HttpMethod.Get, "service/repository/process-definitions/pd-1", new
        {
            id = "pd-1", key = "k", name = "Flow", version = 1,
            graphicalNotationDefined = true
        });
        stub.When(HttpMethod.Get, "service/repository/process-definitions/pd-1/resourcedata", _ =>
            StubHttpMessageHandler.TextResponse(SimpleBpmn, mediaType: "application/xml"));
        // EnsureDiagramXmlPresent runs after all five GETs complete, so the
        // activity/variable calls still need stubs even though we expect to throw.
        stub.WhenJson(HttpMethod.Get, "service/history/historic-activity-instances", new { data = Array.Empty<object>() });
        stub.WhenJson(HttpMethod.Get, "service/history/historic-variable-instances", new { data = Array.Empty<object>() });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.GetWorkflowExecutionDiagramDetailAsync("inst-1"));
        Assert.Contains("does not contain renderable diagram notation", ex.Message);
    }

    // --- DeployProcessAsync --------------------------------------------------

    [Fact]
    public async Task DeployProcessAsync_PostsBpmnAndReturnsDeploymentInfo()
    {
        var (client, stub) = CreateClient();
        stub.WhenJson(HttpMethod.Post, "service/repository/deployments",
            new { id = "dep-7" });
        stub.WhenJson(HttpMethod.Get, "service/repository/process-definitions",
            new
            {
                data = new[]
                {
                    new { id = "pd-7", key = "my_flow", name = "My Flow", version = 2, deploymentId = "dep-7" }
                }
            });

        var deployed = await client.DeployProcessAsync(new WorkflowModel
        {
            Id = Guid.NewGuid(),
            ProcessKey = "my_flow",
            Name = "My Flow",
            BpmnXml = SimpleBpmn
        });

        Assert.Equal("dep-7", deployed.DeploymentId);
        Assert.Equal("pd-7", deployed.ProcessDefinitionId);
        Assert.Equal("my_flow", deployed.ProcessDefinitionKey);
        Assert.Equal(2, deployed.ProcessDefinitionVersion);

        var deployRequest = stub.Requests.Single(r =>
            r.Method == HttpMethod.Post && r.Url.Contains("service/repository/deployments"));
        Assert.Contains("my_flow.bpmn20.xml", deployRequest.Body);
    }

    [Fact]
    public async Task DeployProcessAsync_ThrowsWhenLatestProcessDefinitionNotFound()
    {
        var (client, stub) = CreateClient();
        stub.WhenJson(HttpMethod.Post, "service/repository/deployments", new { id = "dep-7" });
        stub.WhenJson(HttpMethod.Get, "service/repository/process-definitions",
            new { data = Array.Empty<object>() });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.DeployProcessAsync(new WorkflowModel
            {
                Id = Guid.NewGuid(), ProcessKey = "ghost", Name = "Ghost", BpmnXml = SimpleBpmn
            }));
        Assert.Contains("ghost", ex.Message);
    }

    [Fact]
    public async Task DeployProcessAsync_ProbesScriptTaskSupport_WhenBpmnHasScriptTask_AndContinuesIfSupported()
    {
        var (client, stub) = CreateClient();
        stub.WhenJson(HttpMethod.Get, "actuator/scriptTaskSupport",
            new { javaScriptSupported = true, engineNames = new[] { "JavaScript" } });
        stub.WhenJson(HttpMethod.Post, "service/repository/deployments", new { id = "dep-9" });
        stub.WhenJson(HttpMethod.Get, "service/repository/process-definitions",
            new { data = new[] { new { id = "pd-9", key = "k", name = "n", version = 1, deploymentId = "dep-9" } } });

        var deployed = await client.DeployProcessAsync(new WorkflowModel
        {
            Id = Guid.NewGuid(), ProcessKey = "k", Name = "n", BpmnXml = BpmnWithScriptTask
        });

        Assert.Equal("dep-9", deployed.DeploymentId);
        Assert.Contains(stub.Requests, r => r.Url.Contains("scriptTaskSupport"));
    }

    [Fact]
    public async Task DeployProcessAsync_ThrowsHelpfulError_WhenScriptTaskSupportMissing()
    {
        var (client, stub) = CreateClient();
        stub.WhenJson(HttpMethod.Get, "actuator/scriptTaskSupport",
            new { javaScriptSupported = false, engineNames = new[] { "Groovy" } });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.DeployProcessAsync(new WorkflowModel
            {
                Id = Guid.NewGuid(), ProcessKey = "k", Name = "n", BpmnXml = BpmnWithScriptTask
            }));
        Assert.Contains("missing JavaScript script task support", ex.Message);
        Assert.Contains("Groovy", ex.Message);
    }

    [Fact]
    public async Task DeployProcessAsync_ThrowsWhenScriptTaskProbeIsAbsentEntirely()
    {
        var (client, stub) = CreateClient();
        // Both probe URLs return 404 → AutoNate's diagnostic message instructs rebuilding.
        stub.WhenStatus(HttpMethod.Get, "actuator/scriptTaskSupport", HttpStatusCode.NotFound);
        stub.WhenStatus(HttpMethod.Get, "service/autonate/script-task-support", HttpStatusCode.NotFound);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.DeployProcessAsync(new WorkflowModel
            {
                Id = Guid.NewGuid(), ProcessKey = "k", Name = "n", BpmnXml = BpmnWithScriptTask
            }));
        Assert.Contains("AutoNate script task capability probe", ex.Message);
    }

    // --- helpers -------------------------------------------------------------

    private static (FlowableClient client, StubHttpMessageHandler stub) CreateClient()
    {
        var stub = new StubHttpMessageHandler();
        var http = new HttpClient(stub) { BaseAddress = new Uri(BaseAddress) };
        var client = new FlowableClient(http, Options.Create(new FlowableOptions { BaseUrl = BaseAddress }));
        return (client, stub);
    }
}
