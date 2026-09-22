using System.Text.Json;
using AutoNate.E2E.Tests.Support;
using Microsoft.Playwright;
using Xunit;

namespace AutoNate.E2E.Tests;

/// <summary>
/// A message drawn between two pools actually arrives (#170).
/// </summary>
/// <remarks>
/// <para>
/// The unit tests prove a message flow becomes the send's addressing. What only
/// an engine can prove is that the addressing DELIVERS: two definitions deployed
/// as one set (#169), the sender's instance running its send, and the receiver's
/// instance -- a different definition -- advancing because of it. Every fact
/// here is asserted on the receiver, never on the send returning.
/// </para>
/// <para>
/// The failure modes are #112's by construction, and asserted to be: a
/// no-match and a multi-match answer exactly as the API path does, because the
/// send goes through the same correlator. A second, softer failure mode for the
/// same problem is what this story must not introduce.
/// </para>
/// </remarks>
[Trait("RequiresService", "Flowable")]
[Collection(AutoNateE2ECollection.Name)]
public sealed class MessageFlowExecutionTests : E2ETestBase
{
    public MessageFlowExecutionTests(AutoNateE2EFixture fixture) : base(fixture) { }

    /// <summary>
    /// Customer sends over a flow to Supplier's receive task; Supplier is
    /// already waiting. The key crosses the flow: Supplier declares it, and the
    /// send infers it (#170's discretion, tested at the unit level).
    /// </summary>
    private static string SendToReceiveTask(string customerKey, string supplierKey) => $$"""
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          xmlns:flowable="http://flowable.org/bpmn"
                          id="Definitions_1" targetNamespace="http://autonate.dev/workflows">
          <bpmn:collaboration id="Collab_1">
            <bpmn:participant id="P_c" name="Customer" processRef="{{customerKey}}" />
            <bpmn:participant id="P_s" name="Supplier" processRef="{{supplierKey}}" />
            <bpmn:messageFlow id="MF_1" name="order" sourceRef="send" targetRef="receive" />
          </bpmn:collaboration>
          <bpmn:process id="{{customerKey}}" name="Customer" isExecutable="true">
            <bpmn:startEvent id="cs" />
            <bpmn:sequenceFlow id="cf1" sourceRef="cs" targetRef="send" />
            <bpmn:sendTask id="send" name="Send order"
                           flowable:delegateExpression="${autonateBehaviorDelegate}"
                           flowable:autonateServiceKind="behavior"
                           flowable:behaviorKey="autonate.send-message"
                           flowable:async="true" />
            <bpmn:sequenceFlow id="cf2" sourceRef="send" targetRef="csent" />
            <bpmn:userTask id="csent" name="Order sent" />
            <bpmn:sequenceFlow id="cf3" sourceRef="csent" targetRef="ce" />
            <bpmn:endEvent id="ce" />
          </bpmn:process>
          <bpmn:process id="{{supplierKey}}" name="Supplier" isExecutable="true">
            <bpmn:startEvent id="ss" />
            <bpmn:sequenceFlow id="sf1" sourceRef="ss" targetRef="fork" />
            <bpmn:parallelGateway id="fork" />
            <bpmn:sequenceFlow id="sfa" sourceRef="fork" targetRef="idle" />
            <bpmn:userTask id="idle" name="Awaiting order" />
            <bpmn:sequenceFlow id="sfb" sourceRef="fork" targetRef="receive" />
            <bpmn:receiveTask id="receive" name="Receive order" flowable:autonateCorrelationKey="orderId" />
            <bpmn:sequenceFlow id="sf2" sourceRef="receive" targetRef="got" />
            <bpmn:userTask id="got" name="Order received" />
          </bpmn:process>
          {{Di(customerKey, "P_c", "P_s", "send", "csent", "idle", "receive", "got")}}
        </bpmn:definitions>
        """;

    /// <summary>Customer's message end event starts a Supplier instance.</summary>
    private static string EndEventToStartEvent(string customerKey, string supplierKey, string messageName) => $$"""
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          xmlns:flowable="http://flowable.org/bpmn"
                          id="Definitions_1" targetNamespace="http://autonate.dev/workflows">
          <bpmn:message id="Msg_1" name="{{messageName}}" />
          <bpmn:collaboration id="Collab_1">
            <bpmn:participant id="P_c" name="Customer" processRef="{{customerKey}}" />
            <bpmn:participant id="P_s" name="Supplier" processRef="{{supplierKey}}" />
            <bpmn:messageFlow id="MF_1" name="order placed" sourceRef="ce" targetRef="ss" />
          </bpmn:collaboration>
          <bpmn:process id="{{customerKey}}" name="Customer" isExecutable="true">
            <bpmn:startEvent id="cs" />
            <bpmn:sequenceFlow id="cf1" sourceRef="cs" targetRef="ce" />
            <bpmn:endEvent id="ce"><bpmn:messageEventDefinition messageRef="Msg_1" /></bpmn:endEvent>
          </bpmn:process>
          <bpmn:process id="{{supplierKey}}" name="Supplier" isExecutable="true">
            <bpmn:startEvent id="ss"><bpmn:messageEventDefinition messageRef="Msg_1" /></bpmn:startEvent>
            <bpmn:sequenceFlow id="sf1" sourceRef="ss" targetRef="handle" />
            <bpmn:userTask id="handle" name="Handle order" />
            <bpmn:sequenceFlow id="sf2" sourceRef="handle" targetRef="se" />
            <bpmn:endEvent id="se" />
          </bpmn:process>
          {{Di(customerKey, "P_c", "P_s", "ce", "ss", "handle")}}
        </bpmn:definitions>
        """;

    [Fact]
    public async Task A_send_over_a_message_flow_advances_the_receiving_pools_instance()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var api = session.Page.APIRequest;
        var customerKey = $"cust{Guid.NewGuid():N}"[..20];
        var supplierKey = $"supp{Guid.NewGuid():N}"[..20];

        await PublishAsync(api, customerKey, SendToReceiveTask(customerKey, supplierKey));

        // Supplier waits first, keyed by the order it expects. It is a different
        // definition in the same deployment, started by its own key.
        var supplier = await StartAsync(api, supplierKey, new { orderId = "ORD-7" });
        await EventuallyAsync(api, supplier, names => names.Contains("Awaiting order"), "Supplier to park");
        Assert.DoesNotContain("Order received", await TaskNamesAsync(api, supplier));

        // Customer runs its send. Its only addressing is the drawn flow.
        var customer = await StartAsync(api, customerKey, new { orderId = "ORD-7" });

        // THE RECEIVER ADVANCED. Not "the send returned": the Supplier instance,
        // a different definition, moved past its receive task because of it.
        await EventuallyAsync(api, supplier, names => names.Contains("Order received"),
            "the Supplier instance to advance past its receive task");

        // And the sender got past its send.
        await EventuallyAsync(api, customer, names => names.Contains("Order sent"), "Customer to advance");
    }

    /// <summary>
    /// The complement that makes the fact above mean something: a Supplier
    /// waiting on a DIFFERENT order is not advanced. #112's correlation, across
    /// two pools, reached through a drawn flow.
    /// </summary>
    [Fact]
    public async Task A_send_over_a_message_flow_does_not_advance_an_instance_keyed_differently()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var api = session.Page.APIRequest;
        var customerKey = $"cust{Guid.NewGuid():N}"[..20];
        var supplierKey = $"supp{Guid.NewGuid():N}"[..20];

        await PublishAsync(api, customerKey, SendToReceiveTask(customerKey, supplierKey));

        var addressed = await StartAsync(api, supplierKey, new { orderId = "ORD-A" });
        var other = await StartAsync(api, supplierKey, new { orderId = "ORD-B" });
        await EventuallyAsync(api, addressed, names => names.Contains("Awaiting order"), "addressed Supplier to park");
        await EventuallyAsync(api, other, names => names.Contains("Awaiting order"), "other Supplier to park");

        await StartAsync(api, customerKey, new { orderId = "ORD-A" });

        await EventuallyAsync(api, addressed, names => names.Contains("Order received"), "the addressed instance to advance");
        // Given the same window the positive half had, so "did not" cannot mean "not yet".
        await Task.Delay(3_000);
        var untouched = await TaskNamesAsync(api, other);
        Assert.Contains("Awaiting order", untouched);
        Assert.DoesNotContain("Order received", untouched);
    }

    /// <summary>
    /// A flow into a START event begins a new counterpart instance, and the two
    /// are navigable from each other (#170, and #169's deferred AC 6).
    /// </summary>
    [Fact]
    public async Task A_flow_into_a_start_event_starts_a_counterpart_linked_both_ways()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var api = session.Page.APIRequest;
        var customerKey = $"cust{Guid.NewGuid():N}"[..20];
        var supplierKey = $"supp{Guid.NewGuid():N}"[..20];
        var messageName = $"placed{Guid.NewGuid():N}"[..20];

        await PublishAsync(api, customerKey, EndEventToStartEvent(customerKey, supplierKey, messageName));

        using var engine = EngineClient();
        Assert.Equal(0, await EngineInstanceCountAsync(engine, supplierKey));

        var customer = await StartAsync(api, customerKey, new { });

        // A Supplier instance now exists, because of the send.
        var supplierInstance = await EventuallyEngineInstanceAsync(engine, supplierKey);

        // FROM THE SENDER: its counterparts name the started instance.
        var fromSender = await EventuallyCounterpartsAsync(api, customer, ids => ids.Contains(supplierInstance),
            "the sender's counterparts to list the started Supplier instance");
        Assert.Contains(supplierInstance, fromSender);

        // FROM THE STARTED INSTANCE: its counterparts name the sender. Both
        // directions, because a link that only reads one way is half a link.
        var fromStarted = await EventuallyCounterpartsAsync(api, supplierInstance, ids => ids.Contains(customer),
            "the started instance's counterparts to name the sender");
        Assert.Contains(customer, fromStarted);

        // The complement: an unrelated Supplier instance is nobody's counterpart.
        var unrelated = await StartAsync(api, supplierKey, new { });
        await Task.Delay(1_000);
        var counterpartsOfUnrelated = await CounterpartsAsync(api, unrelated);
        Assert.DoesNotContain(customer, counterpartsOfUnrelated);
    }

    /// <summary>
    /// Refused before deployment, and NOTHING reached the engine: the flow's
    /// target pool contains nothing to run. The send-into-the-void shape.
    /// </summary>
    [Fact]
    public async Task A_flow_into_a_pool_that_deploys_nothing_is_refused_and_neither_pool_reaches_the_engine()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var api = session.Page.APIRequest;
        var customerKey = $"cust{Guid.NewGuid():N}"[..20];
        var supplierKey = $"supp{Guid.NewGuid():N}"[..20];

        // Empty the Supplier pool of everything but the start event the flow
        // targets, then remove that too: a pool with nothing to run.
        var xml = EndEventToStartEvent(customerKey, supplierKey, "ping")
            .Replace("<bpmn:startEvent id=\"ss\"><bpmn:messageEventDefinition messageRef=\"Msg_1\" /></bpmn:startEvent>", "", StringComparison.Ordinal)
            .Replace("<bpmn:sequenceFlow id=\"sf1\" sourceRef=\"ss\" targetRef=\"handle\" />", "", StringComparison.Ordinal)
            .Replace("<bpmn:userTask id=\"handle\" name=\"Handle order\" />", "", StringComparison.Ordinal)
            .Replace("<bpmn:sequenceFlow id=\"sf2\" sourceRef=\"handle\" targetRef=\"se\" />", "", StringComparison.Ordinal)
            .Replace("<bpmn:endEvent id=\"se\" />", "", StringComparison.Ordinal)
            // ...and draw the flow to the pool itself, which is what the studio
            // does for a collapsed pool and the only way a flow reaches a pool
            // that deploys nothing.
            .Replace("targetRef=\"ss\"", "targetRef=\"P_s\"", StringComparison.Ordinal);

        var id = Guid.NewGuid();
        var created = await api.PostAsync("/api/workflows/", new APIRequestContextOptions
        {
            DataObject = new { id, name = TestNames.Prefixed(customerKey), processKey = customerKey, bpmnXml = xml }
        });
        Assert.True(created.Ok, await created.TextAsync());
        var published = await api.PostAsync($"/api/workflows/{id}/publish", new APIRequestContextOptions
        {
            DataObject = new { id, name = TestNames.Prefixed(customerKey), processKey = customerKey, bpmnXml = xml }
        });

        Assert.False(published.Ok, "A message flow into an empty pool must not publish.");
        var body = await published.TextAsync();
        Assert.Contains("Supplier", body, StringComparison.Ordinal);

        using var engine = EngineClient();
        foreach (var key in new[] { customerKey, supplierKey })
        {
            var definitions = await engine.GetStringAsync(
                $"service/repository/process-definitions?key={Uri.EscapeDataString(key)}");
            using var page = JsonDocument.Parse(definitions);
            Assert.Equal(0, page.RootElement.GetProperty("total").GetInt32());
        }
    }

    // ---- helpers -------------------------------------------------------------

    private static string Di(string primaryKey, params string[] elementIds)
    {
        var shapes = string.Join("\n", elementIds.Select((id, index) => $"""
                <bpmndi:BPMNShape id="Shape_{id}" bpmnElement="{id}">
                  <dc:Bounds x="{100 + index * 140}" y="80" width="100" height="80" />
                </bpmndi:BPMNShape>
            """));
        return $"""
            <bpmndi:BPMNDiagram id="Diagram_1"
                                xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI"
                                xmlns:dc="http://www.omg.org/spec/DD/20100524/DC">
              <bpmndi:BPMNPlane id="Plane_1" bpmnElement="Collab_1">
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
            DataObject = new { name = TestNames.Prefixed("run"), variables }
        });
        Assert.True(response.Ok, $"Starting {key} failed: {response.Status} {await response.TextAsync()}");
        using var document = JsonDocument.Parse(await response.TextAsync());
        return document.RootElement.GetProperty("id").GetString()!;
    }

    private static async Task<List<string>> TaskNamesAsync(IAPIRequestContext api, string instanceId)
    {
        var response = await api.GetAsync($"/api/executions/{instanceId}/tasks");
        Assert.True(response.Ok, await response.TextAsync());
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

        Assert.Fail($"Timed out waiting for {what}. Tasks were: {string.Join(", ", names)}");
        return names;
    }

    private static async Task<List<string>> CounterpartsAsync(IAPIRequestContext api, string instanceId)
    {
        var response = await api.GetAsync($"/api/executions/{instanceId}/counterparts");
        Assert.True(response.Ok, await response.TextAsync());
        using var document = JsonDocument.Parse(await response.TextAsync());
        return document.RootElement.EnumerateArray()
            .Select(element => element.GetProperty("id").GetString()!)
            .ToList();
    }

    private static async Task<List<string>> EventuallyCounterpartsAsync(
        IAPIRequestContext api, string instanceId, Func<List<string>, bool> until, string what)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        List<string> ids = [];
        while (DateTime.UtcNow < deadline)
        {
            ids = await CounterpartsAsync(api, instanceId);
            if (until(ids)) return ids;
            await Task.Delay(500);
        }

        Assert.Fail($"Timed out waiting for {what}. Counterparts were: {string.Join(", ", ids)}");
        return ids;
    }

    private static HttpClient EngineClient() => FlowableDeploymentSweep.CreateClient(
        Environment.GetEnvironmentVariable("AUTONATE_FLOWABLE_URL") ?? "http://localhost:8080/flowable-rest",
        Environment.GetEnvironmentVariable("AUTONATE_FLOWABLE_USER") ?? "rest-admin",
        Environment.GetEnvironmentVariable("AUTONATE_FLOWABLE_PASSWORD") ?? "test");

    private static async Task<int> EngineInstanceCountAsync(HttpClient engine, string key)
    {
        var body = await engine.GetStringAsync(
            $"service/history/historic-process-instances?processDefinitionKey={Uri.EscapeDataString(key)}");
        using var page = JsonDocument.Parse(body);
        return page.RootElement.GetProperty("total").GetInt32();
    }

    private static async Task<string> EventuallyEngineInstanceAsync(HttpClient engine, string key)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            var body = await engine.GetStringAsync(
                $"service/history/historic-process-instances?processDefinitionKey={Uri.EscapeDataString(key)}");
            using var page = JsonDocument.Parse(body);
            if (page.RootElement.GetProperty("total").GetInt32() > 0)
            {
                return page.RootElement.GetProperty("data")[0].GetProperty("id").GetString()!;
            }
            await Task.Delay(500);
        }

        Assert.Fail($"No instance of '{key}' was started by the message flow.");
        return string.Empty;
    }
}
