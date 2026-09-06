using AutoNate.Web.Services.Workflow;
using Xunit;

namespace AutoNate.Web.Tests;

// #151: a script written against the pre-#147 API, or one reaching for the JVM,
// is refused at publish rather than failing at runtime on whoever happens to
// run the process.
//
// The bar these tests hold is that the message says what to write *instead*.
// Asserting only that publishing fails would pass against a generic "unknown
// identifier", which is the outcome the story exists to avoid.
public sealed class ScriptSurfaceRulesTests
{
    private static string ProcessWith(string script) => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          id="Definitions_1"
                          targetNamespace="http://autonate.dev/workflows">
          <bpmn:process id="script_flow" name="Script Flow" isExecutable="true">
            <bpmn:startEvent id="StartEvent_1" />
            <bpmn:scriptTask id="ScriptTask_1" name="Compute" scriptFormat="javascript">
              <bpmn:script>{System.Security.SecurityElement.Escape(script)}</bpmn:script>
            </bpmn:scriptTask>
          </bpmn:process>
        </bpmn:definitions>
        """;

    [Fact]
    public void SetVariable_IsRejected_AndNamesItsReplacement()
    {
        var result = WorkflowBpmnXml.ValidateProcess(ProcessWith("""execution.setVariable("x", 1);"""));

        var error = Assert.Single(result.Errors);
        Assert.Contains("Compute", error);            // names the task
        Assert.Contains("variables.set", error);      // names the replacement
    }

    [Fact]
    public void GetVariable_IsRejected_AndNamesItsReplacement()
    {
        var result = WorkflowBpmnXml.ValidateProcess(ProcessWith("""var t = execution.getVariable("x");"""));

        var error = Assert.Single(result.Errors);
        Assert.Contains("variables.get", error);
    }

    [Fact]
    public void BareExecution_IsRejected_AndPointsAtTheSupportedSurface()
    {
        // Not every use is a get/set — an author may have passed `execution`
        // around. The general case still has to say where to go.
        var result = WorkflowBpmnXml.ValidateProcess(ProcessWith("var e = execution;"));

        var error = Assert.Single(result.Errors);
        Assert.Contains("variables.get", error);
        Assert.Contains("variables.set", error);
    }

    [Theory]
    [InlineData("""var S = Java.type("java.lang.System");""")]
    [InlineData("var i = new JavaImporter(java.util);")]
    [InlineData("var f = Packages.java.io.File;")]
    public void JavaInteropEntryPoints_AreRejected_AsSandboxPolicy(string script)
    {
        // These fail in the sandbox anyway, as an unresolved identifier. That
        // is correct but late, and "Java is not defined" reads like a missing
        // dependency rather than a deliberate boundary — so the reason is
        // stated here instead.
        var result = WorkflowBpmnXml.ValidateProcess(ProcessWith(script));

        var error = Assert.Single(result.Errors);
        Assert.Contains("sandbox", error);
        Assert.Contains("JVM", error);
    }

    [Fact]
    public void AValidScriptPublishesUnchanged()
    {
        var result = WorkflowBpmnXml.ValidateProcess(ProcessWith("""
            var total = variables.get("orderTotal");
            variables.set("approved", total > 100);
            return total;
            """));

        Assert.Empty(result.Errors);
    }

    // --- the false-positive guard ---------------------------------------
    //
    // These are the cases a substring check gets wrong. A wrongly blocked
    // script leaves an author stuck with no recourse, which is worse than a
    // missed one — a missed script fails at runtime with a clear sandbox error.

    [Fact]
    public void AMentionInALineCommentDoesNotBlockPublishing()
    {
        var result = WorkflowBpmnXml.ValidateProcess(ProcessWith("""
            // replaces execution.setVariable("x", 1) from the old API
            variables.set("x", 1);
            """));

        Assert.Empty(result.Errors);
    }

    [Fact]
    public void AMentionInABlockCommentDoesNotBlockPublishing()
    {
        var result = WorkflowBpmnXml.ValidateProcess(ProcessWith("""
            /* migration note: execution.getVariable is gone; Java.type too */
            variables.set("x", 1);
            """));

        Assert.Empty(result.Errors);
    }

    [Fact]
    public void AMentionInAStringLiteralDoesNotBlockPublishing()
    {
        var result = WorkflowBpmnXml.ValidateProcess(ProcessWith("""
            variables.set("note", "we used to call execution.setVariable here");
            """));

        Assert.Empty(result.Errors);
    }

    [Fact]
    public void AMentionInATemplateLiteralDoesNotBlockPublishing()
    {
        var result = WorkflowBpmnXml.ValidateProcess(ProcessWith(
            "variables.set(\"note\", `old API: execution.setVariable`);"));

        Assert.Empty(result.Errors);
    }

    [Fact]
    public void AnEscapedQuoteDoesNotEndTheLiteralEarly()
    {
        // Without honouring the backslash, the literal would be read as ending
        // at the escaped quote and the rest of the line scanned as code —
        // which would flag this valid script.
        var result = WorkflowBpmnXml.ValidateProcess(ProcessWith(
            """variables.set("note", "it\"s about execution.setVariable");"""));

        Assert.Empty(result.Errors);
    }

    [Fact]
    public void StrippingDoesNotFuseIdentifiersAcrossARemovedComment()
    {
        // `a/*x*/b` must not become the identifier `ab`; a separator has to
        // survive, or stripping could manufacture a match that was not there.
        var stripped = ScriptSurfaceRules.StripCommentsAndStrings("executi/*x*/on.setVariable");

        Assert.DoesNotContain("execution", stripped);
    }

    // --- the existing rules are not regressed ---------------------------

    [Fact]
    public void ScriptFormatAndNonEmptyBodyAreStillEnforced()
    {
        var badFormat = WorkflowBpmnXml.ValidateProcess("""
            <?xml version="1.0" encoding="UTF-8"?>
            <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                              id="Definitions_1" targetNamespace="http://autonate.dev/workflows">
              <bpmn:process id="p" name="p" isExecutable="true">
                <bpmn:scriptTask id="S1" name="Compute" scriptFormat="groovy">
                  <bpmn:script>variables.set("x", 1);</bpmn:script>
                </bpmn:scriptTask>
              </bpmn:process>
            </bpmn:definitions>
            """);
        Assert.Contains(badFormat.Errors, e => e.Contains("javascript", StringComparison.Ordinal));

        var emptyBody = WorkflowBpmnXml.ValidateProcess(ProcessWith("   "));
        Assert.Contains(emptyBody.Errors, e => e.Contains("non-empty", StringComparison.Ordinal));
    }
}
