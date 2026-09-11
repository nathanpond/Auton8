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

    // ── #252: the defined answers, and the id that was reserved ─────────────

    [Fact]
    public async Task Finishing_a_section_with_an_open_activity_is_refused_with_the_reason()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var api = session.Page.APIRequest;

        var key = $"adc{Guid.NewGuid():N}"[..20];
        await PublishAsync(api, key, Diagram(key));
        var instance = await StartAsync(api, key);

        var state = await AdhocStateAsync(api, instance);
        var executionId = state.GetProperty("executionId").GetString()!;

        await StartActivityAsync(api, instance, executionId, "a1");
        await EventuallyAsync(api, instance, n => n.Count == 1, "the chosen activity to start");

        var response = await api.PostAsync(
            $"/api/executions/{instance}/adhoc/{executionId}/complete",
            new APIRequestContextOptions { DataObject = new { } });
        var body = await response.TextAsync();

        // This was a bare 500 with no body, which #163's AC7 requires be
        // "handled in a defined, documented way" and was neither. The operator
        // saw "Could not complete 'adhoc'." with no reason, because the studio's
        // describeAdhocError had no `message` to find.
        Assert.False(response.Ok, $"Expected a refusal, got {response.Status}: {body}");
        Assert.NotEqual(500, response.Status);
        Assert.Equal(409, response.Status);

        // The engine's own sentence, not ours: it names the actual obstacle.
        Assert.Contains("running child executions", body, StringComparison.OrdinalIgnoreCase);

        // And the refusal changed nothing -- the section is still open and the
        // task still there, so the operator can finish it and try again.
        Assert.Equal(["Call the customer"], await TaskNamesAsync(api, instance));
    }

    [Fact]
    public async Task An_unknown_activity_id_is_a_caller_error_not_a_server_fault()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var api = session.Page.APIRequest;

        var key = $"adu{Guid.NewGuid():N}"[..20];
        await PublishAsync(api, key, Diagram(key));
        var instance = await StartAsync(api, key);

        var state = await AdhocStateAsync(api, instance);
        var executionId = state.GetProperty("executionId").GetString()!;

        var response = await api.PostAsync(
            $"/api/executions/{instance}/adhoc/{executionId}/activities/not-an-activity",
            new APIRequestContextOptions { DataObject = new { } });
        var body = await response.TextAsync();

        Assert.False(response.Ok, $"Expected a refusal, got {response.Status}: {body}");

        // 500 was the measured behaviour for every one of these, which is what
        // made every `catch ... when (IsCallerError)` on this route dead code.
        Assert.NotEqual(500, response.Status);
        Assert.InRange(response.Status, 400, 499);
        Assert.False(string.IsNullOrWhiteSpace(body), "the refusal carried no reason");
    }

    [Fact]
    public async Task An_activity_named_complete_is_started_and_does_not_finish_the_section()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var api = session.Page.APIRequest;

        var key = $"adr{Guid.NewGuid():N}"[..20];

        // `complete` used to be a reserved activity id: the start route and the
        // completion route were one actuator operation that branched on the
        // literal string. Starting this task answered 204 and silently completed
        // the WHOLE subprocess instead, advancing the parent.
        var xml = Diagram(key).Replace(
            "<bpmn:userTask id=\"a1\" name=\"Call the customer\" />",
            "<bpmn:userTask id=\"complete\" name=\"Call the customer\" />",
            StringComparison.Ordinal);
        Assert.Contains("id=\"complete\"", xml, StringComparison.Ordinal);

        await PublishAsync(api, key, xml);
        var instance = await StartAsync(api, key);

        var state = await AdhocStateAsync(api, instance);
        var executionId = state.GetProperty("executionId").GetString()!;

        await StartActivityAsync(api, instance, executionId, "complete");

        // It ran as the activity it is.
        var names = await EventuallyAsync(api, instance,
            n => n.Contains("Call the customer"), "the activity named 'complete' to start");
        Assert.Contains("Call the customer", names);

        // And the section did NOT finish -- which is the whole defect. Asserting
        // only that the task appeared would pass even if the subprocess had also
        // completed underneath it.
        var after = await AdhocStateAsync(api, instance);
        Assert.Equal(executionId, after.GetProperty("executionId").GetString());
        Assert.Contains("a2", after.GetProperty("enabledActivities").EnumerateArray()
            .Select(a => a.GetProperty("id").GetString()!));
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

    /// <summary>
    /// Completing an ad-hoc sub-process releases a conditional catch after it (#293).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Flowable never re-evaluates a conditional event on its own, so a catch whose
    /// condition is <b>already true</b> when the token arrives parks forever. Auton8
    /// nudges it after every delivery it makes — and three of its own endpoints were
    /// not doing so: <c>move-state</c> and both ad-hoc routes.
    /// </para>
    /// <para>
    /// Outcome 10's qualification excused the gap on the grounds that Auton8 "can
    /// only ask for writes it can see". These are writes Auton8 makes itself, so the
    /// excuse does not reach them, and neither does #271's engine-side listener.
    /// </para>
    /// <para>
    /// This one matters most of the three: ad-hoc completion is the <b>ordinary</b>
    /// way case-work advances in the sub-process #163 shipped, so the catch parks on
    /// the normal path rather than an exceptional one. The condition is set true
    /// BEFORE the token can reach the catch, which is the shape the engine never
    /// re-checks — a test that set it afterwards would pass through the variable
    /// route that already nudges, and prove nothing about this one.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Completing_the_subprocess_releases_a_conditional_catch_waiting_after_it()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var api = session.Page.APIRequest;

        var key = $"adhc{Guid.NewGuid():N}"[..20];
        await PublishAsync(api, key, ConditionalAfterAdhocDiagram(key));

        // `ready` is true from the start, so by the time the token reaches the
        // catch the condition is already satisfied and nothing will ever change
        // it again. Only an explicit evaluate-conditions can release it.
        var start = await api.PostAsync($"/api/workflows/{key}/start", new APIRequestContextOptions
        {
            DataObject = new { variables = new { ready = true, done = false } }
        });
        Assert.True(start.Ok, $"Starting failed: {start.Status} {await start.TextAsync()}");
        var instance = JsonDocument.Parse(await start.TextAsync())
            .RootElement.GetProperty("id").GetString()!;

        var state = await AdhocStateAsync(api, instance);
        var executionId = state.GetProperty("executionId").GetString()!;

        var completed = await api.PostAsync(
            $"/api/executions/{instance}/adhoc/{executionId}/complete",
            new APIRequestContextOptions { DataObject = new { } });
        Assert.True(completed.Ok,
            $"Completing the ad-hoc sub-process failed: {completed.Status} {await completed.TextAsync()}");

        var names = await EventuallyAsync(api, instance,
            n => n.Contains("After the condition"),
            "the conditional catch to release after the ad-hoc sub-process completed");

        Assert.Contains("After the condition", names);
    }

    private static string ConditionalAfterAdhocDiagram(string key) => $$"""
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
                          id="Definitions_1" targetNamespace="http://autonate.dev/workflows">
          <bpmn:process id="{{key}}" name="Case work then a condition" isExecutable="true">
            <bpmn:startEvent id="s" />
            <bpmn:sequenceFlow id="f0" sourceRef="s" targetRef="adhoc" />
            <bpmn:adHocSubProcess id="adhoc" name="Case work" ordering="Parallel">
              <bpmn:userTask id="a1" name="Call the customer" />
              <bpmn:completionCondition xsi:type="bpmn:tFormalExpression">${done == true}</bpmn:completionCondition>
            </bpmn:adHocSubProcess>
            <bpmn:sequenceFlow id="f1" sourceRef="adhoc" targetRef="wait" />
            <bpmn:intermediateCatchEvent id="wait" name="Wait for ready">
              <bpmn:conditionalEventDefinition id="cd">
                <bpmn:condition xsi:type="bpmn:tFormalExpression">${ready == true}</bpmn:condition>
              </bpmn:conditionalEventDefinition>
            </bpmn:intermediateCatchEvent>
            <bpmn:sequenceFlow id="f2" sourceRef="wait" targetRef="after" />
            <bpmn:userTask id="after" name="After the condition" />
            <bpmn:sequenceFlow id="f3" sourceRef="after" targetRef="e" />
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
              <bpmndi:BPMNShape id="Shape_wait" bpmnElement="wait">
                <dc:Bounds x="560" y="100" width="36" height="36" />
              </bpmndi:BPMNShape>
              <bpmndi:BPMNShape id="Shape_after" bpmnElement="after">
                <dc:Bounds x="640" y="78" width="100" height="80" />
              </bpmndi:BPMNShape>
              <bpmndi:BPMNShape id="Shape_e" bpmnElement="e">
                <dc:Bounds x="780" y="100" width="36" height="36" />
              </bpmndi:BPMNShape>
            </bpmndi:BPMNPlane>
          </bpmndi:BPMNDiagram>
        </bpmn:definitions>
        """;
}
