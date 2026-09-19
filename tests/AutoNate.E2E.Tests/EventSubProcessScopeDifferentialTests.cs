using System.Text.Json;
using AutoNate.E2E.Tests.Support;
using Microsoft.Playwright;
using Xunit;

namespace AutoNate.E2E.Tests;

/// <summary>
/// Two diagrams an author cannot tell apart, and one of them does not interrupt (#229).
/// </summary>
/// <remarks>
/// <para>
/// #162 raised this and left it open rather than guessing. An interrupting error
/// event subprocess cancels its scope — that is what "interrupting" means, and it
/// is asserted in <c>EventSubProcessExecutionTests</c>. But that test uses the
/// shape where the error is thrown inside a <b>nested</b> subprocess. Move the
/// throw up beside the handler, into the same scope, and the parallel sibling
/// keeps running.
/// </para>
/// <para>
/// <b>Both shapes are here on purpose.</b> #162's first probe used only the
/// second, and stopping there would have produced the report "Flowable's
/// interrupting event subprocess does not interrupt" — false, and it would have
/// sent the story chasing an engine bug that is not there. One shape is not a
/// measurement; the pair is.
/// </para>
/// <para>
/// This file does not take a position on what the specification requires. It
/// pins what the engine does, which is what an author gets either way, and it is
/// what makes the publish-time warning in <c>WorkflowBpmnXml</c> honest rather
/// than a guess. If a later Flowable starts cancelling the sibling, this fails
/// and the warning becomes removable.
/// </para>
/// </remarks>
[Trait("RequiresService", "Flowable")]
[Collection(AutoNateE2ECollection.Name)]
public sealed class EventSubProcessScopeDifferentialTests : E2ETestBase
{
    public EventSubProcessScopeDifferentialTests(AutoNateE2EFixture fixture) : base(fixture) { }

    [Fact]
    public async Task A_throw_in_a_nested_scope_cancels_the_sibling_but_a_throw_beside_the_handler_does_not()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var api = session.Page.APIRequest;

        // ── Shape A: the canonical one. Error raised inside a nested subprocess,
        //    handler at the process level. The scope is cancelled.
        var nestedKey = $"esd_n_{Guid.NewGuid():N}"[..22];
        await PublishAsync(api, nestedKey, NestedThrowDiagram(nestedKey));
        var nested = await StartAsync(api, nestedKey);
        var nestedNames = await EventuallyAsync(api, nested,
            n => n.Contains("Handled error"), "the handler to run (nested throw)");

        Assert.Contains("Handled error", nestedNames);
        Assert.DoesNotContain("Ongoing work", nestedNames);

        // ── Shape B: the throw beside the handler, same scope. The sibling lives.
        var siblingKey = $"esd_s_{Guid.NewGuid():N}"[..22];
        await PublishAsync(api, siblingKey, SameScopeThrowDiagram(siblingKey));
        var sibling = await StartAsync(api, siblingKey);
        var siblingNames = await EventuallyAsync(api, sibling,
            n => n.Contains("Handled error"), "the handler to run (same-scope throw)");

        Assert.Contains("Handled error", siblingNames);

        // THE DIFFERENCE. Same element, same isInterrupting="true", same words on
        // the canvas — and the work the author expected to be cancelled is still
        // waiting for someone to do it.
        Assert.Contains("Ongoing work", siblingNames);
    }

    [Fact]
    public async Task At_the_process_level_a_same_scope_throw_DOES_cancel_the_sibling()
    {
        // The third case, and the one that makes the warning's condition precise
        // rather than approximate.
        //
        // #229 described the problem as "the handler beside the error end event in
        // the same scope". That is not quite it: with all three at the PROCESS
        // level -- no enclosing subprocess at all -- the sibling is cancelled, and
        // a warning keyed on "same scope" would fire on this, which is correct.
        //
        // It is specifically an event subprocess inside a SUBPROCESS, catching an
        // error thrown in that same subprocess, that leaves the sibling running.
        // Without this fact the warning would be right by accident and wrong on a
        // shape people actually draw.
        await using var session = await NewSignedInAsAdminAsync();
        var api = session.Page.APIRequest;

        var key = $"esd_p_{Guid.NewGuid():N}"[..22];
        await PublishAsync(api, key, ProcessLevelThrowDiagram(key));
        var instance = await StartAsync(api, key);
        var names = await EventuallyAsync(api, instance,
            n => n.Contains("Handled error"), "the handler to run (process-level throw)");

        Assert.Contains("Handled error", names);
        Assert.DoesNotContain("Ongoing work", names);
    }

    /// <summary>
    /// Shape C — throw, sibling and handler all at the process level.
    /// </summary>
    private static string ProcessLevelThrowDiagram(string key) => $$"""
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          id="Definitions_1" targetNamespace="http://autonate.dev/workflows">
          <bpmn:error id="Err_1" errorCode="E1" name="E1" />
          <bpmn:process id="{{key}}" name="Process Level Throw" isExecutable="true">
            <bpmn:startEvent id="s" />
            <bpmn:sequenceFlow id="f0" sourceRef="s" targetRef="fork" />
            <bpmn:parallelGateway id="fork" />
            <bpmn:sequenceFlow id="f1" sourceRef="fork" targetRef="ongoing" />
            <bpmn:userTask id="ongoing" name="Ongoing work" />
            <bpmn:sequenceFlow id="f2" sourceRef="fork" targetRef="boom" />
            <bpmn:endEvent id="boom" name="Boom">
              <bpmn:errorEventDefinition errorRef="Err_1" />
            </bpmn:endEvent>
            <bpmn:subProcess id="handler" name="Handler" triggeredByEvent="true">
              <bpmn:startEvent id="hs" isInterrupting="true">
                <bpmn:errorEventDefinition errorRef="Err_1" />
              </bpmn:startEvent>
              <bpmn:sequenceFlow id="hf" sourceRef="hs" targetRef="ht" />
              <bpmn:userTask id="ht" name="Handled error" />
            </bpmn:subProcess>
          </bpmn:process>
          {{Di(key, "s", "fork", "ongoing", "boom", "handler")}}
        </bpmn:definitions>
        """;

    /// <summary>Shape A — the throw is one scope down from the handler.</summary>
    private static string NestedThrowDiagram(string key) => $$"""
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          id="Definitions_1" targetNamespace="http://autonate.dev/workflows">
          <bpmn:error id="Err_1" errorCode="E1" name="E1" />
          <bpmn:process id="{{key}}" name="Nested Throw" isExecutable="true">
            <bpmn:startEvent id="s" />
            <bpmn:sequenceFlow id="f0" sourceRef="s" targetRef="fork" />
            <bpmn:parallelGateway id="fork" />
            <bpmn:sequenceFlow id="f1" sourceRef="fork" targetRef="ongoing" />
            <bpmn:userTask id="ongoing" name="Ongoing work" />
            <bpmn:sequenceFlow id="f2" sourceRef="fork" targetRef="inner" />
            <bpmn:subProcess id="inner" name="Inner">
              <bpmn:startEvent id="is" />
              <bpmn:sequenceFlow id="if0" sourceRef="is" targetRef="boom" />
              <bpmn:endEvent id="boom" name="Boom">
                <bpmn:errorEventDefinition errorRef="Err_1" />
              </bpmn:endEvent>
            </bpmn:subProcess>
            <bpmn:subProcess id="handler" name="Handler" triggeredByEvent="true">
              <bpmn:startEvent id="hs" isInterrupting="true">
                <bpmn:errorEventDefinition errorRef="Err_1" />
              </bpmn:startEvent>
              <bpmn:sequenceFlow id="hf" sourceRef="hs" targetRef="ht" />
              <bpmn:userTask id="ht" name="Handled error" />
            </bpmn:subProcess>
          </bpmn:process>
          {{Di(key, "s", "fork", "ongoing", "inner", "handler")}}
        </bpmn:definitions>
        """;

    /// <summary>
    /// Shape B — the throw, the sibling and the handler all inside one subprocess.
    /// </summary>
    /// <remarks>
    /// Same three elements as shape A, one nesting box moved: <c>handler</c> is
    /// inside <c>scope</c> rather than at the process level, so the error is
    /// thrown and caught within the same scope. That is the only structural
    /// difference, and on a canvas it is invisible unless you look at which box
    /// the handler is drawn in.
    /// </remarks>
    private static string SameScopeThrowDiagram(string key) => $$"""
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          id="Definitions_1" targetNamespace="http://autonate.dev/workflows">
          <bpmn:error id="Err_1" errorCode="E1" name="E1" />
          <bpmn:process id="{{key}}" name="Same Scope Throw" isExecutable="true">
            <bpmn:startEvent id="s" />
            <bpmn:sequenceFlow id="f0" sourceRef="s" targetRef="scope" />
            <bpmn:subProcess id="scope" name="Scope">
              <bpmn:startEvent id="ss" />
              <bpmn:sequenceFlow id="sf0" sourceRef="ss" targetRef="fork" />
              <bpmn:parallelGateway id="fork" />
              <bpmn:sequenceFlow id="sf1" sourceRef="fork" targetRef="ongoing" />
              <bpmn:userTask id="ongoing" name="Ongoing work" />
              <bpmn:sequenceFlow id="sf2" sourceRef="fork" targetRef="boom" />
              <bpmn:endEvent id="boom" name="Boom">
                <bpmn:errorEventDefinition errorRef="Err_1" />
              </bpmn:endEvent>
              <bpmn:subProcess id="handler" name="Handler" triggeredByEvent="true">
                <bpmn:startEvent id="hs" isInterrupting="true">
                  <bpmn:errorEventDefinition errorRef="Err_1" />
                </bpmn:startEvent>
                <bpmn:sequenceFlow id="hf" sourceRef="hs" targetRef="ht" />
                <bpmn:userTask id="ht" name="Handled error" />
              </bpmn:subProcess>
            </bpmn:subProcess>
          </bpmn:process>
          {{Di(key, "s", "scope")}}
        </bpmn:definitions>
        """;

    // ── harness, mirroring EventSubProcessExecutionTests ────────────────────

    private static string Di(string processKey, params string[] elementIds)
    {
        var shapes = string.Join("\n", elementIds.Select((id, index) =>
            $"""
                  <bpmndi:BPMNShape id="Shape_{id}" bpmnElement="{id}">
                    <dc:Bounds x="{100 + (index * 160)}" y="100" width="120" height="90" />
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
