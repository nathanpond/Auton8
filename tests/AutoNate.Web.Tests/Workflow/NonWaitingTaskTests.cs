using AutoNate.Web.Services.Workflow;
using Xunit;

namespace AutoNate.Web.Tests.Workflow;

/// <summary>
/// Manual and generic tasks are converted away, and refused if they survive (#167).
/// </summary>
/// <remarks>
/// <para>
/// Neither waits. Verified against Flowable 8.0.0 by running both: the process reached
/// the activity beyond without creating a task or pausing anywhere.
/// <c>ManualTaskActivityBehavior</c> is 488 bytes, and BPMN specifies a manual task as
/// work done outside the system with no engine involvement; a plain <c>bpmn:task</c>
/// behaves identically. A diagram containing either finishes having skipped the step
/// somebody was meant to perform.
/// </para>
/// <para>
/// The studio converts both to user tasks at design time. What is tested here is the
/// **backstop** — a diagram the studio never touched, hand-edited or posted straight
/// to the API.
/// </para>
/// </remarks>
public sealed class NonWaitingTaskTests
{
    private static string Process(string body) => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          xmlns:flowable="http://flowable.org/bpmn"
                          targetNamespace="http://autonate.dev/workflows">
          <bpmn:process id="p" name="P" isExecutable="true">
            <bpmn:startEvent id="s" />
        {body}
            <bpmn:endEvent id="e" />
          </bpmn:process>
        </bpmn:definitions>
        """;

    [Theory]
    [InlineData("""<bpmn:manualTask id="t" name="Post the cheque" />""", "Manual task")]
    [InlineData("""<bpmn:task id="t" name="Post the cheque" />""", "Task")]
    public void A_task_the_engine_walks_past_is_refused(string element, string kind)
    {
        var result = WorkflowBpmnXml.ValidateProcess(Process(element));

        var error = Assert.Single(result.Errors, e => e.Contains("Post the cheque", StringComparison.Ordinal));
        Assert.StartsWith(kind, error, StringComparison.Ordinal);
        // Says what actually happens, not just that it is disallowed — an author who
        // drew this believed it was a step somebody performs.
        Assert.Contains("without waiting for anyone", error, StringComparison.Ordinal);
        // And says where it came from, since the studio would have converted it.
        Assert.Contains("authored elsewhere", error, StringComparison.Ordinal);
    }

    [Fact]
    public void A_user_task_is_not_caught_by_that_rule()
    {
        // The complement. Without it the rule above passes for a validator that
        // refuses every task-shaped element, including the one we convert TO.
        var result = WorkflowBpmnXml.ValidateProcess(Process(
            """<bpmn:userTask id="t" name="Approve" flowable:assignee="alice" />"""));

        Assert.DoesNotContain(result.Errors, e => e.Contains("Approve", StringComparison.Ordinal));
    }

    // ── The converted-task assignee rule, and its deliberate narrowness ──────

    [Fact]
    public void A_converted_task_with_nobody_to_do_it_is_refused()
    {
        var result = WorkflowBpmnXml.ValidateProcess(Process(
            """<bpmn:userTask id="t" name="Post the cheque" flowable:autonateConvertedFrom="manual task" />"""));

        var error = Assert.Single(result.Errors, e => e.Contains("nobody to do it", StringComparison.Ordinal));
        Assert.Contains("Post the cheque", error, StringComparison.Ordinal);
        Assert.Contains("converted", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("flowable:assignee=\"alice\"")]
    [InlineData("flowable:candidateUsers=\"alice,bob\"")]
    [InlineData("flowable:candidateGroups=\"approvers\"")]
    public void A_converted_task_someone_can_do_publishes(string who)
    {
        // Candidates count, not just an assignee — a task offered to a group is
        // still a task somebody can do, and requiring an assignee would force
        // authors to pick a person where a role is what they mean.
        var result = WorkflowBpmnXml.ValidateProcess(Process(
            $"""<bpmn:userTask id="t" name="Post the cheque" flowable:autonateConvertedFrom="manual task" {who} />"""));

        Assert.DoesNotContain(result.Errors, e => e.Contains("nobody to do it", StringComparison.Ordinal));
    }

    [Fact]
    public void An_ordinary_unassigned_user_task_is_NOT_refused()
    {
        // The narrowness is the point, and it is deliberate.
        //
        // An unassigned user task is a first-class state in Auton8 — the execution
        // view renders "(unassigned)" — and 49 of the 50 userTask fixtures in this
        // repo carry no assignee. A blanket rule would refuse workflows that run
        // today and would break this suite's own guards, so the rule keys on the
        // marker the studio writes when it converts, and on nothing else.
        var result = WorkflowBpmnXml.ValidateProcess(Process(
            """<bpmn:userTask id="t" name="Approve" />"""));

        Assert.DoesNotContain(result.Errors, e => e.Contains("nobody to do it", StringComparison.Ordinal));
    }

    [Fact]
    public void An_intermediate_throw_none_is_accepted()
    {
        // The other half of this story. It genuinely passes straight through, and
        // that is correct behaviour for a throw-none rather than a silent no-op —
        // BPMN defines it as a marker in the flow.
        var result = WorkflowBpmnXml.ValidateProcess(Process(
            """<bpmn:intermediateThrowEvent id="t" name="Checkpoint" />"""));

        Assert.DoesNotContain(result.Errors, e => e.Contains("Checkpoint", StringComparison.Ordinal));
    }
}
