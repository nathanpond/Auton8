using AutoNate.Web.Services.Workflow;
using Xunit;

namespace AutoNate.Web.Tests;

/// <summary>
/// A collaboration publishes as a set, or not at all (#169, superseding #578).
/// </summary>
/// <remarks>
/// <para>
/// #578 refused every multi-pool diagram, because only one definition survived a
/// publish: <c>DeployProcessAsync</c> read the deployment back by the workflow key
/// and the other pools existed in the engine where Auton8 could not see them.
/// #169 reads the whole set back by deployment id, so the refusal is no longer
/// "more than one pool" but "a collaboration that could not deploy as a set":
/// a participant whose process is missing, two processes sharing an id, or no
/// executable pool at all.
/// </para>
/// <para>
/// The save-path half is here too. <c>ApplyProcessMetadata</c> used to rename
/// <c>Descendants("process").FirstOrDefault()</c> and leave the participant
/// pointing at the old id -- the collaboration was broken before publish was
/// reached, and nothing covered it.
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

    /// <summary>The counterparty drawn for context: a pool with nothing in it, listed FIRST.</summary>
    private const string EmptyPoolFirst = """
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          id="Definitions_1" targetNamespace="http://autonate.dev/workflows">
          <bpmn:collaboration id="Collab_1">
            <bpmn:participant id="P_customer" name="Customer" processRef="customer" />
            <bpmn:participant id="P_supplier" name="Supplier" processRef="supplier" />
          </bpmn:collaboration>
          <bpmn:process id="customer" name="Customer" isExecutable="true" />
          <bpmn:process id="supplier" name="Supplier" isExecutable="true">
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

    // ── validation: what is refused now, and what no longer is ──────────────

    /// <summary>
    /// The inversion of #578's <c>Two_pools_are_refused_and_both_are_named</c>.
    /// </summary>
    [Fact]
    public void Two_executable_pools_are_no_longer_refused()
    {
        Assert.Empty(WorkflowBpmnXml.ValidateProcess(TwoPools).Errors);
    }

    [Fact]
    public void A_pool_whose_process_is_missing_is_refused_naming_the_pool()
    {
        var xml = TwoPools.Replace("processRef=\"seller\"", "processRef=\"nowhere\"", StringComparison.Ordinal);

        var error = Assert.Single(
            WorkflowBpmnXml.ValidateProcess(xml).Errors,
            e => e.Contains("Seller", StringComparison.Ordinal));
        Assert.Contains("nowhere", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Two_pools_sharing_a_process_id_are_refused()
    {
        var xml = TwoPools
            .Replace("processRef=\"seller\"", "processRef=\"buyer\"", StringComparison.Ordinal)
            .Replace("<bpmn:process id=\"seller\"", "<bpmn:process id=\"buyer\"", StringComparison.Ordinal);

        Assert.Contains(
            WorkflowBpmnXml.ValidateProcess(xml).Errors,
            e => e.Contains("share the process id 'buyer'", StringComparison.Ordinal));
    }

    [Fact]
    public void A_collaboration_with_no_executable_pool_is_refused()
    {
        var xml = EmptyPoolFirst.Replace(
            """
                <bpmn:startEvent id="ss" name="Start" />
                <bpmn:sequenceFlow id="sf" sourceRef="ss" targetRef="se" />
                <bpmn:endEvent id="se" name="End" />
            """,
            string.Empty,
            StringComparison.Ordinal);

        Assert.Contains(
            WorkflowBpmnXml.ValidateProcess(xml).Errors,
            e => e.Contains("None of the pools contains anything to run", StringComparison.Ordinal));
    }

    /// <summary>
    /// The regression guard #578 carried, kept: a lane inside ONE pool never
    /// counted as a second pool, and still does not.
    /// </summary>
    [Fact]
    public void One_pool_with_a_lane_still_publishes()
    {
        Assert.DoesNotContain(
            WorkflowBpmnXml.ValidateProcess(OnePool).Errors,
            e => e.Contains("pool", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_diagram_with_no_collaboration_is_unaffected()
    {
        Assert.Empty(WorkflowBpmnXml.ValidateProcess(NoCollaboration).Errors);
    }

    // ── the save path: the defect the replan found ──────────────────────────

    /// <summary>
    /// Renaming the primary process now carries its participant with it (#169).
    /// </summary>
    /// <remarks>
    /// Before: <c>P_buyer</c> kept <c>processRef="buyer"</c> while the process
    /// became <c>order</c>, so the collaboration referenced a process that no
    /// longer existed -- broken at save, before publish was reached. This asserts
    /// the reference, not the rename, because the rename always worked.
    /// </remarks>
    [Fact]
    public void Preparing_a_collaboration_repoints_the_primary_participant()
    {
        var prepared = WorkflowBpmnXml.ApplyProcessMetadata(TwoPools, "order", "Order");

        Assert.Contains("<bpmn:process id=\"order\"", prepared, StringComparison.Ordinal);
        Assert.Contains("processRef=\"order\"", prepared, StringComparison.Ordinal);
        Assert.DoesNotContain("processRef=\"buyer\"", prepared, StringComparison.Ordinal);

        // The other pool keeps its authored id -- a set, not a merge.
        Assert.Contains("<bpmn:process id=\"seller\"", prepared, StringComparison.Ordinal);
        Assert.Contains("processRef=\"seller\"", prepared, StringComparison.Ordinal);
    }

    /// <summary>
    /// The primary is the first pool WITH something in it, not the first pool in
    /// the file (#169).
    /// </summary>
    /// <remarks>
    /// The counterparty is drawn first here, on purpose. <c>FirstOrDefault()</c>
    /// would have renamed the empty Customer pool to the workflow key and forced
    /// it executable -- publishing a workflow whose "start" starts nothing.
    /// </remarks>
    [Fact]
    public void The_primary_is_the_first_pool_that_contains_a_flow()
    {
        var prepared = WorkflowBpmnXml.ApplyProcessMetadata(EmptyPoolFirst, "order", "Order");

        // Supplier became the workflow's process; Customer did not.
        Assert.Contains("<bpmn:process id=\"order\" name=\"Order\" isExecutable=\"true\"", prepared, StringComparison.Ordinal);
        Assert.Contains("<bpmn:participant id=\"P_supplier\" name=\"Supplier\" processRef=\"order\"", prepared, StringComparison.Ordinal);

        // And the empty pool is said to deploy nothing, in the XML itself.
        Assert.Contains("<bpmn:process id=\"customer\" name=\"Customer\" isExecutable=\"false\"", prepared, StringComparison.Ordinal);
    }

    [Fact]
    public void A_non_primary_pool_takes_its_participant_name_as_its_process_name()
    {
        var renamed = TwoPools.Replace("<bpmn:process id=\"seller\" name=\"Seller\"", "<bpmn:process id=\"seller\" name=\"Process_2\"", StringComparison.Ordinal);

        var prepared = WorkflowBpmnXml.ApplyProcessMetadata(renamed, "order", "Order");

        // The engine's definition name is what an execution will show as its
        // participant, so it has to be the pool's name and not the modeller's id.
        Assert.Contains("<bpmn:process id=\"seller\" name=\"Seller\"", prepared, StringComparison.Ordinal);
    }

    /// <summary>
    /// The regression that matters most: one pool and no pool prepare exactly as
    /// they did before this story.
    /// </summary>
    [Fact]
    public void One_pool_and_no_pool_diagrams_prepare_as_before()
    {
        var one = WorkflowBpmnXml.ApplyProcessMetadata(OnePool, "solo", "Solo");
        Assert.Contains("<bpmn:process id=\"solo\" name=\"Solo\" isExecutable=\"true\"", one, StringComparison.Ordinal);
        Assert.Contains("processRef=\"solo\"", one, StringComparison.Ordinal);
        Assert.DoesNotContain("processRef=\"only\"", one, StringComparison.Ordinal);

        var none = WorkflowBpmnXml.ApplyProcessMetadata(NoCollaboration, "solo", "Solo");
        Assert.Contains("<bpmn:process id=\"solo\" name=\"Solo\" isExecutable=\"true\"", none, StringComparison.Ordinal);
        Assert.DoesNotContain("participant", none, StringComparison.Ordinal);
    }

    // ── what the studio is told ─────────────────────────────────────────────

    [Fact]
    public void Describing_a_collaboration_names_the_primary_and_the_pools_that_deploy_nothing()
    {
        var pools = WorkflowBpmnXml.DescribeCollaboration(EmptyPoolFirst);

        Assert.Equal(2, pools.Count);
        var customer = Assert.Single(pools, p => p.Name == "Customer");
        var supplier = Assert.Single(pools, p => p.Name == "Supplier");

        Assert.False(customer.IsExecutable);
        Assert.False(customer.IsPrimary);
        Assert.True(supplier.IsExecutable);
        Assert.True(supplier.IsPrimary);
    }

    [Fact]
    public void Describing_a_diagram_with_no_collaboration_says_nothing()
    {
        Assert.Empty(WorkflowBpmnXml.DescribeCollaboration(NoCollaboration));
    }
}
