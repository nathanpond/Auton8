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

    [Fact]
    public async Task An_interrupting_signal_boundary_cancels_its_activity()
    {
        // The complement of the non-interrupting test. Asserting only that the
        // boundary path ran would pass for either, which is the opposite feature.
        await using var session = await NewSignedInAsAdminAsync();
        var api = session.Page.APIRequest;

        var key = $"sig_ib_{Guid.NewGuid():N}"[..22];
        await PublishAsync(api, key, BoundaryDiagram(key, interrupting: true));

        var instance = await StartAsync(api, key);
        Assert.Contains("Long work", await TaskNamesAsync(api, instance));

        await CompleteTaskAsync(api, instance, "Trigger");

        var names = await EventuallyAsync(api, instance,
            n => n.Contains("Boundary fired"), "the interrupting boundary to fire");

        Assert.Contains("Boundary fired", names);
        Assert.DoesNotContain("Long work", names);
    }

    [Fact]
    public async Task A_signal_end_event_raises_its_signal()
    {
        // Signal END, in its own right. Unlike the message end event — which
        // deploys, ends the process and sends nothing (#112) — Flowable executes
        // this one as authored, so it needs no expansion.
        //
        // Two instances with a GLOBAL signal, deliberately: a first attempt put
        // the catcher on a parallel branch of the SAME instance, and the end event
        // finished the instance before that branch could react — the tasks came
        // back empty and the test said nothing about whether the signal was
        // raised. Across instances there is no such ambiguity: B hearing it proves
        // the signal left A.
        await using var session = await NewSignedInAsAdminAsync();
        var api = session.Page.APIRequest;

        var key = $"sig_e_{Guid.NewGuid():N}"[..22];
        await PublishAsync(api, key, $$"""
            <?xml version="1.0" encoding="UTF-8"?>
            <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                              xmlns:flowable="http://flowable.org/bpmn"
                              id="Definitions_1" targetNamespace="http://autonate.dev/workflows">
              <bpmn:signal id="Sig_1" name="{{key}}_done" />
              <bpmn:process id="{{key}}" name="Signal End" isExecutable="true">
                <bpmn:startEvent id="s" />
                <bpmn:sequenceFlow id="f0" sourceRef="s" targetRef="fork" />
                <bpmn:parallelGateway id="fork" />

                <bpmn:sequenceFlow id="fa" sourceRef="fork" targetRef="gate" />
                <bpmn:userTask id="gate" name="Trigger" />
                <bpmn:sequenceFlow id="fa1" sourceRef="gate" targetRef="finish" />
                <bpmn:endEvent id="finish">
                  <bpmn:extensionElements>
                    <flowable:autonateSignalScope value="global" />
                  </bpmn:extensionElements>
                  <bpmn:signalEventDefinition signalRef="Sig_1" />
                </bpmn:endEvent>

                <bpmn:sequenceFlow id="fb" sourceRef="fork" targetRef="catch" />
                <bpmn:intermediateCatchEvent id="catch" name="Await">
                  <bpmn:extensionElements>
                    <flowable:autonateSignalScope value="global" />
                  </bpmn:extensionElements>
                  <bpmn:signalEventDefinition signalRef="Sig_1" />
                </bpmn:intermediateCatchEvent>
                <bpmn:sequenceFlow id="fb1" sourceRef="catch" targetRef="heard" />
                <bpmn:userTask id="heard" name="Heard the end signal" />
              </bpmn:process>
              {{Di(key, "s", "fork", "gate", "finish", "catch", "heard")}}
            </bpmn:definitions>
            """);

        var a = await StartAsync(api, key);
        var b = await StartAsync(api, key);

        await CompleteTaskAsync(api, a, "Trigger");

        var bNames = await EventuallyAsync(api, b,
            n => n.Contains("Heard the end signal"),
            "the other instance to hear the signal the end event raised");
        Assert.Contains("Heard the end signal", bNames);
    }

    [Fact]
    public async Task What_publish_emits_for_a_scoped_signal_actually_deploys()
    {
        // #270. The test this replaces the absence of.
        //
        // #244 made ApplySignalScopes clone the <bpmn:signal> root so a scoped
        // catch would not narrow a start event sharing the name. The clone kept
        // the NAME, which Flowable refuses:
        //   [Problem: 'flowable-signal-duplicate-name'] -> HTTP 500
        // The diagram that fix existed to support became unpublishable, and TWO
        // unit tests stayed green through it because both asserted the XML tree
        // and neither deployed what they built.
        //
        // So this one deploys. It is the only assertion that could have caught it,
        // and the general lesson is why it is here rather than another tree
        // comparison: an expansion is only correct if the engine accepts it.
        await using var session = await NewSignedInAsAdminAsync();
        var api = session.Page.APIRequest;

        var key = $"ss{Guid.NewGuid():N}"[..20];
        await PublishAsync(api, key, ScopedCatchDiagram(key));

        // Published means Flowable accepted the expanded copy. Prove the scope
        // survived rather than being quietly dropped to make it deploy.
        using var client = Support.FlowableDeploymentSweep.CreateClient(
            Environment.GetEnvironmentVariable("AUTONATE_FLOWABLE_URL") ?? "http://localhost:8080/flowable-rest",
            Environment.GetEnvironmentVariable("AUTONATE_FLOWABLE_USER") ?? "rest-admin",
            Environment.GetEnvironmentVariable("AUTONATE_FLOWABLE_PASSWORD") ?? "test");

        var definitions = await client.GetStringAsync(
            $"service/repository/process-definitions?key={Uri.EscapeDataString(key)}&latest=true");
        using var document = System.Text.Json.JsonDocument.Parse(definitions);
        var definitionId = document.RootElement.GetProperty("data")[0].GetProperty("id").GetString()!;

        var deployed = await client.GetStringAsync(
            $"service/repository/process-definitions/{Uri.EscapeDataString(definitionId)}/resourcedata");

        Assert.Contains("flowable:scope=\"processInstance\"", deployed, StringComparison.Ordinal);

        // Exactly one signal root — two sharing a name is what the engine refuses.
        Assert.Equal(1, deployed.Split("<bpmn:signal ").Length - 1);
    }

    private static string ScopedCatchDiagram(string key) => $$"""
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          xmlns:flowable="http://flowable.org/bpmn"
                          id="Definitions_1" targetNamespace="http://autonate.dev/workflows">
          <bpmn:signal id="Sig_1" name="{{key}}_scoped" />
          <bpmn:process id="{{key}}" name="Scoped catch" isExecutable="true">
            <bpmn:startEvent id="s" />
            <bpmn:sequenceFlow id="f0" sourceRef="s" targetRef="c" />
            <bpmn:intermediateCatchEvent id="c" name="Wait for it">
              <bpmn:extensionElements>
                <flowable:autonateSignalScope value="instance" />
              </bpmn:extensionElements>
              <bpmn:signalEventDefinition signalRef="Sig_1" />
            </bpmn:intermediateCatchEvent>
            <bpmn:sequenceFlow id="f1" sourceRef="c" targetRef="t" />
            <bpmn:userTask id="t" name="After" />
          </bpmn:process>
          {{Di(key, "s", "c", "t")}}
        </bpmn:definitions>
        """;

    [Fact]
    public async Task A_signal_start_sharing_a_name_with_a_scoped_catch_is_refused_at_publish()
    {
        // #270's complement, and the case #244 was written for. It cannot deploy
        // at any engine — one signal name, one scope — so the author is told,
        // rather than being handed a 500 from Flowable.
        await using var session = await NewSignedInAsAdminAsync();
        var api = session.Page.APIRequest;

        var key = $"sd{Guid.NewGuid():N}"[..20];
        var xml = $$"""
            <?xml version="1.0" encoding="UTF-8"?>
            <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                              xmlns:flowable="http://flowable.org/bpmn"
                              id="Definitions_1" targetNamespace="http://autonate.dev/workflows">
              <bpmn:signal id="Sig_1" name="{{key}}_both" />
              <bpmn:process id="{{key}}" name="Both" isExecutable="true">
                <bpmn:startEvent id="s" name="On record created">
                  <bpmn:signalEventDefinition signalRef="Sig_1" />
                </bpmn:startEvent>
                <bpmn:sequenceFlow id="f0" sourceRef="s" targetRef="c" />
                <bpmn:intermediateCatchEvent id="c" name="Wait for record">
                  <bpmn:extensionElements>
                    <flowable:autonateSignalScope value="instance" />
                  </bpmn:extensionElements>
                  <bpmn:signalEventDefinition signalRef="Sig_1" />
                </bpmn:intermediateCatchEvent>
                <bpmn:sequenceFlow id="f1" sourceRef="c" targetRef="t" />
                <bpmn:userTask id="t" name="After" />
              </bpmn:process>
              {{Di(key, "s", "c", "t")}}
            </bpmn:definitions>
            """;

        var id = Guid.NewGuid();
        var name = TestNames.Prefixed(key);
        var created = await api.PostAsync("/api/workflows/", new APIRequestContextOptions
        {
            DataObject = new { id, name, processKey = key, bpmnXml = xml }
        });
        Assert.True(created.Ok, await created.TextAsync());

        var published = await api.PostAsync($"/api/workflows/{id}/publish", new APIRequestContextOptions
        {
            DataObject = new { id, name, processKey = key, bpmnXml = xml }
        });
        var body = await published.TextAsync();

        // Refused by US, with a message, rather than by the engine with a 500.
        Assert.False(published.Ok, $"Expected a refusal, got {published.Status}");
        Assert.Contains("one scope per signal name", body, StringComparison.Ordinal);
        Assert.Contains("Wait for record", body, StringComparison.Ordinal);
        Assert.Contains("On record created", body, StringComparison.Ordinal);
        Assert.DoesNotContain("duplicate signal name", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Waking_a_waiting_execution_requires_the_signal_name()
    {
        // #262. The contract the whole external-signal path rests on, pinned
        // against the real engine.
        //
        // FlowableClient.SignalExecutionAsync omitted `signalName` for the whole
        // of M4. Flowable answers 400 "Signal name is required", and
        // WorkflowSignalDispatcher's per-execution catch logged it and moved on —
        // so every signal arriving from the bus produced a log line and a process
        // that never advanced. Nothing surfaced, and no test could see it: the
        // client's own unit test asserted the payload against a stub that answers
        // 200 to anything.
        //
        // This asserts BOTH halves against the engine, so the day Flowable stops
        // requiring the name, or starts requiring something else, this fails
        // rather than the feature going quiet again.
        await using var session = await NewSignedInAsAdminAsync();
        var api = session.Page.APIRequest;

        var key = $"sw{Guid.NewGuid():N}"[..20];
        await PublishAsync(api, key, CatchDiagram(key));
        var instance = await StartAsync(api, key);

        using var client = Support.FlowableDeploymentSweep.CreateClient(
            Environment.GetEnvironmentVariable("AUTONATE_FLOWABLE_URL") ?? "http://localhost:8080/flowable-rest",
            Environment.GetEnvironmentVariable("AUTONATE_FLOWABLE_USER") ?? "rest-admin",
            Environment.GetEnvironmentVariable("AUTONATE_FLOWABLE_PASSWORD") ?? "test");

        var executionId = await AwaitingExecutionIdAsync(client, $"{key}_wake");
        Assert.False(string.IsNullOrEmpty(executionId),
            "Precondition: nothing is waiting on the signal, so neither assertion below would mean anything.");

        // Without the name — what shipped.
        using var nameless = await client.PutAsync(
            $"service/runtime/executions/{executionId}",
            new StringContent(
                """{"action":"signalEventReceived","variables":[]}""",
                System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, nameless.StatusCode);
        Assert.Contains("Signal name is required", await nameless.Content.ReadAsStringAsync(),
            StringComparison.OrdinalIgnoreCase);

        // With it — what the fix sends.
        using var named = await client.PutAsync(
            $"service/runtime/executions/{executionId}",
            new StringContent(
                $$"""{"action":"signalEventReceived","signalName":"{{key}}_wake","variables":[]}""",
                System.Text.Encoding.UTF8, "application/json"));
        Assert.True(named.IsSuccessStatusCode,
            $"Waking with the signal name failed: {named.StatusCode} {await named.Content.ReadAsStringAsync()}");

        // And the process actually moved, rather than merely being accepted.
        var names = await EventuallyAsync(api, instance,
            n => n.Contains("After the wake"), "the woken process to advance");
        Assert.Contains("After the wake", names);
    }

    private static async Task<string?> AwaitingExecutionIdAsync(HttpClient client, string signalName)
    {
        var body = await client.GetStringAsync(
            $"service/runtime/executions?signalEventSubscriptionName={Uri.EscapeDataString(signalName)}");
        using var document = System.Text.Json.JsonDocument.Parse(body);
        var data = document.RootElement.GetProperty("data");
        return data.GetArrayLength() == 0 ? null : data[0].GetProperty("id").GetString();
    }

    private static string CatchDiagram(string key) => $$"""
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          id="Definitions_1" targetNamespace="http://autonate.dev/workflows">
          <bpmn:signal id="Sig_1" name="{{key}}_wake" />
          <bpmn:process id="{{key}}" name="Waker" isExecutable="true">
            <bpmn:startEvent id="s" />
            <bpmn:sequenceFlow id="f0" sourceRef="s" targetRef="c" />
            <bpmn:intermediateCatchEvent id="c">
              <bpmn:signalEventDefinition signalRef="Sig_1" />
            </bpmn:intermediateCatchEvent>
            <bpmn:sequenceFlow id="f1" sourceRef="c" targetRef="t" />
            <bpmn:userTask id="t" name="After the wake" />
          </bpmn:process>
          {{Di(key, "s", "c", "t")}}
        </bpmn:definitions>
        """;

    [Fact]
    public async Task A_signal_nobody_listens_for_is_not_an_error_and_is_visible_in_history()
    {
        // Broadcast semantics: reaching nobody is normal, not a failure. But an
        // author debugging "nothing happened" needs to see that the signal WAS
        // raised — otherwise a mistyped name and a correctly-raised signal look
        // identical from the outside.
        await using var session = await NewSignedInAsAdminAsync();
        var api = session.Page.APIRequest;

        var key = $"sig_n_{Guid.NewGuid():N}"[..22];
        await PublishAsync(api, key, $$"""
            <?xml version="1.0" encoding="UTF-8"?>
            <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                              xmlns:flowable="http://flowable.org/bpmn"
                              id="Definitions_1" targetNamespace="http://autonate.dev/workflows">
              <bpmn:signal id="Sig_1" name="{{key}}_nobody" />
              <bpmn:process id="{{key}}" name="Unheard" isExecutable="true">
                <bpmn:startEvent id="s" />
                <bpmn:sequenceFlow id="f0" sourceRef="s" targetRef="shout" />
                <bpmn:intermediateThrowEvent id="shout" name="Shout into the void">
                  <bpmn:extensionElements>
                    <flowable:autonateSignalScope value="instance" />
                  </bpmn:extensionElements>
                  <bpmn:signalEventDefinition signalRef="Sig_1" />
                </bpmn:intermediateThrowEvent>
                <bpmn:sequenceFlow id="f1" sourceRef="shout" targetRef="after" />
                <bpmn:userTask id="after" name="Carried on" />
              </bpmn:process>
              {{Di(key, "s", "shout", "after")}}
            </bpmn:definitions>
            """);

        var instance = await StartAsync(api, key);

        // Not an error: the process carried straight on past the throw.
        var names = await EventuallyAsync(api, instance,
            n => n.Contains("Carried on"), "the process to continue past an unheard signal");
        Assert.Contains("Carried on", names);

        // And visible: the throw is in history, so "nothing happened" can be told
        // apart from "the signal was never raised".
        var history = await api.GetAsync($"/api/executions/{instance}/history");
        Assert.True(history.Ok, $"Reading history failed: {history.Status}");
        Assert.Contains("Shout into the void", await history.TextAsync());
    }

    private static string BoundaryDiagram(string key, bool interrupting) => $$"""
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          xmlns:flowable="http://flowable.org/bpmn"
                          id="Definitions_1" targetNamespace="http://autonate.dev/workflows">
          <bpmn:signal id="Sig_1" name="{{key}}_raise" />
          <bpmn:process id="{{key}}" name="Signal Boundary" isExecutable="true">
            <bpmn:startEvent id="s" />
            <bpmn:sequenceFlow id="f0" sourceRef="s" targetRef="fork" />
            <bpmn:parallelGateway id="fork" />
            <bpmn:sequenceFlow id="fa" sourceRef="fork" targetRef="gate" />
            <bpmn:userTask id="gate" name="Trigger" />
            <bpmn:sequenceFlow id="fa1" sourceRef="gate" targetRef="throw" />
            <bpmn:intermediateThrowEvent id="throw">
              <bpmn:extensionElements>
                <flowable:autonateSignalScope value="instance" />
              </bpmn:extensionElements>
              <bpmn:signalEventDefinition signalRef="Sig_1" />
            </bpmn:intermediateThrowEvent>
            <bpmn:sequenceFlow id="fc" sourceRef="fork" targetRef="work" />
            <bpmn:userTask id="work" name="Long work" />
            <bpmn:boundaryEvent id="bnd" attachedToRef="work"
                                cancelActivity="{{(interrupting ? "true" : "false")}}">
              <bpmn:extensionElements>
                <flowable:autonateSignalScope value="instance" />
              </bpmn:extensionElements>
              <bpmn:signalEventDefinition signalRef="Sig_1" />
            </bpmn:boundaryEvent>
            <bpmn:sequenceFlow id="fc1" sourceRef="bnd" targetRef="bfired" />
            <bpmn:userTask id="bfired" name="Boundary fired" />
          </bpmn:process>
          {{Di(key, "s", "fork", "gate", "throw", "work", "bnd", "bfired")}}
        </bpmn:definitions>
        """;

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
