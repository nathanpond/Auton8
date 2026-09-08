using System.Text.Json;
using AutoNate.E2E.Tests.Support;
using Microsoft.Playwright;
using Xunit;

namespace AutoNate.E2E.Tests;

/// <summary>
/// A signal reaches what its scope says it reaches (#156).
/// </summary>
/// <remarks>
/// The NEGATIVE case is the feature. A test that only checks the thrower's own
/// handler fired would pass against a plain broadcast, which is what BPMN does by
/// default and what this story exists to fence in. So each test asserts an
/// instance that must NOT have reacted.
///
/// Scope decided as INSTANCE (owner's call, option 1): `instance` and `global`,
/// with no "same definition" scope. Enforced by Flowable's own
/// flowable:scope="processInstance" rather than by filtering a broadcast here.
/// </remarks>
[Trait("RequiresService", "Flowable")]
[Collection(AutoNateE2ECollection.Name)]
public sealed class SignalScopeExecutionTests : E2ETestBase
{
    public SignalScopeExecutionTests(AutoNateE2EFixture fixture) : base(fixture) { }

    [Fact]
    public async Task An_instance_scoped_signal_reaches_only_the_run_that_raised_it()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var api = session.Page.APIRequest;

        var key = $"sig_i_{Guid.NewGuid():N}"[..22];
        await PublishAsync(api, key, Diagram(key, scoped: true));

        var a = await StartAsync(api, key);
        var b = await StartAsync(api, key);

        await CompleteTaskAsync(api, a, "Trigger");

        // A's own handler fired. Waiting for THIS first is what makes the
        // negative assertion below meaningful: the signal has demonstrably been
        // raised and delivered by the time B is examined.
        var aNames = await EventuallyAsync(api, a,
            n => n.Contains("Handled") && n.Contains("Boundary fired"),
            "the raising run to handle its own signal on both paths");
        Assert.Contains("Handled", aNames);

        // The half that IS the feature: B is untouched. Without scope, BPMN
        // broadcasts and B's catch AND boundary fire too — verified before
        // implementing.
        //
        // Re-checked after a settle so this cannot pass by looking too early.
        // The first version asserted immediately and passed in isolation while
        // failing under load, because B simply had not reacted yet — a green
        // result that measured scheduling rather than scope.
        await Task.Delay(2000);
        var bNames = await TaskNamesAsync(api, b);
        Assert.Contains("Trigger", bNames);
        Assert.DoesNotContain("Handled", bNames);
        Assert.DoesNotContain("Boundary fired", bNames);
    }

    [Fact]
    public async Task A_global_signal_reaches_every_listening_run()
    {
        // The complement, and the thing that proves the test above is measuring
        // the scope rather than the absence of a second subscriber.
        await using var session = await NewSignedInAsAdminAsync();
        var api = session.Page.APIRequest;

        var key = $"sig_g_{Guid.NewGuid():N}"[..22];
        await PublishAsync(api, key, Diagram(key, scoped: false));

        var a = await StartAsync(api, key);
        var b = await StartAsync(api, key);

        await CompleteTaskAsync(api, a, "Trigger");

        await EventuallyAsync(api, a, n => n.Contains("Handled"), "the raising run to handle it");
        var bNames = await EventuallyAsync(api, b,
            n => n.Contains("Handled"), "the OTHER run to hear the global signal");

        Assert.Contains("Handled", bNames);
    }

    [Fact]
    public async Task A_non_interrupting_signal_boundary_leaves_its_activity_running()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var api = session.Page.APIRequest;

        var key = $"sig_b_{Guid.NewGuid():N}"[..22];
        await PublishAsync(api, key, Diagram(key, scoped: true));

        var instance = await StartAsync(api, key);
        await CompleteTaskAsync(api, instance, "Trigger");

        var names = await EventuallyAsync(api, instance,
            n => n.Contains("Boundary fired"), "the signal boundary to fire");

        // Both: the boundary path ran AND the guarded work is still there. The
        // first alone would pass for an interrupting boundary, the opposite
        // feature.
        Assert.Contains("Boundary fired", names);
        Assert.Contains("Long work", names);
    }

    // start -> parallel: [Trigger -> throw -> After throw]
    //                    [catch -> Handled]
    //                    [Long work + non-interrupting signal boundary -> Boundary fired]
    private static string Diagram(string key, bool scoped)
    {
        // Authored the way the studio writes it — an extension element on each
        // event — so this exercises the real path rather than hand-writing the
        // engine attribute the publish step is supposed to produce.
        var scopeExt = scoped
            ? """
                <bpmn:extensionElements>
                  <flowable:autonateSignalScope value="instance" />
                </bpmn:extensionElements>
              """
            : """
                <bpmn:extensionElements>
                  <flowable:autonateSignalScope value="global" />
                </bpmn:extensionElements>
              """;
        return $$"""
            <?xml version="1.0" encoding="UTF-8"?>
            <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                              xmlns:flowable="http://flowable.org/bpmn"
                              id="Definitions_1" targetNamespace="http://autonate.dev/workflows">
              <bpmn:signal id="Sig_1" name="{{key}}_raise" />
              <bpmn:process id="{{key}}" name="Signal Scope" isExecutable="true">
                <bpmn:startEvent id="s" />
                <bpmn:sequenceFlow id="f0" sourceRef="s" targetRef="fork" />
                <bpmn:parallelGateway id="fork" />

                <bpmn:sequenceFlow id="fa" sourceRef="fork" targetRef="gate" />
                <bpmn:userTask id="gate" name="Trigger" />
                <bpmn:sequenceFlow id="fa1" sourceRef="gate" targetRef="throw" />
                <bpmn:intermediateThrowEvent id="throw" name="Raise">
            {{scopeExt}}
                  <bpmn:signalEventDefinition signalRef="Sig_1" />
                </bpmn:intermediateThrowEvent>
                <bpmn:sequenceFlow id="fa2" sourceRef="throw" targetRef="afterThrow" />
                <bpmn:userTask id="afterThrow" name="After throw" />

                <bpmn:sequenceFlow id="fb" sourceRef="fork" targetRef="catch" />
                <bpmn:intermediateCatchEvent id="catch" name="Await">
            {{scopeExt}}
                  <bpmn:signalEventDefinition signalRef="Sig_1" />
                </bpmn:intermediateCatchEvent>
                <bpmn:sequenceFlow id="fb1" sourceRef="catch" targetRef="handled" />
                <bpmn:userTask id="handled" name="Handled" />

                <bpmn:sequenceFlow id="fc" sourceRef="fork" targetRef="work" />
                <bpmn:userTask id="work" name="Long work" />
                <bpmn:boundaryEvent id="bnd" attachedToRef="work" cancelActivity="false">
            {{scopeExt}}
                  <bpmn:signalEventDefinition signalRef="Sig_1" />
                </bpmn:boundaryEvent>
                <bpmn:sequenceFlow id="fc1" sourceRef="bnd" targetRef="bfired" />
                <bpmn:userTask id="bfired" name="Boundary fired" />
              </bpmn:process>
              {{Di(key, "s", "fork", "gate", "throw", "afterThrow", "catch", "handled", "work", "bnd", "bfired")}}
            </bpmn:definitions>
            """;
    }

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

    private static async Task CompleteTaskAsync(IAPIRequestContext api, string instanceId, string taskName)
    {
        var response = await api.GetAsync($"/api/executions/{instanceId}/tasks");
        using var document = JsonDocument.Parse(await response.TextAsync());
        var taskId = document.RootElement.EnumerateArray()
            .First(e => e.GetProperty("name").GetString() == taskName)
            .GetProperty("id").GetString()!;
        var completed = await api.PostAsync($"/api/tasks/{taskId}/complete", new APIRequestContextOptions
        {
            DataObject = new { }
        });
        Assert.True(completed.Ok, $"Completing '{taskName}' failed: {completed.Status} {await completed.TextAsync()}");
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
