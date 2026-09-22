using System.Text;
using System.Text.Json;
using AutoNate.E2E.Tests.Support;
using Microsoft.Playwright;
using Xunit;

namespace AutoNate.E2E.Tests;

/// <summary>
/// A queue message starts a workflow (#524).
/// </summary>
/// <remarks>
/// <para>
/// The decision rules are unit-tested in <c>WorkflowMessageDispatcherTests</c>,
/// which runs in slim. What is only provable here is the wiring between them: the
/// registry's topic reaching the subscription list, the dispatch gate reaching the
/// dispatcher, and a real engine starting a real instance. Every one of those
/// fails SILENTLY when wrong — a subscription that never exists and a gate that
/// never opens both look exactly like a quiet bus.
/// </para>
/// <para>
/// <b>Dapr, not Flowable alone.</b> This publishes into THIS RUN's sidecar, whose
/// port the fixture chose; 127.0.0.1:3500 is the container sidecar the app under
/// test is not listening to, and publishing there would pass against completely
/// unwired code.
/// </para>
/// </remarks>
[Trait("RequiresService", "Dapr")]
[Trait("RequiresService", "Flowable")]
[Collection(AutoNateE2ECollection.Name)]
public sealed class WorkflowMessageQueueExecutionTests : E2ETestBase
{
    private readonly AutoNateE2EFixture _fixture;

    public WorkflowMessageQueueExecutionTests(AutoNateE2EFixture fixture) : base(fixture) =>
        _fixture = fixture;

    [Fact]
    public async Task A_message_on_the_bus_starts_the_workflow_that_declares_it()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var api = session.Page.APIRequest;

        var key = $"mq{Guid.NewGuid():N}"[..20];
        var messageName = $"m{Guid.NewGuid():N}"[..18];
        await PublishAsync(api, key, StartedByMessage(key, messageName));

        Assert.Equal(0, await InstanceCountAsync(key));

        await PublishToBusAsync(messageName);

        var started = await EventuallyAsync(
            () => InstanceCountAsync(key), n => n > 0,
            $"the bus message '{messageName}' to start an instance of '{key}'");

        // #636. SETTLE, THEN COUNT. The duplicate landed 66 ms after the first
        // instance when it was measured; a count taken on the first read that
        // returns > 0 passes with two. One is the claim, so the count is taken
        // again after every late arrival would have landed.
        Assert.True(started >= 1);
        await Task.Delay(TimeSpan.FromSeconds(3));
        Assert.Equal(1, await InstanceCountAsync(key));
    }

    /// <summary>
    /// #524 AC4, end to end (#524).
    /// </summary>
    /// <remarks>
    /// Two published workflows declare the SAME name — one as a message start,
    /// one as a signal start. A bus event carrying that name must start the
    /// message one and not the signal one. This is the claim the separate
    /// registries exist for, and the one a shared registry keyed on a bare name
    /// would fail while passing everything else.
    /// </remarks>
    [Fact]
    public async Task A_bus_message_does_not_start_a_signal_workflow_of_the_same_name()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var api = session.Page.APIRequest;

        var shared = $"x{Guid.NewGuid():N}"[..18];
        var messageKey = $"mq{Guid.NewGuid():N}"[..20];
        var signalKey = $"sq{Guid.NewGuid():N}"[..20];

        await PublishAsync(api, messageKey, StartedByMessage(messageKey, shared));
        await PublishAsync(api, signalKey, StartedBySignal(signalKey, shared));

        Assert.Equal(0, await InstanceCountAsync(messageKey));
        Assert.Equal(0, await InstanceCountAsync(signalKey));

        await PublishToBusAsync(shared);

        await EventuallyAsync(
            () => InstanceCountAsync(messageKey), n => n > 0,
            $"the bus message '{shared}' to start the MESSAGE workflow");

        // The complement, given the same window the positive half was allowed so
        // "did not start" cannot mean "not yet".
        await Task.Delay(5_000);

        Assert.Equal(0, await InstanceCountAsync(signalKey));
    }

    // ---- bus ----------------------------------------------------------------

    private async Task PublishToBusAsync(string eventType)
    {
        Assert.True(
            _fixture.DaprHttpPort > 0,
            "No sidecar for this run, so nothing can be put on the bus. This class is "
            + "RequiresService=Dapr and the full-local tier sets AUTONATE_E2E_DAPR=1; "
            + "running it without one would pass against unwired code (#487).");

        using var client = new HttpClient();
        var response = await client.PostAsync(
            $"http://127.0.0.1:{_fixture.DaprHttpPort}/v1.0/publish/pubsub/"
            + MessageTopic
            + "?metadata.rawPayload=true",
            new StringContent($$"""{"eventType":"{{eventType}}"}""", Encoding.UTF8, "application/json"));

        Assert.True(
            response.IsSuccessStatusCode,
            $"Publishing '{eventType}' to the bus failed: {(int)response.StatusCode} "
            + await response.Content.ReadAsStringAsync());
    }

    // The E2E project does not reference AutoNate.Web, so this is a literal
    // rather than `WorkflowBpmnXml.DefaultMessageTopic`. A second copy of a
    // constant is exactly the drift this repo keeps finding, so it is pinned:
    // `Every_default_topic_matches_the_product` in the backend suite fails if the
    // product's value moves without this one.
    private const string MessageTopic = "workflow.messages";

    // ---- diagrams ------------------------------------------------------------

    private static string StartedByMessage(string key, string messageName) => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          xmlns:flowable="http://flowable.org/bpmn"
                          id="Definitions_1" targetNamespace="http://autonate.dev/workflows">
          <bpmn:message id="Msg_1" name="{messageName}" />
          <bpmn:process id="{key}" name="Queue start" isExecutable="true">
            <bpmn:startEvent id="s"><bpmn:messageEventDefinition messageRef="Msg_1"/></bpmn:startEvent>
            <bpmn:userTask id="t" name="Started" />
            <bpmn:endEvent id="e" />
            <bpmn:sequenceFlow id="f1" sourceRef="s" targetRef="t" />
            <bpmn:sequenceFlow id="f2" sourceRef="t" targetRef="e" />
          </bpmn:process>
          {Di(key)}
        </bpmn:definitions>
        """;

    private static string StartedBySignal(string key, string signalName) => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          xmlns:flowable="http://flowable.org/bpmn"
                          id="Definitions_1" targetNamespace="http://autonate.dev/workflows">
          <bpmn:signal id="Sig_1" name="{signalName}" />
          <bpmn:process id="{key}" name="Signal start" isExecutable="true">
            <bpmn:startEvent id="s"><bpmn:signalEventDefinition signalRef="Sig_1"/></bpmn:startEvent>
            <bpmn:userTask id="t" name="Started" />
            <bpmn:endEvent id="e" />
            <bpmn:sequenceFlow id="f1" sourceRef="s" targetRef="t" />
            <bpmn:sequenceFlow id="f2" sourceRef="t" targetRef="e" />
          </bpmn:process>
          {Di(key)}
        </bpmn:definitions>
        """;

    private static string Di(string key) => $"""
        <bpmndi:BPMNDiagram id="Diagram_1"
                            xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI"
                            xmlns:dc="http://www.omg.org/spec/DD/20100524/DC">
          <bpmndi:BPMNPlane id="Plane_1" bpmnElement="{key}">
            <bpmndi:BPMNShape id="Shape_t" bpmnElement="t">
              <dc:Bounds x="200" y="80" width="100" height="80" />
            </bpmndi:BPMNShape>
          </bpmndi:BPMNPlane>
        </bpmndi:BPMNDiagram>
        """;

    // ---- helpers -------------------------------------------------------------

    private static async Task<int> InstanceCountAsync(string key)
    {
        using var engine = FlowableDeploymentSweep.CreateClient(
            Environment.GetEnvironmentVariable("AUTONATE_FLOWABLE_URL") ?? "http://localhost:8080/flowable-rest",
            Environment.GetEnvironmentVariable("AUTONATE_FLOWABLE_USER") ?? "rest-admin",
            Environment.GetEnvironmentVariable("AUTONATE_FLOWABLE_PASSWORD") ?? "test");

        var response = await engine.GetAsync(
            "service/history/historic-process-instances"
            + $"?processDefinitionKey={Uri.EscapeDataString(key)}&size=100");
        if (!response.IsSuccessStatusCode) return 0;

        using var page = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return page.RootElement.TryGetProperty("data", out var rows)
               && rows.ValueKind == JsonValueKind.Array
            ? rows.GetArrayLength()
            : 0;
    }

    private static async Task<int> EventuallyAsync(Func<Task<int>> read, Func<int, bool> until, string what)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        var seen = 0;
        while (DateTime.UtcNow < deadline)
        {
            seen = await read();
            if (until(seen)) return seen;
            await Task.Delay(500);
        }

        Assert.Fail($"Timed out after 30s waiting for {what}. Last read: {seen}.");
        return seen;
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
}
