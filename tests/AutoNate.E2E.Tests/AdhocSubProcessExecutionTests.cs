using System.Text.Json;
using AutoNate.E2E.Tests.Support;
using Microsoft.Playwright;
using Xunit;

namespace AutoNate.E2E.Tests;

/// <summary>
/// A person chooses what happens next in an ad-hoc subprocess (#163).
/// </summary>
/// <remarks>
/// Flowable implements the element but exposes no REST surface for it, so the
/// extension adds actuator endpoints and Auton8 gates them. The property worth
/// protecting is repeatability: the same activity can be started again, which is
/// what separates an ad-hoc subprocess from a parallel one and is easy to
/// implement away by accident.
/// </remarks>
[Trait("RequiresService", "Flowable")]
[Collection(AutoNateE2ECollection.Name)]
public sealed class AdhocSubProcessExecutionTests : E2ETestBase
{
    public AdhocSubProcessExecutionTests(AutoNateE2EFixture fixture) : base(fixture) { }

    [Fact]
    public async Task A_person_starts_the_activity_they_choose_and_can_start_it_again()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var api = session.Page.APIRequest;

        var key = $"adh{Guid.NewGuid():N}"[..20];
        await PublishAsync(api, key, Diagram(key));
        var instance = await StartAsync(api, key);

        // Nothing runs until someone picks. A process that auto-started its
        // activities would fail here, and that is the whole premise of the
        // element.
        Assert.Empty(await TaskNamesAsync(api, instance));

        var state = await AdhocStateAsync(api, instance);
        var executionId = state.GetProperty("executionId").GetString()!;
        var enabled = state.GetProperty("enabledActivities").EnumerateArray()
            .Select(a => a.GetProperty("id").GetString()!)
            .ToList();

        Assert.Contains("a1", enabled);
        Assert.Contains("a2", enabled);

        await StartActivityAsync(api, instance, executionId, "a1");
        var afterFirst = await EventuallyAsync(api, instance,
            n => n.Count == 1, "the chosen activity to start");
        Assert.Equal(["Call the customer"], afterFirst);

        // The other activity was NOT started. Asserting only that a1 ran would
        // pass for an implementation that starts everything.
        Assert.DoesNotContain("Review the file", afterFirst);

        // And the same activity can be started again — still enabled, and it
        // runs a second time. A naive implementation removes an activity from
        // the list once started, and nothing else here would catch that.
        Assert.Contains("a1", (await AdhocStateAsync(api, instance))
            .GetProperty("enabledActivities").EnumerateArray()
            .Select(a => a.GetProperty("id").GetString()!));

        await StartActivityAsync(api, instance, executionId, "a1");
        var afterSecond = await EventuallyAsync(api, instance,
            n => n.Count == 2, "the same activity to start a second time");
        Assert.Equal(2, afterSecond.Count(n => n == "Call the customer"));
    }

    [Fact]
    public async Task An_adhoc_subprocess_with_no_completion_condition_is_refused()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var api = session.Page.APIRequest;

        var key = $"adn{Guid.NewGuid():N}"[..20];
        var xml = Diagram(key).Replace(
            "<bpmn:completionCondition xsi:type=\"bpmn:tFormalExpression\">${done == true}</bpmn:completionCondition>",
            "", StringComparison.Ordinal);
        Assert.DoesNotContain("completionCondition", xml, StringComparison.Ordinal);

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

        // Without a completion condition the subprocess never finishes and the
        // parent can never continue. A hang, not a feature.
        Assert.False(published.Ok);
        Assert.Contains("never finish", await published.TextAsync(), StringComparison.Ordinal);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static string Diagram(string key) => $$"""
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
                          id="Definitions_1" targetNamespace="http://autonate.dev/workflows">
          <bpmn:process id="{{key}}" name="Case work" isExecutable="true">
            <bpmn:startEvent id="s" />
            <bpmn:sequenceFlow id="f0" sourceRef="s" targetRef="adhoc" />
            <bpmn:adHocSubProcess id="adhoc" name="Case work" ordering="Parallel">
              <bpmn:userTask id="a1" name="Call the customer" />
              <bpmn:userTask id="a2" name="Review the file" />
              <bpmn:completionCondition xsi:type="bpmn:tFormalExpression">${done == true}</bpmn:completionCondition>
            </bpmn:adHocSubProcess>
            <bpmn:sequenceFlow id="f1" sourceRef="adhoc" targetRef="e" />
            <bpmn:endEvent id="e" />
          </bpmn:process>
          <bpmndi:BPMNDiagram id="Diagram_1"
                              xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI"
                              xmlns:dc="http://www.omg.org/spec/DD/20100524/DC">
            <bpmndi:BPMNPlane id="Plane_1" bpmnElement="{{key}}">
              <bpmndi:BPMNShape id="Shape_s" bpmnElement="s">
                <dc:Bounds x="100" y="100" width="36" height="36" />
              </bpmndi:BPMNShape>
              <bpmndi:BPMNShape id="Shape_adhoc" bpmnElement="adhoc">
                <dc:Bounds x="200" y="60" width="300" height="200" />
              </bpmndi:BPMNShape>
              <bpmndi:BPMNShape id="Shape_e" bpmnElement="e">
                <dc:Bounds x="560" y="100" width="36" height="36" />
              </bpmndi:BPMNShape>
            </bpmndi:BPMNPlane>
          </bpmndi:BPMNDiagram>
        </bpmn:definitions>
        """;

    private static async Task<JsonElement> AdhocStateAsync(IAPIRequestContext api, string instanceId)
    {
        var response = await api.GetAsync($"/api/executions/{instanceId}/adhoc");
        Assert.True(response.Ok, $"Listing ad-hoc state failed: {response.Status} {await response.TextAsync()}");
        var document = JsonDocument.Parse(await response.TextAsync());
        var first = document.RootElement.EnumerateArray().FirstOrDefault();
        Assert.True(first.ValueKind == JsonValueKind.Object,
            "the instance reported no ad-hoc subprocess at all");
        return first.Clone();
    }

    private static async Task StartActivityAsync(
        IAPIRequestContext api, string instanceId, string executionId, string activityId)
    {
        var response = await api.PostAsync(
            $"/api/executions/{instanceId}/adhoc/{executionId}/activities/{activityId}",
            new APIRequestContextOptions { DataObject = new { } });
        Assert.True(response.Ok,
            $"Starting '{activityId}' failed: {response.Status} {await response.TextAsync()}");
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

        Assert.Fail($"Timed out waiting for {what}. Tasks were: {string.Join(", ", names)}");
        return names;
    }
}
