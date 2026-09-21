using AutoNate.Web.Services.Workflow;
using Xunit;

namespace AutoNate.Web.Tests;

/// <summary>
/// Which activities are multi-instance, and how many times they run (#173).
/// </summary>
/// <remarks>
/// <para>
/// <b>The diagram is the signal, because the engine's history is not.</b> Measured
/// on Flowable 8.0.0: a parallel multi-instance user task with cardinality 3
/// reports three historic rows sharing an activityId and an activityType, with no
/// container row — the same shape a loop that ran three times produces. Anything
/// keyed on repetition would collapse an ordinary repeated activity into progress
/// it never earned, which is the complement below.
/// </para>
/// <para>
/// No engine here on purpose, for #356's reason: the rule is a pure function over
/// XML, and making its guard need a running Flowable is what let a fixed-count
/// multi-instance stay unpublishable for four rounds.
/// </para>
/// </remarks>
public sealed class MultiInstanceProgressTests
{
    private static string Diagram(string task) => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          xmlns:autonate="http://autonate.dev/workflows"
                          targetNamespace="http://autonate.dev/workflows">
          <bpmn:process id="p" isExecutable="true">
            <bpmn:startEvent id="s" />
            {task}
            <bpmn:endEvent id="e" />
          </bpmn:process>
        </bpmn:definitions>
        """;

    /// <summary>
    /// BOTH spellings of a fixed count are read (#356, #173).
    /// </summary>
    /// <remarks>
    /// The studio writes <c>autonate:loopCardinality</c> as an attribute because
    /// bpmn-js has no Flowable moddle extension; publish converts it to the child
    /// element. A reader that knows only one of them is wrong for either every
    /// stored diagram or every deployed one — which is exactly the four-round
    /// outage #356 records, and the reason this second reader was put beside the
    /// first rather than given its own opinion.
    /// </remarks>
    [Theory]
    [InlineData("""<bpmn:multiInstanceLoopCharacteristics isSequential="false" autonate:loopCardinality="3" />""")]
    [InlineData("""<bpmn:multiInstanceLoopCharacteristics isSequential="false"><bpmn:loopCardinality>3</bpmn:loopCardinality></bpmn:multiInstanceLoopCharacteristics>""")]
    public void A_fixed_count_is_read_in_either_spelling(string loop)
    {
        var found = WorkflowBpmnXml.ExtractMultiInstanceActivities(
            Diagram($"""<bpmn:userTask id="review" name="Review">{loop}</bpmn:userTask>"""));

        var marker = Assert.Contains("review", found);
        Assert.Equal(3, marker.Cardinality);
        Assert.False(marker.IsSequential);
    }

    [Fact]
    public void A_sequential_marker_is_reported_as_sequential()
    {
        var found = WorkflowBpmnXml.ExtractMultiInstanceActivities(
            Diagram("""
                <bpmn:userTask id="review" name="Review">
                  <bpmn:multiInstanceLoopCharacteristics isSequential="true" autonate:loopCardinality="5" />
                </bpmn:userTask>
                """));

        Assert.True(Assert.Contains("review", found).IsSequential);
    }

    /// <summary>
    /// A collection-driven loop has no literal to read, and says so (#173).
    /// </summary>
    /// <remarks>
    /// Null rather than zero or one: the caller uses it to choose between the
    /// author's declared total and the number of instances the engine created, and
    /// a made-up number would silently win that choice.
    /// </remarks>
    [Fact]
    public void A_collection_driven_loop_declares_no_cardinality()
    {
        var found = WorkflowBpmnXml.ExtractMultiInstanceActivities(
            Diagram("""
                <bpmn:userTask id="review" name="Review">
                  <bpmn:multiInstanceLoopCharacteristics isSequential="false"
                                                        autonate:collection="reviewers" />
                </bpmn:userTask>
                """));

        Assert.Null(Assert.Contains("review", found).Cardinality);
    }

    /// <summary>
    /// THE COMPLEMENT: an ordinary activity is not multi-instance (#173).
    /// </summary>
    /// <remarks>
    /// The one that matters. History gives a multi-instance and a plain activity
    /// that ran twice the same shape — several rows with one activity id — so a
    /// reader keyed on repetition reports "1/2 complete" for a task that simply
    /// ran again. Nothing without the marker may be collapsed.
    /// </remarks>
    [Fact]
    public void An_activity_with_no_marker_is_not_multi_instance()
    {
        var found = WorkflowBpmnXml.ExtractMultiInstanceActivities(
            Diagram("""<bpmn:userTask id="review" name="Review" />"""));

        Assert.Empty(found);
    }
}
