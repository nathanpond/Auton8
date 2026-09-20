using System.Text.Json;
using AutoNate.E2E.Tests.Support;
using Microsoft.Playwright;
using Xunit;
using Xunit.Abstractions;

namespace AutoNate.E2E.Tests;

/// <summary>
/// A failure reaches the execution's error surface, async or not (#222).
/// </summary>
/// <remarks>
/// <para>
/// The async case here began as a MEASUREMENT. #222 establishes one defect and
/// explicitly declines to establish a second: <i>"no `workflow_execution_errors`
/// row appeared within 45 seconds … it is not established. Worth confirming
/// before treating it as a second defect."</i>
/// </para>
/// <para>
/// <b>Confirmed, and it is not a defect.</b> A failing DMN evaluation surfaced
/// within seconds carrying the root cause rather than the wrapper —
/// <c>errorMessage: "Could not resolve function 'bogusFunction'"</c>, attributed
/// to the author's own <c>decide</c> element, with the Java stack alongside. So
/// the issue's second observation was environmental, and #222 is only the
/// synchronous gap. The case is kept as a guard rather than deleted: it is the
/// path the surface was built for, and nothing else asserts it end to end.
/// </para>
/// </remarks>
[Trait("RequiresService", "Flowable")]
[Collection(AutoNateE2ECollection.Name)]
public sealed class ExecutionErrorSurfaceTests : E2ETestBase
{
    private readonly ITestOutputHelper _output;

    public ExecutionErrorSurfaceTests(AutoNateE2EFixture fixture, ITestOutputHelper output)
        : base(fixture) => _output = output;

    [Fact]
    public async Task An_async_failure_reaches_the_execution_error_surface()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var api = session.Page.APIRequest;

        // A decision whose output expression cannot be computed. Publish stamps
        // the expanded task async (#111), so this is the ASYNC path: the failure
        // becomes a retrying job rather than throwing out of the start call.
        var decisionKey = "e2e" + Guid.NewGuid().ToString("n")[..10];
        await PublishFailingTableAsync(api, decisionKey);

        var processKey = $"e2e_err_{Guid.NewGuid():N}"[..22];
        await PublishWorkflowAsync(api, processKey, Diagram(processKey, decisionKey));

        var instance = await StartAsync(api, processKey);

        // The failure HAPPENED -- asserted first, so a missing error row cannot be
        // confused with a run that simply never failed.
        var job = await EventuallyFailedJobAsync(api, instance);
        _output.WriteLine($"failed job: {job}");

        // Now the question this probe exists for.
        var deadline = DateTime.UtcNow.AddSeconds(90);
        string seen = "(never)";
        while (DateTime.UtcNow < deadline)
        {
            var response = await api.GetAsync($"/api/executions/{instance}/history");
            if (response.Ok)
            {
                using var body = JsonDocument.Parse(await response.TextAsync());
                foreach (var row in body.RootElement.EnumerateArray())
                {
                    if (row.TryGetProperty("isErrored", out var errored)
                        && errored.ValueKind == JsonValueKind.True)
                    {
                        _output.WriteLine($"ERROR SURFACED: {row}");
                        return;
                    }
                }

                seen = $"{body.RootElement.GetArrayLength()} history row(s), none errored";
            }

            await Task.Delay(1000);
        }

        Assert.Fail(
            $"90s after a job failed, no history row reports isErrored. Last saw: {seen}. "
            + "The async path is the one the error surface was BUILT for (#222 records that "
            + "`job.execution.failed` is its only source), so if this is the state, the "
            + "synchronous gap the issue describes is the smaller half of the problem.");
    }

    /// <summary>
    /// A step that fails SYNCHRONOUSLY is on the surface too (#222).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The defect this story is actually about. Publish force-asyncs everything
    /// that runs code — script tasks, DMN tasks, send tasks, the complex-gateway
    /// routing script — precisely so their failures become jobs. What stays
    /// synchronous is an expression evaluated inline during a TRANSITION, and a
    /// sequence-flow condition is exactly that: no task, so no job, so nothing the
    /// error recorder's one event type could ever hear.
    /// </para>
    /// <para>
    /// Measured before it was fixed: the completion returned 500, the instance
    /// survived with three history rows, not one of them errored, and
    /// <c>/jobs</c> was empty.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_synchronous_failure_is_on_the_surface_and_the_engines_words_are_not()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var api = session.Page.APIRequest;

        var key = $"e2e_syn_{Guid.NewGuid():N}"[..22];
        await PublishWorkflowAsync(api, key, FailingConditionDiagram(key));

        var instance = await StartAsync(api, key);
        var taskId = await EventuallyOpenTaskAsync(api, instance);

        var completed = await api.PostAsync($"/api/tasks/{taskId}/complete",
            new APIRequestContextOptions { DataObject = new Dictionary<string, object>() });

        Assert.False(completed.Ok, "The gateway condition throws, so completing must not succeed.");

        // THE ENGINE'S OWN WORDS DO NOT REACH THE CALLER. Before this the route had
        // no catch at all, and the app installs no exception middleware -- so what
        // came back depended on the environment: here in Development the developer
        // exception page serialised the engine's whole body, and in production it
        // would have been a bare 500 with nothing in it. Asserted against the
        // Development shape because that is the one a test can see, and it is the
        // stricter of the two.
        var body = await completed.TextAsync();
        Assert.DoesNotContain("Unknown property used in expression", body, StringComparison.Ordinal);
        Assert.DoesNotContain("FlowableRequestException", body, StringComparison.Ordinal);
        Assert.DoesNotContain("org.flowable", body, StringComparison.Ordinal);

        // AND THE FAILURE IS ON THE EXECUTION, which is the whole of this story:
        // the instance is still there, so there was always something to attach to.
        var deadline = DateTime.UtcNow.AddSeconds(30);
        var seen = "(never)";
        while (DateTime.UtcNow < deadline)
        {
            var history = await api.GetAsync($"/api/executions/{instance}/history");
            if (history.Ok)
            {
                using var document = JsonDocument.Parse(await history.TextAsync());
                foreach (var row in document.RootElement.EnumerateArray())
                {
                    if (row.TryGetProperty("isErrored", out var errored)
                        && errored.ValueKind == JsonValueKind.True)
                    {
                        return;
                    }
                }

                seen = $"{document.RootElement.GetArrayLength()} row(s), none errored";
            }

            await Task.Delay(500);
        }

        Assert.Fail(
            $"The step failed and the execution does not say so. Last saw: {seen}. A failure with "
            + "no job behind it could never reach the recorder's one event type, which is the gap "
            + "#222 is about.");
    }

    // ── fixtures ────────────────────────────────────────────────────────────

    private static string FailingConditionDiagram(string key) => $$"""
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI"
                          xmlns:dc="http://www.omg.org/spec/DD/20100524/DC"
                          xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
                          targetNamespace="http://autonate.dev/workflows">
          <bpmn:process id="{{key}}" name="Sync fail" isExecutable="true">
            <bpmn:startEvent id="s" />
            <bpmn:sequenceFlow id="f0" sourceRef="s" targetRef="first" />
            <bpmn:userTask id="first" name="First" />
            <bpmn:sequenceFlow id="f1" sourceRef="first" targetRef="split" />
            <bpmn:exclusiveGateway id="split" default="f_b" />
            <bpmn:sequenceFlow id="f_a" sourceRef="split" targetRef="a">
              <bpmn:conditionExpression xsi:type="bpmn:tFormalExpression">${nosuchvar.length() &gt; 1}</bpmn:conditionExpression>
            </bpmn:sequenceFlow>
            <bpmn:userTask id="a" name="A" />
            <bpmn:sequenceFlow id="f_b" sourceRef="split" targetRef="b" />
            <bpmn:userTask id="b" name="B" />
            <bpmn:sequenceFlow id="f2" sourceRef="a" targetRef="e" />
            <bpmn:sequenceFlow id="f3" sourceRef="b" targetRef="e" />
            <bpmn:endEvent id="e" />
          </bpmn:process>
          <bpmndi:BPMNDiagram id="Diagram_1">
            <bpmndi:BPMNPlane id="Plane_1" bpmnElement="{{key}}">
              <bpmndi:BPMNShape id="Shape_split" bpmnElement="split">
                <dc:Bounds x="240" y="100" width="100" height="80" />
              </bpmndi:BPMNShape>
            </bpmndi:BPMNPlane>
          </bpmndi:BPMNDiagram>
        </bpmn:definitions>
        """;

    private static async Task<string> EventuallyOpenTaskAsync(IAPIRequestContext api, string instance)
    {
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < deadline)
        {
            var tasks = await api.GetAsync($"/api/executions/{instance}/tasks");
            if (tasks.Ok)
            {
                using var document = JsonDocument.Parse(await tasks.TextAsync());
                foreach (var task in document.RootElement.EnumerateArray())
                {
                    return task.GetProperty("id").GetString()!;
                }
            }

            await Task.Delay(500);
        }

        Assert.Fail("No task ever opened, so this test measured nothing.");
        throw new InvalidOperationException("unreachable");
    }


    private static string Diagram(string processKey, string decisionKey) => $$"""
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI"
                          xmlns:dc="http://www.omg.org/spec/DD/20100524/DC"
                          xmlns:autonate="http://autonate.dev/workflows"
                          targetNamespace="http://autonate.dev/workflows">
          <bpmn:process id="{{processKey}}" name="Failing decision" isExecutable="true">
            <bpmn:startEvent id="s" />
            <bpmn:sequenceFlow id="f0" sourceRef="s" targetRef="decide" />
            <bpmn:businessRuleTask id="decide" name="Route the invoice"
                                   autonate:decisionKey="{{decisionKey}}">
              <bpmn:incoming>f0</bpmn:incoming>
              <bpmn:outgoing>f1</bpmn:outgoing>
            </bpmn:businessRuleTask>
            <bpmn:sequenceFlow id="f1" sourceRef="decide" targetRef="after" />
            <bpmn:userTask id="after" name="After" />
            <bpmn:sequenceFlow id="f2" sourceRef="after" targetRef="e" />
            <bpmn:endEvent id="e" />
          </bpmn:process>
          <bpmndi:BPMNDiagram id="Diagram_1">
            <bpmndi:BPMNPlane id="Plane_1" bpmnElement="{{processKey}}">
              <bpmndi:BPMNShape id="Shape_decide" bpmnElement="decide">
                <dc:Bounds x="240" y="100" width="100" height="80" />
              </bpmndi:BPMNShape>
            </bpmndi:BPMNPlane>
          </bpmndi:BPMNDiagram>
        </bpmn:definitions>
        """;

    private static async Task PublishFailingTableAsync(IAPIRequestContext api, string decisionKey)
    {
        var created = await api.PostAsync("/api/decision-tables/", new APIRequestContextOptions
        {
            DataObject = new
            {
                decisionKey,
                name = "Failing",
                hitPolicy = "FIRST",
                inputs = new[] { new { id = "in_1", label = "Amount", name = "amount", typeRef = "number" } },
                outputs = new[] { new { id = "out_1", label = "Route", name = "route", typeRef = "string" } },
                rules = new object[]
                {
                    new { id = "r1", inputEntries = new[] { "" }, outputEntries = new[] { "bogusFunction(1)" } }
                }
            }
        });
        Assert.True(created.Ok, $"Creating the table failed: {created.Status} {await created.TextAsync()}");

        using var body = JsonDocument.Parse(await created.TextAsync());
        var id = body.RootElement.GetProperty("id").GetString()!;

        var published = await api.PostAsync($"/api/decision-tables/{id}/publish",
            new APIRequestContextOptions { DataObject = new { } });
        Assert.True(published.Ok, $"Publishing failed: {published.Status} {await published.TextAsync()}");
    }

    private static async Task PublishWorkflowAsync(IAPIRequestContext api, string key, string xml)
    {
        var id = Guid.NewGuid();
        var displayName = TestNames.Prefixed(key);
        var created = await api.PostAsync("/api/workflows/", new APIRequestContextOptions
        {
            DataObject = new { id, name = displayName, processKey = key, bpmnXml = xml }
        });
        Assert.True(created.Ok, $"Creating the model failed: {created.Status} {await created.TextAsync()}");

        var published = await api.PostAsync($"/api/workflows/{id}/publish", new APIRequestContextOptions
        {
            DataObject = new { id, name = displayName, processKey = key, bpmnXml = xml }
        });
        Assert.True(published.Ok, $"Publishing failed: {published.Status} {await published.TextAsync()}");
    }

    private static async Task<string> StartAsync(IAPIRequestContext api, string key)
    {
        var response = await api.PostAsync($"/api/workflows/{key}/start", new APIRequestContextOptions
        {
            DataObject = new { variables = new { amount = 50 } }
        });
        Assert.True(response.Ok, $"Starting failed: {response.Status} {await response.TextAsync()}");
        using var document = JsonDocument.Parse(await response.TextAsync());
        return document.RootElement.GetProperty("id").GetString()!;
    }

    private static async Task<string> EventuallyFailedJobAsync(IAPIRequestContext api, string instanceId)
    {
        var deadline = DateTime.UtcNow.AddSeconds(90);
        while (DateTime.UtcNow < deadline)
        {
            var response = await api.GetAsync($"/api/executions/{instanceId}/jobs");
            if (response.Ok)
            {
                using var document = JsonDocument.Parse(await response.TextAsync());
                foreach (var job in document.RootElement.EnumerateArray())
                {
                    if (job.TryGetProperty("exceptionMessage", out var message)
                        && !string.IsNullOrWhiteSpace(message.GetString()))
                    {
                        return message.GetString()!;
                    }
                }
            }

            await Task.Delay(1000);
        }

        Assert.Fail("The decision never failed, so this probe measured nothing.");
        throw new InvalidOperationException("unreachable");
    }
}
