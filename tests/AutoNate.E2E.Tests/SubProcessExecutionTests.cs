using System.Text.Json;
using AutoNate.E2E.Tests.Support;
using Microsoft.Playwright;
using Xunit;

namespace AutoNate.E2E.Tests;

/// <summary>
/// Embedded subprocesses execute, and variables cross the boundary (#161).
/// </summary>
/// <remarks>
/// #103 recorded `Sub-Process (Embedded)` as *deploys-and-starts*, which proves
/// instantiation and nothing about whether the inner flow runs, whether the parent
/// resumes, or whether an operator can see where execution is. Those are what this
/// asserts.
/// </remarks>
[Trait("RequiresService", "Flowable")]
[Collection(AutoNateE2ECollection.Name)]
public sealed class SubProcessExecutionTests : E2ETestBase
{
    public SubProcessExecutionTests(AutoNateE2EFixture fixture) : base(fixture) { }

    [Fact]
    public async Task A_nested_subprocess_runs_and_variables_cross_the_boundary_both_ways()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var api = session.Page.APIRequest;

        var key = $"sp_nest_{Guid.NewGuid():N}"[..24];
        await PublishAsync(api, key, $$"""
            <?xml version="1.0" encoding="UTF-8"?>
            <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                              id="Definitions_1" targetNamespace="http://autonate.dev/workflows">
              <bpmn:process id="{{key}}" name="Nested" isExecutable="true">
                <bpmn:startEvent id="s" />
                <bpmn:sequenceFlow id="f0" sourceRef="s" targetRef="outer" />
                <bpmn:subProcess id="outer" name="Outer">
                  <bpmn:startEvent id="os" />
                  <bpmn:sequenceFlow id="of0" sourceRef="os" targetRef="inner" />
                  <bpmn:subProcess id="inner" name="Inner">
                    <bpmn:startEvent id="is" />
                    <bpmn:sequenceFlow id="if0" sourceRef="is" targetRef="deep" />
                    <bpmn:userTask id="deep" name="Deep task" />
                    <bpmn:sequenceFlow id="if1" sourceRef="deep" targetRef="ie" />
                    <bpmn:endEvent id="ie" />
                  </bpmn:subProcess>
                  <bpmn:sequenceFlow id="of1" sourceRef="inner" targetRef="oe" />
                  <bpmn:endEvent id="oe" />
                </bpmn:subProcess>
                <bpmn:sequenceFlow id="f1" sourceRef="outer" targetRef="after" />
                <bpmn:userTask id="after" name="After the subprocess" />
                <bpmn:sequenceFlow id="f2" sourceRef="after" targetRef="e" />
                <bpmn:endEvent id="e" />
              </bpmn:process>
              {{Di(key, "s", "outer", "os", "inner", "is", "deep", "ie", "oe", "after", "e")}}
            </bpmn:definitions>
            """);

        var instanceId = await StartAsync(api, key, new { fromParent = "parent-set" });

        // The inner flow is running two levels down.
        Assert.Contains("Deep task", await TaskNamesAsync(api, instanceId));

        // AC7: an operator can see execution is inside the subprocesses — both
        // report their own activity instance, so the diagram highlights them.
        var current = await CurrentActivitiesAsync(api, instanceId);
        Assert.Contains("outer", current);
        Assert.Contains("inner", current);
        Assert.Contains("deep", current);

        // Set a variable two levels deep and complete out.
        var taskId = await TaskIdAsync(api, instanceId, "Deep task");
        var completed = await api.PostAsync($"/api/tasks/{taskId}/complete", new APIRequestContextOptions
        {
            DataObject = new { variables = new Dictionary<string, object?> { ["fromInside"] = "set-deep" } }
        });
        Assert.True(completed.Ok, $"Completing failed: {completed.Status} {await completed.TextAsync()}");

        // AC2: the parent continues once the subprocess completes. Asserted on the
        // parent's own next activity, not merely on the subprocess being gone.
        Assert.Contains("After the subprocess", await TaskNamesAsync(api, instanceId));

        // AC3: variables cross both ways. `fromParent` was readable inside (the
        // process started with it and ran through), and `fromInside` — set two
        // levels down — is visible at the parent afterwards, because an embedded
        // subprocess shares the parent's scope.
        var variables = await VariablesAsync(api, instanceId);
        Assert.Equal("parent-set", variables["fromParent"]);
        Assert.Equal("set-deep", variables["fromInside"]);
    }

    [Fact]
    public async Task A_subprocess_whose_inner_flow_simply_stops_still_completes()
    {
        // Pins the half of the acceptance criterion that was wrong. The story said a
        // subprocess with no end event "deploys today and hangs"; it does not — the
        // engine completes it once no tokens remain. This asserts the behaviour we
        // deliberately did NOT refuse, so a later well-meaning validation change
        // cannot quietly break it.
        await using var session = await NewSignedInAsAdminAsync();
        var api = session.Page.APIRequest;

        var key = $"sp_noend_{Guid.NewGuid():N}"[..24];
        await PublishAsync(api, key, $$"""
            <?xml version="1.0" encoding="UTF-8"?>
            <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                              id="Definitions_1" targetNamespace="http://autonate.dev/workflows">
              <bpmn:process id="{{key}}" name="Stops quietly" isExecutable="true">
                <bpmn:startEvent id="s" />
                <bpmn:sequenceFlow id="f0" sourceRef="s" targetRef="sub" />
                <bpmn:subProcess id="sub" name="No end inside">
                  <bpmn:startEvent id="ss" />
                  <bpmn:sequenceFlow id="sf0" sourceRef="ss" targetRef="t" />
                  <bpmn:userTask id="t" name="Inner task" />
                </bpmn:subProcess>
                <bpmn:sequenceFlow id="f1" sourceRef="sub" targetRef="after" />
                <bpmn:userTask id="after" name="After" />
                <bpmn:sequenceFlow id="f2" sourceRef="after" targetRef="e" />
                <bpmn:endEvent id="e" />
              </bpmn:process>
              {{Di(key, "s", "sub", "ss", "t", "after", "e")}}
            </bpmn:definitions>
            """);

        var instanceId = await StartAsync(api, key, new { });
        var taskId = await TaskIdAsync(api, instanceId, "Inner task");
        await api.PostAsync($"/api/tasks/{taskId}/complete", new APIRequestContextOptions
        {
            DataObject = new { variables = new Dictionary<string, object?>() }
        });

        // The subprocess completed and the parent moved on, with no end event
        // anywhere inside it.
        Assert.Contains("After", await TaskNamesAsync(api, instanceId));
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static string Di(string processKey, params string[] elementIds)
    {
        var shapes = string.Join("\n", elementIds.Select((id, index) =>
            $"""
                  <bpmndi:BPMNShape id="Shape_{id}" bpmnElement="{id}">
                    <dc:Bounds x="{100 + (index * 130)}" y="100" width="100" height="80" />
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
        // #214: the workflow NAME becomes the Flowable deployment name, and the
        // sweep keys on the `e2e-` prefix TestNames.Prefixed produces. Computed once
        // — a fresh call here and in the publish below would rename the model.
        var displayName = TestNames.Prefixed(key);
        var created = await api.PostAsync("/api/workflows/", new APIRequestContextOptions
        {
            DataObject = new { id, name = displayName, processKey = key, bpmnXml = xml }
        });
        Assert.True(created.Ok, $"Creating failed: {created.Status} {await created.TextAsync()}");
        var published = await api.PostAsync($"/api/workflows/{id}/publish", new APIRequestContextOptions
        {
            DataObject = new { id, name = displayName, processKey = key, bpmnXml = xml }
        });
        Assert.True(published.Ok, $"Publishing failed: {published.Status} {await published.TextAsync()}");
    }

    private static async Task<string> StartAsync(IAPIRequestContext api, string key, object variables)
    {
        var response = await api.PostAsync($"/api/workflows/{key}/start", new APIRequestContextOptions
        {
            DataObject = new { variables }
        });
        Assert.True(response.Ok, $"Starting failed: {response.Status} {await response.TextAsync()}");
        using var document = JsonDocument.Parse(await response.TextAsync());
        return document.RootElement.GetProperty("id").GetString()!;
    }

    private static async Task<List<string>> TaskNamesAsync(IAPIRequestContext api, string instanceId)
    {
        var response = await api.GetAsync($"/api/executions/{instanceId}/tasks");
        Assert.True(response.Ok, $"Reading tasks failed: {response.Status} {await response.TextAsync()}");
        using var document = JsonDocument.Parse(await response.TextAsync());
        return document.RootElement.EnumerateArray()
            .Select(e => e.GetProperty("name").GetString()!).ToList();
    }

    private static async Task<string> TaskIdAsync(IAPIRequestContext api, string instanceId, string name)
    {
        var response = await api.GetAsync($"/api/executions/{instanceId}/tasks");
        using var document = JsonDocument.Parse(await response.TextAsync());
        return document.RootElement.EnumerateArray()
            .First(e => e.GetProperty("name").GetString() == name).GetProperty("id").GetString()!;
    }

    private static async Task<string[]> CurrentActivitiesAsync(IAPIRequestContext api, string instanceId)
    {
        var response = await api.GetAsync($"/api/executions/{instanceId}/diagram");
        Assert.True(response.Ok, $"Reading the diagram failed: {response.Status} {await response.TextAsync()}");
        using var document = JsonDocument.Parse(await response.TextAsync());
        return document.RootElement.GetProperty("currentActivityIds")
            .EnumerateArray().Select(e => e.GetString()!).ToArray();
    }

    private static async Task<Dictionary<string, string>> VariablesAsync(IAPIRequestContext api, string instanceId)
    {
        var response = await api.GetAsync($"/api/executions/{instanceId}/diagram");
        using var document = JsonDocument.Parse(await response.TextAsync());
        var variables = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var v in document.RootElement.GetProperty("variables").EnumerateArray())
        {
            var name = v.GetProperty("name").GetString();
            if (name is null) continue;
            variables[name] = v.GetProperty("value").ToString();
        }
        return variables;
    }
}
