using System.Text.Json;
using AutoNate.E2E.Tests.Support;
using Microsoft.Playwright;
using Xunit;

namespace AutoNate.E2E.Tests;

/// <summary>
/// Conditional events actually react to a condition becoming true (#158).
/// </summary>
/// <remarks>
/// <para>
/// These assert **behaviour, not deployment**. Every element in this milestone
/// deploys; the epic exists because deploying is not the same as working, and a
/// conditional event that deploys and waits forever looks exactly like one that
/// works until someone waits for it.
/// </para>
/// <para>
/// The specific link under test is the one Flowable does not provide: it does not
/// re-evaluate conditional events when a variable changes. Auton8 asks it to, after
/// every variable write. If that call is ever removed these fail, which is the whole
/// point — the symptom in production would be a process that simply stops.
/// </para>
/// <para>
/// Driven through the API rather than the studio canvas: the assertion is about the
/// engine, and clicking a condition into bpmn-js would test the modeller instead.
/// </para>
/// </remarks>
[Trait("RequiresService", "Flowable")]
[Collection(AutoNateE2ECollection.Name)]
public sealed class ConditionalEventExecutionTests : E2ETestBase
{
    public ConditionalEventExecutionTests(AutoNateE2EFixture fixture) : base(fixture) { }

    [Fact]
    public async Task A_catch_resumes_when_a_variable_makes_its_condition_true()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var api = session.Page.APIRequest;

        var key = $"cond_catch_{Guid.NewGuid():N}"[..24];
        await PublishAsync(api, key, $$"""
            <?xml version="1.0" encoding="UTF-8"?>
            <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                              xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
                              id="Definitions_1"
                              targetNamespace="http://autonate.dev/workflows">
              <bpmn:process id="{{key}}" name="Cond Catch" isExecutable="true">
                <bpmn:startEvent id="s" />
                <bpmn:sequenceFlow id="f1" sourceRef="s" targetRef="wait" />
                <bpmn:intermediateCatchEvent id="wait" name="Wait for approval">
                  <bpmn:conditionalEventDefinition id="cd">
                    <bpmn:condition xsi:type="bpmn:tFormalExpression">${approved == true}</bpmn:condition>
                  </bpmn:conditionalEventDefinition>
                </bpmn:intermediateCatchEvent>
                <bpmn:sequenceFlow id="f2" sourceRef="wait" targetRef="after" />
                <bpmn:userTask id="after" name="After the wait" />
                <bpmn:sequenceFlow id="f3" sourceRef="after" targetRef="e" />
                <bpmn:endEvent id="e" />
              </bpmn:process>
              {{Di(key, "s", "wait", "after", "e")}}
            </bpmn:definitions>
            """);

        var instanceId = await StartAsync(api, key, new { approved = false });

        // Precondition: parked at the catch. Without this the test would pass for a
        // process that ran straight through and never waited at all.
        Assert.Equal(new[] { "wait" }, await CurrentActivitiesAsync(api, instanceId));

        // The act under test: a variable changed from OUTSIDE the process, which is
        // what the acceptance criterion asks for — not a condition that happened to
        // be true when the process arrived.
        var set = await api.PutAsync($"/api/executions/{instanceId}/variables", new APIRequestContextOptions
        {
            DataObject = new { variables = new[] { new { name = "approved", value = true, type = "boolean" } } }
        });
        Assert.True(set.Ok, $"Setting the variable failed: {set.Status} {await set.TextAsync()}");

        // And it moved on. This is the assertion that fails if the
        // evaluate-conditions call is ever dropped: Flowable leaves the process
        // parked on an already-true condition, silently and forever.
        Assert.Equal(new[] { "after" }, await CurrentActivitiesAsync(api, instanceId));
    }

    [Fact]
    public async Task A_condition_already_true_on_arrival_does_not_strand_the_process()
    {
        // AC3. Flowable's own behaviour here is to park anyway: it evaluates
        // conditional events only when asked, never on arrival, so a process
        // started with its condition already satisfied waits forever. Verified
        // directly against 8.0.0 before this was written.
        //
        // That is a hang, not a defined behaviour, so Auton8 asks for an evaluation
        // after starting an instance and after completing a task — the two moments
        // a token can land on a catch it can already pass.
        await using var session = await NewSignedInAsAdminAsync();
        var api = session.Page.APIRequest;

        var key = $"cond_true_{Guid.NewGuid():N}"[..24];
        await PublishAsync(api, key, $$"""
            <?xml version="1.0" encoding="UTF-8"?>
            <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                              xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
                              id="Definitions_1"
                              targetNamespace="http://autonate.dev/workflows">
              <bpmn:process id="{{key}}" name="Already True" isExecutable="true">
                <bpmn:startEvent id="s" />
                <bpmn:sequenceFlow id="f1" sourceRef="s" targetRef="wait" />
                <bpmn:intermediateCatchEvent id="wait" name="Wait for approval">
                  <bpmn:conditionalEventDefinition id="cd">
                    <bpmn:condition xsi:type="bpmn:tFormalExpression">${approved == true}</bpmn:condition>
                  </bpmn:conditionalEventDefinition>
                </bpmn:intermediateCatchEvent>
                <bpmn:sequenceFlow id="f2" sourceRef="wait" targetRef="after" />
                <bpmn:userTask id="after" name="After the wait" />
                <bpmn:sequenceFlow id="f3" sourceRef="after" targetRef="e" />
                <bpmn:endEvent id="e" />
              </bpmn:process>
              {{Di(key, "s", "wait", "after", "e")}}
            </bpmn:definitions>
            """);

        // Started with the condition ALREADY satisfied — nothing changes it later.
        var instanceId = await StartAsync(api, key, new { approved = true });

        // It passed straight through rather than parking at a condition that was
        // true the whole time.
        Assert.Equal(new[] { "after" }, await CurrentActivitiesAsync(api, instanceId));
    }

    [Fact]
    public async Task A_boundary_condition_interrupts_or_runs_alongside_as_configured()
    {
        // Both halves in one process, so the two behaviours are compared against the
        // same diagram rather than two that might differ in some other way.
        await using var session = await NewSignedInAsAdminAsync();
        var api = session.Page.APIRequest;

        var key = $"cond_bnd_{Guid.NewGuid():N}"[..24];
        var xml = $$"""
            <?xml version="1.0" encoding="UTF-8"?>
            <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                              xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
                              id="Definitions_1"
                              targetNamespace="http://autonate.dev/workflows">
              <bpmn:process id="{{key}}" name="Cond Boundary" isExecutable="true">
                <bpmn:startEvent id="s" />
                <bpmn:sequenceFlow id="f1" sourceRef="s" targetRef="work" />
                <bpmn:userTask id="work" name="Do the work" />
                <bpmn:sequenceFlow id="f2" sourceRef="work" targetRef="e" />
                <bpmn:endEvent id="e" />

                <bpmn:boundaryEvent id="interrupting" name="Escalate" attachedToRef="work" cancelActivity="true">
                  <bpmn:conditionalEventDefinition id="cdi">
                    <bpmn:condition xsi:type="bpmn:tFormalExpression">${escalate == true}</bpmn:condition>
                  </bpmn:conditionalEventDefinition>
                </bpmn:boundaryEvent>
                <bpmn:sequenceFlow id="f3" sourceRef="interrupting" targetRef="escalated" />
                <bpmn:userTask id="escalated" name="Escalated" />

                <bpmn:boundaryEvent id="continuing" name="Notify" attachedToRef="work" cancelActivity="false">
                  <bpmn:conditionalEventDefinition id="cdn">
                    <bpmn:condition xsi:type="bpmn:tFormalExpression">${notify == true}</bpmn:condition>
                  </bpmn:conditionalEventDefinition>
                </bpmn:boundaryEvent>
                <bpmn:sequenceFlow id="f4" sourceRef="continuing" targetRef="notified" />
                <bpmn:userTask id="notified" name="Notified" />
              </bpmn:process>
              {{Di(key, "s", "work", "e", "interrupting", "escalated", "continuing", "notified")}}
            </bpmn:definitions>
            """;
        await PublishAsync(api, key, xml);

        // A waiting boundary event is itself a current activity — the engine reports
        // the live subscription alongside the activity it is attached to. So these
        // assert on what the two paths do, not on the exact set.

        // ── Non-interrupting: a second path opens, the work carries on ──────
        var carryOn = await StartAsync(api, key, new { escalate = false, notify = false });
        var beforeNotify = await CurrentActivitiesAsync(api, carryOn);
        Assert.Contains("work", beforeNotify);
        Assert.DoesNotContain("notified", beforeNotify);

        await SetVariableAsync(api, carryOn, "notify", true);

        var afterNotify = await CurrentActivitiesAsync(api, carryOn);
        Assert.Contains("notified", afterNotify);
        // The half that is easy to omit and is the entire meaning of
        // "non-interrupting": the attached activity is STILL THERE.
        Assert.Contains("work", afterNotify);

        // ── Interrupting: the work is cancelled ─────────────────────────────
        var interrupted = await StartAsync(api, key, new { escalate = false, notify = false });
        var beforeEscalate = await CurrentActivitiesAsync(api, interrupted);
        Assert.Contains("work", beforeEscalate);
        Assert.DoesNotContain("escalated", beforeEscalate);

        await SetVariableAsync(api, interrupted, "escalate", true);

        var afterEscalate = await CurrentActivitiesAsync(api, interrupted);
        Assert.Contains("escalated", afterEscalate);
        // Asserted cancelled, not merely "the boundary path also ran". This is the
        // difference between the two configurations, and a test that only checked
        // the boundary path would pass for both.
        Assert.DoesNotContain("work", afterEscalate);
        // Both subscriptions go with the activity they were attached to.
        Assert.DoesNotContain("continuing", afterEscalate);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Minimal BPMN diagram notation.
    /// </summary>
    /// <remarks>
    /// Not decoration: <c>GET /api/executions/{id}/diagram</c> — the endpoint these
    /// tests observe state through — fails outright on a definition deployed without
    /// DI, with "it can run, but Flowable cannot provide a visual diagram for it".
    /// The studio always emits DI, so a fixture without it is unrealistic as well as
    /// unobservable.
    /// </remarks>
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
        var created = await api.PostAsync("/api/workflows/", new APIRequestContextOptions
        {
            DataObject = new { id, name = key, processKey = key, bpmnXml = xml }
        });
        Assert.True(created.Ok, $"Creating the model failed: {created.Status} {await created.TextAsync()}");

        // Publish takes the whole model, and checks the body's id against the route.
        var published = await api.PostAsync($"/api/workflows/{id}/publish", new APIRequestContextOptions
        {
            DataObject = new { id, name = key, processKey = key, bpmnXml = xml }
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

    private static async Task SetVariableAsync(
        IAPIRequestContext api, string instanceId, string name, bool value)
    {
        var response = await api.PutAsync($"/api/executions/{instanceId}/variables", new APIRequestContextOptions
        {
            DataObject = new { variables = new[] { new { name, value, type = "boolean" } } }
        });
        Assert.True(response.Ok, $"Setting '{name}' failed: {response.Status} {await response.TextAsync()}");
    }

    /// <summary>Where the instance currently is, sorted so assertions are stable.</summary>
    private static async Task<string[]> CurrentActivitiesAsync(IAPIRequestContext api, string instanceId)
    {
        var response = await api.GetAsync($"/api/executions/{instanceId}/diagram");
        Assert.True(response.Ok, $"Reading the diagram failed: {response.Status} {await response.TextAsync()}");

        using var document = JsonDocument.Parse(await response.TextAsync());
        return document.RootElement.GetProperty("currentActivityIds")
            .EnumerateArray()
            .Select(element => element.GetString()!)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
    }
}
