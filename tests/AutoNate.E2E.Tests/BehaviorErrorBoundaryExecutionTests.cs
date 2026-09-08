using System.Text.Json;
using AutoNate.E2E.Tests.Support;
using Microsoft.Playwright;
using Xunit;

namespace AutoNate.E2E.Tests;

/// <summary>
/// A behaviour's declared BPMN error is caught by an error boundary event (#114,
/// unblocked by #223).
/// </summary>
/// <remarks>
/// This is the criterion #114 could not demonstrate. The bridge was built and
/// unit-tested on both sides, but the catch could never be shown: Flowable's
/// callback reached the app in the autonate-web container while the fixture ran
/// its own app against another database, so a behaviour invoked by a
/// test-published workflow executed where that workflow did not exist.
///
/// #223 lets the deployed diagram name its own callback URL. If that fix is ever
/// undone, this test fails — which is the point of putting the proof here rather
/// than asserting the plumbing.
/// </remarks>
[Trait("RequiresService", "Flowable")]
[Collection(AutoNateE2ECollection.Name)]
public sealed class BehaviorErrorBoundaryExecutionTests : E2ETestBase
{
    public BehaviorErrorBoundaryExecutionTests(AutoNateE2EFixture fixture) : base(fixture) { }

    [Fact]
    public async Task A_behaviours_declared_error_is_caught_by_a_matching_boundary_event()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var api = session.Page.APIRequest;

        var key = $"bh_err_{Guid.NewGuid():N}"[..22];
        await PublishAsync(api, key, Diagram(key, boundaryCode: "Err_Declined"));

        var instance = await StartAsync(api, key);

        var names = await EventuallyAsync(api, instance,
            n => n.Contains("Payment declined"),
            "the behaviour's declared error to reach the boundary event");

        // The error routed the process down the boundary path...
        Assert.Contains("Payment declined", names);
        // ...and the normal path was abandoned, which is what makes it an error
        // rather than an extra branch.
        Assert.DoesNotContain("Charged", names);
    }

    [Fact]
    public async Task An_undeclared_failure_is_not_caught_and_stays_an_unhandled_failure()
    {
        // The asymmetry decided in #114's planning, now demonstrable end to end.
        // If any failure could be caught, "the database was briefly unreachable"
        // would travel down the "payment declined" branch.
        await using var session = await NewSignedInAsAdminAsync();
        var api = session.Page.APIRequest;

        var key = $"bh_und_{Guid.NewGuid():N}"[..22];
        await PublishAsync(api, key, Diagram(key, boundaryCode: "Err_Declined",
            behaviorKey: "autonate.no-such-behavior-for-tests"));

        var instance = await StartAsync(api, key);

        // An unknown behaviour is an operational failure, not a business error:
        // it must NOT reach the boundary. Waited on the dead-letter rather than
        // asserted immediately, so this cannot pass by looking too early.
        var deadLettered = await EventuallyDeadLetteredAsync(instance, "charge");
        Assert.Equal(0, deadLettered);

        Assert.DoesNotContain("Payment declined", await TaskNamesAsync(api, instance));
    }

    private static string Diagram(string key, string boundaryCode, string? behaviorKey = null) => $$"""
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          xmlns:flowable="http://flowable.org/bpmn"
                          id="Definitions_1" targetNamespace="http://autonate.dev/workflows">
          <bpmn:error id="Err_Declined" errorCode="PAYMENT_DECLINED" name="Declined" />
          <bpmn:process id="{{key}}" name="Behaviour Error" isExecutable="true">
            <bpmn:startEvent id="s" />
            <bpmn:sequenceFlow id="f0" sourceRef="s" targetRef="charge" />
            <bpmn:serviceTask id="charge" name="Charge the card"
                              flowable:delegateExpression="${autonateBehaviorDelegate}"
                              flowable:autonateServiceKind="behavior"
                              flowable:behaviorKey="{{behaviorKey ?? "autonate.always-declines"}}"
                              flowable:async="true" />
            <bpmn:boundaryEvent id="declined" attachedToRef="charge">
              <bpmn:errorEventDefinition errorRef="{{boundaryCode}}" />
            </bpmn:boundaryEvent>
            <bpmn:sequenceFlow id="fb" sourceRef="declined" targetRef="handled" />
            <bpmn:userTask id="handled" name="Payment declined" />
            <bpmn:sequenceFlow id="f1" sourceRef="charge" targetRef="ok" />
            <bpmn:userTask id="ok" name="Charged" />
          </bpmn:process>
          {{Di(key, "s", "charge", "declined", "handled", "ok")}}
        </bpmn:definitions>
        """;

    [Fact]
    public async Task An_error_code_the_behaviour_never_declared_is_not_catchable()
    {
        // The sharpest form of the asymmetry, and the only test that exercises
        // EnforceDeclaredBusinessError end to end. The behaviour exists, runs,
        // and returns exactly the code the boundary event carries - and is still
        // not caught, because it never declared it.
        //
        // The test above uses an unknown behaviour key, which 404s before any of
        // that logic runs; it would keep passing with the declaration check
        // deleted. This one would not.
        await using var session = await NewSignedInAsAdminAsync();
        var api = session.Page.APIRequest;

        var key = $"bh_nod_{Guid.NewGuid():N}"[..22];
        await PublishAsync(api, key, Diagram(key, boundaryCode: "Err_Declined",
            behaviorKey: "autonate.always-fails-undeclared"));

        var instance = await StartAsync(api, key);

        // Waited on a POSITIVE signal, not asserted immediately: checked too
        // early, "the boundary did not fire" is true of a process that simply
        // has not run yet, and this test would pass without the engine doing
        // anything at all.
        //
        // "Charged" is the right thing to wait for because stripping the code
        // leaves Failed=true, and the bridge does not throw on Failed - so the
        // process continues down its normal outgoing flow. This was verified
        // against the engine: the historic trace of an undeclared run is
        // charge -> f1 -> ok, never the boundary's own flow fb.
        var names = await EventuallyAsync(api, instance,
            n => n.Contains("Charged"),
            "the undeclared failure to fall through to the normal path");

        // The boundary event carries this exact error code and the behaviour
        // returned this exact error code. It is still not caught, because the
        // behaviour never declared it - which is the entire asymmetry.
        Assert.DoesNotContain("Payment declined", names);
    }

    private static async Task<int> EventuallyDeadLetteredAsync(string processInstanceId, string elementId)
    {
        using var client = Support.FlowableDeploymentSweep.CreateClient(
            Environment.GetEnvironmentVariable("AUTONATE_FLOWABLE_URL") ?? "http://localhost:8080/flowable-rest",
            Environment.GetEnvironmentVariable("AUTONATE_FLOWABLE_USER") ?? "rest-admin",
            Environment.GetEnvironmentVariable("AUTONATE_FLOWABLE_PASSWORD") ?? "test");

        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < deadline)
        {
            // Filtered here: #112 established these endpoints ignore a
            // processInstanceId query parameter.
            var body = await client.GetStringAsync("service/management/deadletter-jobs?size=500");
            using var document = JsonDocument.Parse(body);
            foreach (var row in document.RootElement.GetProperty("data").EnumerateArray())
            {
                if (row.TryGetProperty("processInstanceId", out var owner)
                    && owner.GetString() == processInstanceId
                    && row.TryGetProperty("elementId", out var element)
                    && element.GetString() == elementId)
                {
                    return row.GetProperty("retries").GetInt32();
                }
            }

            await Task.Delay(1000);
        }

        Assert.Fail($"'{elementId}' never dead-lettered in {processInstanceId}; an undeclared failure must stay unhandled.");
        return -1;
    }

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
            await Task.Delay(500);
        }

        Assert.Fail($"Timed out after 45s waiting for {what}. Tasks were: {string.Join(", ", names)}");
        return names;
    }
}
