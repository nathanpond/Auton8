using System.Text.Json;
using AutoNate.E2E.Tests.Support;
using Microsoft.Playwright;
using Xunit;

namespace AutoNate.E2E.Tests;

/// <summary>
/// Errors and escalations route the process (#114).
/// </summary>
/// <remarks>
/// Each test asserts the half a naive implementation omits: that the error path
/// ran AND the normal path did not, that the escalation path ran AND the attached
/// activity kept going. Asserting only the first of each pair passes for the
/// opposite feature.
///
/// The escalation END event is asserted directly rather than by analogy with the
/// error end event — they are different engine paths and this story says so.
/// </remarks>
[Trait("RequiresService", "Flowable")]
[Collection(AutoNateE2ECollection.Name)]
public sealed class ErrorEscalationExecutionTests : E2ETestBase
{
    public ErrorEscalationExecutionTests(AutoNateE2EFixture fixture) : base(fixture) { }

    [Fact]
    public async Task A_matching_error_takes_the_boundary_path_and_abandons_the_normal_one()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var api = session.Page.APIRequest;

        var key = $"ee_err_{Guid.NewGuid():N}"[..24];
        await PublishAsync(api, key, ErrorDiagram(key, boundaryCode: "Err_Known"));

        var instance = await StartAsync(api, key);
        var names = await EventuallyAsync(api, instance,
            n => n.Contains("Caught error"), "the error boundary to fire");

        Assert.Contains("Caught error", names);
        // The half that matters: an error INTERRUPTS. If the normal path also ran,
        // the subprocess completed normally and the boundary merely added a branch.
        Assert.DoesNotContain("No error", names);
    }

    [Fact]
    public async Task An_error_no_boundary_catches_is_refused_at_publish()
    {
        // Verified against Flowable 8.0.0: reaching such an event answers the start
        // call with 500 and leaves NO instance — no history, nothing on the error
        // surface. #114 pre-decided that an instance disappearing is a defect, so
        // it is refused while the author still has the diagram open.
        await using var session = await NewSignedInAsAdminAsync();
        var api = session.Page.APIRequest;

        var key = $"ee_unc_{Guid.NewGuid():N}"[..24];
        var response = await TryPublishAsync(api, key, ErrorDiagram(key, boundaryCode: "Err_Other"));

        Assert.False(response.Ok, "Publishing an uncatchable error should be refused.");
        var body = await response.TextAsync();

        // The CODE the author typed, not the id of the <bpmn:error> element
        // (#242). BPMN matches a thrown error to a boundary on errorCode, so
        // quoting the ref id told the author about a detail that is not the one
        // deciding the outcome -- and two <bpmn:error> roots sharing a code read
        // as non-matching, which refused valid diagrams.
        Assert.Contains("E_KNOWN", body);
        Assert.Contains("nothing in", body);
    }

    [Fact]
    public async Task An_escalation_end_event_raises_its_code_and_a_boundary_catches_it()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var api = session.Page.APIRequest;

        var key = $"ee_esc_{Guid.NewGuid():N}"[..24];
        await PublishAsync(api, key, $$"""
            <?xml version="1.0" encoding="UTF-8"?>
            <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                              id="Definitions_1" targetNamespace="http://autonate.dev/workflows">
              <bpmn:escalation id="Esc_1" escalationCode="ESC_1" name="Esc" />
              <bpmn:process id="{{key}}" name="Escalation End" isExecutable="true">
                <bpmn:startEvent id="s" />
                <bpmn:sequenceFlow id="f0" sourceRef="s" targetRef="sub" />
                <bpmn:subProcess id="sub" name="Escalating">
                  <bpmn:startEvent id="is" />
                  <bpmn:sequenceFlow id="if0" sourceRef="is" targetRef="ie" />
                  <bpmn:endEvent id="ie">
                    <bpmn:escalationEventDefinition escalationRef="Esc_1" />
                  </bpmn:endEvent>
                </bpmn:subProcess>
                <bpmn:boundaryEvent id="bnd" attachedToRef="sub">
                  <bpmn:escalationEventDefinition escalationRef="Esc_1" />
                </bpmn:boundaryEvent>
                <bpmn:sequenceFlow id="fb" sourceRef="bnd" targetRef="escalated" />
                <bpmn:userTask id="escalated" name="Escalated" />
                <bpmn:sequenceFlow id="f1" sourceRef="sub" targetRef="after" />
                <bpmn:userTask id="after" name="After subprocess" />
              </bpmn:process>
              {{Di(key, "s", "sub", "bnd", "escalated", "after")}}
            </bpmn:definitions>
            """);

        var instance = await StartAsync(api, key);
        var names = await EventuallyAsync(api, instance,
            n => n.Contains("Escalated"), "the escalation end event's code to be caught");

        Assert.Contains("Escalated", names);

        // #246. The boundary event is interrupting, so the subprocess is
        // cancelled and its outgoing flow is never taken. Asserting only that
        // "Escalated" appeared passes for a NON-interrupting boundary too, which
        // is the opposite feature -- and every other test in this file carries
        // its complement, so this one was the outlier.
        Assert.DoesNotContain("After subprocess", names);

        // And it stays that way. A cancellation that merely lost a race would
        // show up as the normal path arriving a moment later.
        await Task.Delay(2_000);
        var settled = await TaskNamesAsync(api, instance);
        Assert.Contains("Escalated", settled);
        Assert.DoesNotContain("After subprocess", settled);
    }

    [Fact]
    public async Task A_non_interrupting_escalation_lets_the_attached_activity_continue()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var api = session.Page.APIRequest;

        var key = $"ee_nin_{Guid.NewGuid():N}"[..24];
        await PublishAsync(api, key, $$"""
            <?xml version="1.0" encoding="UTF-8"?>
            <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                              id="Definitions_1" targetNamespace="http://autonate.dev/workflows">
              <bpmn:escalation id="Esc_1" escalationCode="ESC_1" name="Esc" />
              <bpmn:process id="{{key}}" name="Non Interrupting" isExecutable="true">
                <bpmn:startEvent id="s" />
                <bpmn:sequenceFlow id="f0" sourceRef="s" targetRef="sub" />
                <bpmn:subProcess id="sub" name="Escalating">
                  <bpmn:startEvent id="is" />
                  <bpmn:sequenceFlow id="if0" sourceRef="is" targetRef="throw" />
                  <bpmn:intermediateThrowEvent id="throw">
                    <bpmn:escalationEventDefinition escalationRef="Esc_1" />
                  </bpmn:intermediateThrowEvent>
                  <bpmn:sequenceFlow id="if1" sourceRef="throw" targetRef="inner" />
                  <bpmn:userTask id="inner" name="Inner continues" />
                </bpmn:subProcess>
                <bpmn:boundaryEvent id="bnd" attachedToRef="sub" cancelActivity="false">
                  <bpmn:escalationEventDefinition escalationRef="Esc_1" />
                </bpmn:boundaryEvent>
                <bpmn:sequenceFlow id="fb" sourceRef="bnd" targetRef="escalated" />
                <bpmn:userTask id="escalated" name="Escalated" />
              </bpmn:process>
              {{Di(key, "s", "sub", "bnd", "escalated")}}
            </bpmn:definitions>
            """);

        var instance = await StartAsync(api, key);
        var names = await EventuallyAsync(api, instance,
            n => n.Contains("Escalated"), "the escalation boundary to fire");

        // Both halves. The escalation was handled AND the work it interrupted is
        // still running — which is the whole difference from an error, and from an
        // interrupting escalation.
        Assert.Contains("Escalated", names);
        Assert.Contains("Inner continues", names);

        // And the throw did not stop the inner path: execution continued past it,
        // which is why "Inner continues" exists at all.
    }

    private static string ErrorDiagram(string key, string boundaryCode) => $$"""
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          id="Definitions_1" targetNamespace="http://autonate.dev/workflows">
          <bpmn:error id="Err_Known" errorCode="E_KNOWN" name="Known" />
          <bpmn:error id="Err_Other" errorCode="E_OTHER" name="Other" />
          <bpmn:process id="{{key}}" name="Error Routing" isExecutable="true">
            <bpmn:startEvent id="s" />
            <bpmn:sequenceFlow id="f0" sourceRef="s" targetRef="sub" />
            <bpmn:subProcess id="sub" name="Risky">
              <bpmn:startEvent id="is" />
              <bpmn:sequenceFlow id="if0" sourceRef="is" targetRef="ie" />
              <bpmn:endEvent id="ie" name="Boom">
                <bpmn:errorEventDefinition errorRef="Err_Known" />
              </bpmn:endEvent>
            </bpmn:subProcess>
            <bpmn:boundaryEvent id="bnd" attachedToRef="sub">
              <bpmn:errorEventDefinition errorRef="{{boundaryCode}}" />
            </bpmn:boundaryEvent>
            <bpmn:sequenceFlow id="fb" sourceRef="bnd" targetRef="caught" />
            <bpmn:userTask id="caught" name="Caught error" />
            <bpmn:sequenceFlow id="f1" sourceRef="sub" targetRef="normal" />
            <bpmn:userTask id="normal" name="No error" />
          </bpmn:process>
          {{Di(key, "s", "sub", "bnd", "caught", "normal")}}
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

    private static async Task<IAPIResponse> TryPublishAsync(IAPIRequestContext api, string key, string xml)
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

    private static async Task PublishAsync(IAPIRequestContext api, string key, string xml)
    {
        var published = await TryPublishAsync(api, key, xml);
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

        Assert.Fail($"Timed out after 30s waiting for {what}. Tasks were: {string.Join(", ", names)}");
        return names;
    }
}
