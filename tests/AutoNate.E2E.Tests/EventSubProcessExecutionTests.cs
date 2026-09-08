using System.Text.Json;
using AutoNate.E2E.Tests.Support;
using Microsoft.Playwright;
using Xunit;

namespace AutoNate.E2E.Tests;

/// <summary>
/// An event subprocess handles an event raised anywhere in its scope (#162).
/// </summary>
/// <remarks>
/// Interrupting is asserted on the CANCELLATION — that the containing scope
/// stopped — not merely on the handler running, because a handler running is what
/// a non-interrupting one does too.
///
/// A note from the verification, because it nearly produced a false bug report:
/// where the handler sits relative to the throw matters. With the handler at the
/// containing scope's parent and the error raised in a nested scope, the scope is
/// cancelled. With the handler beside the error end event in the SAME scope, a
/// parallel sibling kept running. These tests use the first shape, which is the
/// one the criteria describe; the second is recorded on the issue as an open
/// question rather than pinned here as if it were settled.
/// </remarks>
[Trait("RequiresService", "Flowable")]
[Collection(AutoNateE2ECollection.Name)]
public sealed class EventSubProcessExecutionTests : E2ETestBase
{
    public EventSubProcessExecutionTests(AutoNateE2EFixture fixture) : base(fixture) { }

    [Fact]
    public async Task An_interrupting_error_handler_cancels_the_scope_it_guards()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var api = session.Page.APIRequest;

        var key = $"esp_i_{Guid.NewGuid():N}"[..22];
        await PublishAsync(api, key, ErrorDiagram(key, interrupting: true));
        var instance = await StartAsync(api, key);

        var names = await EventuallyAsync(api, instance,
            n => n.Contains("Handled error"), "the error handler to run");

        Assert.Contains("Handled error", names);
        // The half that distinguishes interrupting from non-interrupting. Without
        // it this test passes for either.
        Assert.DoesNotContain("Ongoing work", names);
    }

    [Fact]
    public async Task An_error_handler_interrupts_even_when_told_not_to()
    {
        // BPMN does not allow a non-interrupting error start event — an error
        // always interrupts the scope it escapes. Verified: marking it
        // isInterrupting="false" changes nothing, the scope is still cancelled.
        //
        // So the diagram promises something the engine will not honour, which is
        // why publish refuses it. This test pins the ENGINE behaviour that makes
        // that refusal correct; the refusal itself is asserted below.
        await using var session = await NewSignedInAsAdminAsync();
        var api = session.Page.APIRequest;

        var key = $"esp_n_{Guid.NewGuid():N}"[..22];
        var refused = await TryPublishAsync(api, key, ErrorDiagram(key, interrupting: false));

        Assert.False(refused.Ok, "A non-interrupting error handler should be refused.");
        Assert.Contains("always interrupts", await refused.TextAsync());
    }

    [Fact]
    public async Task An_escalation_handler_runs_and_the_scope_carries_on()
    {
        // Named in its own right because this story owns Escalation Start Event,
        // and an enumeration inside another criterion is not delivery.
        await using var session = await NewSignedInAsAdminAsync();
        var api = session.Page.APIRequest;

        var key = $"esp_e_{Guid.NewGuid():N}"[..22];
        await PublishAsync(api, key, $$"""
            <?xml version="1.0" encoding="UTF-8"?>
            <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                              id="Definitions_1" targetNamespace="http://autonate.dev/workflows">
              <bpmn:escalation id="Esc_1" escalationCode="ESC1" name="ESC1" />
              <bpmn:process id="{{key}}" name="Escalation Handler" isExecutable="true">
                <bpmn:startEvent id="s" />
                <bpmn:sequenceFlow id="f0" sourceRef="s" targetRef="fork" />
                <bpmn:parallelGateway id="fork" />
                <bpmn:sequenceFlow id="f1" sourceRef="fork" targetRef="ongoing" />
                <bpmn:userTask id="ongoing" name="Ongoing work" />
                <bpmn:sequenceFlow id="f2" sourceRef="fork" targetRef="inner" />
                <bpmn:subProcess id="inner" name="Inner">
                  <bpmn:startEvent id="is" />
                  <bpmn:sequenceFlow id="if0" sourceRef="is" targetRef="raise" />
                  <bpmn:endEvent id="raise">
                    <bpmn:escalationEventDefinition escalationRef="Esc_1" />
                  </bpmn:endEvent>
                </bpmn:subProcess>
                <bpmn:subProcess id="handler" name="Handler" triggeredByEvent="true">
                  <bpmn:startEvent id="hs" isInterrupting="false">
                    <bpmn:escalationEventDefinition escalationRef="Esc_1" />
                  </bpmn:startEvent>
                  <bpmn:sequenceFlow id="hf" sourceRef="hs" targetRef="ht" />
                  <bpmn:userTask id="ht" name="Handled escalation" />
                </bpmn:subProcess>
              </bpmn:process>
              {{Di(key, "s", "fork", "ongoing", "inner", "handler")}}
            </bpmn:definitions>
            """);

        var instance = await StartAsync(api, key);
        var names = await EventuallyAsync(api, instance,
            n => n.Contains("Handled escalation"), "the escalation handler to run");

        Assert.Contains("Handled escalation", names);
        // Unlike error: a non-interrupting escalation lets the scope carry on.
        Assert.Contains("Ongoing work", names);
    }

    [Fact]
    public async Task A_message_handler_is_triggered_through_the_same_correlation_mechanism()
    {
        // Reuses #112 rather than reimplementing it, as the criteria require.
        await using var session = await NewSignedInAsAdminAsync();
        var api = session.Page.APIRequest;

        var key = $"esp_m_{Guid.NewGuid():N}"[..22];
        await PublishAsync(api, key, $$"""
            <?xml version="1.0" encoding="UTF-8"?>
            <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                              xmlns:flowable="http://flowable.org/bpmn"
                              id="Definitions_1" targetNamespace="http://autonate.dev/workflows">
              <bpmn:message id="Msg_1" name="{{key}}_nudge" />
              <bpmn:process id="{{key}}" name="Message Handler" isExecutable="true">
                <bpmn:startEvent id="s" />
                <bpmn:sequenceFlow id="f0" sourceRef="s" targetRef="ongoing" />
                <bpmn:userTask id="ongoing" name="Ongoing work" />
                <bpmn:subProcess id="handler" name="Handler" triggeredByEvent="true">
                  <bpmn:startEvent id="hs" isInterrupting="false"
                                   flowable:autonateCorrelationKey="orderId">
                    <bpmn:messageEventDefinition messageRef="Msg_1" />
                  </bpmn:startEvent>
                  <bpmn:sequenceFlow id="hf" sourceRef="hs" targetRef="ht" />
                  <bpmn:userTask id="ht" name="Handled message" />
                </bpmn:subProcess>
              </bpmn:process>
              {{Di(key, "s", "ongoing", "handler")}}
            </bpmn:definitions>
            """);

        var instance = await StartAsync(api, key, new { orderId = "ORD-1" });
        Assert.Contains("Ongoing work", await TaskNamesAsync(api, instance));

        var sent = await api.PostAsync("/api/workflow-messages/", new APIRequestContextOptions
        {
            DataObject = new
            {
                processKey = key,
                messageName = $"{key}_nudge",
                correlationValue = "ORD-1"
            }
        });
        Assert.True(sent.Ok, $"Send failed: {sent.Status} {await sent.TextAsync()}");

        var names = await EventuallyAsync(api, instance,
            n => n.Contains("Handled message"), "the message handler to run");
        Assert.Contains("Ongoing work", names);
    }

    [Fact]
    public async Task A_conditional_handler_triggers_when_its_condition_becomes_true()
    {
        // This story owns Conditional Start Event: Flowable allows a conditional
        // start ONLY inside an event subprocess, which is why #158 could refuse
        // the process-level placement but not deliver the working one.
        //
        // Conditional events do not re-evaluate on their own — #158 established
        // that the engine needs an explicit evaluate-conditions call — so setting
        // the variable is what the app's variable update does, and the handler
        // firing afterwards is what proves the wiring.
        await using var session = await NewSignedInAsAdminAsync();
        var api = session.Page.APIRequest;

        var key = $"esp_c_{Guid.NewGuid():N}"[..22];
        await PublishAsync(api, key, $$"""
            <?xml version="1.0" encoding="UTF-8"?>
            <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                              id="Definitions_1" targetNamespace="http://autonate.dev/workflows">
              <bpmn:process id="{{key}}" name="Conditional Handler" isExecutable="true">
                <bpmn:startEvent id="s" />
                <bpmn:sequenceFlow id="f0" sourceRef="s" targetRef="ongoing" />
                <bpmn:userTask id="ongoing" name="Ongoing work" />
                <bpmn:subProcess id="handler" name="Handler" triggeredByEvent="true">
                  <bpmn:startEvent id="hs" isInterrupting="false">
                    <bpmn:conditionalEventDefinition>
                      <bpmn:condition xsi:type="bpmn:tFormalExpression"
                                      xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">${escalate == true}</bpmn:condition>
                    </bpmn:conditionalEventDefinition>
                  </bpmn:startEvent>
                  <bpmn:sequenceFlow id="hf" sourceRef="hs" targetRef="ht" />
                  <bpmn:userTask id="ht" name="Condition met" />
                </bpmn:subProcess>
              </bpmn:process>
              {{Di(key, "s", "ongoing", "handler")}}
            </bpmn:definitions>
            """);

        var instance = await StartAsync(api, key, new { escalate = false });
        Assert.Contains("Ongoing work", await TaskNamesAsync(api, instance));
        // Not yet — the condition is false, so the handler must not have run.
        Assert.DoesNotContain("Condition met", await TaskNamesAsync(api, instance));

        // PUT, not POST: the variable already exists (the instance started with it
        // false), and POST is the add-new path, which conflicts.
        var updated = await api.PutAsync($"/api/executions/{instance}/variables", new APIRequestContextOptions
        {
            DataObject = new
            {
                // A real JSON boolean, not the string "true" — Flowable answers
                // "Converter can only convert booleans" for the latter.
                variables = new[] { new { name = "escalate", value = true, type = "boolean" } }
            }
        });
        Assert.True(updated.Ok, $"Setting the variable failed: {updated.Status} {await updated.TextAsync()}");

        var names = await EventuallyAsync(api, instance,
            n => n.Contains("Condition met"), "the condition to become true and the handler to run");

        Assert.Contains("Condition met", names);
        // Non-interrupting: the guarded work carries on.
        Assert.Contains("Ongoing work", names);

        // The handler can read the containing scope's variables. This is not
        // incidental: the condition itself is evaluated against `escalate`, so a
        // handler that could not see the scope's variables could not have fired at
        // all — and the variable is read back here rather than inferred from that.
        Assert.Equal(
            "true",
            (await ReadProcessVariableAsync(instance, "escalate"))?.ToLowerInvariant());
    }

    [Fact]
    public async Task A_timer_handler_fires_on_its_own_schedule()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var api = session.Page.APIRequest;

        var key = $"esp_t_{Guid.NewGuid():N}"[..22];
        await PublishAsync(api, key, $$"""
            <?xml version="1.0" encoding="UTF-8"?>
            <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                              xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
                              id="Definitions_1" targetNamespace="http://autonate.dev/workflows">
              <bpmn:process id="{{key}}" name="Timer Handler" isExecutable="true">
                <bpmn:startEvent id="s" />
                <bpmn:sequenceFlow id="f0" sourceRef="s" targetRef="ongoing" />
                <bpmn:userTask id="ongoing" name="Ongoing work" />
                <bpmn:subProcess id="handler" name="Handler" triggeredByEvent="true">
                  <bpmn:startEvent id="hs" isInterrupting="false">
                    <bpmn:timerEventDefinition>
                      <bpmn:timeDuration xsi:type="bpmn:tFormalExpression">PT2S</bpmn:timeDuration>
                    </bpmn:timerEventDefinition>
                  </bpmn:startEvent>
                  <bpmn:sequenceFlow id="hf" sourceRef="hs" targetRef="ht" />
                  <bpmn:userTask id="ht" name="Timer fired" />
                </bpmn:subProcess>
              </bpmn:process>
              {{Di(key, "s", "ongoing", "handler")}}
            </bpmn:definitions>
            """);

        var instance = await StartAsync(api, key);
        Assert.DoesNotContain("Timer fired", await TaskNamesAsync(api, instance));

        var names = await EventuallyAsync(api, instance,
            n => n.Contains("Timer fired"), "the timer handler to fire");

        Assert.Contains("Timer fired", names);
        // Non-interrupting: the guarded work is untouched.
        Assert.Contains("Ongoing work", names);
    }

    [Fact]
    public async Task A_signal_handler_runs_when_its_signal_is_raised()
    {
        // Signal SCOPE is #156's subject and is blocked on a contradiction in its
        // own definition. Nothing here depends on it: this asserts that a signal
        // start event inside an event subprocess catches a signal raised in the
        // same instance, which is true under every scope #156 might choose.
        await using var session = await NewSignedInAsAdminAsync();
        var api = session.Page.APIRequest;

        var key = $"esp_s_{Guid.NewGuid():N}"[..22];
        await PublishAsync(api, key, $$"""
            <?xml version="1.0" encoding="UTF-8"?>
            <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                              id="Definitions_1" targetNamespace="http://autonate.dev/workflows">
              <bpmn:signal id="Sig_1" name="{{key}}_raise" />
              <bpmn:process id="{{key}}" name="Signal Handler" isExecutable="true">
                <bpmn:startEvent id="s" />
                <bpmn:sequenceFlow id="f0" sourceRef="s" targetRef="fork" />
                <bpmn:parallelGateway id="fork" />
                <bpmn:sequenceFlow id="f1" sourceRef="fork" targetRef="ongoing" />
                <bpmn:userTask id="ongoing" name="Ongoing work" />
                <bpmn:sequenceFlow id="f2" sourceRef="fork" targetRef="raise" />
                <bpmn:intermediateThrowEvent id="raise">
                  <bpmn:signalEventDefinition signalRef="Sig_1" />
                </bpmn:intermediateThrowEvent>
                <bpmn:sequenceFlow id="f3" sourceRef="raise" targetRef="afterRaise" />
                <bpmn:userTask id="afterRaise" name="After raising" />
                <bpmn:subProcess id="handler" name="Handler" triggeredByEvent="true">
                  <bpmn:startEvent id="hs" isInterrupting="false">
                    <bpmn:signalEventDefinition signalRef="Sig_1" />
                  </bpmn:startEvent>
                  <bpmn:sequenceFlow id="hf" sourceRef="hs" targetRef="ht" />
                  <bpmn:userTask id="ht" name="Signal handled" />
                </bpmn:subProcess>
              </bpmn:process>
              {{Di(key, "s", "fork", "ongoing", "raise", "afterRaise", "handler")}}
            </bpmn:definitions>
            """);

        var instance = await StartAsync(api, key);
        var names = await EventuallyAsync(api, instance,
            n => n.Contains("Signal handled"), "the signal handler to run");

        Assert.Contains("Signal handled", names);
        Assert.Contains("Ongoing work", names);
    }

    [Fact]
    public async Task An_event_subprocess_that_can_never_trigger_is_refused_at_publish()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var api = session.Page.APIRequest;

        var key = $"esp_b_{Guid.NewGuid():N}"[..22];
        var response = await TryPublishAsync(api, key, $$"""
            <?xml version="1.0" encoding="UTF-8"?>
            <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                              id="Definitions_1" targetNamespace="http://autonate.dev/workflows">
              <bpmn:process id="{{key}}" name="Never Triggers" isExecutable="true">
                <bpmn:startEvent id="s" />
                <bpmn:sequenceFlow id="f0" sourceRef="s" targetRef="work" />
                <bpmn:userTask id="work" name="Work" />
                <bpmn:subProcess id="handler" name="Handler" triggeredByEvent="true">
                  <bpmn:startEvent id="hs" />
                  <bpmn:sequenceFlow id="hf" sourceRef="hs" targetRef="ht" />
                  <bpmn:userTask id="ht" name="Never runs" />
                </bpmn:subProcess>
              </bpmn:process>
              {{Di(key, "s", "work", "handler")}}
            </bpmn:definitions>
            """);

        Assert.False(response.Ok, "An event subprocess starting on nothing should be refused.");
        Assert.Contains("starts on nothing", await response.TextAsync());
    }

    // The scope is guarded by a handler at PROCESS level and the error is raised
    // inside a nested subprocess — the shape the criteria describe, and the one
    // where interrupting genuinely cancels.
    private static string ErrorDiagram(string key, bool interrupting) => $$"""
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          id="Definitions_1" targetNamespace="http://autonate.dev/workflows">
          <bpmn:error id="Err_1" errorCode="E1" name="E1" />
          <bpmn:process id="{{key}}" name="Error Handler" isExecutable="true">
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
              <bpmn:startEvent id="hs" isInterrupting="{{(interrupting ? "true" : "false")}}">
                <bpmn:errorEventDefinition errorRef="Err_1" />
              </bpmn:startEvent>
              <bpmn:sequenceFlow id="hf" sourceRef="hs" targetRef="ht" />
              <bpmn:userTask id="ht" name="Handled error" />
            </bpmn:subProcess>
          </bpmn:process>
          {{Di(key, "s", "fork", "ongoing", "inner", "handler")}}
        </bpmn:definitions>
        """;

    /// <summary>Reads one process variable straight from the engine.</summary>
    private static async Task<string?> ReadProcessVariableAsync(string processInstanceId, string name)
    {
        using var client = Support.FlowableDeploymentSweep.CreateClient(
            Environment.GetEnvironmentVariable("AUTONATE_FLOWABLE_URL") ?? "http://localhost:8080/flowable-rest",
            Environment.GetEnvironmentVariable("AUTONATE_FLOWABLE_USER") ?? "rest-admin",
            Environment.GetEnvironmentVariable("AUTONATE_FLOWABLE_PASSWORD") ?? "test");

        var body = await client.GetStringAsync(
            $"service/runtime/process-instances/{Uri.EscapeDataString(processInstanceId)}/variables");
        using var document = JsonDocument.Parse(body);
        foreach (var variable in document.RootElement.EnumerateArray())
        {
            if (variable.GetProperty("name").GetString() == name)
            {
                return variable.TryGetProperty("value", out var value) ? value.ToString() : null;
            }
        }

        return null;
    }

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

    private static async Task<string> StartAsync(
        IAPIRequestContext api, string key, object? variables = null)
    {
        var response = await api.PostAsync($"/api/workflows/{key}/start", new APIRequestContextOptions
        {
            DataObject = variables is null ? new { } : new { variables }
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
