using System.Xml.Linq;
using AutoNate.Web.Services.Workflow;
using Xunit;

namespace AutoNate.Web.Tests.Workflow;

/// <summary>
/// Timer boundary events (#157) — serialisation and the refusals.
/// </summary>
/// <remarks>
/// <para>
/// The engine behaviour was established by running it against Flowable 8.0.0 before
/// any of this was written: an interrupting timer cancels the attached activity, a
/// non-interrupting one leaves it running, <c>R3/PT1S</c> fires exactly three times,
/// completing the activity first removes the timer job, and deleting the instance
/// removes pending timers. Those are asserted in
/// <c>tests/AutoNate.E2E.Tests/TimerBoundaryExecutionTests.cs</c>, which needs an
/// engine. What lives here needs none.
/// </para>
/// <para>
/// **A timer boundary does not require <c>flowable:async</c> on the activity it
/// guards.** Four such timers fired correctly on plain user tasks with none set, so
/// nothing in the studio sets it — recorded here because the acceptance criterion
/// asked for the answer to be established rather than left as folklore.
/// </para>
/// </remarks>
public sealed class TimerBoundaryEventTests
{
    private static string Process(string body) => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
                          targetNamespace="http://autonate.dev/workflows">
          <bpmn:process id="p" name="P" isExecutable="true">
            <bpmn:startEvent id="s" />
            <bpmn:userTask id="work" name="Do the work" />
        {body}
            <bpmn:endEvent id="e" />
          </bpmn:process>
        </bpmn:definitions>
        """;

    private const string TimerBoundary = """
            <bpmn:boundaryEvent id="b" name="Escalate" attachedToRef="work">
              <bpmn:timerEventDefinition>
                <bpmn:timeDuration xsi:type="bpmn:tFormalExpression">PT15M</bpmn:timeDuration>
              </bpmn:timerEventDefinition>
            </bpmn:boundaryEvent>
        """;

    // ── The refusals ────────────────────────────────────────────────────────

    [Fact]
    public void A_timer_boundary_with_no_time_is_refused_and_told_what_to_set()
    {
        // It deploys cleanly, produces no job, and never fires — so the activity it
        // guards waits forever with nothing to show why. Epic #40's rule is that a
        // hang is a defect rather than a documented behaviour.
        var result = WorkflowBpmnXml.ValidateProcess(Process("""
            <bpmn:boundaryEvent id="b" name="Escalate" attachedToRef="work">
              <bpmn:timerEventDefinition />
            </bpmn:boundaryEvent>
        """));

        var error = Assert.Single(result.Errors, e => e.Contains("Timer boundary event", StringComparison.Ordinal));
        Assert.Contains("Escalate", error, StringComparison.Ordinal);
        // Says what to do, not merely what is wrong.
        Assert.Contains("PT15M", error, StringComparison.Ordinal);
        Assert.Contains("waits forever", error, StringComparison.Ordinal);
    }

    [Fact]
    public void A_timer_boundary_setting_two_kinds_is_refused()
    {
        // Flowable rejects this at deployment with a parse error naming the
        // definition rather than the event, which an author cannot act on.
        var result = WorkflowBpmnXml.ValidateProcess(Process("""
            <bpmn:boundaryEvent id="b" name="Escalate" attachedToRef="work">
              <bpmn:timerEventDefinition>
                <bpmn:timeDuration xsi:type="bpmn:tFormalExpression">PT15M</bpmn:timeDuration>
                <bpmn:timeCycle xsi:type="bpmn:tFormalExpression">R3/PT1H</bpmn:timeCycle>
              </bpmn:timerEventDefinition>
            </bpmn:boundaryEvent>
        """));

        Assert.Contains(result.Errors, e =>
            e.Contains("Escalate", StringComparison.Ordinal)
            && e.Contains("more than one kind", StringComparison.Ordinal));
    }

    [Fact]
    public void A_properly_configured_timer_boundary_is_accepted()
    {
        // The complement. Without it every refusal above passes for a validator
        // that refuses all timer boundary events.
        var result = WorkflowBpmnXml.ValidateProcess(Process(TimerBoundary));

        Assert.DoesNotContain(result.Errors, e => e.Contains("Timer boundary event", StringComparison.Ordinal));
    }

    [Fact]
    public void A_conditional_boundary_is_not_caught_by_the_timer_rule()
    {
        // The two boundary shapes are distinguished by their event definition, not
        // by being boundary events. A rule keyed on `boundaryEvent` alone would
        // refuse every conditional boundary #158 just shipped.
        var result = WorkflowBpmnXml.ValidateProcess(Process("""
            <bpmn:boundaryEvent id="b" name="On approval" attachedToRef="work">
              <bpmn:conditionalEventDefinition id="cd">
                <bpmn:condition>${approved == true}</bpmn:condition>
              </bpmn:conditionalEventDefinition>
            </bpmn:boundaryEvent>
        """));

        Assert.DoesNotContain(result.Errors, e => e.Contains("Timer boundary event", StringComparison.Ordinal));
    }

    // ── Round trip ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData("duration", "PT30M", "timeDuration")]
    [InlineData("date", "2026-12-31T09:00:00", "timeDate")]
    [InlineData("cycle", "R3/PT1H", "timeCycle")]
    public void Each_timer_kind_round_trips_through_the_snapshot(string kind, string value, string element)
    {
        // The skill's "Apply*Snapshot" failure: the field reaches the backend and
        // never reaches the XML, so the editor shows the value, Apply looks like it
        // worked, and the edit is gone on reload.
        var snapshot = new WorkflowElementSnapshot("b", "bpmn:BoundaryEvent", "Escalate",
            BoundaryTimerDuration: kind == "duration" ? value : null,
            BoundaryTimerDate: kind == "date" ? value : null,
            BoundaryTimerCycle: kind == "cycle" ? value : null);

        var applied = WorkflowBpmnXml.ApplyProcessMetadata(Process(TimerBoundary), "p", "P", [snapshot]);

        var timer = XDocument.Parse(applied).Descendants()
            .Single(e => e.Attribute("id")?.Value == "b")
            .Elements().Single(e => e.Name.LocalName == "timerEventDefinition");

        var kinds = timer.Elements().Select(e => e.Name.LocalName).ToArray();

        // Exactly one kind survives. Flowable rejects a definition carrying two, and
        // a stale timeCycle beside a new timeDuration behaves unpredictably — this
        // is the assertion that catches a handler which adds without clearing.
        Assert.Equal(new[] { element }, kinds);
        Assert.Equal(value, timer.Elements().Single().Value);
    }

    [Theory]
    [InlineData(true, "true")]
    [InlineData(false, "false")]
    public void The_interrupting_flag_is_written_explicitly_in_both_directions(bool interrupting, string expected)
    {
        // BPMN treats an absent cancelActivity as true, so omitting it to mean
        // "interrupting" would make a non-interrupting timer impossible to switch
        // back.
        var snapshot = new WorkflowElementSnapshot("b", "bpmn:BoundaryEvent", "Escalate",
            BoundaryTimerDuration: "PT15M",
            CancelActivity: interrupting);

        var applied = WorkflowBpmnXml.ApplyProcessMetadata(Process(TimerBoundary), "p", "P", [snapshot]);

        var boundary = XDocument.Parse(applied).Descendants().Single(e => e.Attribute("id")?.Value == "b");
        Assert.Equal(expected, boundary.Attribute("cancelActivity")?.Value);
    }

    [Fact]
    public void A_snapshot_carrying_no_timer_leaves_an_existing_one_alone()
    {
        // Snapshots are sent for every element in the diagram. One describing some
        // other element must not blank this timer — which is what an unconditional
        // rewrite would do, silently, on every save.
        var snapshot = new WorkflowElementSnapshot("b", "bpmn:BoundaryEvent", "Renamed only");

        var applied = WorkflowBpmnXml.ApplyProcessMetadata(Process(TimerBoundary), "p", "P", [snapshot]);

        Assert.Contains("PT15M", applied, StringComparison.Ordinal);
        Assert.Contains("Renamed only", applied, StringComparison.Ordinal);
    }
}
