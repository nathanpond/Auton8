using AutoNate.Web.Services.Workflow;
using Xunit;

namespace AutoNate.Web.Tests;

/// <summary>
/// A multi-pool diagram is refused at publish (#578).
/// </summary>
/// <remarks>
/// <para>
/// It used to publish <b>silently</b>. Pool, Participant, Lane and Message Flow
/// are all <c>engine: annotation</c> in <c>bpmn-support.json</c> — documented
/// there as "deploys and carries no execution semantics by design, never
/// refused" — so the unsupported-element check let the diagram through. Flowable
/// accepted a deployment holding two process definitions, and
/// <c>DeployProcessAsync</c> read it back by process key, which resolves one. The
/// second definition existed in the engine, appeared in no <c>workflow_models</c>
/// row, and was unreachable from every Auton8 surface.
/// </para>
/// </remarks>
public sealed class MultiPoolPublishRefusalTests
{
    private const string TwoPools = """
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          id="Definitions_1" targetNamespace="http://autonate.dev/workflows">
          <bpmn:collaboration id="Collab_1">
            <bpmn:participant id="P_buyer" name="Buyer" processRef="buyer" />
            <bpmn:participant id="P_seller" name="Seller" processRef="seller" />
          </bpmn:collaboration>
          <bpmn:process id="buyer" name="Buyer" isExecutable="true">
            <bpmn:startEvent id="bs" name="Start" />
            <bpmn:sequenceFlow id="bf" sourceRef="bs" targetRef="be" />
            <bpmn:endEvent id="be" name="End" />
          </bpmn:process>
          <bpmn:process id="seller" name="Seller" isExecutable="true">
            <bpmn:startEvent id="ss" name="Start" />
            <bpmn:sequenceFlow id="sf" sourceRef="ss" targetRef="se" />
            <bpmn:endEvent id="se" name="End" />
          </bpmn:process>
        </bpmn:definitions>
        """;

    private const string OnePool = """
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          id="Definitions_1" targetNamespace="http://autonate.dev/workflows">
          <bpmn:collaboration id="Collab_1">
            <bpmn:participant id="P_only" name="Only" processRef="only" />
          </bpmn:collaboration>
          <bpmn:process id="only" name="Only" isExecutable="true">
            <bpmn:laneSet id="ls">
              <bpmn:lane id="l1" name="Reviewers"><bpmn:flowNodeRef>os</bpmn:flowNodeRef></bpmn:lane>
            </bpmn:laneSet>
            <bpmn:startEvent id="os" name="Start" />
            <bpmn:sequenceFlow id="of" sourceRef="os" targetRef="oe" />
            <bpmn:endEvent id="oe" name="End" />
          </bpmn:process>
        </bpmn:definitions>
        """;

    private const string NoCollaboration = """
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          id="Definitions_1" targetNamespace="http://autonate.dev/workflows">
          <bpmn:process id="plain" name="Plain" isExecutable="true">
            <bpmn:startEvent id="ps" name="Start" />
            <bpmn:sequenceFlow id="pf" sourceRef="ps" targetRef="pe" />
            <bpmn:endEvent id="pe" name="End" />
          </bpmn:process>
        </bpmn:definitions>
        """;

    [Fact]
    public void Two_pools_are_refused_and_both_are_named()
    {
        var error = Assert.Single(
            WorkflowBpmnXml.ValidateProcess(TwoPools).Errors,
            e => e.Contains("pools", StringComparison.OrdinalIgnoreCase));

        // Named, because a second pool can be collapsed or off-screen and
        // "multi-pool is not supported" alone leaves an author hunting for it.
        Assert.Contains("Buyer", error, StringComparison.Ordinal);
        Assert.Contains("Seller", error, StringComparison.Ordinal);
    }

    /// <summary>
    /// A single pool — with a lane — still publishes (#578).
    /// </summary>
    /// <remarks>
    /// This is AC6, and it is the assertion that keeps the refusal honest. The
    /// manifest is untouched: Pool, Lane and Message Flow keep
    /// <c>engine: annotation</c> and are still never refused on their own. What is
    /// refused is the multi-pool <i>shape</i>, which is why the count is of
    /// participants rather than of any element the manifest names.
    /// </remarks>
    [Fact]
    public void One_pool_with_a_lane_still_publishes()
    {
        Assert.DoesNotContain(
            WorkflowBpmnXml.ValidateProcess(OnePool).Errors,
            e => e.Contains("pools", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// A diagram with no collaboration is untouched (#578).
    /// </summary>
    /// <remarks>
    /// The complement: the refusal must not widen into ordinary diagrams, which is
    /// nearly every diagram in the product.
    /// </remarks>
    [Fact]
    public void A_diagram_with_no_collaboration_is_unaffected()
    {
        Assert.Empty(WorkflowBpmnXml.ValidateProcess(NoCollaboration).Errors);
    }
}
