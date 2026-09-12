using System.Net;
using AutoNate.Web.Endpoints;
using AutoNate.Web.Services.Flowable;
using Xunit;

namespace AutoNate.Web.Tests.Workflow;

/// <summary>
/// What an author is told when the engine refuses a deployment (#334).
/// </summary>
/// <remarks>
/// <para>
/// Epic #40 AC3 says an element that cannot execute is "refused at deployment
/// <b>with a reason</b>". What publish actually did was let the
/// <see cref="FlowableRequestException"/> escape, so the author got HTTP 500
/// carrying a Java stack trace and absolute filesystem paths.
/// </para>
/// <para>
/// Publish validation catches what Auton8 knows about (#316, #333), but the
/// engine will always refuse things we do not predict. This is what happens
/// then, and it is the difference between AC3 being true of the API and true of
/// the product.
/// </para>
/// </remarks>
public sealed class EngineRefusalMessageTests
{
    private static FlowableRequestException Refusal(string message) =>
        new(HttpStatusCode.BadRequest, "deploy process", message);

    /// <summary>The real shapes, copied from live 8.0.0 refusals.</summary>
    private const string ServiceTaskRefusal =
        "[Validation set: 'flowable-executable-process' | Problem: "
        + "'flowable-servicetask-missing-implementation'] : Service task does not have an "
        + "implementation defined - [Extra info : processDefinitionId = z1 | id = st ] "
        + "( line: 4, column: 82)";

    [Fact]
    public void The_engine_problem_code_and_its_sentence_both_survive()
    {
        var described = WorkflowEndpoints.DescribeEngineRefusal(Refusal(ServiceTaskRefusal));

        // The prose, because it is what an author can act on.
        Assert.Contains("Service task does not have an implementation defined", described, StringComparison.Ordinal);
        // The code, because it is stable and searchable.
        Assert.Contains("flowable-servicetask-missing-implementation", described, StringComparison.Ordinal);
    }

    [Fact]
    public void The_diagnostic_tail_with_ids_and_positions_is_dropped()
    {
        var described = WorkflowEndpoints.DescribeEngineRefusal(Refusal(ServiceTaskRefusal));

        Assert.DoesNotContain("Extra info", described, StringComparison.Ordinal);
        Assert.DoesNotContain("processDefinitionId", described, StringComparison.Ordinal);
        Assert.DoesNotContain("line: 4", described, StringComparison.Ordinal);
    }

    /// <summary>
    /// No filesystem path reaches the author, whatever the engine said.
    /// </summary>
    /// <remarks>
    /// The half of #334 that is a leak rather than a usability problem. A stack
    /// trace with absolute paths should not reach a browser regardless of how
    /// readable the rest is.
    /// </remarks>
    [Theory]
    [InlineData("org.flowable.common.engine.api.FlowableException: boom\n\tat org.flowable.Foo.bar(Foo.java:42)\n\tat /Users/someone/app/src/Thing.cs:17")]
    [InlineData("Problem at /opt/flowable/webapps/ROOT/WEB-INF/classes/process.bpmn20.xml")]
    public void No_stack_frame_or_absolute_path_is_passed_through(string raw)
    {
        var described = WorkflowEndpoints.DescribeEngineRefusal(Refusal(raw));

        Assert.DoesNotContain("\tat ", described, StringComparison.Ordinal);
        Assert.DoesNotContain(".java:", described, StringComparison.Ordinal);
        Assert.DoesNotContain("/Users/", described, StringComparison.Ordinal);
        Assert.DoesNotContain("/opt/", described, StringComparison.Ordinal);
    }

    /// <summary>
    /// Built the way the CALLER builds it, which is what #339 turned on.
    /// </summary>
    /// <remarks>
    /// <c>FlowableClient.EnsureSuccessAsync</c> throws
    /// <c>$"Flowable could not {operation}. HTTP {code} {reason}. {rawResponseBody}"</c>
    /// — and the raw body is JSON, so a stack trace arrives with the
    /// two-character escapes <c>\n</c> and <c>\t</c>, not real control
    /// characters. The first version of these tests hand-wrote unescaped strings
    /// and therefore asserted against a shape no caller ever produces: the
    /// sanitiser never fired in production and the tests could not tell.
    /// </remarks>
    private static FlowableRequestException AsTheClientBuildsIt(string rawResponseBody) =>
        new(HttpStatusCode.BadRequest, "deploy process",
            $"Flowable could not deploy process. HTTP 400 Bad Request. {rawResponseBody}");

    [Fact]
    public void A_json_escaped_java_trace_does_not_reach_the_caller()
    {
        // The exact body shape Flowable/Spring returns, escapes and all.
        var body = "{\"timestamp\":\"2026-09-12\",\"status\":400,\"error\":\"Bad Request\","
            + "\"trace\":\"org.flowable.common.engine.api.FlowableException: Error parsing XML"
            + "\\n\\tat org.flowable.bpmn.converter.BpmnXMLConverter.convertToBpmnModel(BpmnXMLConverter.java:198)"
            + "\\n\\tat org.flowable.engine.impl.bpmn.deployer.ParsedDeploymentBuilder.build(ParsedDeploymentBuilder.java:61)\\n\","
            + "\"path\":\"/flowable-rest/service/repository/deployments\"}";

        var described = WorkflowEndpoints.DescribeEngineRefusal(AsTheClientBuildsIt(body));

        Assert.DoesNotContain(".java:", described, StringComparison.Ordinal);
        Assert.DoesNotContain("\tat ", described, StringComparison.Ordinal);
        Assert.DoesNotContain("\\n\\tat", described, StringComparison.Ordinal);
        Assert.DoesNotContain("/flowable-rest/", described, StringComparison.Ordinal);
        Assert.Contains("could not be shown safely", described, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every shape a verifier got a leak through, as a table (#339).
    /// </summary>
    /// <remarks>
    /// The previous implementation redacted dangerous substrings and passed the
    /// remainder through. These are the inputs that beat it — relative paths, a
    /// <c>../</c> prefix, a space inside a path segment, a one-line frame, and a
    /// filesystem path smuggled through the problem-code slot. Redaction is a
    /// losing game; the rule now extracts an allowlist and applies a
    /// post-condition, discarding anything that still looks like internals.
    /// </remarks>
    [Theory]
    // one-line Java frames, no newline and no tab anywhere
    [InlineData("org.flowable.common.engine.api.FlowableException: boom at org.flowable.engine.impl.bpmn.deployer.BpmnDeployer.deploy(BpmnDeployer.java:142)")]
    // a RELATIVE path -- the old regex's lookbehind never masked one
    [InlineData("Could not parse src/main/resources/org/flowable/secret.bpmn20.xml")]
    // a ../ prefix, so the leading separator is preceded by a dot
    [InlineData("Resource ../../Users/npond/RiderProjects/AutoNate/secret.bpmn could not be read")]
    // a space inside a segment leaked the remainder
    [InlineData("Failed reading /Users/npond/My Projects/AutoNate/process.bpmn20.xml")]
    // a Windows drive
    [InlineData(@"Failed reading C:\Users\npond\AppData\flowable\process.bpmn20.xml")]
    public void No_internals_reach_the_caller_whatever_the_engine_said(string body)
    {
        var described = WorkflowEndpoints.DescribeEngineRefusal(AsTheClientBuildsIt(body));

        Assert.DoesNotContain("/Users/", described, StringComparison.Ordinal);
        Assert.DoesNotContain(@"C:\", described, StringComparison.Ordinal);
        Assert.DoesNotContain(".java:", described, StringComparison.Ordinal);
        Assert.DoesNotContain("src/main", described, StringComparison.Ordinal);
        Assert.DoesNotContain("RiderProjects", described, StringComparison.Ordinal);
        Assert.DoesNotContain(".bpmn", described, StringComparison.Ordinal);
    }

    /// <summary>
    /// Internals inside the RECOGNISED prose are discarded, not redacted (#339).
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the row that makes the post-condition load-bearing, and writing it
    /// is how I found that the first set of #339 rows did not: every one of those
    /// bodies lacks the <c>] :</c> marker, so prose extraction found nothing and
    /// they all reached the generic by the empty path. Removing the
    /// post-condition entirely left them green.
    /// </para>
    /// <para>
    /// A test that passes for the right outcome by a path the change does not
    /// touch is not a guard — which is the same lesson as #336 and #292, arrived
    /// at from the opposite direction.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("Failed reading /Users/npond/secrets/process.bpmn20.xml")]
    [InlineData("Parse failed at org.flowable.bpmn.converter.BpmnXMLConverter.convert(BpmnXMLConverter.java:198)")]
    [InlineData("Could not open src/main/resources/flowable/thing.xml")]
    [InlineData(@"Could not open C:\Users\npond\flowable\thing.xml")]
    public void Internals_inside_a_recognised_reason_discard_the_whole_reason(string prose)
    {
        var described = WorkflowEndpoints.DescribeEngineRefusal(AsTheClientBuildsIt(
            $"[Validation set: 'flowable-executable-process' | Problem: 'flowable-bpmn-parse-failure'] : {prose}"));

        // The code survives -- it is shaped so it cannot carry anything.
        Assert.Contains("flowable-bpmn-parse-failure", described, StringComparison.Ordinal);
        // The prose does not.
        Assert.Contains("could not be shown safely", described, StringComparison.Ordinal);
        Assert.DoesNotContain("/Users/", described, StringComparison.Ordinal);
        Assert.DoesNotContain(@"C:\", described, StringComparison.Ordinal);
        Assert.DoesNotContain(".java:", described, StringComparison.Ordinal);
        Assert.DoesNotContain("src/main", described, StringComparison.Ordinal);
    }

    /// <summary>
    /// An escaped trace inside recognised prose is caught too (#339).
    /// </summary>
    /// <remarks>
    /// Without the escape normalisation the literal <c>\n</c> is not a line break,
    /// so <c>[^\r\n]+</c> swallows the whole trace into the "reason". The
    /// post-condition then has to catch it — and this row asserts the two work
    /// together rather than assuming either does alone.
    /// </remarks>
    [Fact]
    public void An_escaped_trace_inside_a_recognised_reason_is_discarded()
    {
        var described = WorkflowEndpoints.DescribeEngineRefusal(AsTheClientBuildsIt(
            "[Validation set: 'flowable-executable-process' | Problem: 'flowable-bpmn-parse-failure'] : "
            + "Error parsing XML\\n\\tat org.flowable.bpmn.converter.BpmnXMLConverter.convert(BpmnXMLConverter.java:198)"));

        Assert.DoesNotContain(".java:", described, StringComparison.Ordinal);
        Assert.DoesNotContain("\\n\\tat", described, StringComparison.Ordinal);
        Assert.DoesNotContain("org.flowable.bpmn.converter", described, StringComparison.Ordinal);
    }

    /// <summary>
    /// The escape normalisation earns its place — readability, not safety (#339).
    /// </summary>
    /// <remarks>
    /// Removing the normalisation leaves every *safety* row here green, because
    /// the post-condition catches anything it would have truncated. So this is
    /// the row that makes it not-dead-code, and the docstring on
    /// <c>DescribeEngineRefusal</c> says plainly which of the two is the guard.
    /// Claiming a defence that another line is actually providing is how #339
    /// happened in the first place.
    /// </remarks>
    [Fact]
    public void An_escaped_newline_does_not_drag_its_continuation_into_the_reason()
    {
        var described = WorkflowEndpoints.DescribeEngineRefusal(AsTheClientBuildsIt(
            "[Validation set: 'flowable-executable-process' | Problem: 'flowable-bpmn-parse-failure'] : "
            + "The element is not allowed here\\n  and some continuation nobody needs"));

        Assert.Contains("The element is not allowed here", described, StringComparison.Ordinal);
        Assert.DoesNotContain("continuation nobody needs", described, StringComparison.Ordinal);
        Assert.DoesNotContain("\\n", described, StringComparison.Ordinal);
    }

    /// <summary>
    /// The problem-code slot cannot smuggle a path (#339).
    /// </summary>
    /// <remarks>
    /// It was appended verbatim from <c>'[^']+'</c>, so anything between quotes
    /// travelled. The pattern is now <c>flowable-[a-z0-9-]+</c>, which a path
    /// cannot satisfy.
    /// </remarks>
    [Fact]
    public void A_path_in_the_problem_code_slot_is_not_emitted()
    {
        var described = WorkflowEndpoints.DescribeEngineRefusal(AsTheClientBuildsIt(
            "[Validation set: 'x' | Problem: '/Users/npond/secrets/keys.java:31'] : Something broke"));

        Assert.DoesNotContain("/Users/", described, StringComparison.Ordinal);
        Assert.DoesNotContain(".java:", described, StringComparison.Ordinal);
    }

    /// <summary>
    /// The real refusal still reads well — the point of all this.
    /// </summary>
    /// <remarks>
    /// A post-condition that discards everything would pass every row above while
    /// making the feature useless. This is the row that stops that.
    /// </remarks>
    [Fact]
    public void A_real_refusal_still_names_the_problem_and_reads_as_a_sentence()
    {
        var described = WorkflowEndpoints.DescribeEngineRefusal(AsTheClientBuildsIt(
            "[Validation set: 'flowable-executable-process' | Problem: "
            + "'flowable-servicetask-missing-implementation'] : Service task does not have an "
            + "implementation defined - [Extra info : processDefinitionId = z1 | id = st ] "
            + "( line: 4, column: 82)"));

        Assert.Contains("Service task does not have an implementation defined", described, StringComparison.Ordinal);
        Assert.Contains("flowable-servicetask-missing-implementation", described, StringComparison.Ordinal);
        Assert.DoesNotContain("Extra info", described, StringComparison.Ordinal);
        Assert.DoesNotContain("line: 4", described, StringComparison.Ordinal);
        Assert.DoesNotContain("could not be shown safely", described, StringComparison.Ordinal);
    }

    [Fact]
    public void A_refusal_in_no_recognised_shape_is_still_reported()
    {
        // Silence is the failure mode this milestone keeps finding. An
        // unrecognised refusal is passed through rather than swallowed.
        // Not passed through any more (#339) -- pass-through is what leaked. The
        // caller is told a refusal happened and where the full text lives.
        var described = WorkflowEndpoints.DescribeEngineRefusal(Refusal("Something else went wrong"));

        Assert.Contains("refused this workflow", described, StringComparison.Ordinal);
        Assert.Contains("server log", described, StringComparison.Ordinal);
    }

    [Fact]
    public void An_empty_refusal_still_says_something()
    {
        var described = WorkflowEndpoints.DescribeEngineRefusal(Refusal("   "));

        Assert.Contains("refused this workflow", described, StringComparison.Ordinal);
    }
}
