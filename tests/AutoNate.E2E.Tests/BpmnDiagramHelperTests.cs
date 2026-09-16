using AutoNate.E2E.Tests.Support;
using Xunit;

namespace AutoNate.E2E.Tests;

/// <summary>
/// The diagram reads behind the live-engine oracle, exercised without one (#474).
/// </summary>
/// <remarks>
/// <para>
/// Deliberately does NOT inherit <c>E2ETestBase</c>. That base carries
/// <c>[Collection(AutoNateE2ECollection.Name)]</c>, and the collection fixture
/// is what spawns AutoNate.Web and Playwright — so staying outside the
/// collection is what makes this class service-free, and therefore untraited,
/// and therefore part of the <b>slim</b> tier that GitHub runs on every push.
/// </para>
/// <para>
/// Every case below is a misreading of XML that actually shipped. They were all
/// fixed on a <c>[Trait("RequiresService", "Flowable")]</c> class, which meant
/// the fixes were only ever re-checked by someone with an engine running.
/// </para>
/// <para>
/// What this does NOT cover: whether the observers <em>call</em> these
/// correctly. That needs an engine and stays in the full tier.
/// </para>
/// </remarks>
public sealed class BpmnDiagramHelperTests
{
    private static string Diagram(string body) =>
        $$"""
        <?xml version="1.0" encoding="UTF-8"?>
        <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL" targetNamespace="http://autonate.dev/workflows">
          <process id="P_1" isExecutable="true">
        {{body}}
          </process>
        </definitions>
        """;

    // ---- ElementTypeIn -------------------------------------------------------

    [Fact]
    public void ElementTypeIn_reads_the_element_carrying_Ev_1()
    {
        var xml = Diagram("""
            <startEvent id="Start_1" />
            <userTask id="Ev_1" name="Approve" />
        """);

        // Not the first element in the document -- the one with the id.
        Assert.Equal("userTask", BpmnDiagram.ElementTypeIn(xml));
    }

    /// <summary>
    /// A namespace-prefixed tag is the same element.
    /// </summary>
    /// <remarks>
    /// The original read the markup with a regex anchored on <c>&lt;[A-Za-z]+</c>,
    /// which cannot see <c>&lt;bpmn:userTask&gt;</c> at all (#412). Every diagram
    /// the studio exports is prefixed.
    /// </remarks>
    [Fact]
    public void ElementTypeIn_sees_through_a_namespace_prefix()
    {
        var xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL">
              <bpmn:process id="P_1" isExecutable="true">
                <bpmn:serviceTask id="Ev_1" name="Charge" />
              </bpmn:process>
            </bpmn:definitions>
            """;

        // serviceTask, not userTask, on purpose: the two ElementTypeIn cases have
        // to disagree, or `=> "userTask"` satisfies both. The first draft used
        // userTask here and the AC-4 mutation sweep walked straight through it.
        Assert.Equal("serviceTask", BpmnDiagram.ElementTypeIn(xml));
    }

    [Fact]
    public void ElementIn_fails_loudly_when_the_id_is_absent()
    {
        var xml = Diagram("""    <userTask id="Somewhere_Else" />""");

        // Silently returning null here would make every downstream read say
        // "no" rather than "this diagram is not the one you think it is".
        //
        // On the MESSAGE, not on `ThrowsAny<Exception>` (#490): any exception
        // satisfied that, so an XmlException from a malformed fixture would have
        // greened it while proving nothing about the id search.
        var thrown = Assert.ThrowsAny<Exception>(() => BpmnDiagram.ElementIn(xml, "Ev_1"));
        Assert.Contains("No element in this diagram carries id", thrown.Message, StringComparison.Ordinal);
    }

    // ---- EventDefinitionIn ---------------------------------------------------

    [Fact]
    public void EventDefinitionIn_reads_the_definition_in_the_manifests_vocabulary()
    {
        var xml = Diagram("""
            <intermediateCatchEvent id="Ev_1">
              <timerEventDefinition />
            </intermediateCatchEvent>
        """);

        // "timerEventDefinition" -> "timer", matching bpmn-support.json's column.
        Assert.Equal("timer", BpmnDiagram.EventDefinitionIn(xml));
    }

    [Fact]
    public void EventDefinitionIn_is_null_for_a_None_event()
    {
        var xml = Diagram("""    <startEvent id="Ev_1" />""");

        // Null is not "unknown" here -- it is the assertion for a None event.
        Assert.Null(BpmnDiagram.EventDefinitionIn(xml));
    }

    /// <summary>
    /// A definition belonging to something nested is not the container's (#435).
    /// </summary>
    /// <remarks>
    /// A descendant search reports "message" for this sub-process, which would
    /// let a container match a manifest row for an event it merely contains.
    /// </remarks>
    [Fact]
    public void EventDefinitionIn_does_not_claim_a_nested_childs_definition()
    {
        var xml = Diagram("""
            <subProcess id="Ev_1">
              <startEvent id="Inner_Start">
                <messageEventDefinition />
              </startEvent>
            </subProcess>
        """);

        Assert.Null(BpmnDiagram.EventDefinitionIn(xml));
    }

    // ---- NestedIdsIn ---------------------------------------------------------

    [Fact]
    public void NestedIdsIn_returns_what_is_inside_and_not_the_container_itself()
    {
        var xml = Diagram("""
            <subProcess id="Ev_1">
              <startEvent id="Inner_Start" />
              <userTask id="Inner_Task" />
            </subProcess>
            <userTask id="Outside" />
        """);

        var nested = BpmnDiagram.NestedIdsIn(xml, "Ev_1");

        Assert.Equal(["Inner_Start", "Inner_Task"], nested.Order());

        // The complement matters as much: a container is not inside itself, and
        // a sibling is not inside it.
        Assert.DoesNotContain("Ev_1", nested);
        Assert.DoesNotContain("Outside", nested);
    }

    /// <summary>
    /// A nested same-tag child does not truncate the window (#434).
    /// </summary>
    /// <remarks>
    /// This is the exact defect the close-tag search had: it took the substring
    /// up to the first <c>&lt;/subProcess&gt;</c>, which is the INNER one, so
    /// everything after it — <c>Late_Task</c> here — was invisible.
    /// </remarks>
    [Fact]
    public void NestedIdsIn_is_not_truncated_by_a_nested_same_tag_child()
    {
        var xml = Diagram("""
            <subProcess id="Ev_1">
              <subProcess id="Inner_Sub">
                <userTask id="Deep_Task" />
              </subProcess>
              <userTask id="Late_Task" />
            </subProcess>
        """);

        Assert.Equal(
            ["Deep_Task", "Inner_Sub", "Late_Task"],
            BpmnDiagram.NestedIdsIn(xml, "Ev_1").Order());
    }

    // ---- CallActivityIdsIn ---------------------------------------------------

    [Fact]
    public void CallActivityIdsIn_finds_call_activities_however_they_are_prefixed()
    {
        var xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL">
              <bpmn:process id="P_1" isExecutable="true">
                <bpmn:callActivity id="Call_1" calledElement="child" />
                <bpmn:subProcess id="Wrapper">
                  <bpmn:callActivity id="Call_2" calledElement="child" />
                </bpmn:subProcess>
                <bpmn:userTask id="Not_A_Call" />
              </bpmn:process>
            </bpmn:definitions>
            """;

        Assert.Equal(["Call_1", "Call_2"], BpmnDiagram.CallActivityIdsIn(xml).Order());
    }

    [Fact]
    public void CallActivityIdsIn_is_empty_when_the_diagram_calls_nothing()
    {
        var xml = Diagram("""    <userTask id="Ev_1" />""");

        // An empty result is the useful answer here -- the observer for a call
        // activity asks "did a child instance start", and "there is nothing to
        // call" has to be distinguishable from "something was called".
        Assert.Empty(BpmnDiagram.CallActivityIdsIn(xml));
    }

    // ---- ConditionalFlowsFrom ------------------------------------------------

    [Fact]
    public void ConditionalFlowsFrom_sees_a_condition_on_an_outgoing_flow()
    {
        var xml = Diagram("""
            <exclusiveGateway id="Ev_1" />
            <sequenceFlow id="F_1" sourceRef="Ev_1" targetRef="A">
              <conditionExpression>${amount &gt; 10}</conditionExpression>
            </sequenceFlow>
            <sequenceFlow id="F_2" sourceRef="Ev_1" targetRef="B" />
        """);

        Assert.True(BpmnDiagram.ConditionalFlowsFrom(xml, "Ev_1"));
    }

    /// <summary>
    /// A diagram whose outgoing flows carry no conditions reports none.
    /// </summary>
    /// <remarks>
    /// This is what the routing complement keys on. Scoping it by gateway
    /// <em>type</em> instead turned the parallel cell red for forking to both
    /// branches — which is exactly what a parallel gateway is for (#452).
    /// </remarks>
    [Fact]
    public void ConditionalFlowsFrom_reports_none_when_a_fork_is_unconditional()
    {
        var xml = Diagram("""
            <parallelGateway id="Ev_1" />
            <sequenceFlow id="F_1" sourceRef="Ev_1" targetRef="A" />
            <sequenceFlow id="F_2" sourceRef="Ev_1" targetRef="B" />
        """);

        Assert.False(BpmnDiagram.ConditionalFlowsFrom(xml, "Ev_1"));
    }

    [Fact]
    public void ConditionalFlowsFrom_ignores_a_condition_on_someone_elses_flow()
    {
        var xml = Diagram("""
            <parallelGateway id="Ev_1" />
            <sequenceFlow id="F_1" sourceRef="Ev_1" targetRef="A" />
            <sequenceFlow id="F_2" sourceRef="Other" targetRef="B">
              <conditionExpression>${amount &gt; 10}</conditionExpression>
            </sequenceFlow>
        """);

        // Without the sourceRef filter this reads true for every diagram that has
        // a condition anywhere, which is most of them.
        Assert.False(BpmnDiagram.ConditionalFlowsFrom(xml, "Ev_1"));
    }

    // ---- FlowTargetsOf -------------------------------------------------------

    [Fact]
    public void FlowTargetsOf_returns_only_the_flows_leaving_that_element()
    {
        var xml = Diagram("""
            <parallelGateway id="Ev_1" />
            <sequenceFlow id="F_1" sourceRef="Ev_1" targetRef="A" />
            <sequenceFlow id="F_2" sourceRef="Ev_1" targetRef="B" />
            <sequenceFlow id="F_3" sourceRef="Other" targetRef="C" />
            <sequenceFlow id="F_4" sourceRef="A" targetRef="Ev_1" />
        """);

        var targets = BpmnDiagram.FlowTargetsOf(xml, "Ev_1");

        Assert.Equal(["A", "B"], targets.Order());

        // Someone else's flow is not yours, and an incoming flow is not outgoing
        // -- a source/target mix-up reads plausibly and is wrong in one direction.
        Assert.DoesNotContain("C", targets);
    }

    [Fact]
    public void FlowTargetsOf_is_empty_for_an_element_with_no_outgoing_flows()
    {
        var xml = Diagram("""
            <endEvent id="Ev_1" />
            <sequenceFlow id="F_1" sourceRef="A" targetRef="Ev_1" />
        """);

        Assert.Empty(BpmnDiagram.FlowTargetsOf(xml, "Ev_1"));
    }

    // ---- MarkerIn ------------------------------------------------------------

    [Theory]
    [InlineData("""<multiInstanceLoopCharacteristics isSequential="false" />""", "multiInstanceLoopCharacteristics:parallel")]
    [InlineData("""<multiInstanceLoopCharacteristics isSequential="true" />""", "multiInstanceLoopCharacteristics:sequential")]
    [InlineData("""<standardLoopCharacteristics />""", "standardLoopCharacteristics")]
    public void MarkerIn_reads_the_marker_in_the_manifests_vocabulary(string marker, string expected)
    {
        var xml = Diagram($"""    <userTask id="Ev_1">{marker}</userTask>""");

        Assert.Equal(expected, BpmnDiagram.MarkerIn(xml));
    }

    /// <summary>
    /// An absent <c>isSequential</c> is parallel, not a third answer.
    /// </summary>
    /// <remarks>
    /// BPMN's default, and Flowable's behaviour. Treating "absent" as unknown
    /// would leave a diagram that omits the attribute matching neither manifest
    /// row, and the cell would fail for a reason that is not a defect.
    /// </remarks>
    [Fact]
    public void MarkerIn_defaults_an_absent_isSequential_to_parallel()
    {
        var xml = Diagram("""    <userTask id="Ev_1"><multiInstanceLoopCharacteristics /></userTask>""");

        Assert.Equal("multiInstanceLoopCharacteristics:parallel", BpmnDiagram.MarkerIn(xml));
    }

    [Fact]
    public void MarkerIn_reads_the_compensation_marker_from_the_attribute()
    {
        var xml = Diagram("""    <userTask id="Ev_1" isForCompensation="true" />""");

        Assert.Equal("isForCompensation", BpmnDiagram.MarkerIn(xml));
    }

    /// <summary>
    /// An unmarked activity carries no marker — the archetype's complement.
    /// </summary>
    /// <remarks>
    /// This is the Loop Marker bug in miniature: the whole point of a marker row
    /// is that removing the marker must be visible. A helper that reported one
    /// anyway would green the exact regression #325 opens on.
    /// </remarks>
    [Fact]
    public void MarkerIn_is_null_for_an_unmarked_activity()
    {
        Assert.Null(BpmnDiagram.MarkerIn(Diagram("""    <userTask id="Ev_1" />""")));

        // And explicitly false is not "carries it".
        Assert.Null(BpmnDiagram.MarkerIn(
            Diagram("""    <userTask id="Ev_1" isForCompensation="false" />""")));
    }

    /// <summary>
    /// A marker on something else in the diagram is not this element's.
    /// </summary>
    [Fact]
    public void MarkerIn_does_not_borrow_a_marker_from_another_element()
    {
        var xml = Diagram("""
            <userTask id="Ev_1" />
            <userTask id="Other"><multiInstanceLoopCharacteristics isSequential="true" /></userTask>
        """);

        Assert.Null(BpmnDiagram.MarkerIn(xml));
    }

    /// <summary>
    /// A marker on a nested child is not the container's (#435, same shape).
    /// </summary>
    [Fact]
    public void MarkerIn_does_not_claim_a_nested_childs_marker()
    {
        var xml = Diagram("""
            <subProcess id="Ev_1">
              <userTask id="Inner"><multiInstanceLoopCharacteristics isSequential="true" /></userTask>
            </subProcess>
        """);

        Assert.Null(BpmnDiagram.MarkerIn(xml));
    }

    // ---- LoopCardinalityIn ---------------------------------------------------

    /// <summary>
    /// Both spellings, because Auton8 rewrites between them at publish.
    /// </summary>
    [Theory]
    [InlineData("""<multiInstanceLoopCharacteristics xmlns:autonate="http://autonate.dev/workflows" isSequential="false" autonate:loopCardinality="3" />""")]
    [InlineData("""<multiInstanceLoopCharacteristics isSequential="false"><loopCardinality>3</loopCardinality></multiInstanceLoopCharacteristics>""")]
    public void LoopCardinalityIn_reads_the_stored_and_the_deployed_spelling(string marker)
    {
        var xml = Diagram($"""    <userTask id="Ev_1">{marker}</userTask>""");

        Assert.Equal(3, BpmnDiagram.LoopCardinalityIn(xml));
    }

    [Fact]
    public void LoopCardinalityIn_is_null_when_there_is_no_marker_or_no_cardinality()
    {
        // No marker at all.
        Assert.Null(BpmnDiagram.LoopCardinalityIn(Diagram("""    <userTask id="Ev_1" />""")));

        // A marker driven by a collection rather than a fixed count -- a real
        // shape, and "no fixed cardinality" is the honest answer for it.
        Assert.Null(BpmnDiagram.LoopCardinalityIn(Diagram(
            """    <userTask id="Ev_1"><multiInstanceLoopCharacteristics isSequential="false" /></userTask>""")));
    }
}
