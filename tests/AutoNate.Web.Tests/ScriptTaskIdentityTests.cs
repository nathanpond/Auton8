using System.Xml.Linq;
using AutoNate.Web.Services.Workflow;
using Xunit;

namespace AutoNate.Web.Tests;

// #153: publishing fails when a script task's identity cannot be determined.
//
// The property is the easy half. The substance is the graph analysis, and its
// failure mode is asymmetric: a wrongly *permissive* answer publishes a script
// with an identity nobody chose, while a wrongly restrictive one merely asks
// the author a question. So the ambiguous cases are asserted to be refused.
public sealed class ScriptTaskIdentityTests
{
    private static string Process(string body, string? runAs = null)
    {
        var attr = runAs is null ? "" : $" an8:runAs=\"{runAs}\"";
        return $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                              xmlns:an8="http://autonate.dev/workflows"
                              id="Definitions_1" targetNamespace="http://autonate.dev/workflows">
              <bpmn:process id="p" name="p" isExecutable="true">
                {body.Replace("@RUNAS@", attr)}
              </bpmn:process>
            </bpmn:definitions>
            """;
    }

    private static string Flow(string id, string from, string to) =>
        $"""<bpmn:sequenceFlow id="{id}" sourceRef="{from}" targetRef="{to}" />""";

    private static string Script(string id, string name = "Compute") =>
        $"""
        <bpmn:scriptTask id="{id}" name="{name}" scriptFormat="javascript"@RUNAS@>
          <bpmn:script>variables.set("x", 1);</bpmn:script>
        </bpmn:scriptTask>
        """;

    private static IReadOnlyList<string> Errors(string xml) =>
        ScriptTaskIdentity.BuildIdentityValidationErrors(XDocument.Parse(xml));

    // --- the common case must stay quiet --------------------------------

    [Fact]
    public void ASingleUnambiguousPrecedingUserTaskPublishesWithRunAsUnset()
    {
        // The ordinary workflow. If this needed an elevated permission the
        // validation would have made everyday authoring worse, which the story
        // explicitly rules out.
        var xml = Process($"""
            <bpmn:startEvent id="s" />
            <bpmn:userTask id="u" name="Approve" />
            {Script("t")}
            <bpmn:endEvent id="e" />
            {Flow("f1", "s", "u")}
            {Flow("f2", "u", "t")}
            {Flow("f3", "t", "e")}
            """);

        Assert.Empty(Errors(xml));
    }

    // --- no preceding user task -----------------------------------------

    [Fact]
    public void AScriptBeforeAnyUserTaskIsRefused()
    {
        var xml = Process($"""
            <bpmn:startEvent id="s" />
            {Script("t")}
            <bpmn:userTask id="u" name="Approve" />
            {Flow("f1", "s", "t")}
            {Flow("f2", "t", "u")}
            """);

        var error = Assert.Single(Errors(xml));
        Assert.Contains("Compute", error);
        Assert.Contains("without a preceding user task", error);
        // The message must say what the choice is, not merely that something is
        // wrong — an author who has not thought about identity needs to learn
        // the options here.
        Assert.Contains("System", error);
        Assert.Contains("Workflow author", error);
    }

    [Fact]
    public void ATimerStartProcessIsRefused()
    {
        // Nobody started it, so there is no assignee anywhere upstream.
        var xml = Process($"""
            <bpmn:startEvent id="s"><bpmn:timerEventDefinition /></bpmn:startEvent>
            {Script("t")}
            {Flow("f1", "s", "t")}
            """);

        Assert.Contains(Errors(xml), e => e.Contains("without a preceding user task"));
    }

    [Fact]
    public void OneBranchWithoutAUserTaskIsEnoughToRefuse()
    {
        // The analysis is over ALL paths, not any path. A script reachable by
        // one route that passes a user task and another that does not still has
        // no determinate identity.
        var xml = Process($"""
            <bpmn:startEvent id="s" />
            <bpmn:exclusiveGateway id="g" />
            <bpmn:userTask id="u" name="Approve" />
            {Script("t")}
            {Flow("f1", "s", "g")}
            {Flow("f2", "g", "u")}
            {Flow("f3", "u", "t")}
            {Flow("f4", "g", "t")}
            """);

        Assert.Contains(Errors(xml), e => e.Contains("without a preceding user task"));
    }

    // --- after a join ----------------------------------------------------

    [Fact]
    public void AScriptAfterAParallelJoinIsRefusedEvenThoughEveryBranchHasAUserTask()
    {
        // Both branches have an assignee, which is exactly the problem: "the
        // last user task" has two answers.
        var xml = Process($"""
            <bpmn:startEvent id="s" />
            <bpmn:parallelGateway id="split" />
            <bpmn:userTask id="u1" name="Approve A" />
            <bpmn:userTask id="u2" name="Approve B" />
            <bpmn:parallelGateway id="join" />
            {Script("t")}
            {Flow("f1", "s", "split")}
            {Flow("f2", "split", "u1")}
            {Flow("f3", "split", "u2")}
            {Flow("f4", "u1", "join")}
            {Flow("f5", "u2", "join")}
            {Flow("f6", "join", "t")}
            """);

        var error = Assert.Single(Errors(xml));
        Assert.Contains("after a parallel join", error);
    }

    [Fact]
    public void AParallelSplitIsNotAJoin()
    {
        // A gateway with one incoming and several outgoing is a split. Treating
        // it as a join would refuse a great many ordinary diagrams.
        var xml = Process($"""
            <bpmn:startEvent id="s" />
            <bpmn:userTask id="u" name="Approve" />
            <bpmn:parallelGateway id="split" />
            {Script("t")}
            <bpmn:endEvent id="e" />
            {Flow("f1", "s", "u")}
            {Flow("f2", "u", "split")}
            {Flow("f3", "split", "t")}
            {Flow("f4", "split", "e")}
            """);

        Assert.Empty(Errors(xml));
    }

    // --- an explicit declaration settles it ------------------------------

    [Theory]
    [InlineData("system")]
    [InlineData("workflowAuthor")]
    public void AnExplicitRunAsSatisfiesTheAmbiguousCase(string runAs)
    {
        // Consistent with the no-assignee case: after a join an explicit
        // declaration is required, and workflowAuthor satisfies it as well as
        // system.
        var xml = Process($"""
            <bpmn:startEvent id="s" />
            {Script("t")}
            {Flow("f1", "s", "t")}
            """, runAs);

        Assert.Empty(Errors(xml));
    }

    // --- conservative where it cannot know -------------------------------

    [Fact]
    public void ACallActivityDoesNotCountAsAUserTask()
    {
        // The called process is not in this document, so whether it contained a
        // user task is unknowable. A permissive guess is the expensive mistake.
        var xml = Process($"""
            <bpmn:startEvent id="s" />
            <bpmn:callActivity id="c" name="Sub" calledElement="other" />
            {Script("t")}
            {Flow("f1", "s", "c")}
            {Flow("f2", "c", "t")}
            """);

        Assert.Contains(Errors(xml), e => e.Contains("without a preceding user task"));
    }

    [Fact]
    public void AnInterruptedUserTaskDoesNotSupplyAnIdentity()
    {
        // A boundary event fires while the user task is still open, so its
        // assignee completed nothing. Counting it would attribute a script to
        // someone who never finished the step.
        var xml = Process($"""
            <bpmn:startEvent id="s" />
            <bpmn:userTask id="u" name="Approve" />
            <bpmn:boundaryEvent id="b" attachedToRef="u"><bpmn:timerEventDefinition /></bpmn:boundaryEvent>
            {Script("t")}
            <bpmn:endEvent id="e" />
            {Flow("f1", "s", "u")}
            {Flow("f2", "u", "e")}
            {Flow("f3", "b", "t")}
            """);

        Assert.Contains(Errors(xml), e => e.Contains("without a preceding user task"));
    }

    [Fact]
    public void ALoopBackToAUserTaskTerminatesAndStaysQuiet()
    {
        // A cycle must not hang the fixpoint or produce a spurious error.
        var xml = Process($"""
            <bpmn:startEvent id="s" />
            <bpmn:userTask id="u" name="Approve" />
            {Script("t")}
            <bpmn:exclusiveGateway id="g" />
            {Flow("f1", "s", "u")}
            {Flow("f2", "u", "t")}
            {Flow("f3", "t", "g")}
            {Flow("f4", "g", "u")}
            """);

        Assert.Empty(Errors(xml));
    }

    // --- reading the declaration -----------------------------------------

    [Fact]
    public void TheDeclarationIsReadFromTheAutoNateNamespace()
    {
        // The namespace is on the do-not-rename list; reading it from anywhere
        // else would orphan the property on the next publish.
        var doc = XDocument.Parse(Process(Script("t"), "system"));
        Assert.True(ScriptTaskIdentity.DeclaresSystemIdentity(doc));
        Assert.Equal("system", ScriptTaskIdentity.DeclaredIdentities(doc)["t"]);
    }

    [Fact]
    public void AWorkflowAuthorDeclarationIsNotASystemDeclaration()
    {
        // The publish-time permission check keys on this, so conflating them
        // would demand an elevated permission for an unprivileged choice.
        var doc = XDocument.Parse(Process(Script("t"), "workflowAuthor"));
        Assert.False(ScriptTaskIdentity.DeclaresSystemIdentity(doc));
    }
}
