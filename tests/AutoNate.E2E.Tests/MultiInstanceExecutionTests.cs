using System.Text.Json;
using AutoNate.E2E.Tests.Support;
using Microsoft.Playwright;
using Xunit;

namespace AutoNate.E2E.Tests;

/// <summary>
/// Run a step once per item in a collection (#159).
/// </summary>
/// <remarks>
/// Multi-instance is an activity MARKER, not a palette entry, which is why the
/// original inventory could not find it — it has no references in Auton8's own
/// code, only in the vendored bpmn-js. The engine has always been able to do it.
/// </remarks>
[Trait("RequiresService", "Flowable")]
[Collection(AutoNateE2ECollection.Name)]
public sealed class MultiInstanceExecutionTests : E2ETestBase
{
    public MultiInstanceExecutionTests(AutoNateE2EFixture fixture) : base(fixture) { }

    [Fact]
    public async Task Each_instance_sees_its_own_item_and_sequential_runs_them_in_order()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var api = session.Page.APIRequest;

        var key = $"mis{Guid.NewGuid():N}"[..20];
        await PublishAsync(api, key, ScriptDiagram(key, sequential: true));
        var instance = await StartAsync(api, key, ["alpha", "beta", "gamma"]);

        var variables = await EventuallyVariablesAsync(api, instance,
            v => v.TryGetValue("trail", out var t) && t.Count(c => c == ';') == 3,
            "all three instances to run");

        // The element variable is the whole feature. A test asserting only the
        // instance COUNT would pass without each instance ever seeing its item.
        // Sequential ordering is asserted here too, because it is invisible
        // otherwise.
        Assert.Equal("alpha;beta;gamma;", variables["trail"]);
    }

    [Fact]
    public async Task An_empty_collection_completes_immediately_instead_of_hanging()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var api = session.Page.APIRequest;

        var key = $"mie{Guid.NewGuid():N}"[..20];
        await PublishAsync(api, key, ScriptDiagram(key, sequential: true));
        var instance = await StartAsync(api, key, []);

        // The edge case most likely to reach production. It must complete the
        // activity without creating instances, not hang and not fail.
        var variables = await EventuallyVariablesAsync(api, instance,
            v => v.ContainsKey("finished"), "the process to complete past an empty collection");

        Assert.Equal("", variables.GetValueOrDefault("trail", ""));
        Assert.True(variables.ContainsKey("finished"));
    }

    [Fact]
    public async Task A_parallel_multi_instance_user_task_produces_one_task_per_item()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var api = session.Page.APIRequest;

        var key = $"mip{Guid.NewGuid():N}"[..20];
        await PublishAsync(api, key, UserTaskDiagram(key, sequential: false));
        var instance = await StartAsync(api, key, ["a", "b", "c"]);

        var names = await EventuallyAsync(api, instance, n => n.Count == 3,
            "three concurrent instances of the task");
        Assert.Equal(3, names.Count(n => n == "Approve"));
    }

    [Fact]
    public async Task A_sequential_multi_instance_user_task_produces_one_at_a_time()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var api = session.Page.APIRequest;

        var key = $"mit{Guid.NewGuid():N}"[..20];
        await PublishAsync(api, key, UserTaskDiagram(key, sequential: true));
        var instance = await StartAsync(api, key, ["a", "b", "c"]);

        var names = await EventuallyAsync(api, instance, n => n.Count >= 1, "the first instance");

        // The complement of the parallel test, and the pair is what makes either
        // meaningful — isSequential ignored in either direction passes one of them.
        Assert.Single(names);
    }

    [Fact]
    public async Task A_loop_marker_is_refused_at_publish_naming_the_step()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var api = session.Page.APIRequest;

        var key = $"mil{Guid.NewGuid():N}"[..20];
        var xml = UserTaskDiagram(key, sequential: true).Replace(
            "<bpmn:multiInstanceLoopCharacteristics isSequential=\"true\" " +
            "flowable:collection=\"${items}\" flowable:elementVariable=\"item\" />",
            "<bpmn:standardLoopCharacteristics loopMaximum=\"3\" />",
            StringComparison.Ordinal);
        Assert.Contains("standardLoopCharacteristics", xml, StringComparison.Ordinal);

        var id = Guid.NewGuid();
        var created = await api.PostAsync("/api/workflows/", new APIRequestContextOptions
        {
            DataObject = new { id, name = TestNames.Prefixed(key), processKey = key, bpmnXml = xml }
        });
        Assert.True(created.Ok, await created.TextAsync());

        var published = await api.PostAsync($"/api/workflows/{id}/publish", new APIRequestContextOptions
        {
            DataObject = new { id, name = TestNames.Prefixed(key), processKey = key, bpmnXml = xml }
        });

        // Flowable never repeats the activity — measured against a control on the
        // same task. Publishing it would give an author a loop that runs once.
        Assert.False(published.Ok);
        var body = await published.TextAsync();
        Assert.Contains("Loop Marker", body, StringComparison.Ordinal);
        Assert.Contains("Approve", body, StringComparison.Ordinal);
    }

    // ── diagrams ─────────────────────────────────────────────────────────────

    private static string Marker(bool sequential) =>
        $"<bpmn:multiInstanceLoopCharacteristics isSequential=\"{(sequential ? "true" : "false")}\" " +
        "flowable:collection=\"${items}\" flowable:elementVariable=\"item\" />";

    private static string ScriptDiagram(string key, bool sequential) => $$"""
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          xmlns:flowable="http://flowable.org/bpmn"
                          xmlns:autonate="http://autonate.dev/workflows"
                          id="Definitions_1" targetNamespace="http://autonate.dev/workflows">
          <bpmn:process id="{{key}}" name="Each item" isExecutable="true">
            <bpmn:startEvent id="s" />
            <bpmn:sequenceFlow id="f0" sourceRef="s" targetRef="t" />
            <bpmn:scriptTask id="t" name="Handle item" scriptFormat="javascript"
                             autonate:runAs="workflowAuthor">
              {{Marker(sequential)}}
              <!-- variables.get, not a bare `item`: the sandbox exposes process
                   variables through the variables API and binds no bare
                   identifiers, so `item` alone is a ReferenceError. -->
              <bpmn:script>variables.set('trail', (variables.get('trail') || '') + variables.get('item') + ';');</bpmn:script>
            </bpmn:scriptTask>
            <bpmn:sequenceFlow id="f1" sourceRef="t" targetRef="done" />
            <bpmn:scriptTask id="done" name="Finish" scriptFormat="javascript"
                             autonate:runAs="workflowAuthor">
              <bpmn:script>variables.set('finished', true);</bpmn:script>
            </bpmn:scriptTask>
            <bpmn:sequenceFlow id="f2" sourceRef="done" targetRef="e" />
            <bpmn:endEvent id="e" />
          </bpmn:process>
          {{Di(key, "s", "t", "done", "e")}}
        </bpmn:definitions>
        """;

    private static string UserTaskDiagram(string key, bool sequential) => $$"""
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          xmlns:flowable="http://flowable.org/bpmn"
                          id="Definitions_1" targetNamespace="http://autonate.dev/workflows">
          <bpmn:process id="{{key}}" name="Approvals" isExecutable="true">
            <bpmn:startEvent id="s" />
            <bpmn:sequenceFlow id="f0" sourceRef="s" targetRef="t" />
            <bpmn:userTask id="t" name="Approve">
              {{Marker(sequential)}}
            </bpmn:userTask>
            <bpmn:sequenceFlow id="f1" sourceRef="t" targetRef="e" />
            <bpmn:endEvent id="e" />
          </bpmn:process>
          {{Di(key, "s", "t", "e")}}
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

    // ── helpers ──────────────────────────────────────────────────────────────

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

    private static async Task<string> StartAsync(
        IAPIRequestContext api, string key, string[] items)
    {
        var response = await api.PostAsync($"/api/workflows/{key}/start", new APIRequestContextOptions
        {
            DataObject = new { variables = new { items } }
        });
        Assert.True(response.Ok, $"Starting failed: {response.Status} {await response.TextAsync()}");
        using var document = JsonDocument.Parse(await response.TextAsync());
        return document.RootElement.GetProperty("id").GetString()!;
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

        Assert.Fail($"Timed out waiting for {what}. Variables: " +
                    string.Join(", ", variables.Select(v => $"{v.Key}={v.Value}")));
        return variables;
    }

    private static async Task<List<string>> EventuallyAsync(
        IAPIRequestContext api, string instanceId, Func<List<string>, bool> until, string what)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        List<string> names = [];
        while (DateTime.UtcNow < deadline)
        {
            var response = await api.GetAsync($"/api/executions/{instanceId}/tasks");
            Assert.True(response.Ok, await response.TextAsync());
            using var document = JsonDocument.Parse(await response.TextAsync());
            names = document.RootElement.EnumerateArray()
                .Select(e => e.GetProperty("name").GetString()!).ToList();
            if (until(names)) return names;
            await Task.Delay(500);
        }

        Assert.Fail($"Timed out waiting for {what}. Tasks were: {string.Join(", ", names)}");
        return names;
    }
}
