using AutoNate.Web.Services.Workflow;
using Xunit;

namespace AutoNate.Web.Tests;

/// <summary>
/// A message flow drawn between two pools becomes the addressing of the send
/// at its source (#170).
/// </summary>
/// <remarks>
/// <para>
/// The engine never executes a message flow. What executes is the send task or
/// message event the flow leaves, which #112 runs through
/// <c>SendMessageBehavior</c> addressed by <c>autonateTargetProcessKey</c>,
/// <c>autonateMessageName</c> and <c>autonateCorrelationKey</c>. Before this
/// story an author typed those by hand and the flow they drew was decoration;
/// now the flow is what those attributes are derived from -- at prepare, onto
/// the stored diagram, and again at run time from a diagram that never went
/// through prepare.
/// </para>
/// <para>
/// The refusals are the other half. A flow that could never deliver -- inside
/// one pool, between endpoints that are not a send/receive pair, into a pool
/// that deploys nothing -- is a send into the void, the silent-no-op shape #40
/// exists to end, and it is refused at publish naming the flow and the pools.
/// </para>
/// </remarks>
public sealed class MessageFlowTests
{
    /// <summary>
    /// The wiring the studio writes on a send task. Without it the send-task
    /// validator refuses the diagram before the message-flow checks are reached,
    /// which is a different story's refusal.
    /// </summary>
    private const string SendWiring =
        " flowable:delegateExpression=\"${autonateBehaviorDelegate}\" flowable:autonateServiceKind=\"behavior\" flowable:behaviorKey=\"autonate.send-message\"";

    private const string Ns = """
        xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
        xmlns:flowable="http://flowable.org/bpmn"
        id="Definitions_1" targetNamespace="http://autonate.dev/workflows"
        """;

    /// <summary>Customer's send task → Supplier's receive task, drawn as a flow.</summary>
    private static string SendTaskToReceiveTask(string? sourceKey = null) => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions {Ns}>
          <bpmn:collaboration id="Collab_1">
            <bpmn:participant id="P_c" name="Customer" processRef="customer" />
            <bpmn:participant id="P_s" name="Supplier" processRef="supplier" />
            <bpmn:messageFlow id="MF_1" name="order" sourceRef="send" targetRef="receive" />
          </bpmn:collaboration>
          <bpmn:process id="customer" name="Customer" isExecutable="true">
            <bpmn:startEvent id="cs" />
            <bpmn:sequenceFlow id="cf1" sourceRef="cs" targetRef="send" />
            <bpmn:sendTask id="send" name="Send order"{SendWiring}{(sourceKey is null ? "" : $" flowable:autonateCorrelationKey=\"{sourceKey}\"")} />
            <bpmn:sequenceFlow id="cf2" sourceRef="send" targetRef="ce" />
            <bpmn:endEvent id="ce" />
          </bpmn:process>
          <bpmn:process id="supplier" name="Supplier" isExecutable="true">
            <bpmn:startEvent id="ss" />
            <bpmn:sequenceFlow id="sf1" sourceRef="ss" targetRef="receive" />
            <bpmn:receiveTask id="receive" name="Await order" flowable:autonateCorrelationKey="orderId" />
            <bpmn:sequenceFlow id="sf2" sourceRef="receive" targetRef="se" />
            <bpmn:endEvent id="se" />
          </bpmn:process>
        </bpmn:definitions>
        """;

    /// <summary>Customer's message end event → Supplier's message start event.</summary>
    private const string EndEventToStartEvent = $$"""
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions {{Ns}}>
          <bpmn:message id="Msg_order" name="orderPlaced" />
          <bpmn:collaboration id="Collab_1">
            <bpmn:participant id="P_c" name="Customer" processRef="customer" />
            <bpmn:participant id="P_s" name="Supplier" processRef="supplier" />
            <bpmn:messageFlow id="MF_1" sourceRef="ce" targetRef="ss" />
          </bpmn:collaboration>
          <bpmn:process id="customer" name="Customer" isExecutable="true">
            <bpmn:startEvent id="cs" />
            <bpmn:sequenceFlow id="cf1" sourceRef="cs" targetRef="ce" />
            <bpmn:endEvent id="ce"><bpmn:messageEventDefinition id="med_ce" /></bpmn:endEvent>
          </bpmn:process>
          <bpmn:process id="supplier" name="Supplier" isExecutable="true">
            <bpmn:startEvent id="ss"><bpmn:messageEventDefinition messageRef="Msg_order" /></bpmn:startEvent>
            <bpmn:sequenceFlow id="sf1" sourceRef="ss" targetRef="se" />
            <bpmn:endEvent id="se" />
          </bpmn:process>
        </bpmn:definitions>
        """;

    // ── prepare stamps the flow onto its source ──────────────────────────────

    // ── #648: the flow fills a blank and never overwrites the author ─────────

    /// <summary>
    /// The rule run time already had, now at prepare too. Every studio save
    /// passes through prepare, so before #648 an author's explicit target was
    /// rewritten on every save while run time -- reading the same attributes --
    /// let it win.
    /// </summary>
    [Fact]
    public void Preparing_keeps_the_authors_explicit_target_and_message_name()
    {
        var xml = SendTaskToReceiveTask().Replace(
            "<bpmn:sendTask id=\"send\" name=\"Send order\"",
            "<bpmn:sendTask id=\"send\" name=\"Send order\" flowable:autonateTargetProcessKey=\"elsewhere\" flowable:autonateMessageName=\"mine\"",
            StringComparison.Ordinal);

        var prepared = WorkflowBpmnXml.ApplyProcessMetadata(xml, "customer", "Customer");
        var send = prepared[prepared.IndexOf("<bpmn:sendTask", StringComparison.Ordinal)..];
        send = send[..send.IndexOf("/>", StringComparison.Ordinal)];

        Assert.Contains("autonateTargetProcessKey=\"elsewhere\"", send, StringComparison.Ordinal);
        Assert.Contains("autonateMessageName=\"mine\"", send, StringComparison.Ordinal);
        // The complement: the flow's answers did NOT land beside the author's.
        Assert.DoesNotContain("autonateTargetProcessKey=\"supplier\"", send, StringComparison.Ordinal);
        Assert.DoesNotContain("autonateMessageName=\"receive\"", send, StringComparison.Ordinal);
    }

    [Fact]
    public void Preparing_keeps_an_end_events_own_message_reference()
    {
        var xml = EndEventToStartEvent
            .Replace("<bpmn:message id=\"Msg_order\" name=\"orderPlaced\" />",
                "<bpmn:message id=\"Msg_order\" name=\"orderPlaced\" /><bpmn:message id=\"Msg_mine\" name=\"mine\" />", StringComparison.Ordinal)
            .Replace("<bpmn:messageEventDefinition id=\"med_ce\" />",
                "<bpmn:messageEventDefinition id=\"med_ce\" messageRef=\"Msg_mine\" />", StringComparison.Ordinal);

        var prepared = WorkflowBpmnXml.ApplyProcessMetadata(xml, "customer", "Customer");

        Assert.Contains("<bpmn:messageEventDefinition id=\"med_ce\" messageRef=\"Msg_mine\"", prepared, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"med_ce\" messageRef=\"Msg_order\"", prepared, StringComparison.Ordinal);
    }

    // ── #649: fan-out is refused, not silently truncated ─────────────────────

    private static string TwoFlowsFromOneSend() => SendTaskToReceiveTask()
        .Replace("<bpmn:messageFlow id=\"MF_1\" name=\"order\" sourceRef=\"send\" targetRef=\"receive\" />",
            "<bpmn:messageFlow id=\"MF_1\" name=\"order\" sourceRef=\"send\" targetRef=\"receive\" />"
            + "<bpmn:messageFlow id=\"MF_2\" name=\"copy\" sourceRef=\"send\" targetRef=\"receive2\" />", StringComparison.Ordinal)
        .Replace("<bpmn:receiveTask id=\"receive\" name=\"Await order\" flowable:autonateCorrelationKey=\"orderId\" />",
            "<bpmn:receiveTask id=\"receive\" name=\"Await order\" flowable:autonateCorrelationKey=\"orderId\" />"
            + "<bpmn:receiveTask id=\"receive2\" name=\"Await copy\" />", StringComparison.Ordinal);

    [Fact]
    public void A_send_with_two_message_flows_is_refused_naming_it_and_both_flows()
    {
        var xml = TwoFlowsFromOneSend();
        Assert.Contains("MF_2", xml, StringComparison.Ordinal);

        var errors = WorkflowBpmnXml.ValidateProcess(xml).Errors;

        Assert.Contains(errors, e => e.Contains("Send order", StringComparison.Ordinal)
            && e.Contains("2 message flows", StringComparison.Ordinal)
            && e.Contains("'order'", StringComparison.Ordinal)
            && e.Contains("'copy'", StringComparison.Ordinal));
    }

    /// <summary>The complement: two flows from two different senders are fine.</summary>
    [Fact]
    public void Two_flows_from_two_senders_are_not_refused()
    {
        var xml = TwoFlowsFromOneSend()
            .Replace("sourceRef=\"send\" targetRef=\"receive2\"", "sourceRef=\"send2\" targetRef=\"receive2\"", StringComparison.Ordinal)
            .Replace("<bpmn:sequenceFlow id=\"cf2\" sourceRef=\"send\" targetRef=\"ce\" />",
                "<bpmn:sequenceFlow id=\"cf2\" sourceRef=\"send\" targetRef=\"send2\" />"
                + "<bpmn:sendTask id=\"send2\" name=\"Send copy\"" + SendWiring + " />"
                + "<bpmn:sequenceFlow id=\"cf3\" sourceRef=\"send2\" targetRef=\"ce\" />", StringComparison.Ordinal);
        Assert.Contains("send2", xml, StringComparison.Ordinal);

        var errors = WorkflowBpmnXml.ValidateProcess(xml).Errors;

        Assert.DoesNotContain(errors, e => e.Contains("message flows leaving it", StringComparison.Ordinal));
    }

    // ── #654: a flow drawn to the pool itself ────────────────────────────────

    [Fact]
    public void A_flow_drawn_to_a_pool_with_one_message_start_resolves_to_that_start()
    {
        var xml = EndEventToStartEvent.Replace("targetRef=\"ss\"", "targetRef=\"P_s\"", StringComparison.Ordinal);

        // Validated AFTER prepare, as the studio's save does: prepare is what
        // gives the end event its message from the flow.
        var prepared = WorkflowBpmnXml.ApplyProcessMetadata(xml, "customer", "Customer");
        Assert.Empty(WorkflowBpmnXml.ValidateProcess(prepared).Errors);
        Assert.Contains("flowable:autonateTargetProcessKey=\"supplier\"", prepared, StringComparison.Ordinal);
        Assert.Contains("<bpmn:messageEventDefinition id=\"med_ce\" messageRef=\"Msg_order\"", prepared, StringComparison.Ordinal);
    }

    [Fact]
    public void A_flow_drawn_to_a_pool_with_two_message_starts_is_refused_naming_the_pool()
    {
        var xml = EndEventToStartEvent
            .Replace("targetRef=\"ss\"", "targetRef=\"P_s\"", StringComparison.Ordinal)
            .Replace("<bpmn:startEvent id=\"ss\"><bpmn:messageEventDefinition messageRef=\"Msg_order\" /></bpmn:startEvent>",
                "<bpmn:startEvent id=\"ss\"><bpmn:messageEventDefinition messageRef=\"Msg_order\" /></bpmn:startEvent>"
                + "<bpmn:startEvent id=\"ss2\"><bpmn:messageEventDefinition messageRef=\"Msg_order\" /></bpmn:startEvent>"
                + "<bpmn:sequenceFlow id=\"sf2\" sourceRef=\"ss2\" targetRef=\"se\" />", StringComparison.Ordinal);
        Assert.Contains("ss2", xml, StringComparison.Ordinal);

        var errors = WorkflowBpmnXml.ValidateProcess(xml).Errors;

        Assert.Contains(errors, e => e.Contains("Supplier", StringComparison.Ordinal)
            && e.Contains("no single message start event", StringComparison.Ordinal));
    }

    [Fact]
    public void Preparing_stamps_the_targets_pool_and_message_onto_the_send_task()
    {
        var prepared = WorkflowBpmnXml.ApplyProcessMetadata(SendTaskToReceiveTask(), "customer", "Customer");

        Assert.Contains("flowable:autonateTargetProcessKey=\"supplier\"", prepared, StringComparison.Ordinal);
        // A receive task has no message of its own; it answers to its id (#112).
        Assert.Contains("flowable:autonateMessageName=\"receive\"", prepared, StringComparison.Ordinal);
    }

    /// <summary>
    /// Claude's Discretion on #170, made explicit: the correlation key is
    /// inferred from the target's declaration when the author set none.
    /// </summary>
    [Fact]
    public void Preparing_infers_the_correlation_key_from_the_target_when_the_author_set_none()
    {
        var prepared = WorkflowBpmnXml.ApplyProcessMetadata(SendTaskToReceiveTask(), "customer", "Customer");

        Assert.Contains("<bpmn:sendTask id=\"send\" name=\"Send order\"", prepared, StringComparison.Ordinal);
        Assert.Contains("autonateCorrelationKey=\"orderId\"", prepared, StringComparison.Ordinal);
    }

    /// <summary>The complement: an author's own key is never overwritten.</summary>
    [Fact]
    public void Preparing_keeps_the_authors_own_correlation_key()
    {
        var prepared = WorkflowBpmnXml.ApplyProcessMetadata(SendTaskToReceiveTask("mine"), "customer", "Customer");

        var send = prepared[prepared.IndexOf("<bpmn:sendTask", StringComparison.Ordinal)..];
        send = send[..send.IndexOf("/>", StringComparison.Ordinal)];
        Assert.Contains("autonateCorrelationKey=\"mine\"", send, StringComparison.Ordinal);
        Assert.DoesNotContain("autonateCorrelationKey=\"orderId\"", send, StringComparison.Ordinal);
    }

    [Fact]
    public void Preparing_points_a_message_end_event_at_the_targets_message()
    {
        var prepared = WorkflowBpmnXml.ApplyProcessMetadata(EndEventToStartEvent, "customer", "Customer");

        Assert.Contains("<bpmn:messageEventDefinition id=\"med_ce\" messageRef=\"Msg_order\"", prepared, StringComparison.Ordinal);
        Assert.Contains("flowable:autonateTargetProcessKey=\"supplier\"", prepared, StringComparison.Ordinal);
    }

    /// <summary>
    /// A target in the PRIMARY pool resolves to the workflow key, not to the id
    /// the process had before prepare renamed it.
    /// </summary>
    [Fact]
    public void A_flow_into_the_primary_pool_resolves_to_the_workflow_key()
    {
        // Reverse the flow: Supplier's send addresses Customer's receive.
        var xml = SendTaskToReceiveTask()
            .Replace("sourceRef=\"send\" targetRef=\"receive\"", "sourceRef=\"ssend\" targetRef=\"creceive\"", StringComparison.Ordinal)
            .Replace("<bpmn:sendTask id=\"send\" name=\"Send order\"" + SendWiring + " />", "<bpmn:receiveTask id=\"creceive\" name=\"Await reply\" />", StringComparison.Ordinal)
            .Replace("<bpmn:receiveTask id=\"receive\" name=\"Await order\" flowable:autonateCorrelationKey=\"orderId\" />", "<bpmn:sendTask id=\"ssend\" name=\"Reply\"" + SendWiring + " />", StringComparison.Ordinal)
            .Replace("targetRef=\"send\"", "targetRef=\"creceive\"", StringComparison.Ordinal)
            .Replace("sourceRef=\"send\"", "sourceRef=\"creceive\"", StringComparison.Ordinal)
            .Replace("targetRef=\"receive\"", "targetRef=\"ssend\"", StringComparison.Ordinal)
            .Replace("sourceRef=\"receive\"", "sourceRef=\"ssend\"", StringComparison.Ordinal);

        var prepared = WorkflowBpmnXml.ApplyProcessMetadata(xml, "orders", "Orders");

        // Customer became `orders`; Supplier's send must say `orders`, not `customer`.
        Assert.Contains("<bpmn:process id=\"orders\"", prepared, StringComparison.Ordinal);
        Assert.Contains("flowable:autonateTargetProcessKey=\"orders\"", prepared, StringComparison.Ordinal);
        Assert.DoesNotContain("autonateTargetProcessKey=\"customer\"", prepared, StringComparison.Ordinal);
    }

    // ── run time reads the flow too ──────────────────────────────────────────

    /// <summary>
    /// A diagram that never went through prepare -- published as raw XML, as
    /// the E2E helpers and the oracle do -- still resolves its send from the
    /// flow, so the behaviour finds a target rather than failing "noTargetProcess".
    /// </summary>
    [Fact]
    public void The_send_declaration_resolves_its_target_from_the_flow_without_prepare()
    {
        var sends = WorkflowBpmnXml.ExtractMessageSendDeclarations(SendTaskToReceiveTask());

        var send = Assert.Single(sends, s => s.ElementId == "send");
        Assert.Equal("supplier", send.TargetProcessKey);
        Assert.Equal("receive", send.MessageName);
        Assert.Equal("orderId", send.CorrelationKey);
    }

    /// <summary>An explicit attribute wins over the drawn flow.</summary>
    [Fact]
    public void An_explicit_target_attribute_beats_the_flow()
    {
        var xml = SendTaskToReceiveTask().Replace(
            "<bpmn:sendTask id=\"send\" name=\"Send order\"" + SendWiring + " />",
            "<bpmn:sendTask id=\"send\" name=\"Send order\"" + SendWiring + " flowable:autonateTargetProcessKey=\"elsewhere\" />",
            StringComparison.Ordinal);

        var send = Assert.Single(WorkflowBpmnXml.ExtractMessageSendDeclarations(xml), s => s.ElementId == "send");
        Assert.Equal("elsewhere", send.TargetProcessKey);
    }

    // ── declarations are scoped to the addressed process ─────────────────────

    /// <summary>
    /// The bug the scoped overload prevents: a receive task in the SENDER's pool
    /// with the same name is not a match for a message addressed to the other.
    /// </summary>
    [Fact]
    public void Declarations_scoped_to_a_process_exclude_the_other_pools()
    {
        var xml = SendTaskToReceiveTask().Replace(
            "<bpmn:sendTask id=\"send\" name=\"Send order\"" + SendWiring + " />",
            "<bpmn:sendTask id=\"send\" name=\"Send order\"" + SendWiring + " /><bpmn:receiveTask id=\"decoy\" name=\"Same name\" />",
            StringComparison.Ordinal);

        var all = WorkflowBpmnXml.ExtractMessageDeclarations(xml);
        var supplierOnly = WorkflowBpmnXml.ExtractMessageDeclarations(xml, "supplier");

        Assert.Contains(all, d => d.ElementId == "decoy");
        Assert.Contains(all, d => d.ElementId == "receive");
        Assert.DoesNotContain(supplierOnly, d => d.ElementId == "decoy");
        Assert.Contains(supplierOnly, d => d.ElementId == "receive");
    }

    [Fact]
    public void Unscoped_declarations_are_what_a_single_process_diagram_always_was()
    {
        // #663. A SINGLE-process diagram, which is what the name claims and what
        // every caller before the scoped overload relied on: asking by a key that
        // is not the process id still answers. The previous version compared the
        // overload with its own default on a TWO-process diagram, which is true
        // by definition.
        const string singleProcess = $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <bpmn:definitions {Ns}>
              <bpmn:message id="Msg_1" name="ping" />
              <bpmn:process id="stored-id" name="Stored" isExecutable="true">
                <bpmn:startEvent id="start"><bpmn:messageEventDefinition messageRef="Msg_1" /></bpmn:startEvent>
                <bpmn:sequenceFlow id="f1" sourceRef="start" targetRef="end" />
                <bpmn:endEvent id="end" />
              </bpmn:process>
            </bpmn:definitions>
            """;

        var unscoped = WorkflowBpmnXml.ExtractMessageDeclarations(singleProcess, processId: null);
        // The key the store was addressed by has drifted from the process id (#558).
        var byAnotherKey = WorkflowBpmnXml.ExtractMessageDeclarations(singleProcess, processId: "the-workflow-key");

        Assert.Equal(new[] { "start" }, unscoped.Select(d => d.ElementId).ToArray());
        Assert.Equal(new[] { "start" }, byAnotherKey.Select(d => d.ElementId).ToArray());
    }

    /// <summary>
    /// #663. The widening the scope rule accepts, stated rather than left to be
    /// discovered: on a MULTI-pool diagram an id that names no pool falls back to
    /// every pool's declarations.
    /// </summary>
    [Fact]
    public void A_multi_pool_diagram_addressed_by_an_unknown_key_answers_for_every_pool()
    {
        var xml = SendTaskToReceiveTask();

        var unknown = WorkflowBpmnXml.ExtractMessageDeclarations(xml, processId: "no-such-process");
        var everything = WorkflowBpmnXml.ExtractMessageDeclarations(xml, processId: null);

        Assert.NotEmpty(unknown);
        Assert.Equal(
            everything.Select(d => d.ElementId).Order(StringComparer.Ordinal),
            unknown.Select(d => d.ElementId).Order(StringComparer.Ordinal));
        // The complement: a key that DOES name a pool scopes to it. Every
        // declaration in this diagram is the Supplier's receive task, so the
        // narrowing shows on the Customer side -- which is the point, since a
        // scope that never excludes anything is not a scope.
        Assert.Equal(
            everything.Select(d => d.ElementId).Order(StringComparer.Ordinal),
            WorkflowBpmnXml.ExtractMessageDeclarations(xml, processId: "supplier")
                .Select(d => d.ElementId).Order(StringComparer.Ordinal));
        Assert.Empty(WorkflowBpmnXml.ExtractMessageDeclarations(xml, processId: "customer"));
    }

    // ── refusals ─────────────────────────────────────────────────────────────

    [Fact]
    public void A_valid_cross_pool_flow_is_not_refused()
    {
        Assert.Empty(WorkflowBpmnXml.ValidateProcess(SendTaskToReceiveTask()).Errors);
    }

    [Fact]
    public void A_flow_inside_one_pool_is_refused_naming_it()
    {
        // Point the flow at a receive task in the Customer pool itself.
        var xml = SendTaskToReceiveTask()
            .Replace("<bpmn:sendTask id=\"send\" name=\"Send order\"" + SendWiring + " />",
                "<bpmn:sendTask id=\"send\" name=\"Send order\"" + SendWiring + " /><bpmn:receiveTask id=\"local\" name=\"Local\" />",
                StringComparison.Ordinal)
            .Replace("targetRef=\"receive\" />", "targetRef=\"local\" />", StringComparison.Ordinal);

        var error = Assert.Single(WorkflowBpmnXml.ValidateProcess(xml).Errors, e => e.Contains("same pool", StringComparison.Ordinal));
        Assert.Contains("'order'", error, StringComparison.Ordinal);
    }

    [Fact]
    public void A_flow_from_an_element_that_does_not_send_is_refused()
    {
        var xml = SendTaskToReceiveTask().Replace("sourceRef=\"send\" targetRef=\"receive\"", "sourceRef=\"cs\" targetRef=\"receive\"", StringComparison.Ordinal);

        Assert.Contains(
            WorkflowBpmnXml.ValidateProcess(xml).Errors,
            e => e.Contains("does not send a message", StringComparison.Ordinal));
    }

    [Fact]
    public void A_flow_into_an_element_that_does_not_receive_is_refused()
    {
        var xml = SendTaskToReceiveTask().Replace("sourceRef=\"send\" targetRef=\"receive\"", "sourceRef=\"send\" targetRef=\"se\"", StringComparison.Ordinal);

        Assert.Contains(
            WorkflowBpmnXml.ValidateProcess(xml).Errors,
            e => e.Contains("does not receive a message", StringComparison.Ordinal));
    }

    /// <summary>
    /// A pool drawn only to show a counterparty deploys as nothing (#169); a
    /// message sent into it would never arrive. Refused by name.
    /// </summary>
    [Fact]
    public void A_flow_into_a_pool_that_deploys_nothing_is_refused_naming_the_pool()
    {
        var xml = $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <bpmn:definitions {Ns}>
              <bpmn:message id="Msg_1" name="ping" />
              <bpmn:collaboration id="Collab_1">
                <bpmn:participant id="P_c" name="Customer" processRef="customer" />
                <bpmn:participant id="P_x" name="External Bank" processRef="bank" />
                <bpmn:messageFlow id="MF_1" name="ping" sourceRef="send" targetRef="bankstart" />
              </bpmn:collaboration>
              <bpmn:process id="customer" name="Customer" isExecutable="true">
                <bpmn:startEvent id="cs" />
                <bpmn:sequenceFlow id="cf1" sourceRef="cs" targetRef="send" />
                <bpmn:sendTask id="send" name="Ping"{SendWiring} />
                <bpmn:sequenceFlow id="cf2" sourceRef="send" targetRef="ce" />
                <bpmn:endEvent id="ce" />
              </bpmn:process>
              <bpmn:process id="bank" name="External Bank" isExecutable="true">
                <bpmn:startEvent id="bankstart"><bpmn:messageEventDefinition messageRef="Msg_1" /></bpmn:startEvent>
              </bpmn:process>
            </bpmn:definitions>
            """;

        // Drawn to the POOL itself -- which BPMN allows and the studio draws for a
        // collapsed pool -- with nothing in the pool. That is the only way a flow
        // reaches a pool that deploys nothing: any element it could end AT is a
        // flow node, and a pool holding one is deployable.
        var empty = xml
            .Replace("<bpmn:startEvent id=\"bankstart\"><bpmn:messageEventDefinition messageRef=\"Msg_1\" /></bpmn:startEvent>", "", StringComparison.Ordinal)
            .Replace("targetRef=\"bankstart\"", "targetRef=\"P_x\"", StringComparison.Ordinal);

        var errors = WorkflowBpmnXml.ValidateProcess(empty).Errors;
        Assert.Contains(errors, e => e.Contains("External Bank", StringComparison.Ordinal) && e.Contains("deploys as nothing", StringComparison.Ordinal));
    }

    [Fact]
    public void A_flow_between_ids_that_do_not_exist_is_refused()
    {
        var xml = SendTaskToReceiveTask().Replace("targetRef=\"receive\" />", "targetRef=\"ghost\" />", StringComparison.Ordinal);

        Assert.Contains(
            WorkflowBpmnXml.ValidateProcess(xml).Errors,
            e => e.Contains("'ghost'", StringComparison.Ordinal) && e.Contains("not in the diagram", StringComparison.Ordinal));
    }

    [Fact]
    public void A_diagram_with_no_message_flows_is_unaffected()
    {
        var xml = SendTaskToReceiveTask().Replace("<bpmn:messageFlow id=\"MF_1\" name=\"order\" sourceRef=\"send\" targetRef=\"receive\" />", "", StringComparison.Ordinal);

        Assert.Empty(WorkflowBpmnXml.ValidateProcess(xml).Errors);
        Assert.Null(Assert.Single(WorkflowBpmnXml.ExtractMessageSendDeclarations(xml), s => s.ElementId == "send").TargetProcessKey);
    }
}
