using System.Text.Json;
using AutoNate.E2E.Tests.Support;
using Microsoft.Playwright;
using Xunit;

namespace AutoNate.E2E.Tests;

/// <summary>
/// A process branches on what a decision table decided (#111).
/// </summary>
/// <remarks>
/// <para>
/// The AC is explicit about why the gateway is the assertion: <i>"asserted end to
/// end, because 'the table was evaluated' and 'the process did something different
/// as a result' are different claims and only the second is the feature."</i>
/// </para>
/// <para>
/// So each run asserts <b>which user task the process is now waiting on</b>, and
/// the two runs differ only in the amount they start with. A test asserting that
/// the output variable was set would pass against a process whose gateway ignored
/// it.
/// </para>
/// </remarks>
[Trait("RequiresService", "Flowable")]
[Collection(AutoNateE2ECollection.Name)]
public sealed class BusinessRuleTaskExecutionTests : E2ETestBase
{
    public BusinessRuleTaskExecutionTests(AutoNateE2EFixture fixture) : base(fixture) { }

    [Fact]
    public async Task The_table_decides_and_a_gateway_after_it_takes_the_path_it_chose()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var api = session.Page.APIRequest;

        // ── A published decision table, authored through Auton8's own API.
        var decisionKey = "e2e" + Guid.NewGuid().ToString("n")[..10];
        var tableId = await PublishTableAsync(api, decisionKey);

        // ── A process whose business rule task points at it.
        var processKey = $"e2e_brt_{Guid.NewGuid():N}"[..22];
        await PublishWorkflowAsync(api, processKey, Diagram(processKey, decisionKey));

        // Big amount -> the table says "escalate" -> the gateway takes that path.
        var escalated = await StartAsync(api, processKey, amount: 50_000);
        await EventuallyActivityAsync(api, escalated, "escalate_task",
            "the escalate path to be taken");

        // Small amount, SAME process definition -> "auto" -> the other path.
        //
        // This is the pair that makes it a feature rather than a coincidence. One
        // run alone passes against a gateway with a stuck default flow.
        var auto = await StartAsync(api, processKey, amount: 50);
        await EventuallyActivityAsync(api, auto, "auto_task", "the auto path to be taken");

        // And the complement: neither run wandered onto the other's path.
        Assert.DoesNotContain("auto_task", await ActivityIdsAsync(api, escalated));
        Assert.DoesNotContain("escalate_task", await ActivityIdsAsync(api, auto));

        await api.DeleteAsync($"/api/decision-tables/{tableId}");
    }

    [Fact]
    public async Task A_business_rule_task_with_no_table_is_refused_at_publish()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var api = session.Page.APIRequest;

        var processKey = $"e2e_brx_{Guid.NewGuid():N}"[..22];
        var refused = await TryPublishWorkflowAsync(api, processKey, UnconfiguredDiagram(processKey));

        Assert.False(refused.Ok,
            "A business rule task with no decision table should be refused at publish.");

        var body = await refused.TextAsync();
        Assert.Contains("decision table", body, StringComparison.Ordinal);
        // Named, so an author knows which element to open.
        Assert.Contains("Route the invoice", body, StringComparison.Ordinal);

        // And it never reached the engine. Without this, an implementation that
        // deployed first and complained after would still pass -- and the engine's
        // own refusal for this element is a 500 naming a Java class
        // (NoClassDefFoundError org/kie/api/runtime/rule/AgendaFilter), which is
        // exactly the message an author must never be handed.
        Assert.DoesNotContain("org/kie/api", body, StringComparison.Ordinal);
        Assert.DoesNotContain("AgendaFilter", body, StringComparison.Ordinal);
    }

    // ── fixtures ────────────────────────────────────────────────────────────

    private static string Diagram(string processKey, string decisionKey) => $$"""
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI"
                          xmlns:dc="http://www.omg.org/spec/DD/20100524/DC"
                          xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
                          xmlns:flowable="http://flowable.org/bpmn"
                          xmlns:autonate="http://autonate.dev/workflows"
                          targetNamespace="http://autonate.dev/workflows">
          <bpmn:process id="{{processKey}}" name="Invoice routing" isExecutable="true">
            <bpmn:startEvent id="s" />
            <bpmn:sequenceFlow id="f0" sourceRef="s" targetRef="decide" />
            <bpmn:businessRuleTask id="decide" name="Route the invoice"
                                   autonate:decisionKey="{{decisionKey}}">
              <bpmn:incoming>f0</bpmn:incoming>
              <bpmn:outgoing>f1</bpmn:outgoing>
            </bpmn:businessRuleTask>
            <bpmn:sequenceFlow id="f1" sourceRef="decide" targetRef="split" />
            <bpmn:exclusiveGateway id="split" default="f_auto" />
            <bpmn:sequenceFlow id="f_esc" sourceRef="split" targetRef="escalate_task">
              <bpmn:conditionExpression xsi:type="bpmn:tFormalExpression">${route == 'escalate'}</bpmn:conditionExpression>
            </bpmn:sequenceFlow>
            <bpmn:userTask id="escalate_task" name="Escalate" />
            <bpmn:sequenceFlow id="f_auto" sourceRef="split" targetRef="auto_task" />
            <bpmn:userTask id="auto_task" name="Auto approve" />
            <bpmn:sequenceFlow id="f2" sourceRef="escalate_task" targetRef="e" />
            <bpmn:sequenceFlow id="f3" sourceRef="auto_task" targetRef="e" />
            <bpmn:endEvent id="e" />
          </bpmn:process>
          {{Di(processKey, "s", "decide", "split", "escalate_task", "auto_task", "e")}}
        </bpmn:definitions>
        """;

    private static string UnconfiguredDiagram(string processKey) => $$"""
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI"
                          xmlns:dc="http://www.omg.org/spec/DD/20100524/DC"
                          xmlns:autonate="http://autonate.dev/workflows"
                          targetNamespace="http://autonate.dev/workflows">
          <bpmn:process id="{{processKey}}" name="Unconfigured" isExecutable="true">
            <bpmn:startEvent id="s" />
            <bpmn:sequenceFlow id="f0" sourceRef="s" targetRef="decide" />
            <bpmn:businessRuleTask id="decide" name="Route the invoice">
              <bpmn:incoming>f0</bpmn:incoming>
              <bpmn:outgoing>f1</bpmn:outgoing>
            </bpmn:businessRuleTask>
            <bpmn:sequenceFlow id="f1" sourceRef="decide" targetRef="e" />
            <bpmn:endEvent id="e" />
          </bpmn:process>
          {{Di(processKey, "s", "decide", "e")}}
        </bpmn:definitions>
        """;

    // ── harness ─────────────────────────────────────────────────────────────

    private static async Task<string> PublishTableAsync(IAPIRequestContext api, string decisionKey)
    {
        var created = await api.PostAsync("/api/decision-tables/", new APIRequestContextOptions
        {
            DataObject = new
            {
                decisionKey,
                name = "Invoice routing",
                hitPolicy = "FIRST",
                inputs = new[]
                {
                    new { id = "in_amount", label = "Amount", name = "amount", typeRef = "number" }
                },
                outputs = new[]
                {
                    new { id = "out_route", label = "Route", name = "route", typeRef = "string" }
                },
                rules = new object[]
                {
                    new { id = "r1", inputEntries = new[] { "> 10000" }, outputEntries = new[] { "\"escalate\"" } },
                    new { id = "r2", inputEntries = new[] { "" }, outputEntries = new[] { "\"auto\"" } }
                }
            }
        });
        Assert.True(created.Ok, $"Creating the table failed: {created.Status} {await created.TextAsync()}");

        using var body = JsonDocument.Parse(await created.TextAsync());
        var id = body.RootElement.GetProperty("id").GetString()!;

        var published = await api.PostAsync($"/api/decision-tables/{id}/publish",
            new APIRequestContextOptions { DataObject = new { } });
        Assert.True(published.Ok,
            $"Publishing the table failed: {published.Status} {await published.TextAsync()}");
        return id;
    }

    private static async Task<IAPIResponse> TryPublishWorkflowAsync(
        IAPIRequestContext api, string key, string xml)
    {
        var id = Guid.NewGuid();
        var displayName = TestNames.Prefixed(key);
        var created = await api.PostAsync("/api/workflows/", new APIRequestContextOptions
        {
            DataObject = new { id, name = displayName, processKey = key, bpmnXml = xml }
        });
        Assert.True(created.Ok, $"Creating the model failed: {created.Status} {await created.TextAsync()}");

        return await api.PostAsync($"/api/workflows/{id}/publish", new APIRequestContextOptions
        {
            DataObject = new { id, name = displayName, processKey = key, bpmnXml = xml }
        });
    }

    private static async Task PublishWorkflowAsync(IAPIRequestContext api, string key, string xml)
    {
        var published = await TryPublishWorkflowAsync(api, key, xml);
        Assert.True(published.Ok,
            $"Publishing the workflow failed: {published.Status} {await published.TextAsync()}");
    }

    private static async Task<string> StartAsync(IAPIRequestContext api, string key, double amount)
    {
        var response = await api.PostAsync($"/api/workflows/{key}/start", new APIRequestContextOptions
        {
            DataObject = new { variables = new { amount } }
        });
        Assert.True(response.Ok, $"Starting failed: {response.Status} {await response.TextAsync()}");
        using var document = JsonDocument.Parse(await response.TextAsync());
        return document.RootElement.GetProperty("id").GetString()!;
    }

    private static async Task<List<string>> ActivityIdsAsync(IAPIRequestContext api, string instanceId)
    {
        var response = await api.GetAsync($"/api/executions/{instanceId}/history");
        Assert.True(response.Ok, $"Reading history failed: {response.Status} {await response.TextAsync()}");
        using var document = JsonDocument.Parse(await response.TextAsync());
        return document.RootElement.EnumerateArray()
            .Select(e => e.GetProperty("activityId").GetString()!)
            .ToList();
    }

    /// <summary>
    /// Waits for an activity to appear in the ENGINE'S history.
    /// </summary>
    /// <remarks>
    /// Not the cached task list. Measured in #172: after a state change, history
    /// shows the new activity while <c>/tasks</c> still lists the previous one, so
    /// an assertion there measures projection latency rather than the feature.
    /// </remarks>
    private static async Task EventuallyActivityAsync(
        IAPIRequestContext api, string instanceId, string activityId, string what)
    {
        var deadline = DateTime.UtcNow.AddSeconds(60);
        var trail = "(none)";
        while (DateTime.UtcNow < deadline)
        {
            var ids = await ActivityIdsAsync(api, instanceId);
            if (ids.Contains(activityId)) return;
            trail = string.Join(" | ", ids);
            await Task.Delay(1000);
        }

        Assert.Fail($"Timed out after 60s waiting for {what}. History was: {trail}");
    }

    private static string Di(string processKey, params string[] elementIds)
    {
        var shapes = string.Join("\n", elementIds.Select((id, index) =>
            $"""
                  <bpmndi:BPMNShape id="Shape_{id}" bpmnElement="{id}">
                    <dc:Bounds x="{100 + (index * 160)}" y="100" width="120" height="90" />
                  </bpmndi:BPMNShape>
             """));

        return $"""
              <bpmndi:BPMNDiagram id="Diagram_1">
                <bpmndi:BPMNPlane id="Plane_1" bpmnElement="{processKey}">
            {shapes}
                </bpmndi:BPMNPlane>
              </bpmndi:BPMNDiagram>
            """;
    }
}
