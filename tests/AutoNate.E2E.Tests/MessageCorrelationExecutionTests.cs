using System.Text.Json;
using AutoNate.E2E.Tests.Support;
using Microsoft.Playwright;
using Xunit;

namespace AutoNate.E2E.Tests;

/// <summary>
/// Advancing a waiting process from outside it, against the real engine (#112).
/// </summary>
/// <remarks>
/// The hand-run probe on this issue proved the Flowable REST calls exist. It did
/// not prove that AutoNate's client sends them correctly, which is what these do.
///
/// The refusals are tested as carefully as the deliveries. A no-match that
/// answered 200 and a multi-match that advanced one instance would both look like
/// a working feature from the happy path alone.
/// </remarks>
[Trait("RequiresService", "Flowable")]
[Collection(AutoNateE2ECollection.Name)]
public sealed class MessageCorrelationExecutionTests : E2ETestBase
{
    public MessageCorrelationExecutionTests(AutoNateE2EFixture fixture) : base(fixture) { }

    [Fact]
    public async Task A_message_advances_the_one_instance_whose_correlation_value_matches()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var api = session.Page.APIRequest;

        var key = $"mc_one_{Guid.NewGuid():N}"[..24];
        await PublishAsync(api, key, CatchDiagram(key));

        // Two instances waiting on the same message, told apart only by the
        // correlation value. This is the shape the feature exists for, and a
        // single-instance test would pass against an implementation that ignores
        // the correlation value entirely.
        var first = await StartAsync(api, key, new { orderId = "ORD-1" });
        var second = await StartAsync(api, key, new { orderId = "ORD-2" });

        Assert.Contains("Waiting", await TaskNamesAsync(api, first));
        Assert.Contains("Waiting", await TaskNamesAsync(api, second));

        var response = await SendAsync(api, key, "paymentCleared", "ORD-2");
        Assert.True(response.Ok, $"Send failed: {response.Status} {await response.TextAsync()}");

        using var body = JsonDocument.Parse(await response.TextAsync());
        Assert.Equal("delivered", body.RootElement.GetProperty("outcome").GetString());

        // The addressed instance moved on...
        await EventuallyAsync(api, second,
            names => names.Contains("Paid"), "the addressed instance to advance");

        // ...and the other one did NOT. Without this, delivering to every waiting
        // instance would pass.
        var untouched = await TaskNamesAsync(api, first);
        Assert.Contains("Waiting", untouched);
        Assert.DoesNotContain("Paid", untouched);
    }

    [Fact]
    public async Task A_correlation_value_matching_nothing_is_refused_and_advances_nothing()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var api = session.Page.APIRequest;

        var key = $"mc_none_{Guid.NewGuid():N}"[..24];
        await PublishAsync(api, key, CatchDiagram(key));
        var instance = await StartAsync(api, key, new { orderId = "ORD-1" });

        var response = await SendAsync(api, key, "paymentCleared", "ORD-NOBODY");

        Assert.False(response.Ok, "A message matching nothing must not report success.");
        Assert.Equal(404, response.Status);
        Assert.Contains("no-match", await response.TextAsync());

        // Nothing moved.
        Assert.Contains("Waiting", await TaskNamesAsync(api, instance));
    }

    [Fact]
    public async Task More_than_one_match_is_refused_with_the_count_and_nothing_is_advanced()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var api = session.Page.APIRequest;

        var key = $"mc_many_{Guid.NewGuid():N}"[..24];
        await PublishAsync(api, key, CatchDiagram(key));

        // Two instances sharing a correlation value: the process is modelled
        // wrong, or the key is badly chosen. Either way the sender is told rather
        // than one of them being picked.
        var a = await StartAsync(api, key, new { orderId = "SAME" });
        var b = await StartAsync(api, key, new { orderId = "SAME" });

        var response = await SendAsync(api, key, "paymentCleared", "SAME");

        Assert.Equal(409, response.Status);
        var text = await response.TextAsync();
        Assert.Contains("multiple-matches", text);
        Assert.Contains("2", text);

        // Neither advanced. This is the assertion that separates "refused" from
        // "reported the problem and delivered anyway".
        Assert.Contains("Waiting", await TaskNamesAsync(api, a));
        Assert.Contains("Waiting", await TaskNamesAsync(api, b));
    }

    [Fact]
    public async Task A_message_start_event_starts_a_new_instance()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var api = session.Page.APIRequest;

        var key = $"mc_start_{Guid.NewGuid():N}"[..24];
        await PublishAsync(api, key, $$"""
            <?xml version="1.0" encoding="UTF-8"?>
            <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                              id="Definitions_1" targetNamespace="http://autonate.dev/workflows">
              <bpmn:message id="Msg_1" name="{{key}}_orderPlaced" />
              <bpmn:process id="{{key}}" name="Message Start" isExecutable="true">
                <bpmn:startEvent id="s">
                  <bpmn:messageEventDefinition messageRef="Msg_1" />
                </bpmn:startEvent>
                <bpmn:sequenceFlow id="f0" sourceRef="s" targetRef="t" />
                <bpmn:userTask id="t" name="Started by message" />
              </bpmn:process>
              {{Di(key, "s", "t")}}
            </bpmn:definitions>
            """);

        var response = await SendAsync(api, key, $"{key}_orderPlaced", correlationValue: null);
        Assert.True(response.Ok, $"Send failed: {response.Status} {await response.TextAsync()}");

        using var body = JsonDocument.Parse(await response.TextAsync());
        Assert.Equal("started", body.RootElement.GetProperty("outcome").GetString());
        var instanceId = body.RootElement.GetProperty("processInstanceId").GetString()!;

        Assert.Contains("Started by message", await TaskNamesAsync(api, instanceId));
    }

    [Fact]
    public async Task An_interrupting_message_boundary_cancels_its_activity_and_a_non_interrupting_one_does_not()
    {
        // Both in one diagram so they are compared against the same definition.
        await using var session = await NewSignedInAsAdminAsync();
        var api = session.Page.APIRequest;

        var key = $"mc_bnd_{Guid.NewGuid():N}"[..24];
        await PublishAsync(api, key, $$"""
            <?xml version="1.0" encoding="UTF-8"?>
            <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                              xmlns:flowable="http://flowable.org/bpmn"
                              id="Definitions_1" targetNamespace="http://autonate.dev/workflows">
              <bpmn:message id="Msg_Stop" name="{{key}}_stop" />
              <bpmn:message id="Msg_Nudge" name="{{key}}_nudge" />
              <bpmn:process id="{{key}}" name="Message Boundary" isExecutable="true">
                <bpmn:startEvent id="s" />
                <bpmn:sequenceFlow id="f0" sourceRef="s" targetRef="fork" />
                <bpmn:parallelGateway id="fork" />

                <bpmn:sequenceFlow id="fa" sourceRef="fork" targetRef="cancelled" />
                <bpmn:userTask id="cancelled" name="Cancelled work" />
                <bpmn:boundaryEvent id="stop" attachedToRef="cancelled" cancelActivity="true"
                                    flowable:autonateCorrelationKey="orderId">
                  <bpmn:messageEventDefinition messageRef="Msg_Stop" />
                </bpmn:boundaryEvent>
                <bpmn:sequenceFlow id="fa2" sourceRef="stop" targetRef="afterStop" />
                <bpmn:userTask id="afterStop" name="After stop" />

                <bpmn:sequenceFlow id="fb" sourceRef="fork" targetRef="surviving" />
                <bpmn:userTask id="surviving" name="Surviving work" />
                <bpmn:boundaryEvent id="nudge" attachedToRef="surviving" cancelActivity="false"
                                    flowable:autonateCorrelationKey="orderId">
                  <bpmn:messageEventDefinition messageRef="Msg_Nudge" />
                </bpmn:boundaryEvent>
                <bpmn:sequenceFlow id="fb2" sourceRef="nudge" targetRef="afterNudge" />
                <bpmn:userTask id="afterNudge" name="After nudge" />
              </bpmn:process>
              {{Di(key, "s", "fork", "cancelled", "stop", "afterStop", "surviving", "nudge", "afterNudge")}}
            </bpmn:definitions>
            """);

        var instance = await StartAsync(api, key, new { orderId = "ORD-1" });
        var before = await TaskNamesAsync(api, instance);
        Assert.Contains("Cancelled work", before);
        Assert.Contains("Surviving work", before);

        var stopped = await SendAsync(api, key, $"{key}_stop", "ORD-1");
        Assert.True(stopped.Ok, $"Interrupting send failed: {stopped.Status} {await stopped.TextAsync()}");

        var afterStop = await EventuallyAsync(api, instance,
            names => names.Contains("After stop"), "the interrupting boundary to fire");

        // Interrupting: the path ran AND the guarded activity is gone. Asserting
        // only the first passes for a non-interrupting boundary — the opposite
        // feature.
        Assert.DoesNotContain("Cancelled work", afterStop);

        var nudged = await SendAsync(api, key, $"{key}_nudge", "ORD-1");
        Assert.True(nudged.Ok, $"Non-interrupting send failed: {nudged.Status} {await nudged.TextAsync()}");

        var afterNudge = await EventuallyAsync(api, instance,
            names => names.Contains("After nudge"), "the non-interrupting boundary to fire");

        // Non-interrupting: the path ran AND the activity is still there.
        Assert.Contains("Surviving work", afterNudge);
    }

    [Fact]
    public async Task A_receive_task_is_advanced_by_the_same_mechanism()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var api = session.Page.APIRequest;

        var key = $"mc_recv_{Guid.NewGuid():N}"[..24];
        await PublishAsync(api, key, $$"""
            <?xml version="1.0" encoding="UTF-8"?>
            <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                              xmlns:flowable="http://flowable.org/bpmn"
                              id="Definitions_1" targetNamespace="http://autonate.dev/workflows">
              <bpmn:process id="{{key}}" name="Receive Task" isExecutable="true">
                <bpmn:startEvent id="s" />
                <bpmn:sequenceFlow id="f0" sourceRef="s" targetRef="awaitShipment" />
                <bpmn:receiveTask id="awaitShipment" name="Await shipment"
                                  flowable:autonateCorrelationKey="orderId" />
                <bpmn:sequenceFlow id="f1" sourceRef="awaitShipment" targetRef="t" />
                <bpmn:userTask id="t" name="Shipped" />
              </bpmn:process>
              {{Di(key, "s", "awaitShipment", "t")}}
            </bpmn:definitions>
            """);

        var mine = await StartAsync(api, key, new { orderId = "ORD-1" });
        var other = await StartAsync(api, key, new { orderId = "ORD-2" });

        // Addressed by element id: a receive task has no message of its own.
        var response = await SendAsync(api, key, "awaitShipment", "ORD-1");
        Assert.True(response.Ok, $"Send failed: {response.Status} {await response.TextAsync()}");

        await EventuallyAsync(api, mine, names => names.Contains("Shipped"), "the receive task to be triggered");
        Assert.DoesNotContain("Shipped", await TaskNamesAsync(api, other));
    }

    [Fact]
    public async Task A_message_payload_lands_on_the_instance_as_process_variables()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var api = session.Page.APIRequest;

        var key = $"mc_vars_{Guid.NewGuid():N}"[..24];
        await PublishAsync(api, key, CatchDiagram(key));
        var instance = await StartAsync(api, key, new { orderId = "ORD-1" });

        var response = await api.PostAsync("/api/workflow-messages/", new APIRequestContextOptions
        {
            DataObject = new
            {
                processKey = key,
                messageName = "paymentCleared",
                correlationValue = "ORD-1",
                variables = new Dictionary<string, object?> { ["paidAmount"] = "42" }
            }
        });
        Assert.True(response.Ok, $"Send failed: {response.Status} {await response.TextAsync()}");

        await EventuallyAsync(api, instance, names => names.Contains("Paid"), "the instance to advance");

        // A payload that is accepted and dropped is worse than one that is
        // refused, so the variable is read back rather than assumed. Read from
        // the engine: the app exposes no GET for execution variables, and
        // asserting against the request I just sent would prove nothing.
        var value = await ReadProcessVariableAsync(instance, "paidAmount");
        Assert.Equal("42", value);
    }

    // start -> catch("paymentCleared", correlate on orderId) -> "Paid",
    // with a user task in front so the instance is observable while it waits.
    private static string CatchDiagram(string key) => $$"""
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          xmlns:flowable="http://flowable.org/bpmn"
                          id="Definitions_1" targetNamespace="http://autonate.dev/workflows">
          <bpmn:message id="Msg_1" name="paymentCleared" />
          <bpmn:process id="{{key}}" name="Message Catch" isExecutable="true">
            <bpmn:startEvent id="s" />
            <bpmn:sequenceFlow id="f0" sourceRef="s" targetRef="fork" />
            <bpmn:parallelGateway id="fork" />
            <bpmn:sequenceFlow id="fa" sourceRef="fork" targetRef="waiting" />
            <bpmn:userTask id="waiting" name="Waiting" />
            <bpmn:sequenceFlow id="fb" sourceRef="fork" targetRef="catch" />
            <bpmn:intermediateCatchEvent id="catch" name="Await payment"
                                         flowable:autonateCorrelationKey="orderId">
              <bpmn:messageEventDefinition messageRef="Msg_1" />
            </bpmn:intermediateCatchEvent>
            <bpmn:sequenceFlow id="f1" sourceRef="catch" targetRef="paid" />
            <bpmn:userTask id="paid" name="Paid" />
          </bpmn:process>
          {{Di(key, "s", "fork", "waiting", "catch", "paid")}}
        </bpmn:definitions>
        """;

    /// <summary>
    /// Reads one process variable straight from Flowable.
    /// </summary>
    private static async Task<string?> ReadProcessVariableAsync(string processInstanceId, string name)
    {
        using var client = Support.FlowableDeploymentSweep.CreateClient(
            Environment.GetEnvironmentVariable("AUTONATE_FLOWABLE_URL") ?? "http://localhost:8080/flowable-rest",
            Environment.GetEnvironmentVariable("AUTONATE_FLOWABLE_USER") ?? "rest-admin",
            Environment.GetEnvironmentVariable("AUTONATE_FLOWABLE_PASSWORD") ?? "test");

        var body = await client.GetStringAsync(
            $"service/runtime/process-instances/{Uri.EscapeDataString(processInstanceId)}/variables");
        using var document = JsonDocument.Parse(body);
        foreach (var variable in document.RootElement.EnumerateArray())
        {
            if (variable.GetProperty("name").GetString() == name)
            {
                return variable.TryGetProperty("value", out var value) ? value.ToString() : null;
            }
        }

        return null;
    }

    private static Task<IAPIResponse> SendAsync(
        IAPIRequestContext api, string processKey, string? messageName, string? correlationValue) =>
        api.PostAsync("/api/workflow-messages/", new APIRequestContextOptions
        {
            DataObject = new { processKey, messageName, correlationValue }
        });

    private static string Di(string processKey, params string[] elementIds)
    {
        var shapes = string.Join("\n", elementIds.Select((id, index) =>
            $"""
                  <bpmndi:BPMNShape id="Shape_{id}" bpmnElement="{id}">
                    <dc:Bounds x="{100 + (index * 140)}" y="100" width="100" height="80" />
                  </bpmndi:BPMNShape>
             """));

        return $"""
              <bpmndi:BPMNDiagram id="Diagram_1"
                                  xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI"
                                  xmlns:dc="http://www.omg.org/spec/DD/20100524/DC">
                <bpmndi:BPMNPlane id="Plane_1" bpmnElement="{processKey}">
            {shapes}
                </bpmndi:BPMNPlane>
              </bpmndi:BPMNDiagram>
            """;
    }

    private static async Task PublishAsync(IAPIRequestContext api, string key, string xml)
    {
        var id = Guid.NewGuid();
        var displayName = TestNames.Prefixed(key);
        var created = await api.PostAsync("/api/workflows/", new APIRequestContextOptions
        {
            DataObject = new { id, name = displayName, processKey = key, bpmnXml = xml }
        });
        Assert.True(created.Ok, $"Creating the model failed: {created.Status} {await created.TextAsync()}");

        var published = await api.PostAsync($"/api/workflows/{id}/publish", new APIRequestContextOptions
        {
            DataObject = new { id, name = displayName, processKey = key, bpmnXml = xml }
        });
        Assert.True(published.Ok, $"Publishing failed: {published.Status} {await published.TextAsync()}");
    }

    private static async Task<string> StartAsync(IAPIRequestContext api, string key, object variables)
    {
        var response = await api.PostAsync($"/api/workflows/{key}/start", new APIRequestContextOptions
        {
            DataObject = new { variables }
        });
        Assert.True(response.Ok, $"Starting failed: {response.Status} {await response.TextAsync()}");
        using var document = JsonDocument.Parse(await response.TextAsync());
        return document.RootElement.GetProperty("id").GetString()!;
    }

    private static async Task<List<string>> TaskNamesAsync(IAPIRequestContext api, string instanceId)
    {
        var response = await api.GetAsync($"/api/executions/{instanceId}/tasks");
        Assert.True(response.Ok, $"Reading tasks failed: {response.Status} {await response.TextAsync()}");
        using var document = JsonDocument.Parse(await response.TextAsync());
        return document.RootElement.EnumerateArray()
            .Select(element => element.GetProperty("name").GetString()!)
            .ToList();
    }

    private static async Task<List<string>> EventuallyAsync(
        IAPIRequestContext api, string instanceId, Func<List<string>, bool> until, string what)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        List<string> names = [];
        while (DateTime.UtcNow < deadline)
        {
            names = await TaskNamesAsync(api, instanceId);
            if (until(names)) return names;
            await Task.Delay(500);
        }

        Assert.Fail($"Timed out after 30s waiting for {what}. Tasks were: {string.Join(", ", names)}");
        return names;
    }
}
