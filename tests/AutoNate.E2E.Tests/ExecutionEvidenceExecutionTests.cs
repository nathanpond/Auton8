using System.Text.Json;
using System.Text.Json.Nodes;
using AutoNate.E2E.Tests.Support;
using Microsoft.Playwright;
using Xunit;
using Xunit.Abstractions;

namespace AutoNate.E2E.Tests;

/// <summary>
/// Every element with a declared effect is RUN, and the effect observed (#325 AC2, #418, #412).
/// </summary>
/// <remarks>
/// <para>
/// Epic #40's founding complaint is <i>"a diagram that draws fine and then does
/// nothing."</i> Two failure modes satisfy it. Publish-accepts/engine-refuses has
/// been instrumented since M4. Publish-accepts, engine-deploys, <b>element never
/// runs</b> had nothing — and that is how the Loop Marker shipped, how Manual
/// Task and Task (Generic) shipped, and how all three were found by a person
/// rather than a test.
/// </para>
/// <para>
/// <b>Why this class exists rather than the probe alone.</b>
/// <c>tools/bpmn-execution-probe/probe.py</c> measured the matrix, and
/// <c>ExecutionEvidenceTests</c> compared the record to the probe's transcript.
/// Verification then falsified the record by editing both files consistently —
/// certifying that <b>Manual Task creates a runtime task</b>, with the suite
/// green (#418). A transcript cannot be the oracle, however many files agree
/// with it. This produces the proof by running.
/// </para>
/// <para>
/// <b>Entry is asserted, not just effect (#412).</b> Four of four probe observers
/// reported <c>proved</c> with the element under test deleted — "an instance
/// ended" and "a task appeared" are true of a great many diagrams. Every case
/// here asserts the element's own activity id appears in the run's history
/// <em>and</em> that its effect happened, so deleting the element fails the cell.
/// </para>
/// <para>
/// <c>RequiresService=Flowable</c> by necessity: the engine is the oracle. That
/// puts it outside CI, alongside ~49% of this suite — stated here because #325's
/// Notes asked it be stated rather than discovered.
/// </para>
/// </remarks>
[Trait("RequiresService", "Flowable")]
[Collection(AutoNateE2ECollection.Name)]
public sealed class ExecutionEvidenceExecutionTests : E2ETestBase
{
    private readonly ITestOutputHelper _output;

    public ExecutionEvidenceExecutionTests(AutoNateE2EFixture fixture, ITestOutputHelper output)
        : base(fixture) => _output = output;

    /// <summary>The declarations this class is obliged to prove.</summary>
    public static TheoryData<string, string> DeclaredEffects()
    {
        var path = Path.Combine(RepoRoot.Path, "src", "shared", "bpmn-execution-evidence.json");
        var elements = JsonNode.Parse(File.ReadAllText(path))!["elements"]!.AsArray();

        var data = new TheoryData<string, string>();
        foreach (var element in elements)
        {
            var effect = element!["declaredEffect"]?.GetValue<string>();
            if (effect is null) continue;

            var name = element["name"]!.GetValue<string>();
            if (Diagram(name, "x") is null) continue;   // no minimal diagram yet

            data.Add(name, effect);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(DeclaredEffects))]
    public async Task The_element_runs_and_has_its_declared_effect(string name, string effect)
    {
        await using var session = await NewSignedInAsAdminAsync();
        var api = session.Page.APIRequest;

        var key = $"ee{Guid.NewGuid():N}"[..18];
        var xml = Diagram(name, key)!;

        // A call activity's callee must be a PUBLISHED workflow of its own --
        // Auton8 refuses the parent otherwise, correctly, naming the missing key.
        // The first version declared both processes in one definition and was
        // refused at publish, which is the validation working.
        if (CalleeDiagram(name, key) is { } callee)
        {
            await PublishAsync(api, $"{key}c", callee);
        }

        await PublishAsync(api, key, xml);
        var instance = await StartAsync(api, key);

        // ENTRY first. Without this, "the instance ended" and "a task appeared"
        // are satisfied by diagrams the element was deleted from -- measured, on
        // four of four probe observers (#412).
        var entered = await EventuallyEnteredAsync(api, instance, "Ev_1");
        Assert.True(
            entered,
            $"{name}: the engine never entered activity 'Ev_1'. The instance ran, so "
            + "whatever effect follows is some other element's (#412).");

        var observed = await ObserveAsync(api, instance, effect);
        _output.WriteLine($"{name,-34} {effect,-17} {observed.Detail}");

        Assert.True(
            observed.Held,
            $"{name} declares the effect '{effect}' and it did not happen: {observed.Detail}\n\n"
            + "The element deployed. That is not the same as it running -- which is the "
            + "whole of #325, and how Loop Marker, Manual Task and Task (Generic) shipped.");
    }

    /// <summary>
    /// The declarations and this class's coverage do not drift apart (#325 AC3).
    /// </summary>
    /// <remarks>
    /// A declared effect with no diagram here is a claim nothing proves, which is
    /// the state this class exists to end. It is listed rather than silently
    /// skipped.
    /// </remarks>
    [Fact]
    public void Every_declared_effect_has_a_diagram_here()
    {
        var path = Path.Combine(RepoRoot.Path, "src", "shared", "bpmn-execution-evidence.json");
        var declared = JsonNode.Parse(File.ReadAllText(path))!["elements"]!.AsArray()
            .Where(e => e!["declaredEffect"] is not null)
            .Select(e => e!["name"]!.GetValue<string>())
            .ToList();

        var unproven = declared.Where(n => Diagram(n, "x") is null).Order(StringComparer.Ordinal).ToList();

        Assert.True(
            unproven.Count == 0,
            "These elements declare an observable effect and this class has no diagram to "
            + "prove it, so the declaration is a claim nothing runs:\n  "
            + string.Join("\n  ", unproven));
    }

    // ---------------------------------------------------------------- helpers

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
            DataObject = new { variables = new { items = new[] { "a", "b" }, ok = false, approver = "ana", taken = true } }
        });
        Assert.True(response.Ok, $"Starting failed: {response.Status} {await response.TextAsync()}");

        using var body = JsonDocument.Parse(await response.TextAsync());
        return body.RootElement.GetProperty("id").GetString()!;
    }

    /// <summary>Did the engine ENTER this activity? Through Auton8's own history route.</summary>
    private static async Task<bool> EventuallyEnteredAsync(
        IAPIRequestContext api, string instance, string activityId)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var response = await api.GetAsync($"/api/executions/{instance}/history");
            if (response.Ok)
            {
                var text = await response.TextAsync();
                if (text.Contains($"\"{activityId}\"", StringComparison.Ordinal)) return true;
            }

            await Task.Delay(250);
        }

        return false;
    }

    private readonly record struct Observation(bool Held, string Detail);

    private static async Task<Observation> ObserveAsync(
        IAPIRequestContext api, string instance, string effect)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var seen = await LookAsync(api, instance, effect);
            if (seen.Held) return seen;
            await Task.Delay(250);
        }

        return await LookAsync(api, instance, effect);
    }

    private static async Task<Observation> LookAsync(
        IAPIRequestContext api, string instance, string effect)
    {
        // Everything comes off /diagram, which is the route the rest of this suite
        // already reads. The first version of this class invented
        // `GET /api/executions/{id}` -- a route that does not exist -- and read
        // the failure as "the instance ran straight through", turning seven
        // healthy elements into seven false negatives. A query that returns
        // nothing reading like a verdict is the exact failure #412 is about, and
        // it happened here inside the fix for it.
        var response = await api.GetAsync($"/api/executions/{instance}/diagram");
        if (!response.Ok) return new(false, $"diagram: {response.Status}");

        using var document = JsonDocument.Parse(await response.TextAsync());
        var root = document.RootElement;

        var current = root.TryGetProperty("currentActivityIds", out var ids)
            && ids.ValueKind == JsonValueKind.Array
                ? ids.EnumerateArray().Select(i => i.GetString() ?? "").ToList()
                : [];

        switch (effect)
        {
            case "task-appears":
            {
                var here = await CountTasksAsync(api, instance);
                if (here > 0) return new(true, $"{here} task(s) on this instance");

                // A CALL ACTIVITY's task belongs to the CALLED instance, so a query
                // on this one returns zero -- which reads exactly like "the element
                // did nothing". The standalone probe hit this too; the difference is
                // that entry on Ev_1 has already been asserted by the time we get
                // here, so a genuine zero is a genuine failure.
                var children = await api.GetAsync($"/api/executions/{instance}/children");
                if (!children.Ok) return new(false, $"0 here; children: {children.Status}");

                using var body = JsonDocument.Parse(await children.TextAsync());
                if (body.RootElement.ValueKind != JsonValueKind.Array) return new(false, "0 task(s)");

                foreach (var child in body.RootElement.EnumerateArray())
                {
                    if (!child.TryGetProperty("id", out var id)) continue;
                    var inChild = await CountTasksAsync(api, id.GetString()!);
                    if (inChild > 0) return new(true, $"{inChild} task(s) in the called instance");
                }

                return new(false, "no task on this instance or any child");
            }

            case "variable-written":
            {
                var written = root.TryGetProperty("variables", out var variables)
                    && variables.ValueKind == JsonValueKind.Array
                    && variables.EnumerateArray().Any(v =>
                        v.TryGetProperty("name", out var n) && n.GetString() == "proof");
                return new(written, written ? "proof variable written" : "no 'proof' variable");
            }

            case "instance-waits":
                return new(current.Count > 0,
                    current.Count > 0
                        ? $"parked at [{string.Join(", ", current)}]"
                        : "nothing is current -- the instance ran straight through");

            case "instance-ends":
                return new(current.Count == 0,
                    current.Count == 0
                        ? "nothing is current -- the instance finished"
                        : $"still parked at [{string.Join(", ", current)}]");

            default:
                return new(false, $"no observer for effect '{effect}'");
        }
    }

    /// <summary>A minimal process whose only interesting element is `Ev_1`.</summary>
    /// <remarks>
    /// The shapes the probe measured, rebuilt here so the proof runs rather than
    /// being read from a transcript. `null` means "no diagram yet", which
    /// `Every_declared_effect_has_a_diagram_here` turns into a finding rather
    /// than a silent skip.
    /// </remarks>
    private static string? Diagram(string name, string key)
    {
        const string Script =
            """<scriptTask id="Ev_1" name="proof" scriptFormat="javascript" autonate:runAs="workflowAuthor">"""
            + """<script>variables.set('proof', 'ran');</script></scriptTask>""";

        // The DI section is not decoration: /api/executions/{id}/diagram renders
        // the authored diagram and answers 500 without it -- which the first
        // version of this class read as "the element did nothing" on all 14 cells.
        string Wrap(string roots, string body) => $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL"
                         xmlns:flowable="http://flowable.org/bpmn"
                         xmlns:autonate="http://autonate.dev/workflows"
                         targetNamespace="http://autonate.dev/workflows">
              {roots}
              <process id="{key}" name="probe" isExecutable="true">
                {body}
              </process>
              <bpmndi:BPMNDiagram id="Diagram_1"
                                  xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI"
                                  xmlns:dc="http://www.omg.org/spec/DD/20100524/DC">
                <bpmndi:BPMNPlane id="Plane_1" bpmnElement="{key}">
                  <bpmndi:BPMNShape id="Shape_Ev_1" bpmnElement="Ev_1">
                    <dc:Bounds x="240" y="100" width="100" height="80" />
                  </bpmndi:BPMNShape>
                </bpmndi:BPMNPlane>
              </bpmndi:BPMNDiagram>
            </definitions>
            """;

        string Linear(string element) =>
            $"""<startEvent id="Start_1"/>{element}<endEvent id="End_1"/>"""
            + """<sequenceFlow id="f1" sourceRef="Start_1" targetRef="Ev_1"/>"""
            + """<sequenceFlow id="f2" sourceRef="Ev_1" targetRef="End_1"/>""";

        return name switch
        {
            "User Task" => Wrap("", Linear("""<userTask id="Ev_1" name="approve"/>""")),
            "Script Task" => Wrap("", Linear(Script)),
            "Receive Task" => Wrap("", Linear("""<receiveTask id="Ev_1" name="wait"/>""")),

            "Start Event (None)" => Wrap("",
                """<startEvent id="Ev_1"/><endEvent id="End_1"/>"""
                + """<sequenceFlow id="f1" sourceRef="Ev_1" targetRef="End_1"/>"""),
            "End Event (None)" => Wrap("",
                """<startEvent id="Start_1"/><endEvent id="Ev_1"/>"""
                + """<sequenceFlow id="f1" sourceRef="Start_1" targetRef="Ev_1"/>"""),
            "Sequence Flow" => Wrap("",
                """<startEvent id="Start_1"/><endEvent id="End_1"/>"""
                + """<sequenceFlow id="Ev_1" sourceRef="Start_1" targetRef="End_1"/>"""),

            // Terminate's whole behaviour is CANCELLING its siblings, so a parallel
            // branch is parked on a user task. The instance can only end if the
            // terminate cancelled it -- the one probe observer that genuinely
            // discriminated, kept as the model for the rest.
            "End Event (Terminate)" => Wrap("",
                """<startEvent id="Start_1"/><parallelGateway id="Gw_1"/>"""
                + """<userTask id="Parked_1" name="parked"/>"""
                + """<endEvent id="Ev_1"><terminateEventDefinition/></endEvent>"""
                + """<sequenceFlow id="f1" sourceRef="Start_1" targetRef="Gw_1"/>"""
                + """<sequenceFlow id="f2" sourceRef="Gw_1" targetRef="Parked_1"/>"""
                + """<sequenceFlow id="f3" sourceRef="Gw_1" targetRef="Ev_1"/>"""),

            "Intermediate Throw (None)" => Wrap("", Linear("""<intermediateThrowEvent id="Ev_1"/>""")),

            "Intermediate Catch (Timer)" => Wrap("", Linear(
                """<intermediateCatchEvent id="Ev_1"><timerEventDefinition>"""
                + """<timeDuration>PT1H</timeDuration></timerEventDefinition></intermediateCatchEvent>""")),
            "Intermediate Catch (Message)" => Wrap("""<message id="Msg_1" name="m"/>""", Linear(
                """<intermediateCatchEvent id="Ev_1"><messageEventDefinition messageRef="Msg_1"/>"""
                + """</intermediateCatchEvent>""")),
            "Intermediate Catch (Signal)" => Wrap("""<signal id="Sig_1" name="s"/>""", Linear(
                """<intermediateCatchEvent id="Ev_1"><signalEventDefinition signalRef="Sig_1"/>"""
                + """</intermediateCatchEvent>""")),
            "Intermediate Catch (Conditional)" => Wrap("", Linear(
                """<intermediateCatchEvent id="Ev_1"><conditionalEventDefinition>"""
                + """<condition>${ok == true}</condition></conditionalEventDefinition></intermediateCatchEvent>""")),

            "Sub-Process (Embedded)" => Wrap("", Linear(
                """<subProcess id="Ev_1"><startEvent id="IS_1"/><userTask id="IT_1" name="inner"/>"""
                + """<endEvent id="IE_1"/><sequenceFlow id="if1" sourceRef="IS_1" targetRef="IT_1"/>"""
                + """<sequenceFlow id="if2" sourceRef="IT_1" targetRef="IE_1"/></subProcess>""")),

            // The three gateways: the script sits on ONE branch with a condition
            // only that branch satisfies, so the `proof` variable can only be
            // written if the gateway routed. The probe's version put the script
            // downstream of the gateway, which meant deleting the gateway left the
            // script on the path and the proof still appeared -- 3 of its 5
            // `variable-written` proofs were unsound for exactly that reason
            // (#412). Entry on Ev_1 is asserted too, so both halves must hold.
            "Exclusive Gateway (XOR)" => GatewayDiagram(key, "exclusiveGateway", Script),
            "Inclusive Gateway (OR)" => GatewayDiagram(key, "inclusiveGateway", Script),
            "Parallel Gateway (AND)" => GatewayDiagram(key, "parallelGateway", Script),

            "Event-Based Gateway" => Wrap("""<message id="Msg_1" name="m"/>""",
                """<startEvent id="Start_1"/><eventBasedGateway id="Ev_1"/>"""
                + """<intermediateCatchEvent id="C_1"><messageEventDefinition messageRef="Msg_1"/></intermediateCatchEvent>"""
                + """<intermediateCatchEvent id="C_2"><timerEventDefinition><timeDuration>PT1H</timeDuration></timerEventDefinition></intermediateCatchEvent>"""
                + """<endEvent id="End_1"/>"""
                + """<sequenceFlow id="f1" sourceRef="Start_1" targetRef="Ev_1"/>"""
                + """<sequenceFlow id="f2" sourceRef="Ev_1" targetRef="C_1"/>"""
                + """<sequenceFlow id="f3" sourceRef="Ev_1" targetRef="C_2"/>"""
                + """<sequenceFlow id="f4" sourceRef="C_1" targetRef="End_1"/>"""
                + """<sequenceFlow id="f5" sourceRef="C_2" targetRef="End_1"/>"""),

            "Ad-Hoc Sub-Process" => Wrap("", Linear(
                """<adHocSubProcess id="Ev_1" ordering="Parallel"><userTask id="IT_1" name="inner"/>"""
                + """<completionCondition xsi:type="tFormalExpression" """
                + """xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">${done == true}</completionCondition>"""
                + """</adHocSubProcess>""")),

            "Call Activity" => Wrap("", Linear(
                $"""<callActivity id="Ev_1" name="call" calledElement="{key}c"/>""")),

            _ => null
        };

        string GatewayDiagram(string processKey, string tag, string script) => Wrap("",
            $"""<startEvent id="Start_1"/><{tag} id="Ev_1"/>"""
            + script.Replace("Ev_1", "S_1", StringComparison.Ordinal)
            + """<userTask id="Other_1" name="other"/><endEvent id="End_1"/><endEvent id="End_2"/>"""
            + """<sequenceFlow id="f1" sourceRef="Start_1" targetRef="Ev_1"/>"""
            + $"""<sequenceFlow id="f2" sourceRef="Ev_1" targetRef="S_1">{Condition(tag, "${taken == true}")}</sequenceFlow>"""
            + $"""<sequenceFlow id="f4" sourceRef="Ev_1" targetRef="Other_1">{Condition(tag, "${taken != true}")}</sequenceFlow>"""
            + """<sequenceFlow id="f3" sourceRef="S_1" targetRef="End_1"/>"""
            + """<sequenceFlow id="f5" sourceRef="Other_1" targetRef="End_2"/>""");

        static string Condition(string tag, string body) => tag == "parallelGateway"
            ? ""
            : $"""<conditionExpression xsi:type="tFormalExpression" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">{body}</conditionExpression>""";
    }


    /// <summary>The separately-published callee a call activity needs.</summary>
    private static string? CalleeDiagram(string name, string key) => name != "Call Activity" ? null : $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL"
                     xmlns:flowable="http://flowable.org/bpmn"
                     targetNamespace="http://autonate.dev/workflows">
          <process id="{key}c" name="callee" isExecutable="true">
            <startEvent id="CS_1"/><userTask id="CT_1" name="inner"/><endEvent id="CE_1"/>
            <sequenceFlow id="cf1" sourceRef="CS_1" targetRef="CT_1"/>
            <sequenceFlow id="cf2" sourceRef="CT_1" targetRef="CE_1"/>
          </process>
          <bpmndi:BPMNDiagram id="Diagram_1"
                              xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI"
                              xmlns:dc="http://www.omg.org/spec/DD/20100524/DC">
            <bpmndi:BPMNPlane id="Plane_1" bpmnElement="{key}c">
              <bpmndi:BPMNShape id="Shape_CT_1" bpmnElement="CT_1">
                <dc:Bounds x="240" y="100" width="100" height="80" />
              </bpmndi:BPMNShape>
            </bpmndi:BPMNPlane>
          </bpmndi:BPMNDiagram>
        </definitions>
        """;


    private static async Task<int> CountTasksAsync(IAPIRequestContext api, string instance)
    {
        var response = await api.GetAsync($"/api/executions/{instance}/tasks");
        if (!response.Ok) return 0;
        using var body = JsonDocument.Parse(await response.TextAsync());
        return body.RootElement.ValueKind == JsonValueKind.Array ? body.RootElement.GetArrayLength() : 0;
    }

}
