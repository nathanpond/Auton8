using System.Xml.Linq;
using AutoNate.Web.Services.Workflow;
using Xunit;

namespace AutoNate.Web.Tests;

/// <summary>
/// A lane's group is the default assignment of the user tasks it lists (#171):
/// written onto the DEPLOYED copy as <c>flowable:candidateGroups</c>, never onto
/// the stored diagram, and never over a task's own assignment.
/// </summary>
public sealed class LaneAssignmentTests
{
    private static readonly XNamespace Bpmn = "http://www.omg.org/spec/BPMN/20100524/MODEL";
    private static readonly XNamespace Flowable = "http://flowable.org/bpmn";
    private static readonly XNamespace Autonate = "http://autonate.dev/workflows";

    private const string Ns =
        "xmlns:bpmn=\"http://www.omg.org/spec/BPMN/20100524/MODEL\" "
        + "xmlns:flowable=\"http://flowable.org/bpmn\" "
        + "xmlns:autonate=\"http://autonate.dev/workflows\" "
        + "id=\"Definitions_1\" targetNamespace=\"http://autonate.dev/workflows\"";

    /// <summary>
    /// One pool, two lanes. Finance names a group; Legal names none. Three user
    /// tasks: one in Finance with nothing of its own, one in Finance with its own
    /// assignee, one in Legal. A fourth user task sits in no lane at all.
    /// </summary>
    private static string Diagram(string financeGroup = "11111111-1111-1111-1111-111111111111", string ownAssignment = " flowable:assignee=\"ana\"") => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions {Ns}>
          <bpmn:collaboration id="Collab_1">
            <bpmn:participant id="P_1" name="Us" processRef="p1" />
          </bpmn:collaboration>
          <bpmn:process id="p1" name="Us" isExecutable="true">
            <bpmn:laneSet id="LS_1">
              <bpmn:lane id="Lane_finance" name="Finance" autonate:groupId="{financeGroup}">
                <bpmn:flowNodeRef>start</bpmn:flowNodeRef>
                <bpmn:flowNodeRef>approve</bpmn:flowNodeRef>
                <bpmn:flowNodeRef>audit</bpmn:flowNodeRef>
              </bpmn:lane>
              <bpmn:lane id="Lane_legal" name="Legal">
                <bpmn:flowNodeRef>review</bpmn:flowNodeRef>
                <bpmn:flowNodeRef>end</bpmn:flowNodeRef>
              </bpmn:lane>
            </bpmn:laneSet>
            <bpmn:startEvent id="start" />
            <bpmn:sequenceFlow id="f1" sourceRef="start" targetRef="approve" />
            <bpmn:userTask id="approve" name="Approve" />
            <bpmn:sequenceFlow id="f2" sourceRef="approve" targetRef="audit" />
            <bpmn:userTask id="audit" name="Audit"{ownAssignment} />
            <bpmn:sequenceFlow id="f3" sourceRef="audit" targetRef="review" />
            <bpmn:userTask id="review" name="Review" />
            <bpmn:sequenceFlow id="f4" sourceRef="review" targetRef="loose" />
            <bpmn:userTask id="loose" name="Loose" />
            <bpmn:sequenceFlow id="f5" sourceRef="loose" targetRef="end" />
            <bpmn:endEvent id="end" />
          </bpmn:process>
        </bpmn:definitions>
        """;

    private static XElement Task(string xml, string id) =>
        XDocument.Parse(xml).Descendants(Bpmn + "userTask").Single(t => t.Attribute("id")?.Value == id);

    private static string? CandidateGroups(string xml, string id) =>
        Task(xml, id).Attribute(Flowable + "candidateGroups")?.Value;

    [Fact]
    public void A_task_in_a_lane_with_nothing_of_its_own_takes_the_lanes_group()
    {
        var deployed = WorkflowBpmnXml.ExpandForDeployment(Diagram());
        Assert.Equal("11111111-1111-1111-1111-111111111111", CandidateGroups(deployed, "approve"));
    }

    [Fact]
    public void A_task_with_its_own_assignee_keeps_it_and_takes_no_group()
    {
        var deployed = WorkflowBpmnXml.ExpandForDeployment(Diagram());
        var audit = Task(deployed, "audit");
        Assert.Equal("ana", audit.Attribute(Flowable + "assignee")?.Value);
        // The complement: the override is honoured by NOT also offering the task
        // to the lane's group, or the assignee's task would be everyone's.
        Assert.Null(audit.Attribute(Flowable + "candidateGroups"));
    }

    [Theory]
    [InlineData(" flowable:candidateUsers=\"ben,cat\"")]
    [InlineData(" flowable:candidateGroups=\"reviewers\"")]
    public void A_task_with_its_own_candidates_keeps_them(string own)
    {
        var deployed = WorkflowBpmnXml.ExpandForDeployment(Diagram(ownAssignment: own));
        var audit = Task(deployed, "audit");
        var lanes = audit.Attribute(Flowable + "candidateGroups")?.Value;
        Assert.NotEqual("11111111-1111-1111-1111-111111111111", lanes);
        if (own.Contains("candidateGroups", StringComparison.Ordinal))
        {
            Assert.Equal("reviewers", lanes);
        }
        else
        {
            Assert.Null(lanes);
            Assert.Equal("ben,cat", audit.Attribute(Flowable + "candidateUsers")?.Value);
        }
    }

    [Fact]
    public void A_task_in_a_lane_with_no_group_gets_nothing()
    {
        var deployed = WorkflowBpmnXml.ExpandForDeployment(Diagram());
        Assert.Null(CandidateGroups(deployed, "review"));
    }

    [Fact]
    public void A_task_no_lane_lists_gets_nothing_which_is_what_moving_out_of_all_lanes_leaves()
    {
        var deployed = WorkflowBpmnXml.ExpandForDeployment(Diagram());
        Assert.Null(CandidateGroups(deployed, "loose"));
    }

    [Fact]
    public void Only_user_tasks_are_assigned_even_when_the_lane_lists_other_nodes()
    {
        var deployed = WorkflowBpmnXml.ExpandForDeployment(Diagram());
        var start = XDocument.Parse(deployed).Descendants(Bpmn + "startEvent").Single();
        Assert.Null(start.Attribute(Flowable + "candidateGroups"));
    }

    [Fact]
    public void The_innermost_lane_naming_a_group_wins_for_a_nested_lane()
    {
        // The outer lane lists the task too, as bpmn-js does for every lane whose
        // bounds contain the shape. The inner lane's group is the one that means
        // "this row".
        var xml = Diagram().Replace(
            "<bpmn:flowNodeRef>audit</bpmn:flowNodeRef>",
            "<bpmn:flowNodeRef>audit</bpmn:flowNodeRef>"
            + "<bpmn:childLaneSet id=\"LS_inner\"><bpmn:lane id=\"Lane_payables\" name=\"Payables\" autonate:groupId=\"22222222-2222-2222-2222-222222222222\">"
            + "<bpmn:flowNodeRef>approve</bpmn:flowNodeRef></bpmn:lane></bpmn:childLaneSet>",
            StringComparison.Ordinal);
        Assert.Contains("Lane_payables", xml, StringComparison.Ordinal);

        var deployed = WorkflowBpmnXml.ExpandForDeployment(xml);
        Assert.Equal("22222222-2222-2222-2222-222222222222", CandidateGroups(deployed, "approve"));
    }

    [Fact]
    public void The_stored_diagram_is_not_rewritten_only_the_deployed_copy()
    {
        // Prepare (what save stores) leaves the task unassigned and the group on
        // the lane; that is what lets the studio say "from the lane" rather than
        // showing an assignment the author never made.
        var prepared = WorkflowBpmnXml.ApplyProcessMetadata(Diagram(), "p1", "Us");
        Assert.Null(CandidateGroups(prepared, "approve"));
        var lane = XDocument.Parse(prepared).Descendants(Bpmn + "lane").Single(l => l.Attribute("id")?.Value == "Lane_finance");
        Assert.Equal("11111111-1111-1111-1111-111111111111", lane.Attribute(Autonate + WorkflowBpmnXml.LaneGroupAttribute)?.Value);
    }

    [Fact]
    public void A_diagram_without_lanes_is_unchanged_by_the_expansion()
    {
        var xml = Diagram().Replace("autonate:groupId=\"11111111-1111-1111-1111-111111111111\"", "", StringComparison.Ordinal);
        var withLanes = WorkflowBpmnXml.ExpandForDeployment(xml);
        var noLanesAtAll = Diagram(financeGroup: "");
        var deployed = WorkflowBpmnXml.ExpandForDeployment(noLanesAtAll);
        foreach (var id in new[] { "approve", "audit", "review", "loose" })
        {
            Assert.Null(CandidateGroups(withLanes, id) is { } g && g != "reviewers" ? g : null);
            Assert.Null(CandidateGroups(deployed, id));
        }
        // audit keeps exactly what it was authored with.
        Assert.Equal("ana", Task(deployed, "audit").Attribute(Flowable + "assignee")?.Value);
    }

    [Fact]
    public void Every_lane_naming_a_group_is_extracted_with_its_label()
    {
        var lanes = WorkflowBpmnXml.ExtractLaneGroups(Diagram());
        var lane = Assert.Single(lanes);
        Assert.Equal("Lane_finance", lane.LaneId);
        Assert.Equal("Finance", lane.LaneName);
        Assert.Equal("11111111-1111-1111-1111-111111111111", lane.GroupId);
    }

    [Fact]
    public void A_lane_with_a_blank_group_is_not_extracted()
    {
        Assert.Empty(WorkflowBpmnXml.ExtractLaneGroups(Diagram(financeGroup: "  ")));
        Assert.Empty(WorkflowBpmnXml.ExtractLaneGroups(""));
    }
}
