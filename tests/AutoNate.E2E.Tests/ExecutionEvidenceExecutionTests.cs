using System.Text.Json;
using System.Xml.Linq;
using System.Text.Json.Nodes;
using AutoNate.E2E.Tests.Support;
using static AutoNate.E2E.Tests.Support.BpmnDiagram;
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
/// puts this class in the <b>full-local</b> tier, not <b>slim</b> — so GitHub
/// never runs it and <c>make test-full-local</c> is what does. Stated here
/// because #325's Notes asked it be stated rather than discovered.
/// </para>
/// <para>
/// The rule, not a count: <b>slim stands up no workflow engine, so nothing slim
/// runs proves any BPMN element executes.</b> This used to read "alongside ~49%
/// of this suite", and a number goes stale and then misleads with authority —
/// which is how it came to say 49% in the first place. The live numbers are the
/// pins in <c>tests/tiers.env</c>, where they are checked rather than recited.
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
    public static TheoryData<string, string, string, string?> DeclaredEffects()
    {
        var path = Path.Combine(RepoRoot.Path, "src", "shared", "bpmn-execution-evidence.json");
        var elements = JsonNode.Parse(File.ReadAllText(path))!["elements"]!.AsArray();

        // The MANIFEST's localName, not the diagram's. The first version derived
        // the expected type from the diagram under test, so mutating the diagram
        // mutated the expectation with it and all nine same-id stand-ins walked
        // past -- measured (#412). An oracle whose expectation is a function of
        // the thing it is judging has no opinion at all.
        var declared = JsonNode
            .Parse(File.ReadAllText(
                Path.Combine(RepoRoot.Path, "src", "shared", "bpmn-support.json")))!
            ["elements"]!.AsArray()
            .ToDictionary(
                e => e!["name"]!.GetValue<string>(),
                e => (Local: e!["localName"]?.GetValue<string>(),
                      Definition: e!["eventDefinition"]?.GetValue<string>()),
                StringComparer.Ordinal);

        var data = new TheoryData<string, string, string, string?>();
        foreach (var element in elements)
        {
            var effect = element!["declaredEffect"]?.GetValue<string>();
            if (effect is null) continue;

            var name = element["name"]!.GetValue<string>();

            // NO SILENT SKIP (#433). This was `if (Diagram(name,"x") is null)
            // continue;`, and that one word is how the forgery survived #429:
            // changing an arm to `=> null` dropped the oracle from 19 cells to 18
            // with every CI gate green. A declaration with no diagram is now a
            // loud failure of this theory rather than a quieter run of it.
            Assert.True(
                Diagram(name, "x") is not null,
                $"'{name}' declares the effect '{effect}' and this class has no diagram "
                + "for it, so nothing runs it. An oracle that shrinks when an element "
                + "stops being exercised is not an oracle (#433).");

            Assert.True(
                declared.TryGetValue(name, out var identity) && !string.IsNullOrEmpty(identity.Local),
                $"'{name}' declares an effect but bpmn-support.json gives it no localName, "
                + "so nothing independent of the diagram says what it should run as (#412).");

            data.Add(name, effect, identity.Local!, identity.Definition);
        }

        return data;
    }

    /// <summary>The oracle is this many cells, and cannot quietly shrink (#429).</summary>
    /// <remarks>
    /// <c>DeclaredEffects</c> skips any element without a minimal diagram, so a
    /// declaration and its diagram can both disappear leaving a smaller, greener
    /// run. The backend suite pins the same number from the other side
    /// (<c>Exactly_these_elements_are_obliged_to_declare_an_effect</c>), where CI
    /// can see it; this is the assertion at the point of use.
    /// </remarks>
    [Fact]
    public void The_oracle_runs_every_declared_cell()
    {
        // Pinned alongside the backend suite's `obliged` list, which names the
        // same set in the slim tier. Both move together or one of them fails,
        // which is the point (#429, #433).
        Assert.Equal(51, DeclaredEffects().Count);
    }

    /// <summary>
    /// One diagram per effect that must be observed as NOT holding (#463).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Everything else in this milestone pins <b>data</b> — which elements owe a
    /// declaration, which diagrams exist, how many cells run, which rows are in
    /// which bucket. Nothing pinned <b>discrimination</b>: that the observers
    /// still tell a held effect from an unheld one.
    /// </para>
    /// <para>
    /// Measured, which is why this exists: weakening <c>instance-ends</c> from
    /// <c>current.Count == 0</c> to <c>!current.Contains("Ev_1")</c> — which
    /// reads <em>tighter</em> and is strictly weaker — left 32 of 32 live cells
    /// and 41 of 41 record guards green while a cell was demonstrably false.
    /// Sixteen of the twenty-nine cells ride on that one observer.
    /// </para>
    /// <para>
    /// The manifest suite has carried this shape for a while — <c>Flipping_one_row_changes_a_tally</c>,
    /// <c>A_gutted_reason_is_not_a_measurement</c> — and the oracle, whose whole
    /// purpose is mutation-resistance, had no case asserting that any diagram
    /// must FAIL. These are those cases. An observer gutted to a tautology kills
    /// the control for its effect on the next run.
    /// </para>
    /// </remarks>
    /// <remarks>
    /// Keyed on a CONTROL id, not on the effect (#488). An effect may need more
    /// than one inert diagram: `tasks-appear-together` has two, because "does
    /// not hold" has two distinct shapes for it — the wrong kind of marker, and
    /// the right kind asking for one instance.
    /// </remarks>
    /// <remarks>
    /// The fourth column is the control's PRECONDITION (#522). Every control
    /// here used to owe the same one -- the engine entered `Ev_1` -- and that
    /// made "the element did not fire" inexpressible: a control whose whole
    /// point is that nothing happened cannot assert it was entered first. The
    /// three values are `entered`, which is the original obligation;
    /// `no-instance`, the self-starting shape's complement, where nothing is
    /// started and nothing may appear; and `not-entered`, for an element the
    /// engine never records as an activity at all. The last is not a relaxation
    /// granted on request — it is the same fact `NeverEntered` records, measured,
    /// and a control claiming it for an element the engine DOES enter would be
    /// giving up #463's precondition for nothing.
    /// </remarks>
    public static TheoryData<string, string, string, string> InertDiagrams() => new()
    {
        { "instance-ends", "instance-ends", "a parallel branch parks on a user task, so the instance never ends", "entered" },
        { "instance-waits", "instance-waits", "nothing waits, so the instance runs straight through", "entered" },
        { "task-appears", "task-appears", "the only task is outside the sub-process, so the container creates none", "entered" },
        { "variable-written", "variable-written", "no script writes `proof`", "entered" },

        // A SECOND SHAPE FOR THE SAME EFFECT (#531), which this table already
        // supports -- `tasks-appear-together` has two for the same reason. A real,
        // working, deployable compensation apparatus whose throw is an ORDINARY
        // end event: the boundary is attached, the handler exists and is
        // reachable, and compensation is simply never triggered, so the handler
        // never runs. An observer that accepted "the handler is in the diagram"
        // rather than "the engine recorded it writing" is satisfied by this.
        { "variable-written", "variable-written:no-compensation-thrown", "nothing throws compensation, so the handler never runs", "not-entered" },

        // THE SAME GATEWAY, ROUTING CORRECTLY, THE OTHER WAY (#533). Real,
        // deployable and working: the script returns the flow to the branch that
        // does NOT write `proof`. An observer satisfied by this is one that reads
        // "the diagram contains a script that writes proof" rather than "the
        // engine recorded this element routing to it".
        { "variable-written", "variable-written:complex-routes-away", "the routing script picks the branch that writes nothing", "entered" },

        // A REGISTERED behaviour that writes a DIFFERENT variable (#535). It has
        // to be registered: an unknown key fails the callback, the job fails, and
        // the element is never entered -- so the control would fail its own
        // precondition instead of testing the observer. `send-message` runs, does
        // its work, reports `sendMessageResult`, and never writes `unlockResult`.
        { "behavior-ran", "behavior-ran", "a different registered behaviour runs and reports its own variable", "entered" },

        // A real, correctly wired reference whose declaration simply carries no
        // value (#534). Deployable, resolving, and the variable is not there.
        { "value-carried", "value-carried", "the declaration carries no value, so nothing reaches the instance", "not-entered" },

        // These two are each other's control (#471). Each diagram is a real,
        // deployable, WORKING multi-instance activity carrying the other kind of
        // marker -- so neither observer can be satisfied by a diagram that simply
        // does nothing, which is the usual way a negative control goes soft.
        { "tasks-appear-together", "tasks-appear-together", "the marker is sequential, so only one instance is live at a time", "entered" },
        { "tasks-appear-in-turn", "tasks-appear-in-turn", "the marker is parallel, so all three appear at once", "entered" },

        // THE FLOOR, as a live control (#488). A parallel marker asking for ONE
        // instance is the founding defect's own result -- "it ran once where the
        // author asked for three" -- reached from the diagram side instead of
        // the engine side. Without the `wanted < 2` guard the observer reports
        // "1 live task(s) on Ev_1 at once, as authored" and this control fails,
        // which is the asymmetry #488 was filed for.
        { "tasks-appear-together", "tasks-appear-together:one", "the marker asks for a single instance, so nothing runs at once", "entered" },

        // THE OPPOSITE FEATURE, not a diagram that does nothing (#522). A
        // NON-interrupting boundary fires for real -- its own path runs, and the
        // host keeps running beside it. That is the same construction the two
        // marker controls use on each other, and it is the strongest form this
        // complement can take: an observer that has stopped discriminating
        // cannot hide behind "well, nothing happened", because something did.
        { "host-cancelled", "host-cancelled", "the boundary is non-interrupting, so its path runs and the host survives", "entered" },

        // ROW-LEVEL, WHERE THE ELEMENT ALLOWS IT (#546). The timer control above
        // is per-EFFECT, and for Error Boundary that is the only control possible
        // -- BPMN makes an error boundary always
        // interrupting and Flowable interrupts regardless. For escalation and conditional it is NOT the only one
        // possible, and #530's commit claimed it had added a row-level control
        // when it had added none. These are the controls that claim was about:
        // the same element, legal, genuinely firing, carrying the other
        // configuration.
        { "host-cancelled", "host-cancelled:escalation", "the escalation boundary is non-interrupting, so its path runs and the host survives", "entered" },
        { "host-cancelled", "host-cancelled:conditional", "the conditional boundary is non-interrupting, so its path runs and the host survives", "entered" },

        // "DID NOT FIRE", which no control could previously express. The start
        // event is stripped of its trigger and nothing else changes, so the
        // assertion is that no instance ever comes into being -- reached from
        // the DIAGRAM side, which is the mutation the positive cell must go red
        // for. There is no instance, so there is nothing to have entered `Ev_1`,
        // which is why the precondition column exists.
        { "instance-starts", "instance-starts:no-trigger", "the start event carries no trigger, so nothing ever creates an instance", "no-instance" },
    };

    [Theory]
    [MemberData(nameof(InertDiagrams))]
    public async Task An_inert_diagram_is_observed_as_not_holding(
        string effect, string control, string why, string precondition)
    {
        await using var session = await NewSignedInAsAdminAsync();
        var api = session.Page.APIRequest;

        var key = $"nc{Guid.NewGuid():N}"[..18];
        var xml = InertDiagram(control, key);

        // "DID NOT FIRE" (#522). There is no instance to start, observe or enter
        // anything, so the whole shape differs: publish, wait the SAME budget the
        // positive path is allowed, and assert nothing appeared. Waiting less
        // than the positive path would make this control weaker than the claim it
        // guards -- "did not fire" has to mean "did not fire in the window where
        // firing is proven to happen", or it is just an impatient read.
        // An instance exists and ran; this element simply leaves no activity
        // record (#531). The observation is still real -- the effect must not
        // hold -- so only the entry precondition is dropped, and only for the
        // elements `NeverEntered` names.
        var mustHaveEntered = !string.Equals(precondition, "not-entered", StringComparison.Ordinal);

        if (string.Equals(precondition, "no-instance", StringComparison.Ordinal))
        {
            var before = await InstancesOfAsync(key);
            Assert.True(
                before.Count == 0,
                $"negative control for '{effect}': an instance of '{key}' existed before this "
                + "control published anything, so its verdict is about somebody else's run (#522).");

            await PublishAsync(api, key, xml);

            var appeared = await SelfStartedInstanceAsync(key);

            Assert.True(
                appeared is null,
                $"NEGATIVE CONTROL FAILED for '{effect}'. This diagram is inert by construction -- "
                + $"{why} -- and instance '{appeared?.Id}' appeared anyway. Either the engine is "
                + "starting instances nothing asked for, or the harness started this one, and in "
                + "both cases every cell that declares this effect is passing on nothing (#463, #522).");

            return;
        }

        await PublishAsync(api, key, xml);
        var instance = await StartAsync(api, key);

        if (mustHaveEntered)
        {
            var entered = await EventuallyEnteredAsync(api, instance, "Ev_1");
            Assert.True(
                entered is not null,
                $"negative control for '{effect}': the engine never entered 'Ev_1', so this control "
                + "is not testing the observer -- it would fail for the wrong reason (#463).");
        }

        var observed = await ObserveAsync(api, instance, effect, ElementTypeIn(xml), xml);

        Assert.False(
            observed.Held,
            $"NEGATIVE CONTROL FAILED for '{effect}'. This diagram is inert by construction -- "
            + $"{why} -- and the observer reported the effect as HELD, saying: {observed.Detail}. "
            + "The observer has stopped discriminating, so every cell that declares this effect "
            + "is now passing on nothing (#463).");
    }

    // The DI section is not decoration: /api/executions/{id}/diagram renders the
    // authored diagram and answers 500 without it -- which the first version of
    // this class read as "the element did nothing" on all 14 cells.
    //
    // Hoisted out of `Diagram` so the negative controls build their diagrams the
    // same way the real cells do (#463). A control assembled differently from
    // the thing it guards is a second construction to get wrong.
    //
    // `xmlns:bpmn` IS GONE, AND ITS ABSENCE IS THE REGRESSION TEST (#482).
    //
    // It used to be bound to the SAME uri as the default namespace, which looked
    // redundant and was not: `ExpandMultiInstanceCardinality` wrote
    // `xsi:type="bpmn:tFormalExpression"` on the loopCardinality child it
    // synthesises, that value is a QName, and without the prefix bound Flowable
    // refused the whole deployment. Measured then -- removing the line failed
    // exactly the two Multi-Instance cells and nothing else -- and filed as #482,
    // because a diagram may legally bind BPMN as its DEFAULT namespace and a
    // hand-written or API-posted one often does. bpmn-js emits prefixed
    // documents, so everything from the studio hid it.
    //
    // #482 resolves the prefix instead of assuming it, so the binding comes out.
    // Every diagram in this class is now a default-namespace document, which is
    // the "publish a default-namespace diagram carrying a fixed cardinality"
    // that issue asked for -- and it covers every other element as well, for
    // free. The two Multi-Instance cells are the ones that fail if the fix is
    // reverted.
    //
    // (`xmlns:xsi` was never needed; XDocument declares it when it serialises the
    // attribute. Measured the same way.)
    private static string WrapIn(string key, string roots, string body) => $"""
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

    private static string LinearIn(string element) =>
        $"""<startEvent id="Start_1"/>{element}<endEvent id="End_1"/>"""
        + """<sequenceFlow id="f1" sourceRef="Start_1" targetRef="Ev_1"/>"""
        + """<sequenceFlow id="f2" sourceRef="Ev_1" targetRef="End_1"/>""";

    /// <summary>A diagram built so that one effect deliberately does not hold (#463).</summary>
    private static string InertDiagram(string control, string key) => control switch
    {
        // Ev_1 is entered and its own branch completes, but a parallel branch
        // parks forever -- so the INSTANCE does not end.
        "instance-ends" => WrapIn(key, "",
            """<startEvent id="Start_1"/><parallelGateway id="Fork_1"/>"""
            + """<scriptTask id="Ev_1" name="inert" scriptFormat="javascript" autonate:runAs="workflowAuthor"><script>var x = 1;</script></scriptTask>"""
            + """<userTask id="Parked_1" name="parked"/><endEvent id="End_1"/><endEvent id="End_2"/>"""
            + """<sequenceFlow id="f1" sourceRef="Start_1" targetRef="Fork_1"/>"""
            + """<sequenceFlow id="f2" sourceRef="Fork_1" targetRef="Ev_1"/>"""
            + """<sequenceFlow id="f3" sourceRef="Fork_1" targetRef="Parked_1"/>"""
            + """<sequenceFlow id="f4" sourceRef="Ev_1" targetRef="End_1"/>"""
            + """<sequenceFlow id="f5" sourceRef="Parked_1" targetRef="End_2"/>"""),

        // Ev_1 is entered and passes straight through, so nothing WAITS.
        "instance-waits" => WrapIn(key, "", LinearIn(
            """<scriptTask id="Ev_1" name="inert" scriptFormat="javascript" autonate:runAs="workflowAuthor"><script>var x = 1;</script></scriptTask>""")),

        // Ev_1 is a sub-process containing nothing that creates a task; the only
        // user task is OUTSIDE it, on a parallel branch.
        "task-appears" => WrapIn(key, "",
            """<startEvent id="Start_1"/><parallelGateway id="Fork_1"/>"""
            + """<subProcess id="Ev_1"><startEvent id="In_1"/><endEvent id="In_2"/><sequenceFlow id="i1" sourceRef="In_1" targetRef="In_2"/></subProcess>"""
            + """<userTask id="Outside_1" name="outside"/><endEvent id="End_1"/><endEvent id="End_2"/>"""
            + """<sequenceFlow id="f1" sourceRef="Start_1" targetRef="Fork_1"/>"""
            + """<sequenceFlow id="f2" sourceRef="Fork_1" targetRef="Ev_1"/>"""
            + """<sequenceFlow id="f3" sourceRef="Fork_1" targetRef="Outside_1"/>"""
            + """<sequenceFlow id="f4" sourceRef="Ev_1" targetRef="End_1"/>"""
            + """<sequenceFlow id="f5" sourceRef="Outside_1" targetRef="End_2"/>"""),

        // The same reference, resolving to the same declaration, with no
        // <flowable:value> on it. Nothing else differs.
        "value-carried" => WrapIn(key, "",
            """<dataObject id="Decl_1" name="carried" autonate:dataType="xsd:double"/>"""
            + """<dataObjectReference id="Ev_1" name="carried" dataObjectRef="Decl_1"/>"""
            + """<startEvent id="Start_1"/><userTask id="Parked_1" name="parked"/><endEvent id="End_1"/>"""
            + """<sequenceFlow id="f1" sourceRef="Start_1" targetRef="Parked_1"/>"""
            + """<sequenceFlow id="f2" sourceRef="Parked_1" targetRef="End_1"/>"""),

        // A real, registered behaviour doing real work -- just not this one.
        "behavior-ran" => WrapIn(key, $"""<message id="Msg_1" name="m1{key}"/>""", LinearIn(
            $"""<sendTask id="Ev_1" name="send" flowable:behaviorKey="autonate.send-message" flowable:autonateMessageName="m1{key}" flowable:autonateTargetProcessKey="{key}r"/>""")),

        // The same complex gateway, routing correctly to the other branch.
        "variable-written:complex-routes-away" => WrapIn(key, "",
            """<startEvent id="Start_1"/>"""
            + """<complexGateway id="Ev_1" name="Choose" scriptFormat="javascript" autonate:runAs="workflowAuthor">"""
            + """<script>return 'fb';</script></complexGateway>"""
            + """<scriptTask id="S_1" name="proof" scriptFormat="javascript" autonate:runAs="workflowAuthor"><script>variables.set('proof', 'ran');</script></scriptTask>"""
            + """<userTask id="Other_1" name="other"/><endEvent id="End_1"/><endEvent id="End_2"/>"""
            + """<sequenceFlow id="f0" sourceRef="Start_1" targetRef="Ev_1"/>"""
            + """<sequenceFlow id="fa" sourceRef="Ev_1" targetRef="S_1"/>"""
            + """<sequenceFlow id="fb" sourceRef="Ev_1" targetRef="Other_1"/>"""
            + """<sequenceFlow id="f3" sourceRef="S_1" targetRef="End_1"/>"""
            + """<sequenceFlow id="f5" sourceRef="Other_1" targetRef="End_2"/>"""),

        // The compensation apparatus, complete and deployable, with an ORDINARY
        // end event where the compensate throw would be (#531). Ev_1 is the
        // boundary; the handler is attached and reachable and never runs.
        "variable-written:no-compensation-thrown" => WrapIn(key, "",
            """<startEvent id="Start_1"/>"""
            + """<scriptTask id="Doer_1" name="do" scriptFormat="javascript" autonate:runAs="workflowAuthor"><script>variables.set('did', 'yes');</script></scriptTask>"""
            + """<boundaryEvent id="Ev_1" attachedToRef="Doer_1"><compensateEventDefinition/></boundaryEvent>"""
            + """<scriptTask id="Undo_1" name="undo" isForCompensation="true" scriptFormat="javascript" autonate:runAs="workflowAuthor"><script>variables.set('proof', 'ran');</script></scriptTask>"""
            + """<endEvent id="End_1"/>"""
            + """<sequenceFlow id="f1" sourceRef="Start_1" targetRef="Doer_1"/>"""
            + """<sequenceFlow id="f2" sourceRef="Doer_1" targetRef="End_1"/>"""
            + """<association id="Assoc_1" associationDirection="One" sourceRef="Ev_1" targetRef="Undo_1"/>"""),

        // Ev_1 runs and writes something that is not `proof`.
        "variable-written" => WrapIn(key, "", LinearIn(
            """<scriptTask id="Ev_1" name="inert" scriptFormat="javascript" autonate:runAs="workflowAuthor"><script>variables.set('echo', 'x');</script></scriptTask>""")),

        // THE TWO MARKER CONTROLS ARE EACH OTHER (#471).
        //
        // Each inert diagram here is a real, deployable, WORKING multi-instance
        // activity -- it simply carries the other kind of marker. That is the
        // strongest form this complement can take: neither control can be
        // satisfied by a diagram that does nothing, so an observer that has
        // stopped discriminating cannot hide behind "well, nothing happened".
        //
        // Sequential runs one at a time, so "more than one at once" must not hold.
        "tasks-appear-together" => WrapIn(key, "", LinearIn(
            """<userTask id="Ev_1" name="approve">"""
            + """<multiInstanceLoopCharacteristics isSequential="true" autonate:loopCardinality="3"/>"""
            + """</userTask>""")),

        // A parallel marker that asks for ONE. The marker is present and the
        // manifest identity still matches; only the count is the founding bug's.
        "tasks-appear-together:one" => WrapIn(key, "", LinearIn(
            """<userTask id="Ev_1" name="approve">"""
            + """<multiInstanceLoopCharacteristics isSequential="false" autonate:loopCardinality="1"/>"""
            + """</userTask>""")),

        // Parallel creates all three at once, so "one at a time" must not hold.
        "tasks-appear-in-turn" => WrapIn(key, "", LinearIn(
            """<userTask id="Ev_1" name="approve">"""
            + """<multiInstanceLoopCharacteristics isSequential="false" autonate:loopCardinality="3"/>"""
            + """</userTask>""")),

        // THE OPPOSITE FEATURE (#522). `cancelActivity="false"` is the only
        // difference from the positive control below: the boundary genuinely
        // fires, `Ev_1` is genuinely entered, its onward path genuinely runs --
        // and `Host_1` is still there, because a non-interrupting boundary does
        // not cancel anything. An observer that checks only "the path ran" is
        // satisfied by this diagram, and that is the whole point: asserting the
        // path without asserting the cancellation passes for the opposite
        // feature.
        //
        // The host is a user task, so it parks and stays parked; nothing but the
        // boundary can remove it.
        "host-cancelled" => WrapIn(key, "",
            """<startEvent id="Start_1"/><userTask id="Host_1" name="host"/>"""
            + """<boundaryEvent id="Ev_1" attachedToRef="Host_1" cancelActivity="false">"""
            + """<timerEventDefinition><timeDuration>PT1S</timeDuration></timerEventDefinition></boundaryEvent>"""
            + """<userTask id="After_1" name="after"/><endEvent id="End_1"/><endEvent id="End_2"/>"""
            + """<sequenceFlow id="f1" sourceRef="Start_1" targetRef="Host_1"/>"""
            + """<sequenceFlow id="f2" sourceRef="Host_1" targetRef="End_1"/>"""
            + """<sequenceFlow id="f3" sourceRef="Ev_1" targetRef="After_1"/>"""
            + """<sequenceFlow id="f4" sourceRef="After_1" targetRef="End_2"/>"""),

        // The escalation row's own complement (#546). An escalation boundary MAY
        // be non-interrupting -- that is precisely what distinguishes it from an
        // error boundary, and `WorkflowBpmnXml`'s error refusal says so: "catch an
        // escalation instead if the work should carry on". So flipping the one
        // attribute produces a valid, deployable diagram describing the opposite
        // feature.
        "host-cancelled:escalation" => WrapIn(key,
            """<escalation id="Esc_1" name="e1" escalationCode="E1"/>""",
            """<startEvent id="Start_1"/>"""
            + """<subProcess id="Host_1"><startEvent id="In_1"/>"""
            + """<intermediateThrowEvent id="Thrown_1"><escalationEventDefinition escalationRef="Esc_1"/></intermediateThrowEvent>"""
            + """<userTask id="In_2" name="inner"/><endEvent id="In_3"/>"""
            + """<sequenceFlow id="i1" sourceRef="In_1" targetRef="Thrown_1"/>"""
            + """<sequenceFlow id="i2" sourceRef="Thrown_1" targetRef="In_2"/>"""
            + """<sequenceFlow id="i3" sourceRef="In_2" targetRef="In_3"/></subProcess>"""
            + """<boundaryEvent id="Ev_1" attachedToRef="Host_1" cancelActivity="false"><escalationEventDefinition escalationRef="Esc_1"/></boundaryEvent>"""
            + """<userTask id="After_1" name="after"/>"""
            + """<endEvent id="End_1"/><endEvent id="End_2"/>"""
            + """<sequenceFlow id="f1" sourceRef="Start_1" targetRef="Host_1"/>"""
            + """<sequenceFlow id="f2" sourceRef="Host_1" targetRef="End_1"/>"""
            + """<sequenceFlow id="f3" sourceRef="Ev_1" targetRef="After_1"/>"""
            + """<sequenceFlow id="f4" sourceRef="After_1" targetRef="End_2"/>"""),

        // And the conditional row's (#546). A non-interrupting conditional
        // boundary is legal too, and `ConditionalEventExecutionTests` already
        // builds one -- so riding the timer control here was a choice, not a
        // constraint. Same condition the positive cell uses, so it genuinely
        // fires.
        "host-cancelled:conditional" => WrapIn(key, "",
            """<startEvent id="Start_1"/><userTask id="Host_1" name="host"/>"""
            + """<boundaryEvent id="Ev_1" attachedToRef="Host_1" cancelActivity="false">"""
            + """<conditionalEventDefinition><condition>${taken == true}</condition>"""
            + """</conditionalEventDefinition></boundaryEvent>"""
            + """<userTask id="After_1" name="after"/><endEvent id="End_1"/><endEvent id="End_2"/>"""
            + """<sequenceFlow id="f1" sourceRef="Start_1" targetRef="Host_1"/>"""
            + """<sequenceFlow id="f2" sourceRef="Host_1" targetRef="End_1"/>"""
            + """<sequenceFlow id="f3" sourceRef="Ev_1" targetRef="After_1"/>"""
            + """<sequenceFlow id="f4" sourceRef="After_1" targetRef="End_2"/>"""),

        // THE TRIGGER, REMOVED (#522). Identical to the positive control except
        // that `Ev_1` carries no <timerEventDefinition>, making it a plain None
        // start -- which the engine will never fire on its own. This is the
        // test-plan mutation reached from the diagram side: if the self-starting
        // cell can pass without a trigger present, it was never observing one.
        "instance-starts:no-trigger" => WrapIn(key, "",
            """<startEvent id="Ev_1"/><userTask id="Parked_1" name="parked"/><endEvent id="End_1"/>"""
            + """<sequenceFlow id="f1" sourceRef="Ev_1" targetRef="Parked_1"/>"""
            + """<sequenceFlow id="f2" sourceRef="Parked_1" targetRef="End_1"/>"""),

        _ => throw new InvalidOperationException(
            $"No inert diagram for control '{control}'. Every observable effect needs one, or the "
            + "observer it belongs to has no negative control (#463)."),
    };

    [Theory]
    [MemberData(nameof(DeclaredEffects))]
    public async Task The_element_runs_and_has_its_declared_effect(
        string name, string effect, string declaredLocalName, string? declaredEventDefinition)
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

        // A send needs somebody to send TO (#454).
        if (ReceiverDiagram(name, key) is { } receiver)
        {
            await PublishAsync(api, $"{key}r", receiver);
        }

        // AND A BUSINESS RULE TASK NEEDS A PUBLISHED TABLE (#111). Same shape as
        // the callee above: the element points at something Auton8 refuses the
        // diagram without, so the dependency is published first and named by the
        // process key rather than shared, so two cells never race for one table.
        if (string.Equals(name, "Business Rule Task", StringComparison.Ordinal))
        {
            await PublishDecisionTableAsync(api, DecisionKeyFor(key));
        }

        // A SELF-STARTING ELEMENT HAS NO CALLER (#522).
        //
        // A Timer, Message, Signal or Conditional START event is not reached by
        // `POST /api/workflows/{key}/start` -- the trigger creates the instance,
        // and calling start would create a SECOND one that proves nothing about
        // the trigger. So the row's effect decides the path: `instance-starts`
        // publishes and then goes looking for what the engine did on its own.
        //
        // The effect name carries this rather than a new manifest key, so
        // `obliged` still pins the (element, effect) pair that selects the path
        // and no row can quietly change lanes.
        string instance;
        if (string.Equals(effect, SelfStarting, StringComparison.Ordinal))
        {
            instance = await SelfStartAsync(api, key, xml, name);
        }
        else
        {
            await PublishAsync(api, key, xml);
            instance = await StartAsync(api, key);

            // A TRIGGER THAT FIRES INTO A RUNNING INSTANCE (#528). A message
            // boundary waits on its host, so the message has to arrive after the
            // instance exists and the host is parked -- the mirror of the
            // self-start hook, which fires before there is an instance at all.
            // Firing either at the other's moment reaches nothing.
            if (AfterStartTriggerMessage(name, key) is { } afterStart)
            {
                var host = AttachedHostOf(xml, "Ev_1");
                if (host is not null)
                {
                    // Parked first, or the message arrives before anything is
                    // waiting for it and the correlator answers no-match. Not a
                    // fixed sleep: a loaded machine makes this slower rather than
                    // flaky.
                    var parked = false;
                    for (var attempt = 0; attempt < 40 && !parked; attempt++)
                    {
                        parked = (await EntryOrderAsync(api, instance)).ContainsKey(host);
                        if (!parked) await Task.Delay(250);
                    }

                    Assert.True(
                        parked,
                        $"{name}: the host '{host}' never started, so there was nothing for "
                        + "the message to interrupt (#528).");
                }

                await DeliverMessageAsync(api, key, afterStart, name);
            }
        }

        // ENTRY, AND OF THE RIGHT KIND (#412).
        //
        // Entry alone was not enough and the first version of this class claimed
        // otherwise: replacing the element with a bare <userTask> under the SAME
        // id passed, because the id is chosen by this test rather than by the
        // element. Nine of nineteen cells were satisfied by a stand-in.
        //
        // The expected type is DERIVED from the diagram this test just built, so
        // there is no second list to drift from the first.
        // The diagram must build what the MANIFEST says this element is. This is
        // the half that catches a swapped diagram; the engine assertion below is
        // the half that catches a swapped behaviour. Deriving one from the other
        // collapses both (#412).
        // A MARKER ROW IS KEYED ON THE MARKER, NOT A TAG (#471).
        //
        // `localName: "*"` means "carried by any host activity". There is no tag
        // to compare, and `eventDefinition` holds
        // `multiInstanceLoopCharacteristics:parallel` rather than an
        // `*EventDefinition` child -- so BOTH assertions below fail on shape, and
        // that is why the three marker rows sat undeclared through all of M4b.
        // One of them is #325's founding example.
        //
        // The contract is unchanged: the MANIFEST says what the diagram must
        // contain and the diagram never votes on its own expectation (#412).
        // Only the vocabulary differs.
        var isMarkerRow = string.Equals(declaredLocalName, "*", StringComparison.Ordinal);

        // AN IDENTITY CARRIED AS AN ATTRIBUTE (#532). An event sub-process is
        // `(subProcess, "triggeredByEvent")`, and that is an attribute -- so
        // `EventDefinitionIn` answers null and the identity assert compares it
        // against "triggeredByEvent". The tag is still asserted from the manifest
        // as usual; only the second half of the identity reads a different place.
        var isAttributeRow = !isMarkerRow
            && string.Equals(declaredEventDefinition, "triggeredByEvent", StringComparison.Ordinal);

        if (isMarkerRow)
        {
            Assert.Equal(declaredEventDefinition, MarkerIn(xml));
        }
        else
        {
            Assert.Equal(declaredLocalName, ElementTypeIn(xml));
        }

        if (!isMarkerRow)
        {

        // AND ITS EVENT DEFINITION (#435). `localName` alone is not an identity:
        // four intermediate catches share `intermediateCatchEvent`, six throws
        // share `intermediateThrowEvent`, seven ends share `endEvent`. Measured,
        // message-catch and signal-catch could BOTH be replaced by a timer and
        // both cells stayed green -- and an event definition is exactly where
        // "draws fine, does nothing" lives. The manifest already keys every row
        // on (localName, eventDefinition) because that pair, not the tag, names
        // an element.
        Assert.Equal(
            declaredEventDefinition,
            isAttributeRow ? AttributeIdentityIn(xml) : EventDefinitionIn(xml));
        }

        // For a marker row the host is whatever the diagram builds, so the
        // engine-name comparison below has no manifest-side expectation to use.
        // The host is pinned a different way instead -- see the deployed-form
        // check further down, which requires the deployed host to be the SAME
        // tag as the authored one and to still carry the marker. What actually
        // stops a stand-in here is the effect: `tasks-appear-together` is not
        // satisfiable by a scriptTask, an unmarked userTask, or a sequential one.
        var expectedType = isMarkerRow ? ElementTypeIn(xml) : declaredLocalName;

        // SOME ELEMENTS ARE NEVER ENTERED, AND THAT IS THE ENGINE'S ANSWER (#531).
        //
        // Measured: a compensation boundary produces no activity instance whether
        // or not compensation fires, so #412's entry check cannot anchor the cell
        // and asserting it would fail every such row for the wrong reason. The
        // deployed-form check below carries the identity instead -- see
        // `NeverEntered` for exactly what that trade gives up.
        var neverEntered = !isMarkerRow
            && NeverEntered.Contains((declaredLocalName, declaredEventDefinition));

        var enteredAs = neverEntered ? null : await EventuallyEnteredAsync(api, instance, "Ev_1");

        Assert.True(
            neverEntered || enteredAs is not null,
            $"{name}: the engine never entered activity 'Ev_1'. The instance ran, so "
            + "whatever effect follows is some other element's (#412).");

        // Guarded rather than OR-ed into the assertion, because `out var alias`
        // is not definitely assigned on a short-circuited disjunct (#531).
        if (!neverEntered)
        {
        Assert.True(
            // EXCLUSIVE, not OR-ed (#446). Where an entry exists the engine name
            // is the ONLY acceptable one, because for five of these rows Auton8
            // REWRITES the element at publish -- a signal end becomes a throw
            // event (#156), a message end and a send task become a service task
            // (#112). Accepting the un-rewritten name as an alternative accepted
            // exactly the pre-#112/#156 defect, and it was measured: with those
            // rewrites disabled, Signal End and Message End stayed GREEN while
            // raising and sending nothing.
            EngineNames.TryGetValue((expectedType, declaredEventDefinition), out var alias)
                ? string.Equals(enteredAs, alias, StringComparison.Ordinal)
                : string.Equals(enteredAs, expectedType, StringComparison.Ordinal),
            $"{name}: activity 'Ev_1' ran, but as a '{enteredAs}' rather than a "
            + $"'{(EngineNames.TryGetValue((expectedType, declaredEventDefinition), out var wanted) ? wanted : expectedType)}'. "
            + "A same-id stand-in satisfies every effect this class observes (#412), and where "
            + "Auton8 rewrites the element at publish the un-rewritten name is the defect the "
            + "rewrite exists to prevent (#446).");
        }

        // THE REWRITE MUST PRESERVE THE SEMANTICS (#454).
        //
        // #446 made the engine-name alias exclusive, which proves the rewrite
        // HAPPENED. This checks it preserved something.
        //
        // The first version of this block was two bypasses wide, both measured.
        // `keeps` was `EndsWith("EventDefinition")` -- a string-suffix test that
        // never consulted `declaredEventDefinition`, a parameter of this very
        // method -- so rewriting a signal end into a COMPENSATION throw passed.
        // And the behaviourKey disjunct accepted the attribute anywhere on any
        // element, so adding a meaningless `flowable:behaviorKey` to a throw
        // event re-greened the exact defect this check was written for.
        //
        // Now: the definition must be the DECLARED one, and the behaviourKey
        // path belongs only to the service task the message rewrite produces.
        // "Auton8 rewrote it" is read from the deployed form, not from a list.
        // `EngineNames` is the wrong predicate: it holds rows where the ENGINE
        // reports a different activityType (an ad-hoc sub-process, an event
        // gateway), which is not the same thing as Auton8 replacing the element
        // at publish. Comparing the deployed tag to the declared one says
        // exactly which happened, and cannot drift from a hand-maintained set.
        var deployed = await DeployedElementAsync(key, "Ev_1");

        Assert.True(
            deployed is not null,
            $"{name}: could not read the deployed form of 'Ev_1' back from the engine, so "
            + "nothing here can say whether the publish rewrite preserved it (#454).");

        // A MARKER MUST SURVIVE PUBLISH (#471).
        //
        // This is the founding bug's own shape: a marker that draws fine, stores
        // fine, and is gone by the time the engine sees it. Auton8 genuinely does
        // rewrite inside this marker -- `autonate:loopCardinality` is an
        // attribute in the stored form and a <bpmn:loopCardinality> CHILD in the
        // deployed one -- so "we rewrite it" is not an excuse for losing it.
        //
        // The host is pinned here too. `localName: "*"` gives the manifest no
        // opinion on the host tag, so the constraint is that publish did not
        // CHANGE it: authored tag == deployed tag.
        if (isMarkerRow)
        {
            Assert.Equal(ElementTypeIn(xml), deployed!.Name.LocalName);

            Assert.Equal(declaredEventDefinition, MarkerOf(deployed));
        }

        // AND AN ATTRIBUTE IDENTITY MUST SURVIVE PUBLISH TOO (#532). The block
        // below asserts the deployed element carries exactly one *EventDefinition
        // child; an event sub-process carries none, so this row would fail there
        // after passing its identity assert. The marker rows met this first and
        // answered it by asserting the MARKER survived instead of skipping the
        // deployed-form check -- same answer here. An attribute that draws fine
        // and is gone by the time the engine sees it is the founding bug's shape.
        if (isAttributeRow)
        {
            Assert.Equal(declaredLocalName, deployed!.Name.LocalName);

            Assert.Equal(declaredEventDefinition, AttributeIdentityOf(deployed));
        }

        // AND A NEVER-ENTERED ROW'S TAG (#549). `NeverEntered`'s own remarks say
        // the entry check is traded for a deployed-form check -- "the element read
        // back from the engine must still be the declared tag carrying the
        // declared event definition". That was true of the compensation boundary
        // and NOT of the data object reference: with a null event definition the
        // block below never runs, so the only deployed-side assertion was that
        // SOMETHING with that id came back, and `DeployedElementAsync` matches by
        // id alone. A justification broader than the code is the thing this
        // milestone's own oracle exists to catch.
        if (neverEntered)
        {
            Assert.True(
                string.Equals(deployed!.Name.LocalName, declaredLocalName, StringComparison.Ordinal),
                $"{name}: the engine deployed 'Ev_1' as a <{deployed.Name.LocalName}> where the "
                + $"manifest says <{declaredLocalName}>. This row is exempt from the ENTRY check "
                + "because the engine records no activity instance for it, and the deployed tag is "
                + "what stands in for it -- without this, a same-id stand-in of any shape would "
                + "reach the effect observation unchallenged (#549).");
        }

        var wasRewritten = !isMarkerRow && !string.Equals(
            deployed!.Name.LocalName, declaredLocalName, StringComparison.Ordinal);

        // WHICH REWRITE, READ FROM THE DEPLOYED FORM (#111). Two publish rewrites
        // now produce a <serviceTask>: the message send (#112) and the DMN
        // expansion. The checks below are different -- a send owes a behaviour
        // key, a decision owes a table key -- so something has to choose between
        // them, and #454's answer stands: read it from the XML the engine holds,
        // not from a list keyed on element name. A list would need editing the
        // next time an element learns to execute by expansion, which is exactly
        // the edit nobody makes.
        var isDmnRewrite = wasRewritten
            && deployed!.Name.LocalName == "serviceTask"
            && string.Equals(
                deployed.Attributes().FirstOrDefault(a => a.Name.LocalName == "type")?.Value,
                "dmn", StringComparison.Ordinal);

        // NOT `return` (#463). Everything from here to the effect observation is
        // a check on the PUBLISH REWRITE; the effect observation is the point of
        // the cell. An early return in this stretch skips it, which is head 5b of
        // #453 -- a cell that runs, stays green, and observes nothing -- and the
        // first draft of the marker branch above did exactly that.
        if (!isMarkerRow && !isAttributeRow && (declaredEventDefinition is not null || wasRewritten))
        {
            if (isDmnRewrite)
            {
                // THE TABLE, NOT A TABLE (#111) -- the demand #454 made of the
                // send rewrite, made of this one. A DMN service task pointing at
                // some other published table deploys, runs, writes variables and
                // decides nothing the author asked for; `variable-written` cannot
                // tell the two apart, because a wrong table still writes.
                //
                // The expectation comes from the AUTHORED diagram's own attribute
                // rather than from `DecisionKeyFor`, so a diagram that stopped
                // configuring the element fails here instead of agreeing with a
                // constant.
                var authoredKey = XDocument.Parse(xml).Descendants()
                    .Where(e => (string?)e.Attribute("id") == "Ev_1")
                    .SelectMany(e => e.Attributes())
                    .Where(a => a.Name.LocalName == "decisionKey")
                    .Select(a => a.Value)
                    .FirstOrDefault();

                Assert.True(
                    !string.IsNullOrWhiteSpace(authoredKey),
                    $"{name}: the authored diagram names no decision key on 'Ev_1', so there is "
                    + "nothing for the deployed form to be checked against (#111).");

                var deployedKey = deployed.Descendants()
                    .Where(e => e.Name.LocalName == "field"
                                && (string?)e.Attribute("name") == "decisionTableReferenceKey")
                    .SelectMany(field => field.Descendants()
                        .Where(child => child.Name.LocalName == "string"))
                    .Select(child => child.Value.Trim())
                    .FirstOrDefault();

                // THE AUTHOR'S TABLE, AT A PINNED VERSION (#111). The deployed key
                // is not the authored one and must not be: publish resolves the
                // table's currently-published version and points the deployed copy
                // at a key naming that version, because Flowable would otherwise
                // resolve the bare key to whatever is latest when the instance
                // runs. So the check is the author's key plus a version pin --
                // which still fails for a rewrite pointing at a different table,
                // and fails for one that dropped the pin and left the process
                // following the latest publish.
                const string PinSeparator = "-v";

                var pin = deployedKey is not null && authoredKey is not null
                          && deployedKey.StartsWith(authoredKey + PinSeparator, StringComparison.Ordinal)
                    ? deployedKey[(authoredKey.Length + PinSeparator.Length)..]
                    : null;

                Assert.True(
                    pin is not null && pin.Length > 0 && pin.All(char.IsAsciiDigit),
                    $"{name}: Auton8 rewrote this into a DMN <serviceTask> evaluating "
                    + $"'{deployedKey ?? "(nothing)"}' where the author wrote '{authoredKey}'. "
                    + $"Expected '{authoredKey}-v<n>' — this element's table, at the version "
                    + "published when the process was. A bare key follows whatever is published "
                    + "later, and a different key is a different table wearing this one's shape "
                    + "(#111, #454).");
            }
            else if (deployed.Name.LocalName == "serviceTask")
            {
                // The message rewrite (#112) replaces the event definition with a
                // behaviour delegate, so there is nothing else left to check.
                // THE KEY, NOT JUST A KEY (#454). This asserted only that the
                // attribute was non-blank, which is half of what this issue's own
                // text asked for. Measured: rewriting the send behaviour to
                // `autonate.unlock-account` -- a real, registered behaviour --
                // left three cells green while the element deployed, ran, ended
                // the instance and did something entirely unrelated. That is not
                // "deploys and does nothing", it is "deploys and does something
                // else", which is worse and is exactly what #112 exists to stop.
                //
                // And the blank-key assertion was unreachable anyway: the rewrite
                // stamps flowable:async="true", so an unresolvable key fails the
                // job and the activity is never entered -- #412's entry check
                // fires first, every time.
                const string SendMessage = "autonate.send-message";

                var behaviorKey = deployed.Attributes()
                    .FirstOrDefault(a => a.Name.LocalName == "behaviorKey")?.Value;

                Assert.True(
                    string.Equals(behaviorKey, SendMessage, StringComparison.Ordinal),
                    $"{name}: Auton8 rewrote this element into a <serviceTask> delegating to "
                    + $"'{behaviorKey ?? "(nothing)"}', not '{SendMessage}'. The rewrite is the "
                    + "send behaviour or it is a different feature wearing its shape (#454).");
            }
            else
            {
                // Rewritten to another plain element with nothing to carry. No
                // shipped row is in this position; if one appears, the rewrite is
                // unguarded -- worth knowing, not worth failing on a shape nobody
                // has produced.
                //
                // This was an early `return`, which also skipped the effect
                // observation further down -- so a row arriving here would have
                // proved nothing at all rather than merely skipping this check.
                // That is head 5b of #453 living inside the oracle already.
                if (declaredEventDefinition is not null)
                {
                    var carried = deployed.Elements()
                        .Select(child => child.Name.LocalName)
                        .Where(local => local.EndsWith("EventDefinition", StringComparison.Ordinal))
                        .ToList();

                    // EXACTLY ONE, AND THE DECLARED ONE (#454). `Contains` asked only
                    // whether it was in the list, and Flowable acts on the FIRST
                    // definition an element carries -- measured on the live engine:
                    // with an escalation definition placed before the signal one, the
                    // signal never fires and no catcher instance is created, while
                    // every cell stayed green.
                    // AND ITS REFERENCE MUST BE THE AUTHOR'S (#454).
                    //
                    // Pinning the definition's TAG says nothing about what it points
                    // at. Measured: a rewrite that invented `<signal id="Ev_1_ghost"/>`
                    // and repointed `signalRef` at it left 35/35 green, with the
                    // author's own signal declared at root and referenced by nothing.
                    // Every signal end raised something no catcher listens for --
                    // #156's headline, surviving four consecutive fixes.
                    //
                    // The diagram declares exactly one root-level signal/message/
                    // escalation/error, so "the reference resolves to a declaration
                    // this diagram's author wrote" is checkable without a second list.
                    var declaredRoots = XDocument.Parse(xml).Root!.Elements()
                        .Select(e => (string?)e.Attribute("id"))
                        .Where(id => id is not null)
                        .ToHashSet(StringComparer.Ordinal);

                    var references = deployed.Elements()
                        .Where(child => child.Name.LocalName.EndsWith("EventDefinition", StringComparison.Ordinal))
                        .SelectMany(child => child.Attributes())
                        .Where(a => a.Name.LocalName.EndsWith("Ref", StringComparison.Ordinal))
                        .Select(a => a.Value)
                        .ToList();

                    var ghosts = references.Where(r => !declaredRoots.Contains(r)).ToList();

                    Assert.True(
                        ghosts.Count == 0,
                        $"{name}: the deployed event definition points at [{string.Join(", ", ghosts)}], "
                        + $"which this diagram never declared -- it declares [{string.Join(", ", declaredRoots)}]. "
                        + "The rewrite repointed the element at something nothing listens for (#454).");

                    Assert.True(
                        carried.Count == 1
                        && string.Equals(carried[0], declaredEventDefinition + "EventDefinition",
                            StringComparison.Ordinal),
                        $"{name}: the manifest declares the event definition '{declaredEventDefinition}', "
                        + $"and the <{deployed.Name.LocalName}> Auton8 deployed carries "
                        + (carried.Count == 0 ? "none at all" : $"[{string.Join(", ", carried)}]")
                        + (carried.Count > 1 ? " -- and the engine acts on the first of them" : "")
                        + ". The publish rewrite dropped or replaced the semantics, which is what #156 "
                        + "and #112 were about (#454).");
                }
            }
        }

        // A SEND THAT FAILED IS NOT A SEND (#454).
        //
        // Not a mutation -- this was the state of the suite. `ExpandMessageSendEvents`
        // removes the event definition and `SendMessageBehavior` resolves the
        // message from the STORED diagram, which these minimal diagrams did not
        // carry. Queried from the live engine after a clean run, all three
        // message rows recorded `sendMessageResult = "noTargetProcess"` or
        // `"noMessageName"`. `instance-ends` was satisfied by a BehaviorResult
        // FAILURE, so three cells certified a send that had never once happened.
        // ONLY WHERE PUBLISH REWROTE SOMETHING INTO A SEND (#535). This was
        // `deployed.Name.LocalName == "serviceTask"` alone, which assumed every
        // deployed service task is one of the three message rewrites. It was true
        // of every row that existed when it was written and false the moment a row
        // authored a service task ON PURPOSE: Service Task (Behavior) failed with
        // "Auton8 rewrote this into a send, and the engine recorded
        // `sendMessageResult = '(nothing)'`" -- a correct observation about a send
        // that was never supposed to happen.
        //
        // `wasRewritten` is exactly the distinction: the send rows are authored as
        // an endEvent, a sendTask or an intermediateThrowEvent and come back as a
        // serviceTask. A behaviour task is authored as one and stays one.
        // AND NOT WHERE THE REWRITE WAS A DECISION (#111). `wasRewritten &&
        // serviceTask` was the send rewrite's signature until a second rewrite
        // produced the same tag; without this the Business Rule Task row fails
        // with "the engine recorded `sendMessageResult = '(nothing)'`" -- a
        // correct observation about a send that was never meant to happen, which
        // is the shape #535 already fixed once from the other side.
        if (wasRewritten && !isDmnRewrite && deployed.Name.LocalName == "serviceTask")
        {
            var sent = await VariableValueAsync(api, instance, "sendMessageResult");

            Assert.True(
                // NOT `noMatch`. That is a legitimate product outcome -- a send
                // to a process nobody is waiting in -- but here it would mean the
                // receiver this test publishes was not found, which is the cell
                // proving nothing again in a quieter way.
                sent is "delivered" or "started",
                $"{name}: Auton8 rewrote this into a send, and the engine recorded "
                + $"`sendMessageResult = '{sent ?? "(nothing)"}'`. The delegate ran and the send "
                + "did not happen, so nothing here is evidence that this element sends (#454).");
        }

        var observed = await ObserveAsync(api, instance, effect, expectedType, xml);
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
            // `userId` is a well-shaped id that resolves to nobody (#535), so
            // `UnlockAccountBehavior` reaches ILocalUserStore and reports
            // `userNotFound` -- an outcome only reachable by the behaviour
            // actually running against the app, rather than merely being
            // constructed.
            DataObject = new
            {
                variables = new
                {
                    items = new[] { "a", "b" }, ok = false, approver = "ana", taken = true,
                    userId = "999999999",
                }
            }
        });
        Assert.True(response.Ok, $"Starting failed: {response.Status} {await response.TextAsync()}");

        using var body = JsonDocument.Parse(await response.TextAsync());
        return body.RootElement.GetProperty("id").GetString()!;
    }

    /// <summary>The effect name that means "the trigger creates the instance" (#522).</summary>
    private const string SelfStarting = "instance-starts";

    /// <summary>
    /// The signal a self-starting row needs fired after publish (#529).
    /// </summary>
    /// <remarks>
    /// <para>
    /// A signal start event cannot be triggered from inside its own diagram --
    /// there is no instance yet, which is the whole point of the element. So one
    /// narrow hook: a row NAMES a signal, and the cell fires it through
    /// <c>POST /api/workflow-signals</c>, the route #523 added.
    /// </para>
    /// <para>
    /// Narrow on purpose. This is not "a row may run arbitrary setup". Everything
    /// else about the self-start path is unchanged -- no <c>POST /start</c>, and
    /// an assertion that no instance existed before.
    /// </para>
    /// <para>
    /// What keeps it honest is NOT a start-user assertion, and saying so was
    /// wrong (#555). This route is authenticated, so Flowable records the REST
    /// user as the start user of the instance the broadcast created -- which is
    /// why <c>SelfStartAsync</c> skips that check for a triggered row. The
    /// honesty comes from the emptiness assertions either side of publish:
    /// nothing existed before publish, nothing existed after publish and before
    /// the signal, and one instance existed afterwards. Firing a signal is not
    /// starting an instance, and that sequence is what proves it.
    /// </para>
    /// </remarks>
    private static string? SelfStartTriggerSignal(string name, string key) => name switch
    {
        "Signal Start Event" => $"st{key}",
        _ => null
    };

    /// <summary>
    /// The message a self-starting row needs delivered after publish (#528).
    /// </summary>
    /// <remarks>
    /// The message counterpart of <see cref="SelfStartTriggerSignal"/>, and it
    /// goes through <c>POST /api/workflow-messages</c> rather than the bus.
    /// #524's queue path works and has its own end-to-end proof; routing the
    /// oracle through Dapr as well would make a question about a BPMN element
    /// depend on a sidecar, and these cells stay Flowable-only like the other
    /// forty-five. The story says explicitly that either trigger path will do.
    /// </remarks>
    private static string? SelfStartTriggerMessage(string name, string key) => name switch
    {
        "Message Start Event" => $"ms{key}",
        _ => null
    };

    /// <summary>
    /// The message a row needs delivered once its instance is running (#528).
    /// </summary>
    /// <remarks>
    /// The mirror of the two above, and the reason they are separate hooks rather
    /// than one: a start trigger fires BEFORE there is an instance, and a boundary
    /// trigger has to fire AFTER one exists and its host is parked. Firing either
    /// at the other's moment reaches nothing.
    /// </remarks>
    private static string? AfterStartTriggerMessage(string name, string key) => name switch
    {
        "Message Boundary" => $"mb{key}",
        _ => null
    };

    /// <summary>Deliver a message to a published workflow through Auton8's API.</summary>
    private static async Task DeliverMessageAsync(
        IAPIRequestContext api, string key, string messageName, string name)
    {
        var delivered = await api.PostAsync("/api/workflow-messages/", new APIRequestContextOptions
        {
            DataObject = new { processKey = key, messageName }
        });

        Assert.True(
            delivered.Ok,
            $"{name}: delivering '{messageName}' to '{key}' failed with {delivered.Status} "
            + $"{await delivered.TextAsync()}. The element cannot be triggered from inside its "
            + "own diagram, so this cell proves nothing without it (#528).");
    }

    /// <summary>
    /// Publish, never call start, and return the instance the row's own trigger
    /// produced (#522).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The two preconditions are not the same check twice. The first -- nothing
    /// exists before publish -- catches a reused key, so the cell cannot certify
    /// somebody else's run. The second is the one the acceptance criterion is
    /// actually about: nobody called start. "An instance exists" is otherwise
    /// satisfiable by the harness itself, and a cell that called
    /// <c>POST /start</c> and then found an instance would report a perfect
    /// green while proving only that starting a workflow starts a workflow.
    /// </para>
    /// <para>
    /// <b>How that second check is made depends on the row, and this comment
    /// used to claim only one of the two (#555).</b> For an UNTRIGGERED row --
    /// timer -- it is the engine's own <c>startUserId</c>, which is empty
    /// because nothing authenticated began it. For a TRIGGERED row -- signal,
    /// message -- <c>startUserId</c> is the wrong instrument: Auton8's trigger
    /// routes are authenticated, so Flowable records the REST user as the start
    /// user of an instance a broadcast created. There the check is the pair of
    /// emptiness assertions either side of publish, plus the fact that the
    /// theory never reaches <c>POST /start</c> on this branch at all.
    /// </para>
    /// <para>
    /// <b>This block sat above <c>SelfStartTriggerSignal</c> for three rounds</b>,
    /// leaving the method it describes undocumented. #555 corrected its wording
    /// and left it where it was; it is finally attached to
    /// <c>SelfStartAsync</c> here (#559).
    /// </para>
    /// </remarks>
    private static async Task<string> SelfStartAsync(
        IAPIRequestContext api, string key, string xml, string name)
    {
        var before = await InstancesOfAsync(key);
        Assert.True(
            before.Count == 0,
            $"{name}: {before.Count} instance(s) of '{key}' existed before this cell published "
            + "anything, so whatever it finds afterwards is not evidence the trigger fired (#522).");

        await PublishAsync(api, key, xml);

        var trigger = SelfStartTriggerSignal(name, key) ?? SelfStartTriggerMessage(name, key);

        if (trigger is not null)
        {
            // PUBLISHING IS NOT TRIGGERING. Checked here rather than only before
            // publish, so the window in which the instance may appear starts
            // AFTER the diagram is deployed. Without it, a publish that started
            // an instance by itself would satisfy "it appeared after the trigger"
            // without the trigger having done anything (#529).
            var afterPublish = await InstancesOfAsync(key);
            Assert.True(
                afterPublish.Count == 0,
                $"{name}: publishing created {afterPublish.Count} instance(s) of '{key}' before "
                + "anything fired the trigger, so whatever appears next is not the trigger's "
                + "doing (#529).");

            if (SelfStartTriggerSignal(name, key) is { } signal)
            {
                var fired = await api.PostAsync("/api/workflow-signals/", new APIRequestContextOptions
                {
                    DataObject = new { signalName = signal }
                });

                Assert.True(
                    fired.Ok,
                    $"{name}: firing '{signal}' failed with {fired.Status} {await fired.TextAsync()}. "
                    + "The element cannot be triggered from inside its own diagram, so this cell "
                    + "proves nothing without it (#529).");
            }
            else
            {
                await DeliverMessageAsync(api, key, trigger, name);
            }
        }

        var found = await SelfStartedInstanceAsync(key);

        Assert.True(
            found is not null,
            $"{name}: nothing called start, and after {SelfStartBudgetSeconds}s the engine had "
            + $"created no instance of '{key}' either. This element is supposed to start its own "
            + "instance; it deployed and did nothing, which is #325.");

        // NO START USER -- BUT ONLY WHERE NOTHING WAS FIRED (#529 correcting #522).
        //
        // The check exists to rule out the harness having called POST /start.
        // For a timer that works: no user is involved anywhere, so any start user
        // is evidence of exactly the false pass it guards against. For a row with
        // a TRIGGER it is simply wrong -- Auton8's own API is authenticated, and
        // MEASURED, Flowable records the REST user as the start user of an
        // instance a signal broadcast created. The cell failed with "started by
        // 'rest-admin'" while doing precisely what it was supposed to.
        //
        // What carries the weight for a triggered row instead: no instance
        // existed before publish, none existed after publish and before the
        // trigger, and one appeared afterwards. The theory never calls
        // `StartAsync` on this path at all -- and for this diagram shape it could
        // not succeed if it did, because Flowable refuses to start a process by
        // key when its start event waits on a signal.
        if (trigger is null)
        {
            Assert.True(
                string.IsNullOrEmpty(found!.Value.StartUserId),
                $"{name}: the instance this cell observed was started by "
                + $"'{found.Value.StartUserId}'. A self-starting element's proof is that the TRIGGER "
                + "created the instance -- an instance somebody called start for satisfies "
                + "\"an instance exists\" while saying nothing about the trigger (#522).");
        }

        return found.Value.Id;
    }

    private readonly record struct EngineInstance(string Id, string? StartUserId);

    /// <summary>
    /// How long a trigger-driven cell may wait for the engine to act (#522).
    /// </summary>
    /// <remarks>
    /// Deliberately NOT <c>ObserveAsync</c>'s five seconds. Flowable acquires
    /// timer and async jobs on its own schedule and can exceed that, but raising
    /// the shared budget would slow every FAILING cell in the class -- which is
    /// the cost #452 deliberately bought down. So the longer wait lives only
    /// here, on the cells that need it. Thirty seconds is what
    /// <c>TimerBoundaryExecutionTests.EventuallyAsync</c> already allows a timer.
    /// </remarks>
    private const int SelfStartBudgetSeconds = 30;

    /// <summary>Poll until the engine creates an instance of this key, or the budget runs out.</summary>
    private static async Task<EngineInstance?> SelfStartedInstanceAsync(string key)
    {
        var deadline = DateTime.UtcNow.AddSeconds(SelfStartBudgetSeconds);
        while (DateTime.UtcNow < deadline)
        {
            var found = await InstancesOfAsync(key);
            if (found.Count > 0) return found[0];
            await Task.Delay(500);
        }

        return null;
    }

    /// <summary>
    /// Every instance of a process key the engine knows about, running or finished.
    /// </summary>
    /// <remarks>
    /// Asked of Flowable directly, and of its HISTORY rather than its runtime.
    /// Auton8's own list route answers a bounded, engine-wide page, so on a busy
    /// suite "no instance" could mean "not on this page" -- and the negative
    /// control's entire verdict is "no instance". A key-filtered history query
    /// has neither problem, and it sees an instance that started and finished,
    /// which a runtime query does not. Reading the engine directly is already
    /// this class's habit for questions Auton8's API cannot answer exactly
    /// (<c>DeployedElementAsync</c>, <c>VariableWriterAsync</c>); what it refuses
    /// to do is PUBLISH around Auton8's validation, which this does not.
    /// </remarks>
    private static async Task<IReadOnlyList<EngineInstance>> InstancesOfAsync(string key)
    {
        using var engine = Support.FlowableDeploymentSweep.CreateClient(
            Environment.GetEnvironmentVariable("AUTONATE_FLOWABLE_URL") ?? "http://localhost:8080/flowable-rest",
            Environment.GetEnvironmentVariable("AUTONATE_FLOWABLE_USER") ?? "rest-admin",
            Environment.GetEnvironmentVariable("AUTONATE_FLOWABLE_PASSWORD") ?? "test");

        var response = await engine.GetAsync(
            "service/history/historic-process-instances"
            + $"?processDefinitionKey={Uri.EscapeDataString(key)}&size=100");

        if (!response.IsSuccessStatusCode) return [];

        using var page = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        if (!page.RootElement.TryGetProperty("data", out var rows)) return [];
        if (rows.ValueKind != JsonValueKind.Array) return [];

        var instances = new List<EngineInstance>();
        foreach (var row in rows.EnumerateArray())
        {
            if (!row.TryGetProperty("id", out var id)) continue;
            if (id.GetString() is not { } instanceId) continue;

            instances.Add(new EngineInstance(
                instanceId,
                row.TryGetProperty("startUserId", out var user) ? user.GetString() : null));
        }

        return instances;
    }

    /// <summary>
    /// What did the engine enter this activity AS? Null if it never entered it.
    /// </summary>
    /// <remarks>
    /// Reads the <c>activityId</c> FIELD. The first version did
    /// <c>text.Contains("\"Ev_1\"")</c> over the whole payload, which also carries
    /// <c>activityName</c> — so an unrelated element merely NAMED <c>Ev_1</c>
    /// satisfied it (#412).
    /// </remarks>
    private static async Task<string?> EventuallyEnteredAsync(
        IAPIRequestContext api, string instance, string activityId)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var response = await api.GetAsync($"/api/executions/{instance}/history");
            if (response.Ok)
            {
                using var body = JsonDocument.Parse(await response.TextAsync());
                if (body.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var row in body.RootElement.EnumerateArray())
                    {
                        if (!row.TryGetProperty("activityId", out var id)) continue;
                        if (id.GetString() != activityId) continue;

                        return row.TryGetProperty("activityType", out var type)
                            ? type.GetString()
                            : "";
                    }
                }
            }

            await Task.Delay(250);
        }

        return null;
    }

    /// <summary>A variable's current value on this instance.</summary>
    private static async Task<string?> VariableValueAsync(
        IAPIRequestContext api, string instance, string variable)
    {
        var response = await api.GetAsync($"/api/executions/{instance}/diagram");
        if (!response.Ok) return null;

        using var body = JsonDocument.Parse(await response.TextAsync());
        if (!body.RootElement.TryGetProperty("variables", out var variables)) return null;
        if (variables.ValueKind != JsonValueKind.Array) return null;

        foreach (var entry in variables.EnumerateArray())
        {
            if (!entry.TryGetProperty("name", out var n)) continue;
            if (n.GetString() != variable) continue;

            return entry.TryGetProperty("value", out var v) ? v.GetString() : null;
        }

        return null;
    }

    /// <summary>The element as Auton8 actually DEPLOYED it, read back from the engine (#454).</summary>
    private static async Task<XElement?> DeployedElementAsync(string key, string id)
    {
        using var engine = Support.FlowableDeploymentSweep.CreateClient(
            Environment.GetEnvironmentVariable("AUTONATE_FLOWABLE_URL") ?? "http://localhost:8080/flowable-rest",
            Environment.GetEnvironmentVariable("AUTONATE_FLOWABLE_USER") ?? "rest-admin",
            Environment.GetEnvironmentVariable("AUTONATE_FLOWABLE_PASSWORD") ?? "test");

        var definitions = await engine.GetAsync(
            $"service/repository/process-definitions?key={Uri.EscapeDataString(key)}&latest=true");
        if (!definitions.IsSuccessStatusCode) return null;

        using var page = JsonDocument.Parse(await definitions.Content.ReadAsStringAsync());
        if (!page.RootElement.TryGetProperty("data", out var rows)) return null;
        if (rows.GetArrayLength() == 0) return null;
        if (!rows[0].TryGetProperty("id", out var definitionId)) return null;

        var resource = await engine.GetAsync(
            $"service/repository/process-definitions/{Uri.EscapeDataString(definitionId.GetString()!)}/resourcedata");
        if (!resource.IsSuccessStatusCode) return null;

        return XDocument.Parse(await resource.Content.ReadAsStringAsync())
            .Descendants()
            .FirstOrDefault(e => (string?)e.Attribute("id") == id);
    }

    /// <summary>
    /// The activity id that wrote a variable, as the ENGINE records it (#452).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Auton8's own execution log carries each variable update's
    /// <c>activityInstanceId</c>, which is an instance id rather than an
    /// activity id. Flowable's historic activity instances resolve one to the
    /// other, and this suite already talks to the engine directly in
    /// <c>CallActivityExecutionTests</c>, <c>ComplexGatewayExecutionTests</c>
    /// and <c>BehaviorErrorBoundaryExecutionTests</c> -- reading the engine is
    /// established here; it is PUBLISHING around Auton8's validation that this
    /// class refuses to do.
    /// </para>
    /// <para>
    /// Null when nothing wrote it, which includes the case that made #452
    /// serious: a value supplied by the start request, belonging to no activity
    /// at all.
    /// </para>
    /// </remarks>
    private readonly record struct VariableWrite(string Activity, string? Value);

    private static async Task<VariableWrite?> VariableWriterAsync(
        IAPIRequestContext api, string instance, string variable)
    {
        // SINGLE ATTEMPT. `ObserveAsync` already retries the whole observation
        // twenty times; retrying again in here multiplied the two loops, and a
        // failing cell took 100 seconds instead of five -- measured, as an
        // eight-minute run of a suite that takes sixteen seconds.
        string? writerInstance = null;
        string? writtenValue = null;

        var response = await api.GetAsync($"/api/executions/{instance}/log");
        if (response.Ok)
        {
            using var body = JsonDocument.Parse(await response.TextAsync());
            if (body.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var entry in body.RootElement.EnumerateArray())
                {
                    if (!entry.TryGetProperty("variableUpdate", out var update)) continue;
                    if (update.ValueKind != JsonValueKind.Object) continue;
                    if (!update.TryGetProperty("name", out var name)) continue;
                    if (name.GetString() != variable) continue;
                    if (!update.TryGetProperty("activityInstanceId", out var owner)) continue;

                    writerInstance = owner.GetString();
                    if (writerInstance is not null) break;
                }
            }
        }

        if (writerInstance is null) return null;

        using var engine = Support.FlowableDeploymentSweep.CreateClient(
            Environment.GetEnvironmentVariable("AUTONATE_FLOWABLE_URL") ?? "http://localhost:8080/flowable-rest",
            Environment.GetEnvironmentVariable("AUTONATE_FLOWABLE_USER") ?? "rest-admin",
            Environment.GetEnvironmentVariable("AUTONATE_FLOWABLE_PASSWORD") ?? "test");

        var activities = await engine.GetAsync(
            $"service/history/historic-activity-instances?processInstanceId={Uri.EscapeDataString(instance)}&size=500");

        if (!activities.IsSuccessStatusCode) return null;

        using var page = JsonDocument.Parse(await activities.Content.ReadAsStringAsync());
        if (!page.RootElement.TryGetProperty("data", out var rows)) return null;

        foreach (var row in rows.EnumerateArray())
        {
            if (!row.TryGetProperty("id", out var id)) continue;
            if (id.GetString() != writerInstance) continue;

            var activityId = row.TryGetProperty("activityId", out var activity)
                ? activity.GetString()
                : null;

            return activityId is null ? null : new VariableWrite(activityId, writtenValue);
        }

        return null;
    }

    /// <summary>When each activity was first entered, by the engine's own clock.</summary>
    private static async Task<IReadOnlyDictionary<string, DateTimeOffset>> EntryOrderAsync(
        IAPIRequestContext api, string instance)
    {
        var order = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);

        var response = await api.GetAsync($"/api/executions/{instance}/history");
        if (!response.Ok) return order;

        using var body = JsonDocument.Parse(await response.TextAsync());
        if (body.RootElement.ValueKind != JsonValueKind.Array) return order;

        foreach (var row in body.RootElement.EnumerateArray())
        {
            if (!row.TryGetProperty("activityId", out var id)) continue;

            // `startedAtUtc`, which is what WorkflowExecutionHistoryEvent calls
            // it -- not Flowable's own `startTime`. Reading the engine's name
            // through Auton8's route found nothing, and the cells went red
            // rather than green, because the field is required not defaulted.
            if (!row.TryGetProperty("startedAtUtc", out var start)) continue;
            if (!start.TryGetDateTimeOffset(out var at)) continue;

            var key = id.GetString();
            if (key is null) continue;
            if (!order.TryGetValue(key, out var seen) || at < seen) order[key] = at;
        }

        return order;
    }

    // ---- reading the diagram -------------------------------------------
    //
    // ALL OF THIS PARSES XML AS XML (#448). Every helper here used to be a
    // regex over the document text, and every one of them was wrong in the same
    // way: `<[A-Za-z]+` cannot see a namespace-prefixed tag, and bpmn.io and
    // Camunda write `<bpmn:signalEventDefinition/>` in every file they produce.
    // Measured: #435's headline mutation went green again simply by writing the
    // element with a prefix, and the deployed model read back from the engine
    // confirmed the element really was a signal throw. The same blindness
    // disarmed the writer check in #444, and `ElementMarkupIn` additionally
    // truncated at the first matching close tag, so containment was never
    // actually computed.

    // The pure diagram reads moved to Support/BpmnDiagram.cs (#474). They need no
    // engine, and while they lived here -- on a Flowable-traited class -- every
    // XML-misreading fix they encode was unguarded on GitHub. ReachableFrom went
    // with them only in the sense that it was deleted: it had no call site.

    private readonly record struct Observation(bool Held, string Detail);

    /// <summary>
    /// Effects whose observation waits on a job the ENGINE schedules (#522).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every other effect here is the synchronous consequence of starting an
    /// instance: the task is created, the variable is written, the instance ends,
    /// all within the transaction. Five seconds is generous for those, and #452
    /// deliberately bought that budget down -- a failing cell used to take 100
    /// seconds and an eight-minute run of a sixteen-second suite.
    /// </para>
    /// <para>
    /// A boundary event's firing is not synchronous. Flowable acquires the timer
    /// job on its own schedule, and MEASURED against the live engine the
    /// interrupting control exhausted the five-second budget with the boundary
    /// still parked -- reported, correctly and uselessly, as "that is a
    /// NON-INTERRUPTING boundary". The observer was right about what it saw and
    /// wrong about what it meant.
    /// </para>
    /// <para>
    /// So the longer budget is keyed on the EFFECT rather than raised for
    /// everyone. The negative control gets it too, and that is the point rather
    /// than a side effect: a control that waits less than the claim it guards
    /// reports "did not happen" by being impatient, which is the same false pass
    /// in the other direction.
    /// </para>
    /// </remarks>
    private static readonly HashSet<string> TriggerDriven =
        new(StringComparer.Ordinal) { "host-cancelled" };

    private static async Task<Observation> ObserveAsync(
        IAPIRequestContext api, string instance, string effect, string elementType,
        string xml)
    {
        // 250ms a turn either way: 5s for the synchronous effects, the
        // trigger-driven budget for the ones waiting on the engine's own clock.
        var attempts = TriggerDriven.Contains(effect) ? SelfStartBudgetSeconds * 4 : 20;

        for (var attempt = 0; attempt < attempts; attempt++)
        {
            var seen = await LookAsync(api, instance, effect, elementType, xml);
            if (seen.Held) return seen;
            await Task.Delay(250);
        }

        return await LookAsync(api, instance, effect, elementType, xml);
    }

    /// <summary>
    /// Where Flowable's runtime `activityType` is not the BPMN tag name.
    /// </summary>
    /// <remarks>
    /// <para>
    /// MEASURED, not assumed: every entry was discovered by this class failing
    /// against the real engine.
    /// </para>
    /// <para>
    /// Keyed on <c>(localName, eventDefinition)</c> -- the same pair
    /// `bpmn-support.json` keys every row on, and for the same reason (#435).
    /// Keyed on the tag alone this map was WRONG, not merely coarse: a message
    /// throw reports `serviceTask` while a signal throw reports `throwEvent`, so
    /// one entry for `intermediateThrowEvent` would have licensed a message
    /// throw to appear as anything a signal throw may appear as. The bug #435
    /// named, inside the table that was supposed to help fix it.
    /// </para>
    /// <para>
    /// The comparison is ORDINAL (#438): Ad-Hoc Sub-Process declares
    /// <c>adHocSubProcess</c> and the engine reports <c>adhocSubProcess</c>, and
    /// a case-insensitive compare swallowed that divergence while the comment
    /// beside it claimed a third would fail loudly.
    /// </para>
    /// </remarks>
    /// <summary>
    /// Elements the engine never records as an activity instance at all (#531).
    /// </summary>
    /// <remarks>
    /// <para>
    /// MEASURED, like <see cref="EngineNames"/>, and for the same reason: every
    /// entry was discovered by a cell failing against the real engine. A
    /// compensation boundary is never entered — not when compensation fires and
    /// not when it does not. Flowable records the HANDLER running; the attachment
    /// leaves no activity instance behind.
    /// </para>
    /// <para>
    /// <b>What this costs, stated rather than implied.</b> #412's entry check is
    /// what stops a same-id stand-in satisfying a cell, and these rows do not get
    /// it. What replaces it is the deployed-form check, which is not weaker here
    /// by accident: the element read back from the engine must still be the
    /// declared tag carrying the declared event definition, so a stand-in would
    /// have to BE a compensation boundary. The effect then has to be produced by
    /// the activity that element points at. A row in this set trades one
    /// independent check for a chain of two, and it is in this set only because
    /// the engine gives it no third option.
    /// </para>
    /// </remarks>
    private static readonly HashSet<(string Local, string? Definition)> NeverEntered =
    [
        ("boundaryEvent", "compensate"),

        // A data object reference is a DECLARATION, not a step (#534). It is
        // never on any path, so the engine has no activity instance to record --
        // which is why this row's effect is a value on the instance rather than
        // anything an activity did.
        ("dataObjectReference", null),
        // #169. A participant is the boundary drawn around a process. It deploys
        // as a definition and its CONTENTS run; the engine records no activity
        // instance for the pool itself, so the proof is the task its process
        // creates.
        ("participant", null),
        // #170. A message flow is a connection between pools, not a step in
        // either; the engine records nothing for it. The proof is the send at
        // its source running to the end of a linear pool.
        ("messageFlow", null),
        // #171. A lane partitions a pool; the engine records nothing for it. The
        // proof is the task it lists.
        ("lane", null),
    ];

    /// <summary>
    /// The behaviour the Service Task row proves, and what it writes (#535).
    /// </summary>
    /// <remarks>
    /// <para>
    /// MEASURED and pinned HERE rather than read from the diagram, and that is
    /// the whole point. A behaviour chooses its own result variable, so an
    /// observer that asked the diagram which behaviour it used and then looked
    /// for THAT behaviour's variable would be deriving its expectation from the
    /// thing under test — #412's founding defect — and the negative control would
    /// pass for the wrong reason, because `send-message` does write its own
    /// result variable.
    /// </para>
    /// <para>
    /// `autonate.unlock-account` is registered UNCONDITIONALLY
    /// (<c>Program.cs:881</c>), unlike `always-declines` and
    /// `always-fails-undeclared`, which are Development-only. A row proving the
    /// running app serves a behaviour should not rest on a registration that an
    /// environment flag can remove.
    /// </para>
    /// </remarks>
    private const string ProvenBehaviorKey = "autonate.unlock-account";

    private const string ProvenBehaviorResult = "unlockResult";

    private static readonly Dictionary<(string Local, string? Definition), string> EngineNames = new()
    {
        [("intermediateThrowEvent", null)] = "throwEvent",
        [("intermediateThrowEvent", "signal")] = "throwEvent",
        [("intermediateThrowEvent", "escalation")] = "throwEvent",
        [("intermediateThrowEvent", "compensate")] = "throwEvent",
        [("intermediateThrowEvent", "message")] = "serviceTask",
        [("endEvent", "signal")] = "throwEvent",
        [("endEvent", "compensate")] = "throwEvent",
        [("endEvent", "message")] = "serviceTask",
        [("sendTask", null)] = "serviceTask",
        [("eventBasedGateway", null)] = "eventGateway",
        [("adHocSubProcess", null)] = "adhocSubProcess",

        // #532, measured the same way as every other row here: the cell failed
        // with "ran, but as a 'eventSubProcess' rather than a 'subProcess'". The
        // key is the manifest's own (localName, eventDefinition) pair, and for
        // this row the second half is an ATTRIBUTE -- which the keying already
        // supports, because it is a pair of strings rather than a pair of shapes.
        [("subProcess", "triggeredByEvent")] = "eventSubProcess",

        // #533, and NOT the value the story predicted -- which is exactly why its
        // AC asked for this to be measured against the EXPANDED deployment rather
        // than recalled. Flowable 8.0.0 has no ComplexGatewayActivityBehavior, so
        // publish expands the element, and MEASURED the engine records the
        // author's `Ev_1` as a scriptTask: the cell failed with "ran, but as a
        // 'scriptTask' rather than a 'exclusiveGateway'" and passes with this.
        //
        // `ComplexGatewayExecutionTests`' "recorded as activityType
        // exclusiveGateway" is a true statement about an IMPORTED complexGateway
        // that Auton8 never expanded. That is the finding which motivated the
        // expansion, not a description of what the expansion produces -- and
        // taking it for the latter is what predicted the wrong value here.
        [("complexGateway", null)] = "scriptTask",

        // #111, measured the same way: the cell failed with "ran, but as a
        // 'serviceTask' rather than a 'businessRuleTask'". Publish expands the
        // element into a DMN service task because BusinessRuleParseHandler
        // resolves a KIE/Drools behaviour at DEPLOY time and KIE is not in the
        // image, so `businessRuleTask` is a name the engine never records here.
        // Being in this table is what makes `serviceTask` the ONLY acceptable
        // answer for this row (#446) -- a raw businessRuleTask reaching the
        // engine would now fail the cell rather than pass it as an alternative.
        [("businessRuleTask", null)] = "serviceTask",
    };


    private static async Task<Observation> LookAsync(
        IAPIRequestContext api, string instance, string effect, string elementType,
        string xml)
    {
        // Everything comes off /diagram, which is the route the rest of this suite
        // already reads. The first version of this class invented
        // `GET /api/executions/{id}` -- a route that does not exist -- and read
        // the failure as "the instance ran straight through", turning seven
        // healthy elements into seven false negatives. A query that returns
        // nothing reading like a verdict is the exact failure #412 is about, and
        // it happened here inside the fix for it.
        var fansTo = FlowTargetsOf(xml, "Ev_1");

        var response = await api.GetAsync($"/api/executions/{instance}/diagram");
        if (!response.Ok) return new(false, $"diagram: {response.Status}");

        using var document = JsonDocument.Parse(await response.TextAsync());
        var root = document.RootElement;

        var present = root.TryGetProperty("currentActivityIds", out var ids)
            && ids.ValueKind == JsonValueKind.Array;
        var current = present
            ? ids.EnumerateArray().Select(i => i.GetString() ?? "").ToList()
            : [];

        switch (effect)
        {
            // THE ARCHETYPE (#471, #325). "It ran exactly once where the author
            // asked for three" is a comparison between the cardinality the AUTHOR
            // wrote and what the ENGINE did. Reading the number from the diagram
            // is not deriving the expectation from the thing under test: the
            // diagram supplies the author's intent, the engine supplies the
            // behaviour, and the whole bug is that they disagreed.
            case "tasks-appear-together":
            {
                var wanted = LoopCardinalityIn(xml);
                if (wanted is null) return new(false, "this diagram declares no loopCardinality");

                // A FLOOR, matching the sequential arm below (#488). Without it
                // the diagram can lower the bar to the exact value the founding
                // defect produced: with `loopCardinality="1"`, one task satisfies
                // `mine.Count == wanted` and the cell reports "1 live task(s) on
                // Ev_1 at once, as authored" for a marker that multiplied
                // nothing. That is #325's own sentence, greened by a one-token
                // edit to the thing under test.
                if (wanted < 2) return new(false, $"loopCardinality is {wanted}; nothing to run at once");

                var mine = (await TasksAsync(api, instance)).Where(t => t.Owner == "Ev_1").ToList();

                // EXACTLY the number asked for. `> 1` would accept two where
                // three were requested, which is the same class of defect as one
                // where three were requested -- just harder to notice.
                return mine.Count == wanted
                    ? new(true, $"{mine.Count} live task(s) on Ev_1 at once, as authored")
                    : new(false,
                        $"the author asked for {wanted} instances and the engine has "
                        + $"{mine.Count} live task(s) on Ev_1. This is #325 exactly: a marker "
                        + "that deploys and then does not multiply.");
            }

            // Sequential is NOT "one task exists" -- that is true of a plain user
            // task with no marker at all, so an oracle that accepted it would be
            // green on the absence of the very thing it is meant to prove. What
            // distinguishes iteration is that completing the one task produces
            // ANOTHER on the same activity.
            case "tasks-appear-in-turn":
            {
                var wanted = LoopCardinalityIn(xml);
                if (wanted is null) return new(false, "this diagram declares no loopCardinality");
                if (wanted < 2) return new(false, $"loopCardinality is {wanted}; nothing to take turns");

                var first = (await TasksAsync(api, instance)).Where(t => t.Owner == "Ev_1").ToList();
                if (first.Count == 0) return new(false, "no task on Ev_1 yet");

                if (first.Count != 1)
                {
                    return new(false,
                        $"{first.Count} live tasks on Ev_1 at once. A sequential marker runs its "
                        + "instances one at a time; this is what a PARALLEL one looks like.");
                }

                var completed = await api.PostAsync($"/api/tasks/{first[0].Id}/complete",
                    new() { DataObject = new Dictionary<string, object>() });

                if (!completed.Ok)
                {
                    // WITH THE BODY. A bare status is undiagnosable -- this cell
                    // reported "could not complete the first task: 500" and the
                    // reason lived only in a log nobody keeps.
                    return new(false,
                        $"could not complete the first task: {completed.Status} "
                        + $"{await completed.TextAsync()}");
                }

                for (var attempt = 0; attempt < 20; attempt++)
                {
                    var next = (await TasksAsync(api, instance)).Where(t => t.Owner == "Ev_1").ToList();

                    // A DIFFERENT task, not the same one read again -- otherwise a
                    // stale read passes for a second iteration.
                    if (next.Count == 1 && next[0].Id != first[0].Id)
                    {
                        return new(true,
                            $"one task at a time: {first[0].Id} completed, then {next[0].Id} appeared");
                    }

                    if (next.Count > 1)
                    {
                        return new(false, $"{next.Count} tasks on Ev_1 after completing one");
                    }

                    await Task.Delay(250);
                }

                return new(false,
                    $"completing the only task on Ev_1 produced no second one, so the marker ran "
                    + $"once where the author asked for {wanted}. That is #325.");
            }

            case "task-appears":
            {
                var here = await TasksAsync(api, instance);
                var mine = here.Where(t => t.Owner == "Ev_1").ToList();
                if (mine.Count > 0) return new(true, $"{mine.Count} task(s) on Ev_1 itself");

                // A CONTAINER's task belongs to an activity INSIDE it, and the
                // ids of those activities come from this diagram (#434). The
                // previous version accepted ANY task on the instance for any of
                // four container tag names -- both container cells took that path
                // even unmutated, and its message asserted a containment the code
                // never checked. Emptying the container, or moving the task out of
                // it, now fails: the id is no longer in the nested set.
                var nested = NestedIdsIn(xml, "Ev_1");
                var inside = here.Where(t => nested.Contains(t.Owner)).ToList();
                if (inside.Count > 0)
                {
                    return new(true,
                        $"{inside.Count} task(s) inside Ev_1 [{string.Join(", ", inside.Select(t => t.Owner))}]");
                }

                // A CALL ACTIVITY's task belongs to the CALLED instance, so a query
                // on this one returns zero -- which reads exactly like "the element
                // did nothing". CALL ACTIVITIES ONLY (#445): this ran for every
                // element type and accepted a task in ANY child instance, because
                // the children route asks for `superProcessInstanceId = parent`.
                // Measured, a sub-process with no inner task passed on an unrelated
                // call activity's task -- with a message asserting a containment
                // the code never checked, which is the sentence this fallback's
                // predecessor was replaced for.
                if (!string.Equals(elementType, "callActivity", StringComparison.Ordinal))
                {
                    return new(false,
                        here.Count > 0
                            ? $"{here.Count} task(s) exist [{string.Join(", ", here.Select(t => t.Owner))}], "
                              + $"but none belongs to Ev_1 or anything inside it [{string.Join(", ", nested)}]"
                            : "no task on this instance");
                }

                // ATTRIBUTION, given that the route cannot provide it.
                // `/children` returns `superProcessInstanceId = <parent>` -- every
                // call activity on the instance -- and the summary carries no
                // calling-activity id, so a child cannot be traced to Ev_1 from
                // the payload. Rather than filter on a field that does not exist
                // (which would accept everything while looking like a check), the
                // requirement is moved somewhere it can actually be tested: Ev_1
                // must be the diagram's ONLY call activity, and then any child is
                // necessarily its. A second call activity makes this cell
                // unattributable, and it says so instead of guessing.
                var callActivities = CallActivityIdsIn(xml);
                if (callActivities.Count != 1 || !callActivities.Contains("Ev_1"))
                {
                    return new(false,
                        $"no task belongs to Ev_1, and this diagram has {callActivities.Count} call "
                        + $"activities [{string.Join(", ", callActivities)}], so a task in a child "
                        + "instance cannot be attributed to Ev_1 (#445)");
                }

                var children = await api.GetAsync($"/api/executions/{instance}/children");
                if (!children.Ok) return new(false, $"0 here; children: {children.Status}");

                using var body = JsonDocument.Parse(await children.TextAsync());
                if (body.RootElement.ValueKind != JsonValueKind.Array) return new(false, "0 task(s)");

                foreach (var child in body.RootElement.EnumerateArray())
                {
                    if (!child.TryGetProperty("id", out var id)) continue;
                    var inChild = await TasksAsync(api, id.GetString()!);
                    if (inChild.Count > 0) return new(true, $"{inChild.Count} task(s) in Ev_1's called instance");
                }

                return new(false,
                    here.Count > 0
                        ? $"{here.Count} task(s) exist [{string.Join(", ", here.Select(t => t.Owner))}], "
                          + "but none belongs to Ev_1, anything inside it, or its called instance"
                        : "no task on this instance or in Ev_1's called instance");
            }

            case "variable-written":
            {
                // WHO WROTE IT, ACCORDING TO THE ENGINE (#452).
                //
                // Two previous attempts asked the DIAGRAM. The first took the
                // first `<scriptTask` in the document; the second took the one
                // whose script text contains `proof`. Both are heuristics over
                // non-evidence, and verification broke the second with two
                // mutations -- a decoy that merely mentions `proof` while the
                // real write says `'pro'+'of'`, and a `proof` supplied by the
                // start request with no element writing it at all. All four
                // cells were green with nothing writing the variable.
                //
                // The engine records the activity instance that performed each
                // variable update. That is the attribution; nothing in the
                // diagram is.
                var write = await VariableWriterAsync(api, instance, "proof");

                if (write is null)
                {
                    return new(false,
                        "no activity in this instance wrote `proof` -- if the variable is set, "
                        + "something other than an element in this diagram set it (#452)");
                }

                // ONE HOP FROM Ev_1, NOT TRANSITIVE REACHABILITY (#452).
                //
                // The previous version accepted any activity reachable from Ev_1
                // by sequence flow. Reachability in the AUTHORED graph is not
                // execution order: measured, a single back edge from a node that
                // never fires into an upstream node put that upstream node in the
                // accepted set, and an upstream listener's write passed while the
                // cell printed "'T_0' ... is Ev_1 or downstream of it".
                //
                // A direct target cannot be reached that way: `Ev_1` flows to it,
                // full stop. Every shipped diagram writes `proof` either on Ev_1
                // itself (Script Task) or on a task Ev_1 flows straight to.
                var wrote = write.Value.Activity;

                var writers = new HashSet<string>(FlowTargetsOf(xml, "Ev_1"), StringComparer.Ordinal)
                {
                    "Ev_1",
                };

                // A COMPENSATION BOUNDARY'S OUTGOING EDGE IS AN ASSOCIATION (#531).
                //
                // Not a widening of "one hop from Ev_1" -- the same rule, applied
                // to the edge this element actually has. A compensation handler is
                // attached by <association>, so `FlowTargetsOf` is empty for one
                // and the handler's write would be rejected as somebody else's.
                // Only the association whose sourceRef is Ev_1 qualifies; "any
                // association in the diagram" would admit a write from wherever an
                // artifact happened to point.
                writers.UnionWith(CompensationHandlersOf(xml, "Ev_1"));

                if (!writers.Contains(wrote))
                {
                    return new(false,
                        $"`proof` was written by activity '{wrote}', which is neither Ev_1 nor a "
                        + $"target Ev_1 flows directly to [{string.Join(", ", writers.Where(w => w != "Ev_1"))}] "
                        + "-- so the variable is not this element's doing (#452)");
                }

                // AND, WHERE Ev_1 ROUTES, THE OTHER BRANCH MUST NOT HAVE RUN.
                //
                // This is the gateway rows' actual claim, and it was the half
                // that kept being defeated. A listener on Ev_1 satisfied "the
                // write is on Ev_1" while the engine routed AWAY from the script,
                // so those cells carried no information about routing at all --
                // which is the only thing a gateway does.
                //
                // Asserting the complement fixes that without needing to know who
                // wrote the variable: if the untaken branch ran, the gateway did
                // not route the way this cell claims.
                // WHERE THE DIAGRAM PUTS CONDITIONS ON THE BRANCHES.
                //
                // Not "where the gateway is exclusive": that is the element's
                // type, and the question is what this diagram asked it to do. A
                // parallel gateway forks to every target by definition and its
                // flows carry no conditions -- measured, applying the complement
                // by count alone turned that cell red, correctly reporting that
                // both branches ran. An INCLUSIVE gateway may fork too, but here
                // its flows carry mutually exclusive conditions, so exactly one
                // should be taken and the complement is a real claim about it.
                //
                // Reading the conditions says which case this is; reading the tag
                // does not, and scoping by tag left the inclusive row defeated by
                // the same listener attack the exclusive row now catches.
                var targets = FlowTargetsOf(xml, "Ev_1");
                // OR A ROUTING SCRIPT (#533). The gate asks whether this diagram
                // asked the element to take ONE branch. A complex gateway's
                // authored flows carry no conditions -- publish adds them -- so
                // the author's instruction is the gateway's own <script>.
                //
                // WHAT IT GUARDS HERE IS FORKING, and that is narrower than the
                // listener attack it guards on the other three gateways. Measured
                // while checking: publish gives the generated routing task its own
                // id (`Ev_1__autonateRoute`), so a write from inside the routing
                // script is already rejected by the ATTRIBUTION check above, one
                // step earlier -- with or without this clause. What is left for
                // the complement is a complex gateway that takes BOTH branches
                // rather than choosing, which is a real failure mode for this
                // element (an unexpanded complexGateway silently picks a branch;
                // a broken expansion could fork) but is not reachable by mutating
                // the diagram, only by breaking publish. So it is kept and
                // disclosed rather than claimed as exercised.
                if (targets.Count > 1
                    && (ConditionalFlowsFrom(xml, "Ev_1") || RoutesByScript(xml, "Ev_1")))
                {
                    var entered = await EntryOrderAsync(api, instance);
                    var alsoRan = targets.Where(t => t != wrote && entered.ContainsKey(t)).ToList();

                    if (alsoRan.Count > 0)
                    {
                        return new(false,
                            $"`proof` was written by '{wrote}', but Ev_1's other branch(es) "
                            + $"[{string.Join(", ", alsoRan)}] also ran, so this cell says nothing "
                            + "about how the gateway routed (#452)");
                    }
                }

                // NO VALUE CHECK, AND THAT IS A CORRECTION (#452).
                //
                // The previous version read the value from a second query --
                // `/diagram`, which returns the LATEST value per name -- while
                // the attribution came from the EARLIEST update, so neither half
                // of the sentence it printed need be jointly true. The fix was
                // to take both from one record. Measured, that record does not
                // carry one: Flowable's historic-detail leaves the value null on
                // these updates, so the check compared against nothing and four
                // cells went red saying `its value is ''`.
                //
                // Rather than reinstate a second source to keep a check alive,
                // the check is gone. What replaced it is stronger where it
                // mattered: the routing complement above is the gateway rows'
                // actual claim, and it was those rows the listener attack
                // defeated.
                //
                // WHAT REMAINS UNCAUGHT, stated rather than implied: on the
                // Script Task row, an execution listener on Ev_1 writing `proof`
                // passes. Both are this element's behaviour and nothing outside
                // can separate them. The gateway rows no longer share that
                // weakness, which is where the previous disclosure was wrong to
                // imply the defence transferred.
                return new(true, $"`proof` written by '{wrote}'");
            }

            case "instance-waits":
            {
                // THE ELEMENT MUST BE WHAT IS HOLDING THE INSTANCE (#434).
                //
                // The previous version accepted `Ev_1` OR anything `Ev_1` fans
                // to, for all seven cells. On a linear diagram that is the whole
                // rest of the process: an inert, correctly-typed catch event that
                // fires straight through passed, with a downstream USER TASK
                // doing the waiting (measured: `parked at [End_1]`).
                //
                // Only an event-based gateway genuinely waits somewhere other
                // than on itself -- the engine parks at the catches it fans to,
                // measured as `[C_1, C_2]`. So the allowance is granted to that
                // one element type instead of to all of them, and it requires
                // EVERY current activity to be one of its own targets, and more
                // than one of them, which is what an event gateway is for.
                if (current.Contains("Ev_1"))
                {
                    return new(true, $"parked on Ev_1 itself [{string.Join(", ", current)}]");
                }

                if (string.Equals(elementType, "eventBasedGateway", StringComparison.Ordinal))
                {
                    var allMine = current.Count > 1 && current.All(fansTo.Contains);
                    return new(allMine,
                        allMine
                            ? $"parked at the gateway's own targets [{string.Join(", ", current)}]"
                            : $"parked at [{string.Join(", ", current)}], which is not "
                              + $"{(current.Count > 1 ? "all" : "more than one")} of the "
                              + $"gateway's targets [{string.Join(", ", fansTo)}]");
                }

                return new(false,
                    current.Count == 0
                        ? "nothing is current -- the instance ran straight through"
                        : $"parked at [{string.Join(", ", current)}], not on Ev_1. Something "
                          + "else is holding this instance (#434).");
            }

            case "instance-ends":
                // The PROPERTY MUST BE PRESENT (#412). This returned true whenever
                // `currentActivityIds` was absent, so renaming the field the
                // observer reads left every instance-ends cell green -- "a query
                // returning nothing reading like a verdict", for the fourth time
                // in this milestone.
                if (!present)
                {
                    return new(false,
                        "the diagram payload carried no `currentActivityIds` at all, so "
                        + "nothing here can say whether the instance finished (#412)");
                }

                return new(current.Count == 0,
                    current.Count == 0
                        ? "nothing is current -- the instance finished"
                        : $"still parked at [{string.Join(", ", current)}]");

            // AN INSTANCE CAME INTO BEING, AND IT BEGAN HERE (#522).
            //
            // "An instance exists" alone is not an observation -- by the time
            // this runs, one does, or the cell already failed finding it. What
            // this adds is WHERE it began: the trigger's own event has to be the
            // entry point, so an instance that arrived some other way and merely
            // passed through `Ev_1` later does not satisfy it.
            //
            // The other half of the claim -- that nobody called start -- cannot
            // be seen from here, because the instance carries the answer, not the
            // diagram. It is established in `SelfStartAsync`: for an UNTRIGGERED
            // row by asserting the instance carries no `startUserId`, and for a
            // triggered one by the pair of emptiness checks either side of
            // publish, since an authenticated trigger legitimately records a
            // start user. This comment used to claim the first of those for every
            // row, which stopped being true at #529 (#550).
            case SelfStarting:
            {
                var order = await EntryOrderAsync(api, instance);
                if (order.Count == 0)
                {
                    return new(false,
                        "the engine has recorded no activity at all on this instance, so nothing "
                        + "here can say where it began");
                }

                if (!order.TryGetValue("Ev_1", out var mine))
                {
                    return new(false,
                        $"this instance entered [{string.Join(", ", order.Keys)}] and never Ev_1, "
                        + "so whatever started it, it was not this element");
                }

                // STRICTLY earlier, not "is the minimum". Two activities can
                // share a millisecond on the engine's clock, and a tie would make
                // this flaky rather than wrong. What it must reject is an
                // activity that demonstrably ran BEFORE the start event, which is
                // what "something else started this instance" looks like.
                var earlier = order.Where(kv => kv.Value < mine).Select(kv => kv.Key).ToList();

                return earlier.Count == 0
                    ? new(true, $"the instance began at Ev_1, with nobody calling start")
                    : new(false,
                        $"[{string.Join(", ", earlier)}] ran before Ev_1 on this instance, so it "
                        + "did not begin here -- this element is not what created it");
            }

            // THE PATH RAN *AND* THE HOST IS GONE (#522).
            //
            // Both halves, in one observer, because either alone is the opposite
            // feature. A non-interrupting boundary runs its path and leaves the
            // host alive; a host that vanished for some other reason says nothing
            // about the boundary. The host comes from the DIAGRAM's
            // `attachedToRef` -- the author's statement of what this boundary
            // interrupts -- and whether it is still live comes from the engine.
            case "host-cancelled":
            {
                var host = AttachedHostOf(xml, "Ev_1");
                if (host is null)
                {
                    return new(false,
                        "Ev_1 is not a boundary event in this diagram, so it is attached to "
                        + "nothing and there is no host for it to have cancelled");
                }

                var order = await EntryOrderAsync(api, instance);

                if (!order.ContainsKey(host))
                {
                    return new(false,
                        $"the host '{host}' never ran, so there was nothing for Ev_1 to cancel -- "
                        + "an absent host is not a cancelled one");
                }

                if (current.Contains(host))
                {
                    // NOT "Ev_1 fired and the host is still live" (#529). That
                    // was the wording, and it asserts something this observer
                    // cannot see: a boundary event appears in `current` as a
                    // REGISTERED subscription as well as after firing, so both
                    // "it fired and did not interrupt" and "it never fired at
                    // all" arrive here. Measured while mutating a signal boundary
                    // to listen for a name nothing throws -- the verdict was
                    // right and the sentence was not. A message that claims more
                    // than the code checked is how a reader is sent to the wrong
                    // place, which this suite has paid for before (#445).
                    return new(false,
                        $"the host '{host}' is still live [{string.Join(", ", current)}], so Ev_1 "
                        + "did not interrupt it. Either the boundary fired and is NON-INTERRUPTING "
                        + "-- the opposite feature, not a near miss -- or it never fired at all; "
                        + "this observer cannot tell those apart, because a boundary is `current` "
                        + "while merely subscribed.");
                }

                // And the boundary's own path. Without this, a host that ended
                // normally -- completed by anyone, at any time -- reads as a
                // cancellation, and the boundary need never have fired at all.
                var onward = FlowTargetsOf(xml, "Ev_1");
                var ran = onward.Where(order.ContainsKey).ToList();

                if (ran.Count == 0)
                {
                    return new(false,
                        $"the host '{host}' is gone, but nothing Ev_1 flows to "
                        + $"[{string.Join(", ", onward)}] ever ran. The host ended on its own; this "
                        + "boundary did not interrupt it.");
                }

                return new(true,
                    $"Ev_1 fired, its path ran [{string.Join(", ", ran)}], and the host '{host}' "
                    + "is gone");
            }

            // THE DECLARATION'S VALUE REACHED THE INSTANCE (#534).
            //
            // `variable-written` cannot express this, and bending it to would
            // weaken the thing that makes it strong: it attributes a write to an
            // ACTIVITY INSTANCE, and a data object's value is seeded by a
            // declaration, so no activity writes it.
            //
            // The diagram supplies the author's intent -- which declaration this
            // reference resolves to, and what value it declares -- and the engine
            // supplies the behaviour. Neither derives the other (#412).
            case "value-carried":
            {
                var (declaredName, declaredValue) = DataObjectDeclarationOf(xml, "Ev_1");

                if (declaredName is null)
                {
                    return new(false,
                        "Ev_1 is not a data object reference that resolves to a declaration, so "
                        + "there is nothing for it to carry");
                }

                if (declaredValue is null)
                {
                    return new(false,
                        $"the declaration '{declaredName}' declares no value, so a variable "
                        + "carrying one would not be this element's doing");
                }

                // AND NO ACTIVITY MAY HAVE WRITTEN IT. This is the half that
                // makes the cell mean something: a script task setting the same
                // name satisfies "the variable is there and equal" while the data
                // object carries nothing, which is this element's entire job.
                var writer = await VariableWriterAsync(api, instance, declaredName);
                if (writer is not null)
                {
                    return new(false,
                        $"'{declaredName}' was written by activity '{writer.Value.Activity}'. The "
                        + "value on this instance is that activity's doing, so nothing here says "
                        + "the data object reference carried anything (#534)");
                }

                var actual = await VariableValueAsync(api, instance, declaredName);

                if (actual is null)
                {
                    return new(false,
                        $"the declaration '{declaredName}' names no variable on this instance -- "
                        + "the reference resolved to it and its value never arrived");
                }

                return string.Equals(actual, declaredValue, StringComparison.Ordinal)
                    ? new(true, $"'{declaredName}' = '{actual}', from the declaration and no activity")
                    : new(false,
                        $"'{declaredName}' is '{actual}' on the instance and the declaration says "
                        + $"'{declaredValue}'");
            }

            // THE ENGINE CALLED BACK INTO THE RUNNING APP AND A BEHAVIOUR RAN (#535).
            //
            // The attribution is what makes this a proof rather than a variable
            // check: the engine records that `Ev_1` performed the update, which
            // is a chain nothing in the diagram can fake -- the job ran, the
            // callback reached the app, the app resolved the key, the behaviour
            // executed and reported an outcome.
            //
            // The expected variable comes from `ProvenBehaviorResult`, not from
            // the diagram's own behaviorKey. See that constant for why.
            case "behavior-ran":
            {
                var write = await VariableWriterAsync(api, instance, ProvenBehaviorResult);

                if (write is null)
                {
                    return new(false,
                        $"no activity in this instance wrote `{ProvenBehaviorResult}`. The element "
                        + $"deployed, and '{ProvenBehaviorKey}' either never ran or never reported "
                        + "-- which is the same thing from outside (#535)");
                }

                if (!string.Equals(write.Value.Activity, "Ev_1", StringComparison.Ordinal))
                {
                    return new(false,
                        $"`{ProvenBehaviorResult}` was written by activity "
                        + $"'{write.Value.Activity}', not Ev_1, so it is not this element's doing");
                }

                var outcome = await VariableValueAsync(api, instance, ProvenBehaviorResult);

                return string.IsNullOrEmpty(outcome)
                    ? new(false,
                        $"Ev_1 wrote `{ProvenBehaviorResult}` and it is empty, so the behaviour "
                        + "reported no outcome")
                    : new(true, $"Ev_1 ran '{ProvenBehaviorKey}', which reported '{outcome}'");
            }

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
        string Wrap(string roots, string body) => WrapIn(key, roots, body);


        string Linear(string element) => LinearIn(element);

        return name switch
        {
            "User Task" => Wrap("", Linear("""<userTask id="Ev_1" name="approve"/>""")),

            // THE ARCHETYPE (#471, #325). A marker on a host activity, not an
            // element with a tag -- which is why these three rows went undeclared
            // for the whole of M4b. `autonate:loopCardinality` is an ATTRIBUTE
            // here and Auton8 rewrites it into a <bpmn:loopCardinality> child at
            // publish; that rewrite is itself part of what these cells prove,
            // because a marker that survives the studio and dies at publish is
            // the founding bug wearing a later timestamp.
            "Multi-Instance (Parallel)" => Wrap("", Linear(
                """<userTask id="Ev_1" name="approve">"""
                + """<multiInstanceLoopCharacteristics isSequential="false" autonate:loopCardinality="3"/>"""
                + """</userTask>""")),
            "Multi-Instance (Sequential)" => Wrap("", Linear(
                """<userTask id="Ev_1" name="approve">"""
                + """<multiInstanceLoopCharacteristics isSequential="true" autonate:loopCardinality="3"/>"""
                + """</userTask>""")),

            // A compensation handler is only reachable through a throw, so this
            // diagram has to carry the whole apparatus: a completed activity, a
            // boundary compensate event, an association, and a thrower. Ev_1 is
            // the HANDLER -- the element the manifest row is about.
            "Compensation Marker" => Wrap("",
                """<startEvent id="Start_1"/>"""
                + """<scriptTask id="Doer_1" name="do" scriptFormat="javascript" autonate:runAs="workflowAuthor"><script>variables.set('did', 'yes');</script></scriptTask>"""
                + """<boundaryEvent id="Bnd_1" attachedToRef="Doer_1"><compensateEventDefinition/></boundaryEvent>"""
                + """<scriptTask id="Ev_1" name="undo" isForCompensation="true" scriptFormat="javascript" autonate:runAs="workflowAuthor"><script>variables.set('proof', 'ran');</script></scriptTask>"""
                + """<endEvent id="End_1"><compensateEventDefinition/></endEvent>"""
                + """<sequenceFlow id="f1" sourceRef="Start_1" targetRef="Doer_1"/>"""
                + """<sequenceFlow id="f2" sourceRef="Doer_1" targetRef="End_1"/>"""
                // LAST, and that is not style. <association> is an ARTIFACT, and
                // the BPMN schema puts artifacts after every flow element -- put
                // it earlier and Flowable refuses the deployment with
                // cvc-complex-type.2.4.a naming whichever element follows it.
                // Measured against the engine directly, because Auton8 answers
                // "the reason is in the server log" for a code it has no sentence
                // for, and the E2E fixture buffers that log in memory.
                + """<association id="Assoc_1" associationDirection="One" sourceRef="Bnd_1" targetRef="Ev_1"/>"""),
            // Ev_1 IS THE BOUNDARY, not the handler (#531). The `Compensation
            // Marker` row above builds the same apparatus with Ev_1 as the
            // handler; this row is about the attachment itself, so the ids swap
            // and the handler becomes an ordinary element. What proves the
            // boundary ran is that the thing it is associated with executed --
            // and its edge to that thing is an <association>, which is why the
            // `variable-written` observer had to learn about them.
            //
            // <association> LAST, as in the Compensation Marker arm: it is an
            // ARTIFACT, and the BPMN schema puts artifacts after every flow
            // element. Earlier, and Flowable refuses the deployment with
            // cvc-complex-type.2.4.a naming whichever element follows it.
            // AN ACTUAL dataObjectReference (#534). `DataObjectExecutionTests`
            // authors a bare <bpmn:dataObject>, which is the DECLARATION -- this
            // row is about the reference that resolves to one, and the two are
            // different elements. A user task keeps the instance alive so the
            // variable is still readable.
            // A timer inside the handler, non-interrupting, so the main flow
            // parks and the instance is still readable while the handler's task
            // is observed. `task-appears` is already container-aware -- it falls
            // back to the ids nested inside Ev_1 -- so the handler's own user task
            // is exactly the shape it looks for (#532).
            // `GatewayDiagram` cannot be reused: publish EXPANDS this element --
            // a script task in front, conditions synthesised onto its own
            // outgoing flows -- so the author writes a routing script returning a
            // FLOW ID, not conditions. The script sits on one branch and a user
            // task on the other, so `proof` can only be written if the gateway
            // routed (#533).
            // A BEHAVIOUR THE RUNNING APP SERVES (#535). Not a fixture's: the
            // row's old reason claimed `autonate.noop` was "registered by a test
            // fixture", and nothing registers it at all. `autonate.unlock-account`
            // is registered unconditionally by Program.cs.
            //
            // The start request supplies a `userId` that resolves to nobody, so
            // the behaviour reaches ILocalUserStore and reports `userNotFound` --
            // an outcome only a behaviour that really ran can produce.
            //
            // `delegateExpression`, `autonateServiceKind` and `async` are not
            // decoration: publish REFUSES a behaviour task carrying only a
            // behaviorKey ("has no behaviour chosen yet"), because the studio's
            // prepare step is what normally writes the delegate and this class
            // publishes the diagram an author would end up with. Measured -- the
            // first version omitted them and was refused with that sentence.
            // `BehaviorErrorBoundaryExecutionTests` authors the same four.
            "Service Task (Behavior)" => Wrap("", Linear(
                """<serviceTask id="Ev_1" name="unlock" flowable:delegateExpression="${autonateBehaviorDelegate}" """
                + """flowable:autonateServiceKind="behavior" """
                + $"""flowable:behaviorKey="{ProvenBehaviorKey}" """
                + """flowable:async="true"/>""")),

            // #231 DELIBERATELY LEAVES THIS ON THE SPLIT SHAPE, and says so here
            // because the AC requires the choice to be visible rather than
            // implied.
            //
            // Ev_1 has ONE incoming flow, so the expansion keeps #218's single
            // routing script and this cell goes on certifying exactly what it
            // always did: the element routes a token to the branch the script
            // chose. That claim did not change, and neither did the three pins.
            //
            // Moving it to the accumulating shape would BREAK the observer rather
            // than strengthen it. `variable-written` requires the writing activity
            // to be exactly one hop from Ev_1; N accumulators put a generated node
            // on every inbound edge, so a correct implementation would read as a
            // failure and the fix would be to loosen the one-hop rule -- weakening
            // the strictest thing about this oracle to accommodate a shape it was
            // never asked to prove.
            //
            // What proves the accumulating shape instead:
            // `ComplexGatewayExecutionTests` -- out-of-order arrivals firing the
            // join ONCE (measured: arrived='in2,in1', fired=True, route='fj'), a
            // later arrival absorbed without the script being consulted again, and
            // a withheld branch leaving the join waiting. Those are claims about
            // accumulation over time, which a single-token oracle cell cannot make
            // whatever shape it carries.
            "Complex Gateway" => Wrap("",
                """<startEvent id="Start_1"/>"""
                + """<complexGateway id="Ev_1" name="Choose" scriptFormat="javascript" autonate:runAs="workflowAuthor">"""
                + """<script>return 'fa';</script></complexGateway>"""
                + """<scriptTask id="S_1" name="proof" scriptFormat="javascript" autonate:runAs="workflowAuthor"><script>variables.set('proof', 'ran');</script></scriptTask>"""
                + """<userTask id="Other_1" name="other"/><endEvent id="End_1"/><endEvent id="End_2"/>"""
                + """<sequenceFlow id="f0" sourceRef="Start_1" targetRef="Ev_1"/>"""
                + """<sequenceFlow id="fa" sourceRef="Ev_1" targetRef="S_1"/>"""
                + """<sequenceFlow id="fb" sourceRef="Ev_1" targetRef="Other_1"/>"""
                + """<sequenceFlow id="f3" sourceRef="S_1" targetRef="End_1"/>"""
                + """<sequenceFlow id="f5" sourceRef="Other_1" targetRef="End_2"/>"""),

            // CONTROL: per-EFFECT (#546). This row rides `task-appears`'
            // control -- a plain <subProcess> whose only user task sits OUTSIDE
            // it. That is arguably the other configuration for this element (no
            // `triggeredByEvent`), but it is shared with three other rows, so it
            // is disclosed here the way the error boundary's fallback is.
            "Event Sub-Process" => Wrap("",
                """<startEvent id="Start_1"/><userTask id="Main_1" name="main"/><endEvent id="End_1"/>"""
                + """<sequenceFlow id="f1" sourceRef="Start_1" targetRef="Main_1"/>"""
                + """<sequenceFlow id="f2" sourceRef="Main_1" targetRef="End_1"/>"""
                + """<subProcess id="Ev_1" triggeredByEvent="true">"""
                + """<startEvent id="Esp_1" isInterrupting="false"><timerEventDefinition>"""
                + """<timeDuration>PT1S</timeDuration></timerEventDefinition></startEvent>"""
                + """<userTask id="Handled_1" name="handled"/><endEvent id="Ee_1"/>"""
                + """<sequenceFlow id="s1" sourceRef="Esp_1" targetRef="Handled_1"/>"""
                + """<sequenceFlow id="s2" sourceRef="Handled_1" targetRef="Ee_1"/></subProcess>"""),

            "Data Object Reference" => Wrap("",
                """<dataObject id="Decl_1" name="carried" autonate:dataType="xsd:double">"""
                + """<extensionElements><flowable:value>42.5</flowable:value></extensionElements></dataObject>"""
                + """<dataObjectReference id="Ev_1" name="carried" dataObjectRef="Decl_1"/>"""
                + """<startEvent id="Start_1"/><userTask id="Parked_1" name="parked"/><endEvent id="End_1"/>"""
                + """<sequenceFlow id="f1" sourceRef="Start_1" targetRef="Parked_1"/>"""
                + """<sequenceFlow id="f2" sourceRef="Parked_1" targetRef="End_1"/>"""),

            "Compensation Boundary" => Wrap("",
                """<startEvent id="Start_1"/>"""
                + """<scriptTask id="Doer_1" name="do" scriptFormat="javascript" autonate:runAs="workflowAuthor"><script>variables.set('did', 'yes');</script></scriptTask>"""
                + """<boundaryEvent id="Ev_1" attachedToRef="Doer_1"><compensateEventDefinition/></boundaryEvent>"""
                + """<scriptTask id="Undo_1" name="undo" isForCompensation="true" scriptFormat="javascript" autonate:runAs="workflowAuthor"><script>variables.set('proof', 'ran');</script></scriptTask>"""
                + """<endEvent id="End_1"><compensateEventDefinition/></endEvent>"""
                + """<sequenceFlow id="f1" sourceRef="Start_1" targetRef="Doer_1"/>"""
                + """<sequenceFlow id="f2" sourceRef="Doer_1" targetRef="End_1"/>"""
                + """<association id="Assoc_1" associationDirection="One" sourceRef="Ev_1" targetRef="Undo_1"/>"""),

            // EXPANDED AT PUBLISH, LIKE THE MESSAGE ROWS (#111). The author draws
            // the element BPMN means for this; the deployed copy is a
            // <serviceTask flowable:type="dmn"> carrying the decision key as a
            // field extension, because BusinessRuleParseHandler resolves a
            // KIE/Drools behaviour at DEPLOY time and KIE is not in the image.
            //
            // So the table is the write. `proof` is not set by a script here --
            // it is the decision table's own OUTPUT COLUMN, so the engine
            // attributes the variable update to Ev_1 itself and the observer's
            // "who wrote it, according to the engine" (#452) is answering about
            // the DMN evaluation rather than about a neighbouring script. A
            // diagram that ran the table and then had a script copy the result
            // would prove the copy.
            "Business Rule Task" => Wrap("", Linear(
                $"""<businessRuleTask id="Ev_1" name="decide" autonate:decisionKey="{DecisionKeyFor(key)}"/>""")),

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

            // NOBODY CALLS START (#525). Publishing is the whole trigger: the
            // engine's timer job fires a second later and creates the instance,
            // which is why this row declares `instance-starts` and the shared
            // theory makes no POST /start for it. It parks on a user task
            // afterwards so the instance is still there to read -- one that
            // started and finished inside the poll interval would make "no
            // instance appeared" and "it already ended" the same observation.
            // NOT A SELF-STARTING ELEMENT, and that is the finding (#526). A
            // conditional start is an EVENT SUB-PROCESS start -- `WorkflowBpmnXml`
            // says so in its own refusal, listing "an error, message, timer,
            // signal, escalation or condition" as what an event subprocess reacts
            // to. It fires inside a RUNNING instance, so `instance-starts` would
            // be the wrong claim; what proves it ran is that its handler's body
            // executed.
            //
            // The condition is `${taken == true}`, which `StartAsync` supplies.
            // Flowable never fires conditional events by itself (#158), so Auton8
            // POSTs `.../evaluate-conditions`, and instance start is one of the
            // paths that does (FlowableClient.cs:196). So this fires through a
            // path the product genuinely drives rather than one #271 says it
            // cannot -- this cell neither needs #271 fixed nor pretends it is.
            //
            // Non-interrupting, so the main flow's own user task survives and the
            // instance is still readable while the handler's write is observed.
            // AN EVENT SUB-PROCESS START, like the conditional one (#527, #526).
            // BPMN has no top-level error start, and `WorkflowBpmnXml` lists
            // error among what an event subprocess reacts to. So what proves it
            // ran is that its handler's body executed.
            //
            // The error is thrown from an error END inside an embedded
            // sub-process, which is the one shape an error end is publishable in
            // here -- Auton8 refuses an uncaught one, naming the cost, and the
            // `Error End` arm below documents the same constraint.
            // THE THROW SITS ONE SCOPE DOWN FROM THE HANDLER (#530). BPMN
            // propagates an escalation to the PARENT scope, so a throw at process
            // level has no parent to be caught by. Thrown inside an embedded
            // sub-process, caught by the event sub-process at process level.
            //
            // Non-interrupting, which for an escalation is the point of the
            // element: `WorkflowBpmnXml`'s error refusal says so itself -- "catch
            // an escalation instead if the work should carry on."
            // CONTROL: per-EFFECT, not per-row (#555). Like the Error Start and
            // Conditional Start rows further DOWN this switch, it rides
            // `variable-written`'s generic control -- a linear
            // diagram whose script writes something that is not `proof`. The
            // row-level shape would be an escalation start inside an event
            // sub-process whose escalation is never thrown; it is not built.
            // #546 disclosed the four rows it was filed about and this fifth was
            // in the same state, unmentioned.
            "Escalation Start Event" => Wrap(
                """<escalation id="Esc_1" name="e1" escalationCode="E1"/>""",
                """<startEvent id="Start_1"/>"""
                + """<subProcess id="Sub_1"><startEvent id="In_1"/>"""
                + """<intermediateThrowEvent id="Thrown_1"><escalationEventDefinition escalationRef="Esc_1"/></intermediateThrowEvent>"""
                + """<endEvent id="In_2"/>"""
                + """<sequenceFlow id="i1" sourceRef="In_1" targetRef="Thrown_1"/>"""
                + """<sequenceFlow id="i2" sourceRef="Thrown_1" targetRef="In_2"/></subProcess>"""
                + """<endEvent id="End_1"/>"""
                + """<sequenceFlow id="f1" sourceRef="Start_1" targetRef="Sub_1"/>"""
                + """<sequenceFlow id="f2" sourceRef="Sub_1" targetRef="End_1"/>"""
                + """<subProcess id="Esp_1" triggeredByEvent="true">"""
                + """<startEvent id="Ev_1" isInterrupting="false"><escalationEventDefinition escalationRef="Esc_1"/></startEvent>"""
                + """<scriptTask id="W_1" name="proof" scriptFormat="javascript" autonate:runAs="workflowAuthor"><script>variables.set('proof', 'ran');</script></scriptTask>"""
                + """<endEvent id="Ee_1"/>"""
                + """<sequenceFlow id="s1" sourceRef="Ev_1" targetRef="W_1"/>"""
                + """<sequenceFlow id="s2" sourceRef="W_1" targetRef="Ee_1"/></subProcess>"""),

            // `cancelActivity="true"` written out, and here it is a REAL choice
            // rather than the only legal one (#530). An error boundary cannot be
            // non-interrupting; an escalation boundary can, and that difference is
            // why BPMN has both. So flipping this one attribute produces a valid
            // diagram describing the opposite feature, which is the strongest
            // form the #471 complement can take.
            "Escalation Boundary" => Wrap(
                """<escalation id="Esc_1" name="e1" escalationCode="E1"/>""",
                """<startEvent id="Start_1"/>"""
                + """<subProcess id="Host_1"><startEvent id="In_1"/>"""
                + """<intermediateThrowEvent id="Thrown_1"><escalationEventDefinition escalationRef="Esc_1"/></intermediateThrowEvent>"""
                + """<userTask id="In_2" name="inner"/><endEvent id="In_3"/>"""
                + """<sequenceFlow id="i1" sourceRef="In_1" targetRef="Thrown_1"/>"""
                + """<sequenceFlow id="i2" sourceRef="Thrown_1" targetRef="In_2"/>"""
                + """<sequenceFlow id="i3" sourceRef="In_2" targetRef="In_3"/></subProcess>"""
                + """<boundaryEvent id="Ev_1" attachedToRef="Host_1" cancelActivity="true"><escalationEventDefinition escalationRef="Esc_1"/></boundaryEvent>"""
                + """<userTask id="After_1" name="after"/><endEvent id="End_1"/><endEvent id="End_2"/>"""
                + """<sequenceFlow id="f1" sourceRef="Start_1" targetRef="Host_1"/>"""
                + """<sequenceFlow id="f2" sourceRef="Host_1" targetRef="End_1"/>"""
                + """<sequenceFlow id="f3" sourceRef="Ev_1" targetRef="After_1"/>"""
                + """<sequenceFlow id="f4" sourceRef="After_1" targetRef="End_2"/>"""),

            // CONTROL: per-EFFECT, not per-row (#546), same as the Conditional
            // Start Event row further DOWN this switch. An error start whose error is never thrown would be
            // the row-level shape and is not built.
            "Error Start Event" => Wrap(
                """<error id="Err_1" errorCode="E1" name="e1"/>""",
                """<startEvent id="Start_1"/>"""
                + """<subProcess id="Sub_1"><startEvent id="In_1"/>"""
                + """<endEvent id="Thrown_1"><errorEventDefinition errorRef="Err_1"/></endEvent>"""
                + """<sequenceFlow id="i1" sourceRef="In_1" targetRef="Thrown_1"/></subProcess>"""
                + """<endEvent id="End_1"/>"""
                + """<sequenceFlow id="f1" sourceRef="Start_1" targetRef="Sub_1"/>"""
                + """<sequenceFlow id="f2" sourceRef="Sub_1" targetRef="End_1"/>"""
                + """<subProcess id="Esp_1" triggeredByEvent="true">"""
                + """<startEvent id="Ev_1"><errorEventDefinition errorRef="Err_1"/></startEvent>"""
                + """<scriptTask id="W_1" name="proof" scriptFormat="javascript" autonate:runAs="workflowAuthor"><script>variables.set('proof', 'ran');</script></scriptTask>"""
                + """<endEvent id="Ee_1"/>"""
                + """<sequenceFlow id="s1" sourceRef="Ev_1" targetRef="W_1"/>"""
                + """<sequenceFlow id="s2" sourceRef="W_1" targetRef="Ee_1"/></subProcess>"""),

            // NO `cancelActivity` HERE, and that is not an omission (#527). An
            // error boundary cannot be non-interrupting: BPMN makes it always
            // interrupting and Flowable interrupts regardless, so the attribute
            // would promise something the engine does not do. There is therefore
            // no same-element control carrying the other configuration for this
            // row; the `host-cancelled` control is per-EFFECT and is #522's
            // non-interrupting timer boundary.
            //
            // THE PRODUCT DOES NOT REFUSE IT, and this comment used to say it
            // did (#555). `WorkflowBpmnXml`'s only non-interrupting-error rule
            // is scoped to an event sub-process error START (#162); a
            // `<boundaryEvent cancelActivity="false"><errorEventDefinition/>`
            // publishes cleanly today. The conclusion above is unaffected --
            // BPMN and the engine settle it -- but a guard that was cited twice
            // and never existed is exactly what this oracle is against.
            "Error Boundary" => Wrap(
                """<error id="Err_1" errorCode="E1" name="e1"/>""",
                """<startEvent id="Start_1"/>"""
                + """<subProcess id="Host_1"><startEvent id="In_1"/>"""
                + """<endEvent id="Thrown_1"><errorEventDefinition errorRef="Err_1"/></endEvent>"""
                + """<sequenceFlow id="i1" sourceRef="In_1" targetRef="Thrown_1"/></subProcess>"""
                + """<boundaryEvent id="Ev_1" attachedToRef="Host_1"><errorEventDefinition errorRef="Err_1"/></boundaryEvent>"""
                + """<userTask id="After_1" name="after"/><endEvent id="End_1"/><endEvent id="End_2"/>"""
                + """<sequenceFlow id="f1" sourceRef="Start_1" targetRef="Host_1"/>"""
                + """<sequenceFlow id="f2" sourceRef="Host_1" targetRef="End_1"/>"""
                + """<sequenceFlow id="f3" sourceRef="Ev_1" targetRef="After_1"/>"""
                + """<sequenceFlow id="f4" sourceRef="After_1" targetRef="End_2"/>"""),

            // CONTROL: per-EFFECT, not per-row (#546). This row rides
            // `variable-written`'s generic control -- a linear diagram whose
            // script writes something that is not `proof`. A conditional start
            // whose condition is never satisfiable would be the row-level shape;
            // it is not built, and that is a gap rather than an impossibility.
            "Conditional Start Event" => Wrap("",
                """<startEvent id="Start_1"/><userTask id="Main_1" name="main"/><endEvent id="End_1"/>"""
                + """<sequenceFlow id="f1" sourceRef="Start_1" targetRef="Main_1"/>"""
                + """<sequenceFlow id="f2" sourceRef="Main_1" targetRef="End_1"/>"""
                + """<subProcess id="Esp_1" triggeredByEvent="true">"""
                + """<startEvent id="Ev_1" isInterrupting="false"><conditionalEventDefinition>"""
                + """<condition>${taken == true}</condition></conditionalEventDefinition></startEvent>"""
                + """<scriptTask id="W_1" name="proof" scriptFormat="javascript" autonate:runAs="workflowAuthor"><script>variables.set('proof', 'ran');</script></scriptTask>"""
                + """<endEvent id="Ee_1"/>"""
                + """<sequenceFlow id="s1" sourceRef="Ev_1" targetRef="W_1"/>"""
                + """<sequenceFlow id="s2" sourceRef="W_1" targetRef="Ee_1"/></subProcess>"""),

            // Same condition, same nudge, and `cancelActivity="true"` written out
            // because the negative control's whole difference is that attribute.
            "Conditional Boundary" => Wrap("",
                """<startEvent id="Start_1"/><userTask id="Host_1" name="host"/>"""
                + """<boundaryEvent id="Ev_1" attachedToRef="Host_1" cancelActivity="true">"""
                + """<conditionalEventDefinition><condition>${taken == true}</condition>"""
                + """</conditionalEventDefinition></boundaryEvent>"""
                + """<userTask id="After_1" name="after"/><endEvent id="End_1"/><endEvent id="End_2"/>"""
                + """<sequenceFlow id="f1" sourceRef="Start_1" targetRef="Host_1"/>"""
                + """<sequenceFlow id="f2" sourceRef="Host_1" targetRef="End_1"/>"""
                + """<sequenceFlow id="f3" sourceRef="Ev_1" targetRef="After_1"/>"""
                + """<sequenceFlow id="f4" sourceRef="After_1" targetRef="End_2"/>"""),

            // TRIGGERED FROM OUTSIDE, like the signal start one, and for the
            // same reason: there is no instance until the message arrives (#528).
            // Delivered through `POST /api/workflow-messages`, which
            // `MessageCorrelationExecutionTests` already proves starts an
            // instance -- that test owns the CORRELATION feature; this cell owns
            // whether the element deployed as the manifest says, survived publish
            // unrewritten, and had the declared effect. The trigger is the
            // overlap, not the assertion.
            //
            // The message name carries the run's key, because Flowable keeps
            // message START subscriptions unique per name across the engine and a
            // fixed one would make the second run ambiguous (#454).
            "Message Start Event" => Wrap(
                $"""<message id="Msg_1" name="ms{key}"/>""",
                """<startEvent id="Ev_1"><messageEventDefinition messageRef="Msg_1"/></startEvent>"""
                + """<userTask id="Parked_1" name="parked"/><endEvent id="End_1"/>"""
                + """<sequenceFlow id="f1" sourceRef="Ev_1" targetRef="Parked_1"/>"""
                + """<sequenceFlow id="f2" sourceRef="Parked_1" targetRef="End_1"/>"""),

            // The message arrives AFTER the host is parked -- see the after-start
            // trigger in the shared theory. `cancelActivity="true"` written out,
            // because the negative control's whole difference is that attribute.
            // CONTROL: per-EFFECT, not per-row (#555). Rides the shared
            // `host-cancelled` control, #522's non-interrupting TIMER boundary.
            // Unlike the error boundary, a non-interrupting MESSAGE boundary is
            // perfectly legal, so the row-level control is available here and
            // simply unbuilt -- a gap, not an impossibility.
            "Message Boundary" => Wrap(
                $"""<message id="Msg_1" name="mb{key}"/>""",
                """<startEvent id="Start_1"/><userTask id="Host_1" name="host"/>"""
                + """<boundaryEvent id="Ev_1" attachedToRef="Host_1" cancelActivity="true">"""
                + """<messageEventDefinition messageRef="Msg_1"/></boundaryEvent>"""
                + """<userTask id="After_1" name="after"/><endEvent id="End_1"/><endEvent id="End_2"/>"""
                + """<sequenceFlow id="f1" sourceRef="Start_1" targetRef="Host_1"/>"""
                + """<sequenceFlow id="f2" sourceRef="Host_1" targetRef="End_1"/>"""
                + """<sequenceFlow id="f3" sourceRef="Ev_1" targetRef="After_1"/>"""
                + """<sequenceFlow id="f4" sourceRef="After_1" targetRef="End_2"/>"""),

            // NOTHING INSIDE THE DIAGRAM CAN FIRE THIS (#529). There is no
            // instance until the signal arrives, which is what a signal start
            // event is. The cell fires it through Auton8's own API after publish
            // -- see `SelfStartTriggerSignal`.
            //
            // The start-user assertion does NOT apply to this row, and saying it
            // did was wrong (#550). Auton8's route is authenticated, so Flowable
            // records the REST user as the start user of an instance a broadcast
            // created. What holds instead: no instance before publish, none after
            // publish and before the trigger, one afterwards -- and the theory
            // never calls POST /start on this branch at all.
            //
            // The signal name carries the run's key: Flowable keeps signal START
            // subscriptions per name across the engine, and a fixed name would
            // make every run after the first ambiguous -- the same hazard the
            // three message rows already hit (#454).
            "Signal Start Event" => Wrap(
                $"""<signal id="Sig_1" name="st{key}"/>""",
                """<startEvent id="Ev_1"><signalEventDefinition signalRef="Sig_1"/></startEvent>"""
                + """<userTask id="Parked_1" name="parked"/><endEvent id="End_1"/>"""
                + """<sequenceFlow id="f1" sourceRef="Ev_1" targetRef="Parked_1"/>"""
                + """<sequenceFlow id="f2" sourceRef="Parked_1" targetRef="End_1"/>"""),

            // THE DIAGRAM TRIGGERS ITSELF (#529), deliberately, rather than
            // reaching for #523's route. This row is about the boundary catching
            // and cancelling its host; a cell that needed a second feature to
            // fire would go red for two different reasons and only one of them
            // would be this element.
            //
            // A parallel gateway forks: one branch parks on the host, the other
            // throws the signal.
            // CONTROL: per-EFFECT, not per-row (#555). Same as Message
            // Boundary above: rides the timer control, and a non-interrupting
            // signal boundary is legal, so this one is unbuilt rather than
            // impossible.
            "Signal Boundary" => Wrap(
                $"""<signal id="Sig_1" name="sb{key}"/>""",
                """<startEvent id="Start_1"/><parallelGateway id="Fork_1"/>"""
                + """<userTask id="Host_1" name="host"/>"""
                + """<boundaryEvent id="Ev_1" attachedToRef="Host_1" cancelActivity="true">"""
                + """<signalEventDefinition signalRef="Sig_1"/></boundaryEvent>"""
                + """<intermediateThrowEvent id="Throw_1"><signalEventDefinition signalRef="Sig_1"/></intermediateThrowEvent>"""
                + """<userTask id="After_1" name="after"/>"""
                + """<endEvent id="End_1"/><endEvent id="End_2"/><endEvent id="End_3"/>"""
                + """<sequenceFlow id="f1" sourceRef="Start_1" targetRef="Fork_1"/>"""
                + """<sequenceFlow id="f2" sourceRef="Fork_1" targetRef="Host_1"/>"""
                + """<sequenceFlow id="f3" sourceRef="Fork_1" targetRef="Throw_1"/>"""
                + """<sequenceFlow id="f4" sourceRef="Host_1" targetRef="End_1"/>"""
                + """<sequenceFlow id="f5" sourceRef="Throw_1" targetRef="End_2"/>"""
                + """<sequenceFlow id="f6" sourceRef="Ev_1" targetRef="After_1"/>"""
                + """<sequenceFlow id="f7" sourceRef="After_1" targetRef="End_3"/>"""),

            "Timer Start Event" => Wrap("",
                """<startEvent id="Ev_1"><timerEventDefinition>"""
                + """<timeDuration>PT1S</timeDuration></timerEventDefinition></startEvent>"""
                + """<userTask id="Parked_1" name="parked"/><endEvent id="End_1"/>"""
                + """<sequenceFlow id="f1" sourceRef="Ev_1" targetRef="Parked_1"/>"""
                + """<sequenceFlow id="f2" sourceRef="Parked_1" targetRef="End_1"/>"""),

            // `cancelActivity="true"` is the default and is written out anyway,
            // because the negative control's entire difference from this diagram
            // is that one attribute (#525). A reader should not have to know the
            // default to see which feature is under test -- the non-interrupting
            // variant is the opposite feature, not a near miss.
            //
            // The host is a user task, so it parks and stays parked: nothing but
            // this boundary can remove it, which is what makes "the host is gone"
            // attributable to Ev_1.
            "Timer Boundary" => Wrap("",
                """<startEvent id="Start_1"/><userTask id="Host_1" name="host"/>"""
                + """<boundaryEvent id="Ev_1" attachedToRef="Host_1" cancelActivity="true">"""
                + """<timerEventDefinition><timeDuration>PT1S</timeDuration></timerEventDefinition></boundaryEvent>"""
                + """<userTask id="After_1" name="after"/><endEvent id="End_1"/><endEvent id="End_2"/>"""
                + """<sequenceFlow id="f1" sourceRef="Start_1" targetRef="Host_1"/>"""
                + """<sequenceFlow id="f2" sourceRef="Host_1" targetRef="End_1"/>"""
                + """<sequenceFlow id="f3" sourceRef="Ev_1" targetRef="After_1"/>"""
                + """<sequenceFlow id="f4" sourceRef="After_1" targetRef="End_2"/>"""),

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

            // #169. A two-pool collaboration. `Ev_1` IS the pool -- the oracle
            // asserts the declared element is what carries that id, deployed and
            // authored -- and its proof is the task its process creates, which
            // NestedIdsIn reaches through `processRef` (BpmnDiagram.cs). The
            // primary pool carries `key` and a user task; the other is a
            // counterparty drawn for context, with
            // nothing in it, which #169 marks non-executable and deploys as nothing.
            // Both of #169's shapes in one probe: the set deploys, and the pool
            // that contains a process is the one whose process runs.
            "Pool / Participant" => $"""
                <?xml version="1.0" encoding="UTF-8"?>
                <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL"
                             xmlns:flowable="http://flowable.org/bpmn"
                             xmlns:autonate="http://autonate.dev/workflows"
                             targetNamespace="http://autonate.dev/workflows">
                  <collaboration id="Collab_1">
                    <participant id="Ev_1" name="Us" processRef="{key}" />
                    <participant id="P_2" name="Counterparty" processRef="{key}cp" />
                  </collaboration>
                  <process id="{key}" name="probe" isExecutable="true">
                    <startEvent id="Start_1"/><userTask id="Task_1" name="approve"/><endEvent id="End_1"/>
                    <sequenceFlow id="f1" sourceRef="Start_1" targetRef="Task_1"/>
                    <sequenceFlow id="f2" sourceRef="Task_1" targetRef="End_1"/>
                  </process>
                  <process id="{key}cp" name="Counterparty" isExecutable="true" />
                  <bpmndi:BPMNDiagram id="Diagram_1"
                                      xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI"
                                      xmlns:dc="http://www.omg.org/spec/DD/20100524/DC">
                    <bpmndi:BPMNPlane id="Plane_1" bpmnElement="Collab_1">
                      <bpmndi:BPMNShape id="Shape_Ev_1" bpmnElement="Ev_1" isHorizontal="true">
                        <dc:Bounds x="100" y="60" width="600" height="160" />
                      </bpmndi:BPMNShape>
                      <bpmndi:BPMNShape id="Shape_Task_1" bpmnElement="Task_1">
                        <dc:Bounds x="240" y="100" width="100" height="80" />
                      </bpmndi:BPMNShape>
                      <bpmndi:BPMNShape id="Shape_P_2" bpmnElement="P_2" isHorizontal="true">
                        <dc:Bounds x="100" y="260" width="600" height="120" />
                      </bpmndi:BPMNShape>
                    </bpmndi:BPMNPlane>
                  </bpmndi:BPMNDiagram>
                </definitions>
                """,

            // #171. `Ev_1` IS the lane, listing the pool's user task. The oracle
            // asserts the element carrying that id is a lane, deployed and
            // authored, and its proof is the task it lists, which NestedIdsIn
            // reaches through `flowNodeRef` (BpmnDiagram.cs) -- a lane is an
            // element with no children but references, like a participant. No
            // group on the lane here: the oracle has none to name, and the
            // assignment it would produce is LaneAssignmentExecutionTests' claim.
            "Lane" => $"""
                <?xml version="1.0" encoding="UTF-8"?>
                <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL"
                             xmlns:flowable="http://flowable.org/bpmn"
                             xmlns:autonate="http://autonate.dev/workflows"
                             targetNamespace="http://autonate.dev/workflows">
                  <collaboration id="Collab_1">
                    <participant id="P_1" name="Us" processRef="{key}" />
                  </collaboration>
                  <process id="{key}" name="probe" isExecutable="true">
                    <laneSet id="LaneSet_1">
                      <lane id="Ev_1" name="Finance">
                        <flowNodeRef>Start_1</flowNodeRef>
                        <flowNodeRef>Task_1</flowNodeRef>
                        <flowNodeRef>End_1</flowNodeRef>
                      </lane>
                    </laneSet>
                    <startEvent id="Start_1"/><userTask id="Task_1" name="approve"/><endEvent id="End_1"/>
                    <sequenceFlow id="f1" sourceRef="Start_1" targetRef="Task_1"/>
                    <sequenceFlow id="f2" sourceRef="Task_1" targetRef="End_1"/>
                  </process>
                  <bpmndi:BPMNDiagram id="Diagram_1"
                                      xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI"
                                      xmlns:dc="http://www.omg.org/spec/DD/20100524/DC">
                    <bpmndi:BPMNPlane id="Plane_1" bpmnElement="Collab_1">
                      <bpmndi:BPMNShape id="Shape_P_1" bpmnElement="P_1" isHorizontal="true">
                        <dc:Bounds x="100" y="60" width="600" height="160" />
                      </bpmndi:BPMNShape>
                      <bpmndi:BPMNShape id="Shape_Ev_1" bpmnElement="Ev_1" isHorizontal="true">
                        <dc:Bounds x="130" y="60" width="570" height="160" />
                      </bpmndi:BPMNShape>
                      <bpmndi:BPMNShape id="Shape_Task_1" bpmnElement="Task_1">
                        <dc:Bounds x="240" y="100" width="100" height="80" />
                      </bpmndi:BPMNShape>
                    </bpmndi:BPMNPlane>
                  </bpmndi:BPMNDiagram>
                </definitions>
                """,

            // #170. `Ev_1` IS the message flow, from a send task in the primary
            // pool to a message start event in the counterparty. The primary is
            // linear, so instance-ends proves the send ran -- and the send's
            // addressing is nothing but this flow, resolved at run time from the
            // stored diagram; delete the flow and the send fails "noTargetProcess".
            "Message Flow" => $$"""
                <?xml version="1.0" encoding="UTF-8"?>
                <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL"
                             xmlns:flowable="http://flowable.org/bpmn"
                             xmlns:autonate="http://autonate.dev/workflows"
                             targetNamespace="http://autonate.dev/workflows">
                  <message id="Msg_1" name="{{key}}-order" />
                  <collaboration id="Collab_1">
                    <participant id="P_1" name="Us" processRef="{{key}}" />
                    <participant id="P_2" name="Counterparty" processRef="{{key}}cp" />
                    <messageFlow id="Ev_1" name="order" sourceRef="Send_1" targetRef="Start_cp" />
                  </collaboration>
                  <process id="{{key}}" name="probe" isExecutable="true">
                    <startEvent id="Start_1"/>
                    <sendTask id="Send_1" name="send order"
                              flowable:delegateExpression="${autonateBehaviorDelegate}"
                              flowable:autonateServiceKind="behavior"
                              flowable:behaviorKey="autonate.send-message"
                              flowable:async="true"/>
                    <endEvent id="End_1"/>
                    <sequenceFlow id="f1" sourceRef="Start_1" targetRef="Send_1"/>
                    <sequenceFlow id="f2" sourceRef="Send_1" targetRef="End_1"/>
                  </process>
                  <process id="{{key}}cp" name="Counterparty" isExecutable="true">
                    <startEvent id="Start_cp"><messageEventDefinition messageRef="Msg_1"/></startEvent>
                    <userTask id="Task_cp" name="handle order"/>
                    <endEvent id="End_cp"/>
                    <sequenceFlow id="c1" sourceRef="Start_cp" targetRef="Task_cp"/>
                    <sequenceFlow id="c2" sourceRef="Task_cp" targetRef="End_cp"/>
                  </process>
                  <bpmndi:BPMNDiagram id="Diagram_1"
                                      xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI"
                                      xmlns:dc="http://www.omg.org/spec/DD/20100524/DC"
                                      xmlns:di="http://www.omg.org/spec/DD/20100524/DI">
                    <bpmndi:BPMNPlane id="Plane_1" bpmnElement="Collab_1">
                      <bpmndi:BPMNShape id="Shape_P_1" bpmnElement="P_1" isHorizontal="true">
                        <dc:Bounds x="100" y="60" width="600" height="160" />
                      </bpmndi:BPMNShape>
                      <bpmndi:BPMNShape id="Shape_Send_1" bpmnElement="Send_1">
                        <dc:Bounds x="240" y="100" width="100" height="80" />
                      </bpmndi:BPMNShape>
                      <bpmndi:BPMNShape id="Shape_P_2" bpmnElement="P_2" isHorizontal="true">
                        <dc:Bounds x="100" y="260" width="600" height="160" />
                      </bpmndi:BPMNShape>
                      <bpmndi:BPMNShape id="Shape_Start_cp" bpmnElement="Start_cp">
                        <dc:Bounds x="272" y="322" width="36" height="36" />
                      </bpmndi:BPMNShape>
                      <bpmndi:BPMNEdge id="Edge_Ev_1" bpmnElement="Ev_1">
                        <di:waypoint x="290" y="180" /><di:waypoint x="290" y="322" />
                      </bpmndi:BPMNEdge>
                    </bpmndi:BPMNPlane>
                  </bpmndi:BPMNDiagram>
                </definitions>
                """,

            "Call Activity" => Wrap("", Linear(
                $"""<callActivity id="Ev_1" name="call" calledElement="{key}c"/>""")),

            // ---- AC5 tranche: elements that need no trigger and no host (#325).
            //
            // Task (Generic) and Manual Task are NOT here, and their absence is
            // the finding. Auton8 refuses both at publish, by design and with a
            // written reason -- they are `studio: withdrawn`. They cannot be
            // proven through the product's own API, and this class deliberately
            // publishes the way the product does rather than around it. Their
            // rows carry that as an `undeclaredReason`.
            // Each is a linear process, so `instance-ends` means the engine
            // entered THIS element as THIS type and ran the instance to
            // completion through it. That is the whole claim for a task or a
            // throw that creates nothing -- and it is exactly what Manual Task
            // and Task (Generic) were missing when they shipped doing nothing.
            "Send Task" => Wrap(
                $"""<message id="Msg_1" name="m1{key}"/>""",
                LinearIn($"""<sendTask id="Ev_1" name="send" flowable:behaviorKey="autonate.send-message" flowable:autonateMessageName="m1{key}" flowable:autonateTargetProcessKey="{key}r"/>""")),

            "Intermediate Throw (Message)" => Wrap(
                $"""<message id="Msg_1" name="m1{key}"/>""",
                LinearIn($"""<intermediateThrowEvent id="Ev_1" flowable:autonateTargetProcessKey="{key}r"><messageEventDefinition messageRef="Msg_1"/></intermediateThrowEvent>""")),

            "Intermediate Throw (Signal)" => Wrap(
                """<signal id="Sig_1" name="s1"/>""",
                Linear("""<intermediateThrowEvent id="Ev_1"><signalEventDefinition signalRef="Sig_1"/></intermediateThrowEvent>""")),

            "Intermediate Throw (Escalation)" => Wrap(
                """<escalation id="Esc_1" name="e1" escalationCode="E1"/>""",
                Linear("""<intermediateThrowEvent id="Ev_1"><escalationEventDefinition escalationRef="Esc_1"/></intermediateThrowEvent>""")),

            "Intermediate Throw (Compensation)" => Wrap("", Linear(
                """<intermediateThrowEvent id="Ev_1"><compensateEventDefinition/></intermediateThrowEvent>""")),

            // The three send rows name a TARGET (#454). Without
            // `flowable:autonateTargetProcessKey`, SendMessageBehavior fails with
            // `noTargetProcess` and `instance-ends` was satisfied by that failure.
            // `{key}r` is published alongside, waiting on the same message.
            "Message End" => Wrap(
                $"""<message id="Msg_1" name="m1{key}"/>""",
                $"""<startEvent id="Start_1"/><endEvent id="Ev_1" flowable:autonateTargetProcessKey="{key}r"><messageEventDefinition messageRef="Msg_1"/></endEvent>"""
                + """<sequenceFlow id="f1" sourceRef="Start_1" targetRef="Ev_1"/>"""),

            "Signal End" => Wrap(
                """<signal id="Sig_1" name="s1"/>""",
                """<startEvent id="Start_1"/><endEvent id="Ev_1"><signalEventDefinition signalRef="Sig_1"/></endEvent>"""
                + """<sequenceFlow id="f1" sourceRef="Start_1" targetRef="Ev_1"/>"""),

            // An error end must be CAUGHT. Auton8 refuses it otherwise, naming
            // the cost: "reaching this event destroys the whole process instance
            // -- there is no history to look at afterwards." So the element under
            // test sits inside a sub-process whose boundary catches its code,
            // which is the only shape in which an error end is publishable here.
            "Error End" => Wrap(
                """<error id="Err_1" errorCode="E1" name="e1"/>""",
                """<startEvent id="Start_1"/>"""
                + """<subProcess id="Sub_1"><startEvent id="In_1"/>"""
                + """<endEvent id="Ev_1"><errorEventDefinition errorRef="Err_1"/></endEvent>"""
                + """<sequenceFlow id="i1" sourceRef="In_1" targetRef="Ev_1"/></subProcess>"""
                + """<boundaryEvent id="Catch_1" attachedToRef="Sub_1"><errorEventDefinition errorRef="Err_1"/></boundaryEvent>"""
                + """<endEvent id="End_1"/><endEvent id="End_2"/>"""
                + """<sequenceFlow id="f1" sourceRef="Start_1" targetRef="Sub_1"/>"""
                + """<sequenceFlow id="f2" sourceRef="Sub_1" targetRef="End_1"/>"""
                + """<sequenceFlow id="f3" sourceRef="Catch_1" targetRef="End_2"/>"""),

            "Escalation End" => Wrap(
                """<escalation id="Esc_1" name="e1" escalationCode="E1"/>""",
                """<startEvent id="Start_1"/><endEvent id="Ev_1"><escalationEventDefinition escalationRef="Esc_1"/></endEvent>"""
                + """<sequenceFlow id="f1" sourceRef="Start_1" targetRef="Ev_1"/>"""),

            "Compensation End" => Wrap("",
                """<startEvent id="Start_1"/><endEvent id="Ev_1"><compensateEventDefinition/></endEvent>"""
                + """<sequenceFlow id="f1" sourceRef="Start_1" targetRef="Ev_1"/>"""),

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


    /// <summary>The separately-published receiver the three send rows need (#454).</summary>
    /// <remarks>
    /// The message name carries the run's key. Flowable keeps message START
    /// subscriptions unique per name across the engine, so a fixed name meant the
    /// second receiver this suite ever published was refused -- measured, as a
    /// bare 502 "the engine refused this workflow".
    /// </remarks>
    private static string? ReceiverDiagram(string name, string key) =>
        name is not ("Message End" or "Send Task" or "Intermediate Throw (Message)") ? null : $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL"
                     xmlns:flowable="http://flowable.org/bpmn"
                     targetNamespace="http://autonate.dev/workflows">
          <message id="RMsg_1" name="m1{key}"/>
          <process id="{key}r" name="receiver" isExecutable="true">
            <startEvent id="RS_1"><messageEventDefinition messageRef="RMsg_1"/></startEvent>
            <userTask id="RT_1" name="received"/>
            <endEvent id="RE_1"/>
            <sequenceFlow id="rf1" sourceRef="RS_1" targetRef="RT_1"/>
            <sequenceFlow id="rf2" sourceRef="RT_1" targetRef="RE_1"/>
          </process>
          <bpmndi:BPMNDiagram id="RDiagram_1"
                              xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI"
                              xmlns:dc="http://www.omg.org/spec/DD/20100524/DC">
            <bpmndi:BPMNPlane id="RPlane_1" bpmnElement="{key}r">
              <bpmndi:BPMNShape id="RShape_1" bpmnElement="RS_1">
                <dc:Bounds x="240" y="100" width="36" height="36" />
              </bpmndi:BPMNShape>
            </bpmndi:BPMNPlane>
          </bpmndi:BPMNDiagram>
        </definitions>
        """;

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

    /// <summary>The decision key the Business Rule Task row's table is published under.</summary>
    /// <remarks>
    /// Derived from the process key rather than fixed, so a repeat run, a retry,
    /// or a parallel collection never evaluates a table some other run rewrote.
    /// `key` is already a letter followed by hex, which is what the decision-key
    /// validator accepts.
    /// </remarks>
    private static string DecisionKeyFor(string key) => $"{key}d";

    /// <summary>
    /// A one-rule table whose only output column is `proof` (#111).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The blank input entry is DMN's "any": the rule fires on every input, so the
    /// cell cannot fail for the uninteresting reason that no rule matched --
    /// which the engine reports identically to a type mismatch (measured in #106).
    /// </para>
    /// <para>
    /// The output column is named `proof` on purpose. Flowable writes a DMN
    /// decision's outputs as process variables attributed to the activity that
    /// evaluated it, so the observer's engine-side attribution question — "which
    /// activity instance wrote this?" — is answered by `Ev_1` itself. Nothing in
    /// this diagram copies the result, because a copy is what would be proved.
    /// </para>
    /// <para>
    /// Published through Auton8's own API rather than deployed to the engine
    /// directly. A hand-deployed DMN would prove that Flowable can run a decision
    /// table, which nobody doubted; what is in question is whether a table an
    /// author created in Auton8 is the one a business rule task they drew
    /// evaluates.
    /// </para>
    /// </remarks>
    private static async Task PublishDecisionTableAsync(IAPIRequestContext api, string decisionKey)
    {
        var created = await api.PostAsync("/api/decision-tables/", new APIRequestContextOptions
        {
            DataObject = new
            {
                decisionKey,
                name = "probe",
                hitPolicy = "FIRST",
                inputs = new[]
                {
                    new { id = "in_1", label = "Amount", name = "amount", typeRef = "number" }
                },
                outputs = new[]
                {
                    new { id = "out_1", label = "Proof", name = "proof", typeRef = "string" }
                },
                rules = new object[]
                {
                    new { id = "r1", inputEntries = new[] { "" }, outputEntries = new[] { "\"ran\"" } }
                }
            }
        });
        Assert.True(created.Ok,
            $"Creating the decision table failed: {created.Status} {await created.TextAsync()}");

        using var body = JsonDocument.Parse(await created.TextAsync());
        var id = body.RootElement.GetProperty("id").GetString()!;

        var published = await api.PostAsync($"/api/decision-tables/{id}/publish",
            new APIRequestContextOptions { DataObject = new { } });
        Assert.True(published.Ok,
            $"Publishing the decision table failed: {published.Status} {await published.TextAsync()}");
    }


    private readonly record struct RuntimeTask(string Id, string Owner);

    /// <summary>The instance's runtime tasks, each with the activity that owns it.</summary>
    private static async Task<IReadOnlyList<RuntimeTask>> TasksAsync(
        IAPIRequestContext api, string instance)
    {
        var response = await api.GetAsync($"/api/executions/{instance}/tasks");
        if (!response.Ok) return [];

        using var body = JsonDocument.Parse(await response.TextAsync());
        if (body.RootElement.ValueKind != JsonValueKind.Array) return [];

        var tasks = new List<RuntimeTask>();
        foreach (var task in body.RootElement.EnumerateArray())
        {
            tasks.Add(new RuntimeTask(
                task.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "",
                task.TryGetProperty("taskDefinitionKey", out var key) ? key.GetString() ?? "" : ""));
        }

        return tasks;
    }

}
