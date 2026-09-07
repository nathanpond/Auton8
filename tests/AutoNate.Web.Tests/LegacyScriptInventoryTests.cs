using AutoNate.Web.Services.Workflow;
using Xunit;

namespace AutoNate.Web.Tests;

// #194: find saved workflow models written against the API #147 removed.
//
// #151 protects everything authored from now on. This is for the diagrams that
// were already published, which fail at runtime with nothing having warned
// anyone — the gap /n8-verify found on a real database, where 4 of 5 workflows
// with script tasks were affected.
public sealed class LegacyScriptInventoryTests
{
    private static string Model(params string[] scripts)
    {
        var tasks = string.Join("", scripts.Select((s, i) =>
            $"""
             <bpmn:scriptTask id="t{i}" name="Task{i}" scriptFormat="javascript">
               <bpmn:script>{System.Security.SecurityElement.Escape(s)}</bpmn:script>
             </bpmn:scriptTask>
             """));
        return $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                              id="D" targetNamespace="http://autonate.dev/workflows">
              <bpmn:process id="p" name="p" isExecutable="true">{tasks}</bpmn:process>
            </bpmn:definitions>
            """;
    }

    [Fact]
    public void ItFindsTheRemovedApiAndNamesTheTaskAndTheReplacement()
    {
        var findings = LegacyScriptInventory.Scan(Model("""execution.setVariable("x", 1);"""));

        var finding = Assert.Single(findings);
        Assert.Equal("Task0", finding.ScriptTask);
        // Naming the task alone would leave an operator opening every script
        // to find out what to change.
        Assert.Contains("variables.set", finding.Problem);
    }

    [Fact]
    public void ItReportsEachAffectedTaskSeparately()
    {
        // A model with several bad scripts is several pieces of work, and an
        // operator planning the edit needs the count, not a boolean.
        var findings = LegacyScriptInventory.Scan(Model(
            """execution.setVariable("x", 1);""",
            """variables.set("ok", true);""",
            """var v = execution.getVariable("y");"""));

        Assert.Equal(2, findings.Count);
        Assert.Equal(new List<string> { "Task0", "Task2" }, findings.Select(f => f.ScriptTask).ToList());
    }

    [Fact]
    public void AModelUsingTheCurrentApiIsNotReported()
    {
        Assert.Empty(LegacyScriptInventory.Scan(Model("""variables.set("x", 1);""")));
    }

    [Fact]
    public void AMentionInACommentIsNotReported()
    {
        // Same false-positive guard as the publish validator, because it is
        // the same detection. A report that cries wolf gets ignored.
        Assert.Empty(LegacyScriptInventory.Scan(Model("""
            // replaces execution.setVariable
            variables.set("x", 1);
            """)));
    }

    [Fact]
    public void UnparseableXmlYieldsNothingRatherThanThrowing()
    {
        // This runs over whatever is already stored, including rows written by
        // older builds. A report that dies on one malformed model tells an
        // operator less than one that skips it.
        Assert.Empty(LegacyScriptInventory.Scan("<not-xml"));
        Assert.Empty(LegacyScriptInventory.Scan(null));
        Assert.Empty(LegacyScriptInventory.Scan(""));
    }

    [Fact]
    public void DetectionIsTheSameSourceAsThePublishValidator()
    {
        // The point of reusing ScriptSurfaceRules: a fourth consumer with its
        // own copy of the rules is how they start disagreeing. If the two ever
        // diverge, this fails.
        const string script = """execution.removeVariable("x");""";
        var fromInventory = LegacyScriptInventory.Scan(Model(script)).Select(f => f.Problem);
        var fromRules = ScriptSurfaceRules.FindRejected(script);

        Assert.Equal(fromRules, fromInventory);
    }
}
