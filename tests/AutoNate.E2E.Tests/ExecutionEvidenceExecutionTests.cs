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
        // same set where CI can see it. Both move together or one of them fails,
        // which is the point (#429, #433).
        Assert.Equal(32, DeclaredEffects().Count);
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
    public static TheoryData<string, string> InertDiagrams() => new()
    {
        { "instance-ends", "a parallel branch parks on a user task, so the instance never ends" },
        { "instance-waits", "nothing waits, so the instance runs straight through" },
        { "task-appears", "the only task is outside the sub-process, so the container creates none" },
        { "variable-written", "no script writes `proof`" },

        // These two are each other's control (#471). Each diagram is a real,
        // deployable, WORKING multi-instance activity carrying the other kind of
        // marker -- so neither observer can be satisfied by a diagram that simply
        // does nothing, which is the usual way a negative control goes soft.
        { "tasks-appear-together", "the marker is sequential, so only one instance is live at a time" },
        { "tasks-appear-in-turn", "the marker is parallel, so all three appear at once" },
    };

    [Theory]
    [MemberData(nameof(InertDiagrams))]
    public async Task An_inert_diagram_is_observed_as_not_holding(string effect, string why)
    {
        await using var session = await NewSignedInAsAdminAsync();
        var api = session.Page.APIRequest;

        var key = $"nc{Guid.NewGuid():N}"[..18];
        var xml = InertDiagram(effect, key);

        await PublishAsync(api, key, xml);
        var instance = await StartAsync(api, key);

        var entered = await EventuallyEnteredAsync(api, instance, "Ev_1");
        Assert.True(
            entered is not null,
            $"negative control for '{effect}': the engine never entered 'Ev_1', so this control "
            + "is not testing the observer -- it would fail for the wrong reason (#463).");

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
    // `xmlns:bpmn` is bound to the SAME uri as the default namespace, which looks
    // redundant and is not (#471). `ExpandMultiInstanceCardinality` writes
    // `xsi:type="bpmn:tFormalExpression"` on the loopCardinality child it
    // synthesises, and that value is a QName: without the prefix bound, Flowable
    // refuses the deployment. Measured -- removing this one line fails exactly
    // the two Multi-Instance cells and nothing else. (`xmlns:xsi` is NOT needed;
    // XDocument declares it when it serialises the attribute. Measured the same
    // way: removing it leaves 32/32.)
    private static string WrapIn(string key, string roots, string body) => $"""
                <?xml version="1.0" encoding="UTF-8"?>
                <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL"
                             xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
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
    private static string InertDiagram(string effect, string key) => effect switch
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

        // Parallel creates all three at once, so "one at a time" must not hold.
        "tasks-appear-in-turn" => WrapIn(key, "", LinearIn(
            """<userTask id="Ev_1" name="approve">"""
            + """<multiInstanceLoopCharacteristics isSequential="false" autonate:loopCardinality="3"/>"""
            + """</userTask>""")),

        _ => throw new InvalidOperationException(
            $"No inert diagram for effect '{effect}'. Every observable effect needs one, or the "
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

        await PublishAsync(api, key, xml);
        var instance = await StartAsync(api, key);

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
        Assert.Equal(declaredEventDefinition, EventDefinitionIn(xml));
        }

        // For a marker row the host is whatever the diagram builds, so the
        // engine-name comparison below has no manifest-side expectation to use.
        // The host is pinned a different way instead -- see the deployed-form
        // check further down, which requires the deployed host to be the SAME
        // tag as the authored one and to still carry the marker. What actually
        // stops a stand-in here is the effect: `tasks-appear-together` is not
        // satisfiable by a scriptTask, an unmarked userTask, or a sequential one.
        var expectedType = isMarkerRow ? ElementTypeIn(xml) : declaredLocalName;
        var enteredAs = await EventuallyEnteredAsync(api, instance, "Ev_1");

        Assert.True(
            enteredAs is not null,
            $"{name}: the engine never entered activity 'Ev_1'. The instance ran, so "
            + "whatever effect follows is some other element's (#412).");

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

        var wasRewritten = !isMarkerRow && !string.Equals(
            deployed!.Name.LocalName, declaredLocalName, StringComparison.Ordinal);

        // NOT `return` (#463). Everything from here to the effect observation is
        // a check on the PUBLISH REWRITE; the effect observation is the point of
        // the cell. An early return in this stretch skips it, which is head 5b of
        // #453 -- a cell that runs, stays green, and observes nothing -- and the
        // first draft of the marker branch above did exactly that.
        if (!isMarkerRow && (declaredEventDefinition is not null || wasRewritten))
        {
            if (deployed.Name.LocalName == "serviceTask")
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
        if (deployed.Name.LocalName == "serviceTask")
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
            DataObject = new { variables = new { items = new[] { "a", "b" }, ok = false, approver = "ana", taken = true } }
        });
        Assert.True(response.Ok, $"Starting failed: {response.Status} {await response.TextAsync()}");

        using var body = JsonDocument.Parse(await response.TextAsync());
        return body.RootElement.GetProperty("id").GetString()!;
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

    private static async Task<Observation> ObserveAsync(
        IAPIRequestContext api, string instance, string effect, string elementType,
        string xml)
    {
        for (var attempt = 0; attempt < 20; attempt++)
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
                    return new(false, $"could not complete the first task: {completed.Status}");
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
                if (targets.Count > 1 && ConditionalFlowsFrom(xml, "Ev_1"))
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
