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

    [Fact]
    public void A_refusal_in_no_recognised_shape_is_still_reported()
    {
        // Silence is the failure mode this milestone keeps finding. An
        // unrecognised refusal is passed through rather than swallowed.
        var described = WorkflowEndpoints.DescribeEngineRefusal(Refusal("Something else went wrong"));

        Assert.Contains("Something else went wrong", described, StringComparison.Ordinal);
    }

    [Fact]
    public void An_empty_refusal_still_says_something()
    {
        var described = WorkflowEndpoints.DescribeEngineRefusal(Refusal("   "));

        Assert.Contains("gave no reason", described, StringComparison.Ordinal);
    }
}
