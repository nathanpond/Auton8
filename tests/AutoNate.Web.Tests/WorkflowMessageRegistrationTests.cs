using AutoNate.Web.Services.Workflow;
using Xunit;

namespace AutoNate.Web.Tests;

/// <summary>
/// Which message START events a published definition offers the bus (#524).
/// </summary>
public sealed class WorkflowMessageRegistrationTests
{
    [Fact]
    public void A_message_start_event_registers_its_name_topic_and_process_key()
    {
        var registrations = WorkflowBpmnXml.ExtractMessageRegistrations(Diagram(
            """<bpmn:startEvent id="s"><bpmn:messageEventDefinition messageRef="Msg_1"/></bpmn:startEvent>"""));

        var one = Assert.Single(registrations);
        Assert.Equal("orderPlaced", one.MessageName);
        Assert.Equal(WorkflowBpmnXml.DefaultMessageTopic, one.Topic);
        Assert.Equal("p", one.ProcessDefinitionKey);
    }

    /// <summary>The author's topic wins, the same way a signal's does.</summary>
    [Fact]
    public void An_authored_topic_overrides_the_default()
    {
        var registrations = WorkflowBpmnXml.ExtractMessageRegistrations(Diagram(
            """<bpmn:startEvent id="s"><bpmn:messageEventDefinition messageRef="Msg_1"/></bpmn:startEvent>""",
            topic: "orders.inbound"));

        Assert.Equal("orders.inbound", Assert.Single(registrations).Topic);
    }

    /// <summary>
    /// #162's lesson, borrowed rather than rediscovered (#524).
    /// </summary>
    /// <remarks>
    /// A start event inside an EVENT SUB-PROCESS starts that handler within an
    /// already-running instance. Registering it as a way to start a PROCESS makes
    /// the correlator ask Flowable to start one by a message no process-level
    /// start event carries, and the engine refuses — which is exactly the defect
    /// #112 shipped and #162 found.
    /// </remarks>
    [Fact]
    public void A_start_event_inside_an_event_sub_process_is_not_a_way_in()
    {
        var registrations = WorkflowBpmnXml.ExtractMessageRegistrations(Diagram(
            """<bpmn:startEvent id="s"/>"""
            + """<bpmn:subProcess id="esp" triggeredByEvent="true">"""
            + """<bpmn:startEvent id="handler"><bpmn:messageEventDefinition messageRef="Msg_1"/></bpmn:startEvent>"""
            + """</bpmn:subProcess>"""));

        Assert.Empty(registrations);
    }

    /// <summary>
    /// A catch is not a way in either (#524).
    /// </summary>
    /// <remarks>
    /// The complement that keeps this from being "every message event": an
    /// intermediate catch or a boundary event belongs to a RUNNING instance and
    /// is reached by correlation, not by starting something. Registering one
    /// would have the bus start a new instance every time a waiting one was
    /// supposed to advance.
    /// </remarks>
    [Fact]
    public void An_intermediate_catch_is_not_a_way_in()
    {
        var registrations = WorkflowBpmnXml.ExtractMessageRegistrations(Diagram(
            """<bpmn:startEvent id="s"/>"""
            + """<bpmn:intermediateCatchEvent id="c"><bpmn:messageEventDefinition messageRef="Msg_1"/></bpmn:intermediateCatchEvent>"""));

        Assert.Empty(registrations);
    }

    /// <summary>
    /// A reference that resolves to nothing is not addressable (#524).
    /// </summary>
    /// <remarks>
    /// The engine subscribes under the message's NAME, so a messageRef pointing
    /// at no declaration names nothing. Skipped rather than guessed at — the same
    /// rule <c>ExtractMessageDeclarations</c> applies.
    /// </remarks>
    [Fact]
    public void An_unresolvable_message_reference_registers_nothing()
    {
        var registrations = WorkflowBpmnXml.ExtractMessageRegistrations(Diagram(
            """<bpmn:startEvent id="s"><bpmn:messageEventDefinition messageRef="Nope"/></bpmn:startEvent>"""));

        Assert.Empty(registrations);
    }

    /// <summary>A signal start event is not a message registration (#524).</summary>
    /// <remarks>
    /// Half of the cross-talk boundary, asserted at the source rather than only
    /// at the dispatcher: if a signal start could produce a message registration,
    /// separate registries would not separate anything.
    /// </remarks>
    [Fact]
    public void A_signal_start_event_produces_no_message_registration()
    {
        var xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                              xmlns:flowable="http://flowable.org/bpmn"
                              targetNamespace="http://autonate.dev/workflows">
              <bpmn:signal id="Sig_1" name="orderPlaced" />
              <bpmn:process id="p" isExecutable="true">
                <bpmn:startEvent id="s"><bpmn:signalEventDefinition signalRef="Sig_1"/></bpmn:startEvent>
              </bpmn:process>
            </bpmn:definitions>
            """;

        Assert.Empty(WorkflowBpmnXml.ExtractMessageRegistrations(xml));
    }

    /// <summary>And the mirror: a message start is not a signal registration.</summary>
    [Fact]
    public void A_message_start_event_produces_no_signal_registration()
    {
        var xml = Diagram(
            """<bpmn:startEvent id="s"><bpmn:messageEventDefinition messageRef="Msg_1"/></bpmn:startEvent>""");

        Assert.Empty(WorkflowBpmnXml.ExtractSignalRegistrations(xml));
    }

    [Fact]
    public void Unparseable_xml_registers_nothing_rather_than_throwing()
    {
        Assert.Empty(WorkflowBpmnXml.ExtractMessageRegistrations("<definitions"));
        Assert.Empty(WorkflowBpmnXml.ExtractMessageRegistrations(""));
    }

    /// <summary>
    /// The E2E copy of the default topic still matches the product's (#524).
    /// </summary>
    /// <remarks>
    /// <c>WorkflowMessageQueueExecutionTests</c> publishes to a literal, because
    /// the E2E project does not reference AutoNate.Web. A second copy of a
    /// constant drifting is how a test starts publishing into a topic nothing
    /// listens on and reports a timeout that looks like a product bug — so the
    /// copy is pinned from this side, where a merge can see it.
    /// </remarks>
    [Fact]
    public void The_E2E_copy_of_the_default_topic_matches_the_product()
    {
        var source = File.ReadAllText(Path.Combine(
            Infrastructure.RepoRoot.Path, "tests", "AutoNate.E2E.Tests",
            "WorkflowMessageQueueExecutionTests.cs"));

        Assert.Contains(
            $"private const string MessageTopic = \"{WorkflowBpmnXml.DefaultMessageTopic}\";",
            source,
            StringComparison.Ordinal);
    }

    /// <summary>The E2E copy of the default SIGNAL topic matches too (#540).</summary>
    /// <remarks>
    /// Same reason as the message one above, and the same class of defect #540
    /// was: a topic nothing listens on produces a timeout that reads like a
    /// product bug.
    /// </remarks>
    [Fact]
    public void The_E2E_copy_of_the_default_signal_topic_matches_the_product()
    {
        var source = File.ReadAllText(Path.Combine(
            Infrastructure.RepoRoot.Path, "tests", "AutoNate.E2E.Tests",
            "WorkflowSignalBusExecutionTests.cs"));

        Assert.Contains(
            $"private const string DefaultSignalTopic = \"{WorkflowBpmnXml.DefaultSignalTopic}\";",
            source,
            StringComparison.Ordinal);
    }

    private static string Diagram(string body, string? topic = null) => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          xmlns:flowable="http://flowable.org/bpmn"
                          targetNamespace="http://autonate.dev/workflows">
          <bpmn:message id="Msg_1" name="orderPlaced"{(topic is null ? "" : $" flowable:topic=\"{topic}\"")} />
          <bpmn:process id="p" isExecutable="true">
            {body}
          </bpmn:process>
        </bpmn:definitions>
        """;
}
