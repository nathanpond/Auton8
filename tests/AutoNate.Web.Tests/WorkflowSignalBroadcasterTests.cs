using AutoNate.Web.Models;
using AutoNate.Web.Services.Workflow;
using Xunit;

namespace AutoNate.Web.Tests;

/// <summary>
/// Firing a named signal from outside any instance (#523).
/// </summary>
/// <remarks>
/// <para>
/// The engine half — that a woken instance actually advances — needs Flowable and
/// lives in the E2E suite. Everything here is decidable without one: which
/// workflows declare a catch for a name, whether a name nothing declares is
/// refused rather than broadcast, and whether the broadcast reached the client at
/// all. That split is deliberate: put the decision inline in the endpoint and the
/// merge gate can see none of it.
/// </para>
/// </remarks>
public sealed class WorkflowSignalBroadcasterTests
{
    private const string Caught = "order.cancelled";

    [Fact]
    public async Task A_signal_a_published_workflow_catches_is_broadcast()
    {
        var flowable = new StubFlowableClient();
        var broadcaster = Broadcaster(flowable, ("orders", StartCatching(Caught)));

        var result = await broadcaster.BroadcastAsync(Caught, null);

        Assert.Equal(WorkflowSignalBroadcaster.Outcome.Broadcast, result.Outcome);
        Assert.Equal(["orders"], result.Declaring);

        // THE BROADCAST ACTUALLY HAPPENED. Asserting only the outcome enum would
        // pass for a service that decided correctly and then called nothing,
        // which is "deploys and does nothing" wearing a different hat (#325).
        Assert.Equal([(Caught, (IReadOnlyDictionary<string, object?>?)null)], flowable.BroadcastedSignals);
    }

    /// <summary>
    /// The complement, and the reason this service exists at all (#523 AC4).
    /// </summary>
    /// <remarks>
    /// Flowable accepts a broadcast for a name nothing subscribes to and answers
    /// 204. A pass-through endpoint would report success for a signal that
    /// reached nobody — indistinguishable from the feature being broken.
    /// </remarks>
    [Fact]
    public async Task A_signal_nothing_catches_is_refused_and_nothing_is_broadcast()
    {
        var flowable = new StubFlowableClient();
        var broadcaster = Broadcaster(flowable, ("orders", StartCatching(Caught)));

        var result = await broadcaster.BroadcastAsync("nobody.listens", null);

        Assert.Equal(WorkflowSignalBroadcaster.Outcome.UnknownSignal, result.Outcome);
        Assert.Equal([Caught], result.Available);

        // NOTHING WAS FIRED. The assertion that matters: a refusal that still
        // broadcast would be a refusal in the response body only.
        Assert.Empty(flowable.BroadcastedSignals);
    }

    /// <summary>
    /// A boundary catch is a declaration (#523).
    /// </summary>
    /// <remarks>
    /// <c>IWorkflowSignalRegistry</c> holds signal START events, because its job
    /// is deciding which Dapr topics to subscribe to. Resolving refusals against
    /// it would refuse a name caught only by a boundary or intermediate event —
    /// a running instance waiting on a signal, which is exactly what a broadcast
    /// is for, and exactly the shape #529's Signal Boundary row needs.
    /// </remarks>
    [Fact]
    public async Task A_name_caught_only_by_a_boundary_event_still_counts_as_declared()
    {
        var flowable = new StubFlowableClient();
        var broadcaster = Broadcaster(flowable, ("orders", BoundaryCatching(Caught)));

        var result = await broadcaster.BroadcastAsync(Caught, null);

        Assert.Equal(WorkflowSignalBroadcaster.Outcome.Broadcast, result.Outcome);
        Assert.Equal(["orders"], result.Declaring);
    }

    /// <summary>
    /// Raising a name is not waiting for it (#523).
    /// </summary>
    /// <remarks>
    /// If a throw counted as a declaration, "nothing catches this signal" would
    /// be unreachable for any name already in use — and the refusal AC4 asks for
    /// would never fire on a real engine.
    /// </remarks>
    [Fact]
    public async Task A_workflow_that_only_throws_the_name_does_not_declare_it()
    {
        var flowable = new StubFlowableClient();
        var broadcaster = Broadcaster(flowable, ("orders", ThrowingOnly(Caught)));

        var result = await broadcaster.BroadcastAsync(Caught, null);

        Assert.Equal(WorkflowSignalBroadcaster.Outcome.UnknownSignal, result.Outcome);
        Assert.Empty(flowable.BroadcastedSignals);
    }

    /// <summary>Every workflow listening is named, not just the first (#523).</summary>
    /// <summary>
    /// The broadcaster's answer follows the store's list (#563).
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the test that owns that property, and a note is worth leaving
    /// because it took three rounds to work out. #544 shipped a test named
    /// <c>A_draft_edit_that_drops_the_name_does_not_undeclare_it</c> that tested
    /// nothing — the broadcaster sees only <c>ListPublishedAsync</c>'s output, so
    /// a draft edit is invisible from here by construction. #552 renamed it and
    /// left it assertion-for-assertion identical to
    /// <c>A_signal_a_published_workflow_catches_is_broadcast</c>. #559 gave it a
    /// second workflow and wrote "and nothing else does" beside it — while THIS
    /// test, shipped in #539, already asserted the same property with three
    /// workflows and a larger fixture.
    /// </para>
    /// <para>
    /// So the fourth round deleted it rather than sharpening it again. The
    /// published-versus-draft xml is guarded where the query is, in
    /// <c>EfCoreWorkflowModelStoreTests</c>; the filtering is guarded here; and
    /// <c>UnknownSignal</c> is asserted three times already in this file. There
    /// was nothing left for a fourth test to carry.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Every_workflow_catching_the_name_is_reported()
    {
        var flowable = new StubFlowableClient();
        var broadcaster = Broadcaster(
            flowable,
            ("orders", StartCatching(Caught)),
            ("returns", BoundaryCatching(Caught)),
            ("billing", StartCatching("something.else")));

        var result = await broadcaster.BroadcastAsync(Caught, null);

        Assert.Equal(WorkflowSignalBroadcaster.Outcome.Broadcast, result.Outcome);
        Assert.Equal(["orders", "returns"], result.Declaring);
    }

    [Fact]
    public async Task The_callers_variables_reach_the_engine()
    {
        var flowable = new StubFlowableClient();
        var broadcaster = Broadcaster(flowable, ("orders", StartCatching(Caught)));
        var variables = new Dictionary<string, object?> { ["reason"] = "fraud" };

        await broadcaster.BroadcastAsync(Caught, variables);

        Assert.Single(flowable.BroadcastedSignals);
        Assert.Equal(variables, flowable.BroadcastedSignals[0].Variables);
    }

    /// <summary>
    /// A never-published draft is not a declaration (#544).
    /// </summary>
    /// <remarks>
    /// The reported defect. The broadcaster scanned every model row and read each
    /// one's WORKING xml, so a draft nobody had published counted as a
    /// declaration and the endpoint answered 200 for a name nothing in the engine
    /// subscribes to — "report success for a signal that reached nobody", which
    /// is the exact failure the refusal exists to prevent, arriving by the one
    /// door the refusal did not cover.
    /// </remarks>
    [Fact]
    public async Task A_never_published_draft_does_not_declare_anything()
    {
        var flowable = new StubFlowableClient();

        // The store answers what the STORE would answer: a draft is not in the
        // published list at all.
        var broadcaster = new WorkflowSignalBroadcaster(
            new ListingModelStore([]), flowable);

        var result = await broadcaster.BroadcastAsync(Caught, null);

        Assert.Equal(WorkflowSignalBroadcaster.Outcome.UnknownSignal, result.Outcome);
        Assert.Empty(flowable.BroadcastedSignals);
    }

    // ---- diagrams ------------------------------------------------------------

    private static string StartCatching(string signalName) => Wrap(signalName,
        $"""<startEvent id="s"><signalEventDefinition signalRef="Sig_1"/></startEvent>""");

    private static string BoundaryCatching(string signalName) => Wrap(signalName,
        """<startEvent id="s"/><userTask id="t" name="wait"/>"""
        + """<boundaryEvent id="b" attachedToRef="t"><signalEventDefinition signalRef="Sig_1"/></boundaryEvent>""");

    private static string ThrowingOnly(string signalName) => Wrap(signalName,
        """<startEvent id="s"/><endEvent id="e"><signalEventDefinition signalRef="Sig_1"/></endEvent>""");

    private static string Wrap(string signalName, string body) => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          targetNamespace="http://autonate.dev/workflows">
          <bpmn:signal id="Sig_1" name="{signalName}" />
          <bpmn:process id="p" isExecutable="true">
            {body}
          </bpmn:process>
        </bpmn:definitions>
        """.Replace("<startEvent", "<bpmn:startEvent", StringComparison.Ordinal)
           .Replace("</startEvent", "</bpmn:startEvent", StringComparison.Ordinal)
           .Replace("<endEvent", "<bpmn:endEvent", StringComparison.Ordinal)
           .Replace("</endEvent", "</bpmn:endEvent", StringComparison.Ordinal)
           .Replace("<userTask", "<bpmn:userTask", StringComparison.Ordinal)
           .Replace("<boundaryEvent", "<bpmn:boundaryEvent", StringComparison.Ordinal)
           .Replace("</boundaryEvent", "</bpmn:boundaryEvent", StringComparison.Ordinal)
           .Replace("<signalEventDefinition", "<bpmn:signalEventDefinition", StringComparison.Ordinal);

    private static WorkflowSignalBroadcaster Broadcaster(
        StubFlowableClient flowable, params (string Key, string Xml)[] models) =>
        new(new ListingModelStore(models
            .Select(m => new WorkflowModel
            {
                Id = Guid.NewGuid(),
                Name = m.Key,
                ProcessKey = m.Key,
                BpmnXml = m.Xml
            })
            .ToList()), flowable);

    // Answers `ListPublishedAsync`, not `ListAsync` (#544). The old fixture
    // handed everything to a broadcaster that filtered nothing, so every "is
    // broadcast" assertion in this file was made against an unpublished draft --
    // the bug was not merely untested, it was baked into the harness. Now the
    // store models the real contract: only published rows, carrying the
    // PUBLISHED xml.
    private sealed class ListingModelStore(IReadOnlyList<WorkflowModel> models) : IWorkflowModelStore
    {
        public Task<IReadOnlyList<WorkflowModel>> ListPublishedAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(models);

        public Task<IReadOnlyList<WorkflowModel>> ListAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException(
                "The broadcaster must ask for PUBLISHED workflows. Reaching ListAsync means the "
                + "filter came off again (#544).");

        public Task<WorkflowModel?> GetByProcessKeyAsync(string processKey, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<WorkflowModel?> GetPublishedByProcessKeyAsync(string processKey, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<WorkflowModel?> GetAsync(Guid workflowModelId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<WorkflowModel?> GetMostRecentAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<WorkflowModel> SaveAsync(WorkflowModel model, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<WorkflowModel> PublishAsync(
            WorkflowModel model, WorkflowDeploymentInfo deployment, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<WorkflowModelVersion>> ListVersionsAsync(
            Guid workflowModelId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<WorkflowModel?> DeleteAsync(Guid workflowModelId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
