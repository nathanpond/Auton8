using System.Xml.Linq;
using AutoNate.Web.Services.Workflow;
using Xunit;

namespace AutoNate.Web.Tests.Workflow;

/// <summary>
/// Publish-time condition checking (#158) — the shared one, that #159 and #163 reuse.
/// </summary>
/// <remarks>
/// The bug this exists to end: a mistyped variable name in a gateway condition fails
/// silently at runtime by taking the wrong branch. Nothing errors, nothing logs, and
/// the process merely goes the wrong way. These tests are as much about the
/// false-positive half as the detection half, because a warning that fires on correct
/// diagrams is worse than no warning — authors stop reading it, and then the real one
/// is invisible too.
/// </remarks>
public sealed class WorkflowConditionValidationTests
{
    private static string Process(string body) => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          xmlns:flowable="http://flowable.org/bpmn"
                          targetNamespace="http://autonate.dev/workflows">
          <bpmn:process id="p" name="P" isExecutable="true">
        {body}
          </bpmn:process>
        </bpmn:definitions>
        """;

    private static WorkflowBpmnValidationResult Validate(string body) =>
        WorkflowBpmnXml.ValidateProcess(Process(body));

    // ── AC5: a condition that cannot parse is an error ──────────────────────

    [Theory]
    [InlineData("${amount > }", "unfinished")]
    [InlineData("${amount > 100", "never closes")]
    [InlineData("${(amount > 100}", "never closed")]
    [InlineData("${amount == 'open}", "never closed")]
    [InlineData("${amount > 100)}", "nothing to close")]
    public void An_unparseable_condition_is_refused_and_the_message_quotes_it(
        string expression, string because)
    {
        var result = Validate($"""
                <bpmn:startEvent id="s" />
                <bpmn:exclusiveGateway id="g" name="Route" />
                <bpmn:sequenceFlow id="f" name="Big orders" sourceRef="g" targetRef="e">
                  <bpmn:conditionExpression>{System.Security.SecurityElement.Escape(expression)}</bpmn:conditionExpression>
                </bpmn:sequenceFlow>
                <bpmn:endEvent id="e" />
            """);

        var error = Assert.Single(result.Errors, e => e.Contains("cannot be parsed", StringComparison.Ordinal));

        // Names the element, says what is wrong, and quotes the text. An author
        // with forty elements needs all three; "validation failed" gives none.
        Assert.Contains("Big orders", error, StringComparison.Ordinal);
        Assert.Contains(because, error, StringComparison.Ordinal);
        Assert.Contains(expression, error, StringComparison.Ordinal);
    }

    [Fact]
    public void A_valid_condition_is_not_refused()
    {
        // The complement. Without it, every test above passes for a validator that
        // rejects everything.
        var result = Validate("""
                <bpmn:startEvent id="s" />
                <bpmn:scriptTask id="calc" name="Calc" scriptFormat="javascript">
                  <bpmn:script>execution.setVariable("amount", 42);</bpmn:script>
                </bpmn:scriptTask>
                <bpmn:exclusiveGateway id="g" name="Route" />
                <bpmn:sequenceFlow id="f" name="Big" sourceRef="g" targetRef="e">
                  <bpmn:conditionExpression>${amount &gt; 100 &amp;&amp; !(amount == null)}</bpmn:conditionExpression>
                </bpmn:sequenceFlow>
                <bpmn:endEvent id="e" />
            """);

        Assert.DoesNotContain(result.Errors, e => e.Contains("cannot be parsed", StringComparison.Ordinal));
        Assert.Empty(result.Warnings);
    }

    // ── AC6 / AC9: the unset-variable warning, and not crying wolf ──────────

    [Fact]
    public void A_condition_reading_a_variable_nothing_sets_warns_and_does_not_block()
    {
        // The story's demo: `aproved` misspelt. This is the whole point of the
        // check — the runtime symptom is a branch quietly taken the wrong way.
        var result = Validate("""
                <bpmn:startEvent id="s" />
                <bpmn:scriptTask id="calc" name="Calc" scriptFormat="javascript">
                  <bpmn:script>execution.setVariable("approved", true);</bpmn:script>
                </bpmn:scriptTask>
                <bpmn:exclusiveGateway id="g" name="Route" />
                <bpmn:sequenceFlow id="f" name="Yes" sourceRef="g" targetRef="e">
                  <bpmn:conditionExpression>${aproved == true}</bpmn:conditionExpression>
                </bpmn:sequenceFlow>
                <bpmn:endEvent id="e" />
            """);

        var warning = Assert.Single(result.Warnings, w => w.Contains("aproved", StringComparison.Ordinal));
        Assert.Contains("Yes", warning, StringComparison.Ordinal);

        // A warning, not an error: a variable can legitimately arrive from outside,
        // so blocking publication here would refuse working diagrams.
        Assert.DoesNotContain(result.Errors, e => e.Contains("aproved", StringComparison.Ordinal));

        // And the correctly-spelt one it sits beside is not warned about, which is
        // what makes the warning readable.
        Assert.DoesNotContain(result.Warnings, w => w.Contains("'approved'", StringComparison.Ordinal));
    }

    [Theory]
    // Set by a script task…
    [InlineData("""<bpmn:scriptTask id="x" scriptFormat="javascript"><bpmn:script>execution.setVariable("total", 1);</bpmn:script></bpmn:scriptTask>""")]
    // …by a service task's result…
    [InlineData("""<bpmn:serviceTask id="x" flowable:resultVariable="total" />""")]
    // …by a data object declaration (#166)…
    [InlineData("""<bpmn:dataObject id="d" name="total" />""")]
    // …or as a multi-instance element variable (#159).
    [InlineData("""<bpmn:task id="x"><bpmn:multiInstanceLoopCharacteristics flowable:elementVariable="total" /></bpmn:task>""")]
    public void A_variable_the_process_does_set_earns_no_warning(string setter)
    {
        // AC9's false-positive guard, run over every source the tracer knows. Each
        // source missed here would produce a warning on a correct diagram, and a
        // few of those are all it takes for authors to stop reading warnings.
        var result = Validate($$"""
                <bpmn:startEvent id="s" />
                {{setter}}
                <bpmn:exclusiveGateway id="g" name="Route" />
                <bpmn:sequenceFlow id="f" name="Enough" sourceRef="g" targetRef="e">
                  <bpmn:conditionExpression>${total &gt; 100}</bpmn:conditionExpression>
                </bpmn:sequenceFlow>
                <bpmn:endEvent id="e" />
            """);

        Assert.DoesNotContain(result.Warnings, w => w.Contains("total", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("${order.customer.name == 'x'}", new[] { "order" })]      // property path: only the head
    [InlineData("${name == 'amount'}", new[] { "name" })]                 // a name inside a string is not a reference
    [InlineData("${total.isEmpty()}", new[] { "total" })]                 // a method is not a variable
    [InlineData("${a > 1 && b < 2}", new[] { "a", "b" })]
    [InlineData("${true}", new string[0])]                                // language, not variables
    [InlineData("${empty someList}", new[] { "someList" })]
    public void Only_real_variable_references_are_collected(string expression, string[] expected)
    {
        // Each of these was chosen because getting it wrong produces a false
        // warning — `order.customer.name` warning about "customer" and "name" is
        // the fastest way to make this feature hated.
        Assert.Equal(expected, WorkflowConditionValidation.ReferencedVariableNames(expression));
    }

    // ── AC7 / AC8: one check, every site ────────────────────────────────────

    [Fact]
    public void A_conditional_event_condition_is_checked_the_same_way_a_sequence_flow_is()
    {
        // AC8 asks that the reuse cannot silently become a copy. Asserting the two
        // sites produce the *same* message for the same mistake is what pins that:
        // two implementations would drift in wording long before they drifted in
        // behaviour, so wording is the sensitive detector.
        const string broken = "${amount > }";

        var viaFlow = Validate($"""
                <bpmn:startEvent id="s" />
                <bpmn:exclusiveGateway id="g" />
                <bpmn:sequenceFlow id="f" name="Same Name" sourceRef="g" targetRef="e">
                  <bpmn:conditionExpression>{System.Security.SecurityElement.Escape(broken)}</bpmn:conditionExpression>
                </bpmn:sequenceFlow>
                <bpmn:endEvent id="e" />
            """);

        var viaEvent = Validate($"""
                <bpmn:startEvent id="s" />
                <bpmn:intermediateCatchEvent id="c" name="Same Name">
                  <bpmn:conditionalEventDefinition id="cd">
                    <bpmn:condition>{System.Security.SecurityElement.Escape(broken)}</bpmn:condition>
                  </bpmn:conditionalEventDefinition>
                </bpmn:intermediateCatchEvent>
                <bpmn:endEvent id="e" />
            """);

        var flowError = Assert.Single(viaFlow.Errors, e => e.Contains("cannot be parsed", StringComparison.Ordinal));
        var eventError = Assert.Single(viaEvent.Errors, e => e.Contains("cannot be parsed", StringComparison.Ordinal));

        Assert.Equal(flowError, eventError);
    }

    [Fact]
    public void The_shared_check_is_callable_directly_by_the_stories_that_will_reuse_it()
    {
        // #159's multi-instance completion condition and #163's ad-hoc completion
        // condition are not sequence flows or conditional events, so they cannot go
        // through CheckDocument. They call Check with their own site description —
        // this asserts that entry point exists and behaves, so those stories have
        // something to reuse rather than a reason to write their own.
        var assigned = new HashSet<string>(StringComparer.Ordinal) { "done" };

        var good = WorkflowConditionValidation.Check(
            new WorkflowConditionValidation.Site("Review each", "The completion condition", "${done}"),
            assigned);
        Assert.Empty(good.Errors);
        Assert.Empty(good.Warnings);

        var bad = WorkflowConditionValidation.Check(
            new WorkflowConditionValidation.Site("Review each", "The completion condition", "${done ==}"),
            assigned);
        var error = Assert.Single(bad.Errors);
        Assert.Contains("Review each", error, StringComparison.Ordinal);
        Assert.Contains("The completion condition", error, StringComparison.Ordinal);
    }

    [Fact]
    public void An_empty_condition_is_refused_rather_than_ignored()
    {
        // A condition element with no body evaluates to nothing and the flow is
        // never taken — silent, and indistinguishable from a wrong condition.
        var result = Validate("""
                <bpmn:startEvent id="s" />
                <bpmn:exclusiveGateway id="g" />
                <bpmn:sequenceFlow id="f" name="Empty" sourceRef="g" targetRef="e">
                  <bpmn:conditionExpression></bpmn:conditionExpression>
                </bpmn:sequenceFlow>
                <bpmn:endEvent id="e" />
            """);

        Assert.Contains(result.Errors, e =>
            e.Contains("Empty", StringComparison.Ordinal)
            && e.Contains("no condition", StringComparison.Ordinal));
    }

    // ── The conditional start placement constraint ──────────────────────────

    [Fact]
    public void A_conditional_start_event_at_process_level_is_refused_with_the_constraint()
    {
        // Flowable rejects this with `flowable-start-event-invalid-event-definition`,
        // which tells an author nothing. #103 proved it by deploying one.
        var result = Validate("""
                <bpmn:startEvent id="s" name="When approved">
                  <bpmn:conditionalEventDefinition id="cd">
                    <bpmn:condition>${approved == true}</bpmn:condition>
                  </bpmn:conditionalEventDefinition>
                </bpmn:startEvent>
                <bpmn:endEvent id="e" />
            """);

        var error = Assert.Single(result.Errors, e => e.Contains("Conditional start event", StringComparison.Ordinal));
        Assert.Contains("When approved", error, StringComparison.Ordinal);
        Assert.Contains("event subprocess", error, StringComparison.Ordinal);
        // And it says what to do instead, not merely what is wrong.
        Assert.Contains("intermediate catch", error, StringComparison.Ordinal);
    }

    [Fact]
    public void A_conditional_start_event_inside_an_event_subprocess_is_accepted()
    {
        // The half that stops this becoming "conditional starts are banned".
        // EventSubProcessConditionalStartEventActivityBehavior ships and runs it,
        // and #162 builds on exactly this shape.
        var result = Validate("""
                <bpmn:startEvent id="s" />
                <bpmn:subProcess id="handler" name="On approval" triggeredByEvent="true">
                  <bpmn:startEvent id="cs" name="When approved">
                    <bpmn:conditionalEventDefinition id="cd">
                      <bpmn:condition>${approved == true}</bpmn:condition>
                    </bpmn:conditionalEventDefinition>
                  </bpmn:startEvent>
                  <bpmn:endEvent id="he" />
                </bpmn:subProcess>
                <bpmn:endEvent id="e" />
            """);

        Assert.DoesNotContain(result.Errors, e => e.Contains("Conditional start event", StringComparison.Ordinal));
    }

    // ── Round trip: what the studio writes survives a save ──────────────────

    [Fact]
    public void A_conditional_events_condition_round_trips_through_the_snapshot()
    {
        // The failure this catches is the skill's "Apply*Snapshot" row: the field
        // reaches the backend and never reaches the XML, so the editor shows the
        // value, Apply looks like it worked, and the edit is gone on reload.
        var xml = Process("""
                <bpmn:startEvent id="s" />
                <bpmn:intermediateCatchEvent id="wait" name="Wait">
                  <bpmn:conditionalEventDefinition id="cd">
                    <bpmn:condition>${old == true}</bpmn:condition>
                  </bpmn:conditionalEventDefinition>
                </bpmn:intermediateCatchEvent>
                <bpmn:endEvent id="e" />
            """);

        var applied = WorkflowBpmnXml.ApplyProcessMetadata(
            xml, "p", "P",
            [new WorkflowElementSnapshot("wait", "bpmn:IntermediateCatchEvent", "Wait for approval",
                ConditionExpression: "${approved == true}")]);

        Assert.Contains("${approved == true}", applied, StringComparison.Ordinal);
        Assert.DoesNotContain("${old == true}", applied, StringComparison.Ordinal);
        Assert.Contains("Wait for approval", applied, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true, "true")]
    [InlineData(false, "false")]
    public void A_boundary_events_interrupting_flag_is_written_explicitly(bool interrupting, string expected)
    {
        // Written in BOTH directions on purpose. BPMN treats an absent
        // cancelActivity as true, so omitting the attribute to mean "interrupting"
        // would make a non-interrupting event impossible to switch back.
        var xml = Process("""
                <bpmn:startEvent id="s" />
                <bpmn:userTask id="work" name="Work" />
                <bpmn:boundaryEvent id="b" name="Escalate" attachedToRef="work">
                  <bpmn:conditionalEventDefinition id="cd">
                    <bpmn:condition>${escalate == true}</bpmn:condition>
                  </bpmn:conditionalEventDefinition>
                </bpmn:boundaryEvent>
                <bpmn:endEvent id="e" />
            """);

        var applied = WorkflowBpmnXml.ApplyProcessMetadata(
            xml, "p", "P",
            [new WorkflowElementSnapshot("b", "bpmn:BoundaryEvent", "Escalate",
                ConditionExpression: "${escalate == true}",
                CancelActivity: interrupting)]);

        var boundary = XDocument.Parse(applied)
            .Descendants().Single(e => e.Attribute("id")?.Value == "b");

        Assert.Equal(expected, boundary.Attribute("cancelActivity")?.Value);
    }

    [Fact]
    public void The_interrupting_flag_is_not_written_onto_something_that_cannot_interrupt()
    {
        // cancelActivity is meaningful only on a boundary event. Writing it onto an
        // intermediate catch would be noise in the XML that a future reader has to
        // work out is meaningless.
        var xml = Process("""
                <bpmn:startEvent id="s" />
                <bpmn:intermediateCatchEvent id="wait" name="Wait">
                  <bpmn:conditionalEventDefinition id="cd">
                    <bpmn:condition>${approved == true}</bpmn:condition>
                  </bpmn:conditionalEventDefinition>
                </bpmn:intermediateCatchEvent>
                <bpmn:endEvent id="e" />
            """);

        var applied = WorkflowBpmnXml.ApplyProcessMetadata(
            xml, "p", "P",
            [new WorkflowElementSnapshot("wait", "bpmn:IntermediateCatchEvent", "Wait",
                ConditionExpression: "${approved == true}",
                CancelActivity: false)]);

        var wait = XDocument.Parse(applied)
            .Descendants().Single(e => e.Attribute("id")?.Value == "wait");

        Assert.Null(wait.Attribute("cancelActivity"));
    }

    [Fact]
    public void A_process_with_no_conditions_at_all_produces_nothing()
    {
        // The quietest false-positive guard, and the one that catches a validator
        // wired to run over the wrong node set.
        var result = Validate("""
                <bpmn:startEvent id="s" />
                <bpmn:userTask id="t" name="Approve" />
                <bpmn:endEvent id="e" />
            """);

        Assert.Empty(result.Warnings);
        Assert.DoesNotContain(result.Errors, e => e.Contains("condition", StringComparison.OrdinalIgnoreCase));
    }
}
