using System.Xml.Linq;
using AutoNate.Web.Services.Workflow;
using Xunit;

namespace AutoNate.Web.Tests.Workflow;

/// <summary>
/// The expansions write a QName this document can resolve (#482).
/// </summary>
/// <remarks>
/// <para>
/// <c>xsi:type</c> takes a QName, and six places hard-coded the prefix
/// <c>bpmn</c>. A diagram that binds the BPMN namespace as the DEFAULT — legal
/// BPMN, and what a hand-written or API-posted diagram often does — leaves that
/// prefix unbound, and Flowable refuses the whole deployment with nothing
/// actionable reaching the author.
/// </para>
/// <para>
/// The live-engine half of this is the whole execution oracle: its diagrams are
/// default-namespace documents precisely so that every element re-proves this.
/// But that is Flowable-only, and this is a one-line regression waiting to
/// happen, so the shape is pinned here where a merge can see it.
/// </para>
/// </remarks>
public sealed class FormalExpressionPrefixTests
{
    private static readonly XNamespace Bpmn = "http://www.omg.org/spec/BPMN/20100524/MODEL";
    private static readonly XNamespace Xsi = "http://www.w3.org/2001/XMLSchema-instance";

    /// <summary>
    /// The case #482 was filed for: no <c>bpmn:</c> prefix anywhere.
    /// </summary>
    /// <remarks>
    /// Unprefixed is the RIGHT answer, not a dropped attribute: an unprefixed
    /// QName in an attribute value resolves against the default namespace, which
    /// here is the BPMN namespace.
    /// </remarks>
    [Fact]
    public void A_default_namespace_diagram_gets_an_unprefixed_type()
    {
        var expanded = XDocument.Parse(WorkflowBpmnXml.ExpandForDeployment(DefaultNamespace));

        var cardinality = expanded.Descendants(Bpmn + "loopCardinality").Single();

        Assert.Equal("3", cardinality.Value);
        Assert.Equal("tFormalExpression", (string?)cardinality.Attribute(Xsi + "type"));
    }

    /// <summary>
    /// And the spelling that already worked keeps working (#482).
    /// </summary>
    /// <remarks>
    /// The half that makes the fix a fix rather than a trade. bpmn-js emits
    /// prefixed documents, so this is every diagram the studio produces —
    /// answering "unprefixed" for them would have turned #482 into a wider
    /// version of itself.
    /// </remarks>
    [Fact]
    public void A_prefixed_diagram_keeps_its_prefix()
    {
        var expanded = XDocument.Parse(WorkflowBpmnXml.ExpandForDeployment(Prefixed));

        var cardinality = expanded.Descendants(Bpmn + "loopCardinality").Single();

        Assert.Equal("3", cardinality.Value);
        Assert.Equal("bpmn:tFormalExpression", (string?)cardinality.Attribute(Xsi + "type"));
    }

    /// <summary>An author's own prefix is theirs, not a guess (#482).</summary>
    /// <remarks>
    /// Nothing says the prefix has to be <c>bpmn</c>. A diagram binding the
    /// namespace as <c>b</c> is as legal as either other spelling, and a fix that
    /// merely swapped one hard-coded prefix for a conditional would still be
    /// wrong for it.
    /// </remarks>
    [Fact]
    public void An_unusual_prefix_is_used_as_the_author_bound_it()
    {
        var expanded = XDocument.Parse(WorkflowBpmnXml.ExpandForDeployment(
            Prefixed.Replace("bpmn", "b", StringComparison.Ordinal)));

        var cardinality = expanded.Descendants(Bpmn + "loopCardinality").Single();

        Assert.Equal("b:tFormalExpression", (string?)cardinality.Attribute(Xsi + "type"));
    }

    /// <summary>
    /// The SNAPSHOT paths emit the document's own prefix too (#549).
    /// </summary>
    /// <remarks>
    /// <para>
    /// #482 fixed <b>seven</b> sites and this file now reaches all seven — which
    /// took two goes, because both #549 and its fix miscounted in the same
    /// direction (#554). The three <c>loopCardinality</c> facts above reach
    /// <b>one</b> expansion site, not three: the other two need an
    /// <c>autonate:completionCondition</c> and a <c>complexGateway</c>, and no
    /// fixture had either, so that code never ran. The site map, so the next
    /// reader can check the claim rather than trust it:
    /// </para>
    /// <list type="bullet">
    ///   <item><c>ExpandMultiInstanceCardinality</c> — the three facts above</item>
    ///   <item><c>ExpandCompletionConditions</c> — <c>A_completion_condition_…</c></item>
    ///   <item><c>ExpandComplexGateways</c> — <c>A_complex_gateways_generated_condition_…</c></item>
    ///   <item><c>ApplyAutoNateGatewayConditions</c> — <c>A_gateway_choice_condition_…</c></item>
    ///   <item><c>ApplySequenceFlowSnapshot</c> — the two snapshot tests below</item>
    ///   <item><c>ApplyConditionalEventSnapshot</c> — <c>A_conditional_events_condition_…</c></item>
    ///   <item><c>ApplyTimerBoundaryEventSnapshot</c> — <c>A_timer_boundary_…</c></item>
    /// </list>
    /// <para>
    /// Every one runs both spellings, so a re-hard-coded <c>bpmn:</c> and an
    /// always-unprefixed answer each fail one row of the same theory.
    /// </para>
    /// <para>
    /// This drives the snapshot path directly, on both spellings, because the
    /// near miss #482 caught in review was in exactly this half: one of these
    /// sites resolved the prefix from an element that may not yet be in the tree,
    /// and a detached element answers "unprefixed" for EVERY document.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(false, "tFormalExpression")]
    [InlineData(true, "bpmn:tFormalExpression")]
    public void A_sequence_flow_condition_from_a_snapshot_uses_the_documents_prefix(
        bool prefixed, string expected)
    {
        var applied = XDocument.Parse(WorkflowBpmnXml.ApplyProcessMetadata(
            prefixed ? Prefixed : DefaultNamespace,
            "p",
            "Flow",
            [new WorkflowElementSnapshot(
                Id: "f2", Type: "bpmn:SequenceFlow", Name: null,
                ConditionExpression: "${ok == true}")]));

        var condition = applied.Descendants(Bpmn + "conditionExpression").Single();

        Assert.Equal("${ok == true}", condition.Value);
        Assert.Equal(expected, (string?)condition.Attribute(Xsi + "type"));
    }

    /// <summary>
    /// And the one whose element may still be detached when it is written (#549).
    /// </summary>
    /// <remarks>
    /// A flow with no existing <c>conditionExpression</c> takes the branch that
    /// CONSTRUCTS one and adds it afterwards. Resolving the prefix from that
    /// element rather than from the flow would answer "unprefixed" for every
    /// document — turning #482 into a wider version of itself, silently.
    /// </remarks>
    [Fact]
    public void A_condition_added_to_a_flow_that_had_none_still_uses_the_prefix()
    {
        var applied = XDocument.Parse(WorkflowBpmnXml.ApplyProcessMetadata(
            Prefixed,
            "p",
            "Flow",
            [new WorkflowElementSnapshot(
                Id: "f1", Type: "bpmn:SequenceFlow", Name: null,
                ConditionExpression: "${ok == true}")]));

        var condition = applied.Descendants(Bpmn + "conditionExpression")
            .Single(c => c.Parent!.Attribute("id")!.Value == "f1");

        Assert.Equal("bpmn:tFormalExpression", (string?)condition.Attribute(Xsi + "type"));
    }

    /// <summary>
    /// The other two expansion sites, which no fixture reached (#554).
    /// </summary>
    /// <remarks>
    /// <c>ExpandCompletionConditions</c> needs an
    /// <c>autonate:completionCondition</c> and <c>ExpandComplexGateways</c> needs
    /// a <c>&lt;complexGateway&gt;</c>. The <c>loopCardinality</c> fixtures carry
    /// neither, so both sites were dead code under test while the comment above
    /// counted them as covered.
    /// </remarks>
    [Theory]
    [InlineData(false, "tFormalExpression")]
    [InlineData(true, "bpmn:tFormalExpression")]
    public void A_completion_condition_uses_the_documents_prefix(bool prefixed, string expected)
    {
        var expanded = XDocument.Parse(WorkflowBpmnXml.ExpandForDeployment(Doc(prefixed, """
              <:startEvent id="s" />
              <:userTask id="t" name="approve">
                <:multiInstanceLoopCharacteristics isSequential="false"
                     autonate:loopCardinality="3" autonate:completionCondition="${done}" />
              </:userTask>
              <:endEvent id="e" />
              <:sequenceFlow id="f1" sourceRef="s" targetRef="t" />
              <:sequenceFlow id="f2" sourceRef="t" targetRef="e" />
            """)));

        var completion = expanded.Descendants(Bpmn + "completionCondition").Single();
        Assert.Equal(expected, (string?)completion.Attribute(Xsi + "type"));
    }

    /// <inheritdoc cref="A_completion_condition_uses_the_documents_prefix"/>
    [Theory]
    [InlineData(false, "tFormalExpression")]
    [InlineData(true, "bpmn:tFormalExpression")]
    public void A_complex_gateways_generated_condition_uses_the_documents_prefix(
        bool prefixed, string expected)
    {
        var expanded = XDocument.Parse(WorkflowBpmnXml.ExpandForDeployment(Doc(prefixed, """
              <:startEvent id="s" />
              <:complexGateway id="g" autonate:routeScript="return 'f2';" autonate:scriptFormat="javascript" />
              <:endEvent id="e1" />
              <:endEvent id="e2" />
              <:sequenceFlow id="f1" sourceRef="s" targetRef="g" />
              <:sequenceFlow id="f2" sourceRef="g" targetRef="e1" />
              <:sequenceFlow id="f3" sourceRef="g" targetRef="e2" />
            """)));

        var conditions = expanded.Descendants(Bpmn + "conditionExpression").ToList();
        Assert.NotEmpty(conditions);
        Assert.All(conditions, c => Assert.Equal(expected, (string?)c.Attribute(Xsi + "type")));
    }

    /// <summary>
    /// The synthetic gateway-choice condition (#554).
    /// </summary>
    /// <remarks>
    /// <c>ApplyAutoNateGatewayConditions</c> writes <c>${__autonateChosenFlow ==
    /// 'f'}</c> onto each unconditioned outflow of an exclusive gateway a user
    /// task feeds. Reached by <c>ApplyProcessMetadata</c>, not by
    /// <c>ExpandForDeployment</c>, and previously unguarded for this attribute.
    /// </remarks>
    [Theory]
    [InlineData(false, "tFormalExpression")]
    [InlineData(true, "bpmn:tFormalExpression")]
    public void A_gateway_choice_condition_uses_the_documents_prefix(bool prefixed, string expected)
    {
        var applied = XDocument.Parse(WorkflowBpmnXml.ApplyProcessMetadata(
            Doc(prefixed, """
              <:startEvent id="s" />
              <:userTask id="t" name="approve" />
              <:exclusiveGateway id="g" />
              <:endEvent id="e1" />
              <:endEvent id="e2" />
              <:sequenceFlow id="f1" sourceRef="s" targetRef="t" />
              <:sequenceFlow id="f2" sourceRef="t" targetRef="g" />
              <:sequenceFlow id="f3" sourceRef="g" targetRef="e1" />
              <:sequenceFlow id="f4" sourceRef="g" targetRef="e2" />
            """),
            "p",
            "Flow"));

        var conditions = applied.Descendants(Bpmn + "conditionExpression").ToList();
        Assert.NotEmpty(conditions);
        Assert.All(conditions, c => Assert.Equal(expected, (string?)c.Attribute(Xsi + "type")));
    }

    /// <summary>The other two snapshot sites (#554).</summary>
    [Theory]
    [InlineData(false, "tFormalExpression")]
    [InlineData(true, "bpmn:tFormalExpression")]
    public void A_conditional_events_condition_from_a_snapshot_uses_the_documents_prefix(
        bool prefixed, string expected)
    {
        var applied = XDocument.Parse(WorkflowBpmnXml.ApplyProcessMetadata(
            Doc(prefixed, """
              <:startEvent id="s" />
              <:intermediateCatchEvent id="c">
                <:conditionalEventDefinition />
              </:intermediateCatchEvent>
              <:endEvent id="e" />
              <:sequenceFlow id="f1" sourceRef="s" targetRef="c" />
              <:sequenceFlow id="f2" sourceRef="c" targetRef="e" />
            """),
            "p",
            "Flow",
            [new WorkflowElementSnapshot(
                Id: "c", Type: "bpmn:IntermediateCatchEvent", Name: null,
                ConditionExpression: "${ready == true}")]));

        var condition = applied.Descendants(Bpmn + "condition").Single();
        Assert.Equal("${ready == true}", condition.Value);
        Assert.Equal(expected, (string?)condition.Attribute(Xsi + "type"));
    }

    /// <inheritdoc cref="A_conditional_events_condition_from_a_snapshot_uses_the_documents_prefix"/>
    [Theory]
    [InlineData(false, "tFormalExpression")]
    [InlineData(true, "bpmn:tFormalExpression")]
    public void A_timer_boundary_from_a_snapshot_uses_the_documents_prefix(
        bool prefixed, string expected)
    {
        var applied = XDocument.Parse(WorkflowBpmnXml.ApplyProcessMetadata(
            Doc(prefixed, """
              <:startEvent id="s" />
              <:userTask id="t" name="approve" />
              <:boundaryEvent id="b" attachedToRef="t">
                <:timerEventDefinition />
              </:boundaryEvent>
              <:endEvent id="e1" />
              <:endEvent id="e2" />
              <:sequenceFlow id="f1" sourceRef="s" targetRef="t" />
              <:sequenceFlow id="f2" sourceRef="t" targetRef="e1" />
              <:sequenceFlow id="f3" sourceRef="b" targetRef="e2" />
            """),
            "p",
            "Flow",
            [new WorkflowElementSnapshot(
                Id: "b", Type: "bpmn:BoundaryEvent", Name: null,
                BoundaryTimerDuration: "PT5M")]));

        var duration = applied.Descendants(Bpmn + "timeDuration").Single();
        Assert.Equal("PT5M", duration.Value);
        Assert.Equal(expected, (string?)duration.Attribute(Xsi + "type"));
    }

    /// <summary>
    /// One fixture shape, both spellings (#554).
    /// </summary>
    /// <remarks>
    /// <c>&lt;:tag&gt;</c> marks a BPMN-namespaced element, so each body is
    /// written once and rendered either default-namespaced or <c>bpmn:</c>-
    /// prefixed. Two hand-maintained copies of five diagrams is how one of them
    /// quietly stops matching the other.
    /// </remarks>
    private static string Doc(bool prefixed, string body)
    {
        var tag = prefixed ? "bpmn:" : string.Empty;
        var declaration = prefixed
            ? "xmlns:bpmn=\"http://www.omg.org/spec/BPMN/20100524/MODEL\""
            : "xmlns=\"http://www.omg.org/spec/BPMN/20100524/MODEL\"";

        return $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <{tag}definitions {declaration}
                 xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
                 xmlns:flowable="http://flowable.org/bpmn"
                 xmlns:autonate="http://autonate.dev/workflows"
                 targetNamespace="http://autonate.dev/workflows">
              <{tag}process id="p" isExecutable="true">
            {body.Replace("</:", $"</{tag}").Replace("<:", $"<{tag}")}
              </{tag}process>
            </{tag}definitions>
            """;
    }

    private const string DefaultNamespace = """
        <?xml version="1.0" encoding="UTF-8"?>
        <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL"
                     xmlns:flowable="http://flowable.org/bpmn"
                     xmlns:autonate="http://autonate.dev/workflows"
                     targetNamespace="http://autonate.dev/workflows">
          <process id="p" isExecutable="true">
            <startEvent id="s" />
            <userTask id="t" name="approve">
              <multiInstanceLoopCharacteristics isSequential="false" autonate:loopCardinality="3" />
            </userTask>
            <endEvent id="e" />
            <sequenceFlow id="f1" sourceRef="s" targetRef="t" />
            <sequenceFlow id="f2" sourceRef="t" targetRef="e" />
          </process>
        </definitions>
        """;

    private const string Prefixed = """
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          xmlns:flowable="http://flowable.org/bpmn"
                          xmlns:autonate="http://autonate.dev/workflows"
                          targetNamespace="http://autonate.dev/workflows">
          <bpmn:process id="p" isExecutable="true">
            <bpmn:startEvent id="s" />
            <bpmn:userTask id="t" name="approve">
              <bpmn:multiInstanceLoopCharacteristics isSequential="false" autonate:loopCardinality="3" />
            </bpmn:userTask>
            <bpmn:endEvent id="e" />
            <bpmn:sequenceFlow id="f1" sourceRef="s" targetRef="t" />
            <bpmn:sequenceFlow id="f2" sourceRef="t" targetRef="e" />
          </bpmn:process>
        </bpmn:definitions>
        """;
}
