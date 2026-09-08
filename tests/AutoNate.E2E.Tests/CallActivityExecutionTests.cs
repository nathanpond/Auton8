using System.Text.Json;
using AutoNate.E2E.Tests.Support;
using Microsoft.Playwright;
using Xunit;

namespace AutoNate.E2E.Tests;

/// <summary>
/// One process calls another, and keeps calling the version it was published
/// against (#113).
/// </summary>
/// <remarks>
/// Version binding is the test that matters. Flowable's own behaviour is the
/// opposite — a `calledElement` key resolves at run time to whatever is latest,
/// so an unchanged parent silently changes behaviour when someone republishes the
/// child. Verified before implementing; publish now pins the child by definition
/// id.
///
/// The negative half is what proves it: after publishing a NEW child version, the
/// old parent must still run the OLD one. A test that only checked the parent
/// calls its child would pass either way.
/// </remarks>
[Trait("RequiresService", "Flowable")]
[Collection(AutoNateE2ECollection.Name)]
public sealed class CallActivityExecutionTests : E2ETestBase
{
    public CallActivityExecutionTests(AutoNateE2EFixture fixture) : base(fixture) { }

    [Fact]
    public async Task A_parent_waits_for_its_child_and_gets_its_mapped_output_back()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var api = session.Page.APIRequest;

        var childKey = $"ca_c_{Guid.NewGuid():N}"[..22];
        var parentKey = $"ca_p_{Guid.NewGuid():N}"[..22];
        await PublishAsync(api, childKey, ChildDiagram(childKey, "Child work"));
        await PublishAsync(api, parentKey, ParentDiagram(parentKey, childKey));

        var parent = await StartAsync(api, parentKey, new { orderId = "ORD-1" });

        // The parent's OWN task list is empty while it waits — the work is on the
        // child. Asserted because it is the thing that makes a stuck call activity
        // look like a hung process from the parent alone.
        Assert.Empty(await TaskNamesAsync(api, parent));

        // #113 AC: the child is visible FROM THE PARENT through the app, not only
        // by asking the engine directly. This is what makes a waiting call
        // activity inspectable rather than a process that appears hung.
        var childrenResponse = await api.GetAsync($"/api/executions/{parent}/children");
        Assert.True(childrenResponse.Ok,
            $"Reading children failed: {childrenResponse.Status} {await childrenResponse.TextAsync()}");
        using var childrenDoc = JsonDocument.Parse(await childrenResponse.TextAsync());
        Assert.True(
            childrenDoc.RootElement.GetArrayLength() > 0,
            "The parent reported no child executions while it was waiting on a call activity.");
        var childFromApi = childrenDoc.RootElement[0].GetProperty("id").GetString()!;

        var child = await ChildOfAsync(parent);
        Assert.Equal(child, childFromApi);
        Assert.Contains("Child work", await TaskNamesAsync(api, child));

        // And the child's own state is reachable through the same surfaces the
        // parent's is — "navigable" means its history is there to open.
        var childHistory = await api.GetAsync($"/api/executions/{child}/history");
        Assert.True(childHistory.Ok, $"Reading the child's history failed: {childHistory.Status}");

        await CompleteFirstTaskAsync(api, child);

        var parentTasks = await EventuallyAsync(api, parent,
            names => names.Contains("Parent after child"), "the parent to resume");
        Assert.Contains("Parent after child", parentTasks);
    }

    [Fact]
    public async Task Republishing_the_child_does_not_change_what_an_existing_parent_calls()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var api = session.Page.APIRequest;

        var childKey = $"ca_vc_{Guid.NewGuid():N}"[..22];
        var parentKey = $"ca_vp_{Guid.NewGuid():N}"[..22];

        var childId = Guid.NewGuid();
        await PublishAsync(api, childKey, ChildDiagram(childKey, "Child work v1"), childId);
        await PublishAsync(api, parentKey, ParentDiagram(parentKey, childKey));

        // A new version of the child, published AFTER the parent.
        await PublishAsync(api, childKey, ChildDiagram(childKey, "Child work v2"), childId);

        var parent = await StartAsync(api, parentKey, new { orderId = "ORD-2" });
        var child = await ChildOfAsync(parent);
        var names = await TaskNamesAsync(api, child);

        // The whole feature: the parent still runs the version it was published
        // against. Against Flowable's default this reads "Child work v2".
        Assert.Contains("Child work v1", names);
        Assert.DoesNotContain("Child work v2", names);
    }

    [Fact]
    public async Task Publishing_a_parent_whose_child_does_not_exist_is_refused()
    {
        // Flowable deploys this happily and fails only when an instance reaches
        // the call — by which time it is someone else's problem, at the worst
        // possible moment. Verified: the raw deploy returned 201.
        await using var session = await NewSignedInAsAdminAsync();
        var api = session.Page.APIRequest;

        var parentKey = $"ca_mp_{Guid.NewGuid():N}"[..22];
        var response = await TryPublishAsync(
            api, parentKey, ParentDiagram(parentKey, "definitely_not_published"), Guid.NewGuid());

        Assert.False(response.Ok, "Publishing a parent whose child does not exist should be refused.");
        var body = await response.TextAsync();
        Assert.Contains("definitely_not_published", body);
    }

    [Fact]
    public async Task A_workflow_whose_first_version_calls_itself_is_refused()
    {
        // Recursion is bounded by construction: a parent can only pin to a
        // definition that already exists, so every call points strictly backwards
        // in deployment order and the chain terminates. The degenerate case — a
        // first version calling itself — has nothing to resolve, and is refused
        // rather than deployed as an unbounded loop.
        await using var session = await NewSignedInAsAdminAsync();
        var api = session.Page.APIRequest;

        var key = $"ca_rec_{Guid.NewGuid():N}"[..22];
        var response = await TryPublishAsync(api, key, ParentDiagram(key, key), Guid.NewGuid());

        Assert.False(response.Ok, "A workflow whose first version calls itself should be refused.");
        Assert.Contains(key, await response.TextAsync());
    }

    private static string ChildDiagram(string key, string taskName) => $$"""
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          id="Definitions_1" targetNamespace="http://autonate.dev/workflows">
          <bpmn:process id="{{key}}" name="Child" isExecutable="true">
            <bpmn:startEvent id="cs" />
            <bpmn:sequenceFlow id="cf0" sourceRef="cs" targetRef="ct" />
            <bpmn:userTask id="ct" name="{{taskName}}" />
            <bpmn:sequenceFlow id="cf1" sourceRef="ct" targetRef="ce" />
            <bpmn:endEvent id="ce" />
          </bpmn:process>
          {{Di(key, "cs", "ct", "ce")}}
        </bpmn:definitions>
        """;

    private static string ParentDiagram(string key, string childKey) => $$"""
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          xmlns:flowable="http://flowable.org/bpmn"
                          id="Definitions_1" targetNamespace="http://autonate.dev/workflows">
          <bpmn:process id="{{key}}" name="Parent" isExecutable="true">
            <bpmn:startEvent id="ps" />
            <bpmn:sequenceFlow id="pf0" sourceRef="ps" targetRef="call" />
            <bpmn:callActivity id="call" name="Sub" calledElement="{{childKey}}">
              <bpmn:extensionElements>
                <flowable:in source="orderId" target="childOrderId" />
                <flowable:out source="childOrderId" target="returned" />
              </bpmn:extensionElements>
            </bpmn:callActivity>
            <bpmn:sequenceFlow id="pf1" sourceRef="call" targetRef="pt" />
            <bpmn:userTask id="pt" name="Parent after child" />
          </bpmn:process>
          {{Di(key, "ps", "call", "pt")}}
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

    /// <summary>Finds the child instance the engine records against the parent.</summary>
    private static async Task<string> ChildOfAsync(string parentInstanceId)
    {
        using var client = Support.FlowableDeploymentSweep.CreateClient(
            Environment.GetEnvironmentVariable("AUTONATE_FLOWABLE_URL") ?? "http://localhost:8080/flowable-rest",
            Environment.GetEnvironmentVariable("AUTONATE_FLOWABLE_USER") ?? "rest-admin",
            Environment.GetEnvironmentVariable("AUTONATE_FLOWABLE_PASSWORD") ?? "test");

        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < deadline)
        {
            using var content = new StringContent(
                $"{{\"superProcessInstanceId\":\"{parentInstanceId}\"}}",
                System.Text.Encoding.UTF8, "application/json");
            var response = await client.PostAsync("service/query/process-instances", content);
            if (response.IsSuccessStatusCode)
            {
                using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                var data = document.RootElement.GetProperty("data");
                if (data.GetArrayLength() > 0) return data[0].GetProperty("id").GetString()!;
            }

            await Task.Delay(500);
        }

        Assert.Fail($"No child instance was recorded for parent {parentInstanceId}.");
        return string.Empty;
    }

    private static async Task<IAPIResponse> TryPublishAsync(
        IAPIRequestContext api, string key, string xml, Guid id)
    {
        var displayName = TestNames.Prefixed(key);
        // A republish reuses the same model id, which is what produces a second
        // version rather than a second workflow.
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

    private static async Task PublishAsync(
        IAPIRequestContext api, string key, string xml, Guid? id = null)
    {
        var published = await TryPublishAsync(api, key, xml, id ?? Guid.NewGuid());
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

    private static async Task CompleteFirstTaskAsync(IAPIRequestContext api, string instanceId)
    {
        var response = await api.GetAsync($"/api/executions/{instanceId}/tasks");
        using var document = JsonDocument.Parse(await response.TextAsync());
        var taskId = document.RootElement[0].GetProperty("id").GetString()!;
        var completed = await api.PostAsync($"/api/tasks/{taskId}/complete", new APIRequestContextOptions
        {
            DataObject = new { }
        });
        Assert.True(completed.Ok, $"Completing failed: {completed.Status} {await completed.TextAsync()}");
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
        var deadline = DateTime.UtcNow.AddSeconds(30);
        List<string> names = [];
        while (DateTime.UtcNow < deadline)
        {
            names = await TaskNamesAsync(api, instanceId);
            if (until(names)) return names;
            await Task.Delay(500);
        }

        Assert.Fail($"Timed out after 30s waiting for {what}. Tasks were: {string.Join(", ", names)}");
        return names;
    }
}
