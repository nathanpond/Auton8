using System.Xml.Linq;
using AutoNate.Web.Services.Workflow;
using Xunit;

namespace AutoNate.Web.Tests.Workflow;

/// <summary>
/// Multi-instance settings survive the studio and reach the engine (#245).
/// </summary>
/// <remarks>
/// <para>
/// #159 ticked two criteria that were not implemented: results aggregating back
/// into a variable on the parent, and a fixed instance count. Neither had a field
/// in the panel, neither was read or written by <c>workflow.js</c>, and the
/// manifest asserted the second worked on the strength of a manual probe.
/// </para>
/// <para>
/// They are stored as attributes and rebuilt at publish for the same reason the
/// completion condition is: bpmn-js is vendored with no Flowable moddle
/// extension, so a <c>&lt;bpmn:loopCardinality&gt;</c> child and a
/// <c>&lt;flowable:variableAggregation&gt;</c> extension element are both dropped
/// on the author's next save, silently.
/// </para>
/// <para>
/// The ordering assertions are not fussiness. The completion condition one
/// element over had to be appended LAST or Flowable refuses the deployment with
/// <c>cvc-complex-type.2.4.d</c>; these children sit before it in the same
/// sequence, so getting them wrong breaks publish for every diagram that has both.
/// </para>
/// </remarks>
public sealed class MultiInstanceExpansionTests
{
    private static readonly XNamespace Bpmn = "http://www.omg.org/spec/BPMN/20100524/MODEL";
    private static readonly XNamespace Flowable = "http://flowable.org/bpmn";
    private static readonly XNamespace AutoNate = "http://autonate.dev/workflows";

    private static string Diagram(string loopAttributes) => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          xmlns:flowable="http://flowable.org/bpmn"
                          xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
                          xmlns:autonate="http://autonate.dev/workflows"
                          id="Definitions_1" targetNamespace="http://autonate.dev/workflows">
          <bpmn:process id="mi" name="Repeat" isExecutable="true">
            <bpmn:startEvent id="s" />
            <bpmn:sequenceFlow id="f0" sourceRef="s" targetRef="work" />
            <bpmn:userTask id="work" name="Review one">
              <bpmn:multiInstanceLoopCharacteristics isSequential="false" {loopAttributes} />
            </bpmn:userTask>
            <bpmn:sequenceFlow id="f1" sourceRef="work" targetRef="e" />
            <bpmn:endEvent id="e" />
          </bpmn:process>
        </bpmn:definitions>
        """;

    private static XElement Loop(string xml) =>
        XDocument.Parse(xml).Descendants(Bpmn + "multiInstanceLoopCharacteristics").Single();

    // ── Cardinality ─────────────────────────────────────────────────────────

    [Fact]
    public void A_fixed_count_becomes_a_loopCardinality_child()
    {
        var deployed = WorkflowBpmnXml.ExpandForDeployment(
            Diagram("""autonate:loopCardinality="3" """));

        var cardinality = Loop(deployed).Element(Bpmn + "loopCardinality");
        Assert.NotNull(cardinality);
        Assert.Equal("3", cardinality!.Value);

        // The stored spelling does not reach the engine: Flowable does not know
        // the attribute, and leaving it on is how an unknown attribute becomes a
        // schema refusal the next time the validation tightens.
        Assert.Null(Loop(deployed).Attribute(AutoNate + "loopCardinality"));
    }

    [Fact]
    public void An_expression_count_is_carried_through_unevaluated()
    {
        // The count is often "however many approvers this order needs", which is
        // an expression the ENGINE resolves. Quoting or escaping it here would
        // turn it into a literal.
        var deployed = WorkflowBpmnXml.ExpandForDeployment(
            Diagram("""autonate:loopCardinality="${approverCount}" """));

        Assert.Equal("${approverCount}", Loop(deployed).Element(Bpmn + "loopCardinality")!.Value);
    }

    [Fact]
    public void A_hand_written_loopCardinality_is_left_alone()
    {
        var authored = Diagram("").Replace(
            """<bpmn:multiInstanceLoopCharacteristics isSequential="false"  />""",
            """
            <bpmn:multiInstanceLoopCharacteristics isSequential="false">
                  <bpmn:loopCardinality xsi:type="bpmn:tFormalExpression">7</bpmn:loopCardinality>
                </bpmn:multiInstanceLoopCharacteristics>
            """.Trim(),
            StringComparison.Ordinal);

        var deployed = WorkflowBpmnXml.ExpandForDeployment(authored);

        var children = Loop(deployed).Elements(Bpmn + "loopCardinality").ToList();
        Assert.Single(children);
        Assert.Equal("7", children[0].Value);
    }

    // ── Aggregation ─────────────────────────────────────────────────────────

    [Fact]
    public void Collecting_results_becomes_a_flowable_variableAggregation()
    {
        var deployed = WorkflowBpmnXml.ExpandForDeployment(Diagram(
            """flowable:collection="${items}" flowable:elementVariable="item" """ +
            """autonate:aggregateSource="score" autonate:aggregateTarget="scores" """));

        var aggregation = Loop(deployed)
            .Element(Bpmn + "extensionElements")?
            .Element(Flowable + "variableAggregation");

        Assert.NotNull(aggregation);
        Assert.Equal("scores", aggregation!.Attribute("target")?.Value);

        var variable = Assert.Single(aggregation.Elements(Flowable + "variable"));
        Assert.Equal("score", variable.Attribute("source")?.Value);

        Assert.Null(Loop(deployed).Attribute(AutoNate + "aggregateSource"));
        Assert.Null(Loop(deployed).Attribute(AutoNate + "aggregateTarget"));
    }

    [Theory]
    [InlineData("""autonate:aggregateSource="score" """)]
    [InlineData("""autonate:aggregateTarget="scores" """)]
    public void Half_an_aggregation_produces_no_extension_element(string half)
    {
        // Publish refuses this pairing (see the validation tests below), but the
        // expansion must not emit a malformed aggregation on the way there --
        // a <variableAggregation> with no target is a deployment failure rather
        // than a message the author can act on.
        var deployed = WorkflowBpmnXml.ExpandForDeployment(
            Diagram($$"""flowable:collection="${items}" {{half}}"""));

        Assert.DoesNotContain("variableAggregation", deployed, StringComparison.Ordinal);
    }

    // ── The schema sequence ─────────────────────────────────────────────────

    [Fact]
    public void The_children_land_in_the_order_the_schema_demands()
    {
        // extensionElements, then loopCardinality, then completionCondition.
        // Appending all three -- the obvious implementation -- deploys fine until
        // an author uses two of them together, and then Flowable refuses with
        // cvc-complex-type.2.4.d and the message names an element the author
        // never typed.
        var deployed = WorkflowBpmnXml.ExpandForDeployment(Diagram(
            """autonate:loopCardinality="3" autonate:aggregateSource="score" """ +
            """autonate:aggregateTarget="scores" """ +
            """autonate:completionCondition="${nrOfCompletedInstances >= 2}" """));

        var names = Loop(deployed).Elements().Select(child => child.Name.LocalName).ToList();

        Assert.Equal(["extensionElements", "loopCardinality", "completionCondition"], names);
    }

    [Fact]
    public void An_aggregation_joins_extension_elements_that_are_already_there()
    {
        var authored = Diagram("""autonate:aggregateSource="score" autonate:aggregateTarget="scores" """)
            .Replace(
                """<bpmn:multiInstanceLoopCharacteristics isSequential="false" autonate:aggregateSource="score" autonate:aggregateTarget="scores"  />""",
                """
                <bpmn:multiInstanceLoopCharacteristics isSequential="false"
                                                       autonate:aggregateSource="score"
                                                       autonate:aggregateTarget="scores">
                      <bpmn:extensionElements>
                        <flowable:something />
                      </bpmn:extensionElements>
                    </bpmn:multiInstanceLoopCharacteristics>
                """.Trim(),
                StringComparison.Ordinal);

        var deployed = WorkflowBpmnXml.ExpandForDeployment(authored);
        var extensions = Loop(deployed).Elements(Bpmn + "extensionElements").ToList();

        // A second extensionElements element is invalid; the existing one is
        // where the aggregation belongs.
        Assert.Single(extensions);
        Assert.NotNull(extensions[0].Element(Flowable + "variableAggregation"));
        Assert.NotNull(extensions[0].Element(Flowable + "something"));
    }

    [Fact]
    public void A_completion_condition_on_a_multi_instance_owner_is_expanded_too()
    {
        // Only the ad-hoc owner had a test. The same function serves both, and
        // the schema-ordering trap it guards applies to this owner as well.
        var deployed = WorkflowBpmnXml.ExpandForDeployment(
            Diagram("""autonate:completionCondition="${nrOfCompletedInstances >= 2}" """));

        var condition = Loop(deployed).Element(Bpmn + "completionCondition");
        Assert.NotNull(condition);
        Assert.Equal("${nrOfCompletedInstances >= 2}", condition!.Value);
    }

    // ── The refusals ────────────────────────────────────────────────────────

    [Fact]
    public void A_list_and_a_fixed_count_together_are_refused()
    {
        // Flowable reads the collection and ignores the count. The author asked
        // for exactly N runs, gets one per item, and nothing anywhere says so.
        var result = WorkflowBpmnXml.ValidateProcess(Diagram(
            """flowable:collection="${items}" autonate:loopCardinality="3" """));

        var error = Assert.Single(result.Errors);
        Assert.Contains("Review one", error, StringComparison.Ordinal);
        Assert.Contains("ignores the number", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""autonate:aggregateTarget="scores" """, "which variable to collect")]
    [InlineData("""autonate:aggregateSource="score" """, "where to put the results")]
    public void Half_an_aggregation_is_refused_with_the_half_that_is_missing(
        string half, string expected)
    {
        var result = WorkflowBpmnXml.ValidateProcess(
            Diagram($$"""flowable:collection="${items}" {{half}}"""));

        var error = Assert.Single(result.Errors);
        Assert.Contains("Review one", error, StringComparison.Ordinal);
        Assert.Contains(expected, error, StringComparison.Ordinal);
    }

    [Fact]
    public void A_complete_multi_instance_configuration_is_accepted()
    {
        // The complement of every refusal above. Without it, a validation that
        // refused everything would pass all four.
        var result = WorkflowBpmnXml.ValidateProcess(Diagram(
            """flowable:collection="${items}" flowable:elementVariable="item" """ +
            """autonate:aggregateSource="score" autonate:aggregateTarget="scores" """ +
            """autonate:completionCondition="${nrOfCompletedInstances >= 2}" """));

        Assert.Empty(result.Errors);
    }
}
