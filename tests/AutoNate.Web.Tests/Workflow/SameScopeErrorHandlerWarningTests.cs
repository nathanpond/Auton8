using AutoNate.Web.Services.Workflow;
using Xunit;

namespace AutoNate.Web.Tests.Workflow;

/// <summary>
/// The warning for a handler that says "interrupting" and will not interrupt (#229).
/// </summary>
/// <remarks>
/// <para>
/// The engine behaviour this warns about is pinned by
/// <c>EventSubProcessScopeDifferentialTests</c>, which is
/// <c>RequiresService=Flowable</c> and full-local only. These facts need no
/// engine and run on every merge, so the rule cannot drift out from under the
/// measurement that justifies it.
/// </para>
/// <para>
/// The negative cases carry the weight here. A warning that fires on every event
/// subprocess would satisfy the positive case and be useless — two of the three
/// arrangements below <em>do</em> interrupt correctly, and warning about them
/// would teach authors to ignore the warning.
/// </para>
/// </remarks>
public sealed class SameScopeErrorHandlerWarningTests
{
    private const string Marker = "WITHOUT cancelling";

    /// <summary>The throw beside the handler, both inside one subprocess.</summary>
    private const string HandlerInsideThrowingScope = """
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          targetNamespace="http://autonate.dev/workflows">
          <bpmn:error id="Err_1" errorCode="E1" name="E1" />
          <bpmn:process id="p" isExecutable="true">
            <bpmn:startEvent id="s" />
            <bpmn:sequenceFlow id="f0" sourceRef="s" targetRef="scope" />
            <bpmn:subProcess id="scope" name="Scope">
              <bpmn:startEvent id="ss" />
              <bpmn:sequenceFlow id="sf0" sourceRef="ss" targetRef="fork" />
              <bpmn:parallelGateway id="fork" />
              <bpmn:sequenceFlow id="sf1" sourceRef="fork" targetRef="ongoing" />
              <bpmn:userTask id="ongoing" name="Ongoing work" />
              <bpmn:sequenceFlow id="sf2" sourceRef="fork" targetRef="boom" />
              <bpmn:endEvent id="boom" name="Boom">
                <bpmn:errorEventDefinition errorRef="Err_1" />
              </bpmn:endEvent>
              <bpmn:subProcess id="handler" name="Handler" triggeredByEvent="true">
                <bpmn:startEvent id="hs" isInterrupting="true">
                  <bpmn:errorEventDefinition errorRef="Err_1" />
                </bpmn:startEvent>
                <bpmn:sequenceFlow id="hf" sourceRef="hs" targetRef="ht" />
                <bpmn:userTask id="ht" name="Handled error" />
              </bpmn:subProcess>
            </bpmn:subProcess>
          </bpmn:process>
        </bpmn:definitions>
        """;

    /// <summary>The throw one scope deeper. This one interrupts.</summary>
    private const string HandlerAboveNestedThrow = """
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          targetNamespace="http://autonate.dev/workflows">
          <bpmn:error id="Err_1" errorCode="E1" name="E1" />
          <bpmn:process id="p" isExecutable="true">
            <bpmn:startEvent id="s" />
            <bpmn:sequenceFlow id="f0" sourceRef="s" targetRef="scope" />
            <bpmn:subProcess id="scope" name="Scope">
              <bpmn:startEvent id="ss" />
              <bpmn:sequenceFlow id="sf0" sourceRef="ss" targetRef="fork" />
              <bpmn:parallelGateway id="fork" />
              <bpmn:sequenceFlow id="sf1" sourceRef="fork" targetRef="ongoing" />
              <bpmn:userTask id="ongoing" name="Ongoing work" />
              <bpmn:sequenceFlow id="sf2" sourceRef="fork" targetRef="inner" />
              <bpmn:subProcess id="inner" name="Inner">
                <bpmn:startEvent id="is" />
                <bpmn:sequenceFlow id="if0" sourceRef="is" targetRef="boom" />
                <bpmn:endEvent id="boom" name="Boom">
                  <bpmn:errorEventDefinition errorRef="Err_1" />
                </bpmn:endEvent>
              </bpmn:subProcess>
              <bpmn:subProcess id="handler" name="Handler" triggeredByEvent="true">
                <bpmn:startEvent id="hs" isInterrupting="true">
                  <bpmn:errorEventDefinition errorRef="Err_1" />
                </bpmn:startEvent>
                <bpmn:sequenceFlow id="hf" sourceRef="hs" targetRef="ht" />
                <bpmn:userTask id="ht" name="Handled error" />
              </bpmn:subProcess>
            </bpmn:subProcess>
          </bpmn:process>
        </bpmn:definitions>
        """;

    /// <summary>All three at the process level. This one interrupts too.</summary>
    private const string EverythingAtProcessLevel = """
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          targetNamespace="http://autonate.dev/workflows">
          <bpmn:error id="Err_1" errorCode="E1" name="E1" />
          <bpmn:process id="p" isExecutable="true">
            <bpmn:startEvent id="s" />
            <bpmn:sequenceFlow id="f0" sourceRef="s" targetRef="fork" />
            <bpmn:parallelGateway id="fork" />
            <bpmn:sequenceFlow id="f1" sourceRef="fork" targetRef="ongoing" />
            <bpmn:userTask id="ongoing" name="Ongoing work" />
            <bpmn:sequenceFlow id="f2" sourceRef="fork" targetRef="boom" />
            <bpmn:endEvent id="boom" name="Boom">
              <bpmn:errorEventDefinition errorRef="Err_1" />
            </bpmn:endEvent>
            <bpmn:subProcess id="handler" name="Handler" triggeredByEvent="true">
              <bpmn:startEvent id="hs" isInterrupting="true">
                <bpmn:errorEventDefinition errorRef="Err_1" />
              </bpmn:startEvent>
              <bpmn:sequenceFlow id="hf" sourceRef="hs" targetRef="ht" />
              <bpmn:userTask id="ht" name="Handled error" />
            </bpmn:subProcess>
          </bpmn:process>
        </bpmn:definitions>
        """;

    private static IReadOnlyList<string> WarningsFor(string xml) =>
        WorkflowBpmnXml.ValidateProcess(xml).Warnings;

    [Fact]
    public void A_handler_inside_the_throwing_subprocess_is_warned_about()
    {
        var warning = Assert.Single(WarningsFor(HandlerInsideThrowingScope), w => w.Contains(Marker));

        // Both names, because "an event subprocess somewhere does not interrupt"
        // is not actionable on a diagram with several.
        Assert.Contains("Handler", warning, StringComparison.Ordinal);
        Assert.Contains("Scope", warning, StringComparison.Ordinal);

        // And it stays a warning. An author who wants this arrangement can have
        // it, and refusing it would break diagrams already deployed and working.
        Assert.Empty(WorkflowBpmnXml.ValidateProcess(HandlerInsideThrowingScope).Errors);
    }

    [Fact]
    public void A_handler_above_a_nested_throw_is_not_warned_about()
    {
        // Measured: this arrangement cancels the sibling. Warning about it would
        // be a false alarm on the shape the documentation recommends.
        Assert.DoesNotContain(WarningsFor(HandlerAboveNestedThrow), w => w.Contains(Marker));
    }

    [Fact]
    public void A_process_level_handler_is_not_warned_about()
    {
        // The case that makes the rule precise rather than approximate. #229
        // described the problem as "the handler beside the throw in the same
        // scope" -- but measured at the PROCESS level, with no enclosing
        // subprocess, the sibling IS cancelled. A rule keyed on "same scope"
        // would fire here and be wrong.
        Assert.DoesNotContain(WarningsFor(EverythingAtProcessLevel), w => w.Contains(Marker));
    }

    [Fact]
    public void A_non_interrupting_handler_is_not_warned_about()
    {
        // It does not claim to interrupt, so there is nothing misleading to say.
        // (Publish refuses a non-interrupting ERROR start for its own reasons --
        // asserted in EventSubProcessExecutionTests -- which is why this checks
        // the warning specifically rather than the whole result.)
        var nonInterrupting = HandlerInsideThrowingScope.Replace(
            """isInterrupting="true" """.TrimEnd(), """isInterrupting="false" """.TrimEnd());

        Assert.DoesNotContain(WarningsFor(nonInterrupting), w => w.Contains(Marker));
    }
}
