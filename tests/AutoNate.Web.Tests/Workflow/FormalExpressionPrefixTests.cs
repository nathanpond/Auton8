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
