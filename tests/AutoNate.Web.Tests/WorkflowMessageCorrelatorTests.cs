using AutoNate.Web.Models;
using AutoNate.Web.Services.Workflow;
using Xunit;

namespace AutoNate.Web.Tests;

/// <summary>
/// Addressing a running process from outside it (#112).
/// </summary>
/// <remarks>
/// The outcomes that REFUSE carry as much weight here as the one that delivers.
/// A no-match that returned success is indistinguishable from the feature being
/// broken, and a multi-match that picked one instance hides a badly modelled
/// process until it causes something worse — so both are asserted on the
/// observable consequence (nothing was advanced), not merely on the status.
/// </remarks>
public sealed class WorkflowMessageCorrelatorTests
{
    private const string ProcessKey = "orders";

    // start -> catch(paymentCleared, correlate on orderId) -> user task
    private static string CatchDiagram(string correlationKey = "orderId") => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          xmlns:flowable="http://flowable.org/bpmn"
                          targetNamespace="http://autonate.dev/workflows">
          <bpmn:message id="Msg_1" name="paymentCleared" />
          <bpmn:process id="{ProcessKey}" isExecutable="true">
            <bpmn:startEvent id="s" />
            <bpmn:intermediateCatchEvent id="wait" flowable:autonateCorrelationKey="{correlationKey}">
              <bpmn:messageEventDefinition messageRef="Msg_1" />
            </bpmn:intermediateCatchEvent>
          </bpmn:process>
        </bpmn:definitions>
        """;

    [Fact]
    public async Task A_single_matching_instance_is_advanced()
    {
        var flowable = new StubFlowableClient();
        flowable.WaitingExecutionsByMessage["paymentCleared"] = new[] { "exec-1" };
        var correlator = NewCorrelator(CatchDiagram(), flowable);

        var result = await correlator.CorrelateAsync(ProcessKey, "paymentCleared", "ORD-1", null);

        Assert.Equal(WorkflowMessageCorrelator.Outcome.Delivered, result.Outcome);
        Assert.Equal("exec-1", result.ProcessInstanceId);
        Assert.Contains("DeliverMessageToExecution:exec-1:paymentCleared", flowable.Calls);

        // The correlation key from the diagram reached the engine query. Without
        // this the narrowing could be silently absent and every test above would
        // still pass, because the stub returns the same list either way.
        Assert.Contains(flowable.Calls, c => c.Contains("orderId=ORD-1", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_value_matching_nothing_is_reported_and_advances_nothing()
    {
        var flowable = new StubFlowableClient();
        var correlator = NewCorrelator(CatchDiagram(), flowable);

        var result = await correlator.CorrelateAsync(ProcessKey, "paymentCleared", "ORD-NOPE", null);

        Assert.Equal(WorkflowMessageCorrelator.Outcome.NoMatch, result.Outcome);
        Assert.DoesNotContain(flowable.Calls, c => c.StartsWith("DeliverMessageToExecution", StringComparison.Ordinal));
        Assert.DoesNotContain(flowable.Calls, c => c.StartsWith("StartProcessInstanceByMessage", StringComparison.Ordinal));
    }

    [Fact]
    public async Task More_than_one_match_is_refused_with_the_count_and_advances_nothing()
    {
        var flowable = new StubFlowableClient();
        flowable.WaitingExecutionsByMessage["paymentCleared"] = new[] { "exec-1", "exec-2", "exec-3" };
        var correlator = NewCorrelator(CatchDiagram(), flowable);

        var result = await correlator.CorrelateAsync(ProcessKey, "paymentCleared", "ORD-1", null);

        Assert.Equal(WorkflowMessageCorrelator.Outcome.MultipleMatches, result.Outcome);
        Assert.Equal(3, result.MatchCount);

        // The half that matters: no instance was advanced. Reporting the refusal
        // while still delivering to one of them would be the worst of both.
        Assert.DoesNotContain(flowable.Calls, c => c.StartsWith("DeliverMessageToExecution", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_message_start_event_starts_an_instance_when_nothing_is_waiting()
    {
        const string xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                              targetNamespace="http://autonate.dev/workflows">
              <bpmn:message id="Msg_1" name="orderPlaced" />
              <bpmn:process id="orders" isExecutable="true">
                <bpmn:startEvent id="s">
                  <bpmn:messageEventDefinition messageRef="Msg_1" />
                </bpmn:startEvent>
              </bpmn:process>
            </bpmn:definitions>
            """;
        var flowable = new StubFlowableClient { StartedByMessageInstanceId = "new-instance" };
        var correlator = NewCorrelator(xml, flowable);

        var result = await correlator.CorrelateAsync(ProcessKey, "orderPlaced", null, null);

        Assert.Equal(WorkflowMessageCorrelator.Outcome.Started, result.Outcome);
        Assert.Equal("new-instance", result.ProcessInstanceId);
    }

    [Fact]
    public async Task A_waiting_instance_wins_over_starting_a_new_one()
    {
        // Both a message start and a catch on the SAME message. The start event's
        // job is to start "when its message arrives with nothing waiting", so
        // something waiting must take precedence — otherwise every delivery to a
        // waiting instance would silently spawn a duplicate process instead.
        const string xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                              xmlns:flowable="http://flowable.org/bpmn"
                              targetNamespace="http://autonate.dev/workflows">
              <bpmn:message id="Msg_1" name="orderPlaced" />
              <bpmn:process id="orders" isExecutable="true">
                <bpmn:startEvent id="s">
                  <bpmn:messageEventDefinition messageRef="Msg_1" />
                </bpmn:startEvent>
                <bpmn:intermediateCatchEvent id="wait" flowable:autonateCorrelationKey="orderId">
                  <bpmn:messageEventDefinition messageRef="Msg_1" />
                </bpmn:intermediateCatchEvent>
              </bpmn:process>
            </bpmn:definitions>
            """;
        var flowable = new StubFlowableClient();
        flowable.WaitingExecutionsByMessage["orderPlaced"] = new[] { "exec-1" };
        var correlator = NewCorrelator(xml, flowable);

        var result = await correlator.CorrelateAsync(ProcessKey, "orderPlaced", "ORD-1", null);

        Assert.Equal(WorkflowMessageCorrelator.Outcome.Delivered, result.Outcome);
        Assert.DoesNotContain(flowable.Calls, c => c.StartsWith("StartProcessInstanceByMessage", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_message_start_event_inside_an_event_subprocess_delivers_rather_than_starting()
    {
        // #162 found this as a defect in #112 as shipped. A start event inside an
        // EVENT SUBPROCESS starts that handler within an already-running
        // instance — it is a catch, not a way to start a process. Classifying it
        // as a process start made the correlator call StartProcessInstanceByMessage,
        // which Flowable refuses:
        //
        //   "Cannot start process instance by message: no subscription to
        //    message with name '…' found."
        //
        // …because no PROCESS-level start event carries that message. The symptom
        // was a 500 on a send that should simply have been delivered.
        const string xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                              xmlns:flowable="http://flowable.org/bpmn"
                              targetNamespace="http://autonate.dev/workflows">
              <bpmn:message id="Msg_1" name="nudge" />
              <bpmn:process id="orders" isExecutable="true">
                <bpmn:startEvent id="s" />
                <bpmn:subProcess id="handler" triggeredByEvent="true">
                  <bpmn:startEvent id="hs" flowable:autonateCorrelationKey="orderId">
                    <bpmn:messageEventDefinition messageRef="Msg_1" />
                  </bpmn:startEvent>
                </bpmn:subProcess>
              </bpmn:process>
            </bpmn:definitions>
            """;
        var flowable = new StubFlowableClient();
        flowable.WaitingExecutionsByMessage["nudge"] = new[] { "exec-7" };
        var correlator = NewCorrelator(xml, flowable);

        var result = await correlator.CorrelateAsync(ProcessKey, "nudge", "ORD-1", null);

        Assert.Equal(WorkflowMessageCorrelator.Outcome.Delivered, result.Outcome);
        Assert.Contains("DeliverMessageToExecution:exec-7:nudge", flowable.Calls);

        // The half that pins the fix: it must NOT try to start a new instance.
        Assert.DoesNotContain(flowable.Calls,
            c => c.StartsWith("StartProcessInstanceByMessage", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_receive_task_is_triggered_rather_than_sent_a_message()
    {
        // Verified against Flowable 8.0.0: a receive task carries no message
        // subscription, so it is found by activity id and advanced with `trigger`.
        // Asserting the verb, not just that it advanced — `messageEventReceived`
        // on a receive task does nothing at all.
        const string xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                              xmlns:flowable="http://flowable.org/bpmn"
                              targetNamespace="http://autonate.dev/workflows">
              <bpmn:process id="orders" isExecutable="true">
                <bpmn:receiveTask id="awaitShipment" flowable:autonateCorrelationKey="orderId" />
              </bpmn:process>
            </bpmn:definitions>
            """;
        var flowable = new StubFlowableClient();
        flowable.WaitingExecutionsByMessage["awaitShipment"] = new[] { "exec-9" };
        var correlator = NewCorrelator(xml, flowable);

        var result = await correlator.CorrelateAsync(ProcessKey, "awaitShipment", "ORD-1", null);

        Assert.Equal(WorkflowMessageCorrelator.Outcome.Delivered, result.Outcome);
        Assert.Contains("TriggerExecution:exec-9", flowable.Calls);
        Assert.DoesNotContain(flowable.Calls, c => c.StartsWith("DeliverMessageToExecution", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_workflow_with_several_ways_in_refuses_an_unnamed_message()
    {
        const string xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                              xmlns:flowable="http://flowable.org/bpmn"
                              targetNamespace="http://autonate.dev/workflows">
              <bpmn:message id="Msg_1" name="paymentCleared" />
              <bpmn:message id="Msg_2" name="orderCancelled" />
              <bpmn:process id="orders" isExecutable="true">
                <bpmn:intermediateCatchEvent id="a" flowable:autonateCorrelationKey="orderId">
                  <bpmn:messageEventDefinition messageRef="Msg_1" />
                </bpmn:intermediateCatchEvent>
                <bpmn:intermediateCatchEvent id="b" flowable:autonateCorrelationKey="orderId">
                  <bpmn:messageEventDefinition messageRef="Msg_2" />
                </bpmn:intermediateCatchEvent>
              </bpmn:process>
            </bpmn:definitions>
            """;
        var flowable = new StubFlowableClient();
        var correlator = NewCorrelator(xml, flowable);

        var result = await correlator.CorrelateAsync(ProcessKey, messageName: null, "ORD-1", null);

        Assert.Equal(WorkflowMessageCorrelator.Outcome.AmbiguousMessage, result.Outcome);
        Assert.Equal(
            new[] { "orderCancelled", "paymentCleared" },
            result.AvailableMessages!.OrderBy(n => n, StringComparer.Ordinal));
        Assert.Empty(flowable.Calls);
    }

    [Fact]
    public async Task One_way_in_needs_no_naming()
    {
        var flowable = new StubFlowableClient();
        flowable.WaitingExecutionsByMessage["paymentCleared"] = new[] { "exec-1" };
        var correlator = NewCorrelator(CatchDiagram(), flowable);

        var result = await correlator.CorrelateAsync(ProcessKey, messageName: null, "ORD-1", null);

        Assert.Equal(WorkflowMessageCorrelator.Outcome.Delivered, result.Outcome);
    }

    [Fact]
    public async Task An_unpublished_process_key_is_reported_as_such()
    {
        var flowable = new StubFlowableClient();
        var correlator = new WorkflowMessageCorrelator(new StubModelStore(null), flowable);

        var result = await correlator.CorrelateAsync("nothing-here", "paymentCleared", "ORD-1", null);

        Assert.Equal(WorkflowMessageCorrelator.Outcome.UnknownProcess, result.Outcome);
        Assert.Empty(flowable.Calls);
    }

    [Fact]
    public async Task A_message_the_workflow_does_not_declare_is_reported_with_what_it_does()
    {
        var flowable = new StubFlowableClient();
        var correlator = NewCorrelator(CatchDiagram(), flowable);

        var result = await correlator.CorrelateAsync(ProcessKey, "somethingElse", "ORD-1", null);

        Assert.Equal(WorkflowMessageCorrelator.Outcome.UnknownMessage, result.Outcome);
        Assert.Equal(new[] { "paymentCleared" }, result.AvailableMessages);
        Assert.Empty(flowable.Calls);
    }

    private static WorkflowMessageCorrelator NewCorrelator(string xml, StubFlowableClient flowable) =>
        new(new StubModelStore(new WorkflowModel
        {
            Id = Guid.NewGuid(),
            Name = "Orders",
            ProcessKey = ProcessKey,
            BpmnXml = xml
        }), flowable);

    private sealed class StubModelStore(WorkflowModel? model) : IWorkflowModelStore
    {
        public Task<WorkflowModel?> GetByProcessKeyAsync(string processKey, CancellationToken cancellationToken = default) =>
            Task.FromResult(model);

        public Task<IReadOnlyList<WorkflowModel>> ListAsync(CancellationToken cancellationToken = default) =>
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
