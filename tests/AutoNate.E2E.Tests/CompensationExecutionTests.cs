using System.Text.Json;
using AutoNate.E2E.Tests.Support;
using Microsoft.Playwright;
using Xunit;

namespace AutoNate.E2E.Tests;

/// <summary>
/// Compensation undoes work that already completed (#115).
/// </summary>
/// <remarks>
/// Flowable runs compensation correctly when it is thrown by an INTERMEDIATE
/// event: handlers run in reverse order, only for activities that completed, and
/// the throw waits for them. What it does not do is honour the compensation END
/// event — that ends the process and runs nothing, verified with an empty handler
/// trail. Publish rewrites the end form into the throw form, and this asserts the
/// result end to end.
/// </remarks>
[Trait("RequiresService", "Flowable")]
[Collection(AutoNateE2ECollection.Name)]
public sealed class CompensationExecutionTests : E2ETestBase
{
    public CompensationExecutionTests(AutoNateE2EFixture fixture) : base(fixture) { }

    [Fact]
    public async Task A_compensation_end_event_runs_the_handlers_for_completed_work()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var api = session.Page.APIRequest;

        var key = $"cmp{Guid.NewGuid():N}"[..20];
        await PublishAsync(api, key, Diagram(key));

        var instance = await StartAsync(api, key);

        // 'Take payment' is a user task, so it has to be completed before the
        // process can reach the compensation end event. That is also the point:
        // compensation exists for work that already SUCCEEDED.
        var payment = await EventuallyAsync(api, instance,
            n => n.Contains("Take payment"), "the first step to be waiting");
        Assert.Contains("Take payment", payment);
        await CompleteFirstTaskAsync(api, instance, "Take payment");

        // The handlers are script tasks, so what they DID is the observable
        // proof. They cannot be user tasks: Flowable fails its own transaction
        // when a waiting handler is compensated during a task completion, which
        // is why publish refuses that shape.
        //
        // Before publish rewrote the end event, this process ended immediately
        // and no handler ran at all.
        var variables = await EventuallyVariablesAsync(api, instance,
            v => v.ContainsKey("refunded"),
            "the compensation handler to run for the completed step");

        Assert.Equal("true", variables["refunded"], ignoreCase: true);

        // And only for work that completed. 'Reserve stock' was skipped by the
        // gateway, so compensating it would be the classic wrong behaviour here —
        // undoing something that never happened.
        Assert.False(variables.ContainsKey("released"),
            "a step that never ran must not be compensated");
    }

    [Fact]
    public async Task Handlers_run_in_reverse_order_of_the_work_they_undo()
    {
        // The specification requires reverse order, and it is invisible with a
        // single handler — which is why the test above cannot cover it. Each
        // handler appends to one variable, so the assertion is on the true
        // execution order rather than on timestamps: in the first probe of this
        // every handler shared a millisecond, and sorting by start time was
        // really just reporting list order.
        await using var session = await NewSignedInAsAdminAsync();
        var api = session.Page.APIRequest;

        var key = $"cmo{Guid.NewGuid():N}"[..20];
        await PublishAsync(api, key, OrderedDiagram(key));

        var instance = await StartAsync(api, key);

        await EventuallyAsync(api, instance, n => n.Contains("Take payment"), "the first step");
        await CompleteFirstTaskAsync(api, instance, "Take payment");
        await EventuallyAsync(api, instance, n => n.Contains("Reserve stock"), "the second step");
        await CompleteFirstTaskAsync(api, instance, "Reserve stock");

        var variables = await EventuallyVariablesAsync(api, instance,
            v => v.TryGetValue("trail", out var t) && t.Contains("h1", StringComparison.Ordinal)
                 && t.Contains("h2", StringComparison.Ordinal),
            "both compensation handlers to run");

        // Payment ran first, stock second — so stock is undone first.
        Assert.Equal("h2;h1;", variables["trail"]);
    }

    private static string OrderedDiagram(string key) => $$"""
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          xmlns:autonate="http://autonate.dev/workflows"
                          id="Definitions_1" targetNamespace="http://autonate.dev/workflows">
          <bpmn:process id="{{key}}" name="Undo in order" isExecutable="true">
            <bpmn:startEvent id="s" />
            <bpmn:sequenceFlow id="f0" sourceRef="s" targetRef="t1" />
            <bpmn:userTask id="t1" name="Take payment" />
            <bpmn:sequenceFlow id="f1" sourceRef="t1" targetRef="t2" />
            <bpmn:userTask id="t2" name="Reserve stock" />
            <bpmn:sequenceFlow id="f2" sourceRef="t2" targetRef="done" />
            <bpmn:endEvent id="done" name="Undo everything">
              <bpmn:compensateEventDefinition />
            </bpmn:endEvent>

            <bpmn:boundaryEvent id="b1" attachedToRef="t1">
              <bpmn:compensateEventDefinition />
            </bpmn:boundaryEvent>
            <bpmn:boundaryEvent id="b2" attachedToRef="t2">
              <bpmn:compensateEventDefinition />
            </bpmn:boundaryEvent>
            <bpmn:scriptTask id="h1" name="Refund payment" isForCompensation="true"
                             scriptFormat="javascript" autonate:runAs="workflowAuthor">
              <bpmn:script>variables.set('trail', (variables.get('trail') || '') + 'h1;');</bpmn:script>
            </bpmn:scriptTask>
            <bpmn:scriptTask id="h2" name="Release stock" isForCompensation="true"
                             scriptFormat="javascript" autonate:runAs="workflowAuthor">
              <bpmn:script>variables.set('trail', (variables.get('trail') || '') + 'h2;');</bpmn:script>
            </bpmn:scriptTask>
            <bpmn:association id="a1" sourceRef="b1" targetRef="h1" associationDirection="One" />
            <bpmn:association id="a2" sourceRef="b2" targetRef="h2" associationDirection="One" />
          </bpmn:process>
          {{Di(key, "s", "t1", "t2", "done", "h1", "h2")}}
        </bpmn:definitions>
        """;

    private static string Diagram(string key) => $$"""
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
                          xmlns:autonate="http://autonate.dev/workflows"
                          id="Definitions_1" targetNamespace="http://autonate.dev/workflows">
          <bpmn:process id="{{key}}" name="Undo" isExecutable="true">
            <bpmn:startEvent id="s" />
            <bpmn:sequenceFlow id="f0" sourceRef="s" targetRef="t1" />
            <bpmn:userTask id="t1" name="Take payment" />
            <bpmn:sequenceFlow id="f1" sourceRef="t1" targetRef="g" />
            <bpmn:exclusiveGateway id="g" default="fskip" />
            <bpmn:sequenceFlow id="fskip" sourceRef="g" targetRef="done" />
            <bpmn:sequenceFlow id="frun" sourceRef="g" targetRef="t2">
              <bpmn:conditionExpression xsi:type="bpmn:tFormalExpression">${1 == 2}</bpmn:conditionExpression>
            </bpmn:sequenceFlow>
            <bpmn:userTask id="t2" name="Reserve stock" />
            <bpmn:sequenceFlow id="f2" sourceRef="t2" targetRef="done" />
            <bpmn:endEvent id="done" name="Undo everything">
              <bpmn:compensateEventDefinition />
            </bpmn:endEvent>

            <bpmn:boundaryEvent id="b1" attachedToRef="t1">
              <bpmn:compensateEventDefinition />
            </bpmn:boundaryEvent>
            <bpmn:boundaryEvent id="b2" attachedToRef="t2">
              <bpmn:compensateEventDefinition />
            </bpmn:boundaryEvent>
            <bpmn:scriptTask id="h1" name="Refund payment" isForCompensation="true"
                             scriptFormat="javascript" autonate:runAs="workflowAuthor">
              <bpmn:script>variables.set('refunded', true);</bpmn:script>
            </bpmn:scriptTask>
            <bpmn:scriptTask id="h2" name="Release stock" isForCompensation="true"
                             scriptFormat="javascript" autonate:runAs="workflowAuthor">
              <bpmn:script>variables.set('released', true);</bpmn:script>
            </bpmn:scriptTask>
            <bpmn:association id="a1" sourceRef="b1" targetRef="h1" associationDirection="One" />
            <bpmn:association id="a2" sourceRef="b2" targetRef="h2" associationDirection="One" />
          </bpmn:process>
          {{Di(key, "s", "t1", "g", "t2", "done", "h1", "h2")}}
        </bpmn:definitions>
        """;

    private static string Di(string processKey, params string[] elementIds)
    {
        var shapes = string.Join("\n", elementIds.Select((id, index) =>
            $"""
                  <bpmndi:BPMNShape id="Shape_{id}" bpmnElement="{id}">
                    <dc:Bounds x="{100 + (index * 140)}" y="100" width="100" height="80" />
                  </bpmndi:BPMNShape>
             """));

        return $"""
              <bpmndi:BPMNDiagram id="Diagram_1"
                                  xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI"
                                  xmlns:dc="http://www.omg.org/spec/DD/20100524/DC">
                <bpmndi:BPMNPlane id="Plane_1" bpmnElement="{processKey}">
            {shapes}
                </bpmndi:BPMNPlane>
              </bpmndi:BPMNDiagram>
            """;
    }

    private static async Task PublishAsync(IAPIRequestContext api, string key, string xml)
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
            DataObject = new { }
        });
        Assert.True(response.Ok, $"Starting failed: {response.Status} {await response.TextAsync()}");
        using var document = JsonDocument.Parse(await response.TextAsync());
        return document.RootElement.GetProperty("id").GetString()!;
    }

    private static async Task CompleteFirstTaskAsync(
        IAPIRequestContext api, string instanceId, string name)
    {
        var response = await api.GetAsync($"/api/executions/{instanceId}/tasks");
        Assert.True(response.Ok, await response.TextAsync());
        using var document = JsonDocument.Parse(await response.TextAsync());
        var taskId = document.RootElement.EnumerateArray()
            .First(t => t.GetProperty("name").GetString() == name)
            .GetProperty("id").GetString()!;

        var completed = await api.PostAsync(
            $"/api/executions/{instanceId}/tasks/{taskId}/force-complete",
            new APIRequestContextOptions { DataObject = new { } });
        Assert.True(completed.Ok,
            $"Completing '{name}' failed: {completed.Status} {await completed.TextAsync()}");
    }

    private static async Task<Dictionary<string, string>> VariablesAsync(
        IAPIRequestContext api, string instanceId)
    {
        var response = await api.GetAsync($"/api/executions/{instanceId}/diagram");
        Assert.True(response.Ok, $"Reading the execution failed: {response.Status} {await response.TextAsync()}");
        using var document = JsonDocument.Parse(await response.TextAsync());

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in document.RootElement.GetProperty("variables").EnumerateArray())
        {
            result[entry.GetProperty("name").GetString()!] =
                entry.GetProperty("value").GetString() ?? string.Empty;
        }
        return result;
    }

    private static async Task<Dictionary<string, string>> EventuallyVariablesAsync(
        IAPIRequestContext api, string instanceId,
        Func<Dictionary<string, string>, bool> until, string what)
    {
        var deadline = DateTime.UtcNow.AddSeconds(45);
        var variables = new Dictionary<string, string>(StringComparer.Ordinal);
        while (DateTime.UtcNow < deadline)
        {
            variables = await VariablesAsync(api, instanceId);
            if (until(variables)) return variables;
            await Task.Delay(1000);
        }

        Assert.Fail($"Timed out after 45s waiting for {what}. " +
                    $"Variables were: {string.Join(", ", variables.Keys)}");
        return variables;
    }

    private static async Task<List<string>> TaskNamesAsync(IAPIRequestContext api, string instanceId)
    {
        var response = await api.GetAsync($"/api/executions/{instanceId}/tasks");
        Assert.True(response.Ok, $"Reading tasks failed: {response.Status} {await response.TextAsync()}");
        using var document = JsonDocument.Parse(await response.TextAsync());
        return document.RootElement.EnumerateArray()
            .Select(element => element.GetProperty("name").GetString()!)
            .ToList();
    }

    private static async Task<List<string>> EventuallyAsync(
        IAPIRequestContext api, string instanceId, Func<List<string>, bool> until, string what)
    {
        var deadline = DateTime.UtcNow.AddSeconds(45);
        List<string> names = [];
        while (DateTime.UtcNow < deadline)
        {
            names = await TaskNamesAsync(api, instanceId);
            if (until(names)) return names;
            await Task.Delay(1000);
        }

        Assert.Fail($"Timed out after 45s waiting for {what}. Tasks were: {string.Join(", ", names)}");
        return names;
    }
}
