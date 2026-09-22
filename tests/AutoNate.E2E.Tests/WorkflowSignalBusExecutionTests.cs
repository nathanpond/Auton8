using System.Text;
using System.Text.Json;
using AutoNate.E2E.Tests.Support;
using Microsoft.Playwright;
using Xunit;

namespace AutoNate.E2E.Tests;

/// <summary>
/// A signal reaches a workflow over the bus (#540).
/// </summary>
/// <remarks>
/// <para>
/// Its own class, with BOTH service traits at class level, rather than a
/// method-level trait on <c>WorkflowSignalApiExecutionTests</c>. That class is
/// Flowable-only and about the API route; this is about the queue hop, needs a
/// sidecar as well, and a method-level trait would have made the class carry two
/// services while only one of its five tests did -- which
/// `The_multi_service_pin_matches_the_classes_that_carry_two_traits` counts per
/// class. Bending that guard to accommodate one test would have cost more than
/// a second file.
/// </para>
/// <para>
/// The sibling of <c>WorkflowMessageQueueExecutionTests</c>, and the test whose
/// absence is why #540 survived: nothing published to the default signal topic,
/// so the one hop where the missing JetStream subject lived was the one hop
/// nothing crossed.
/// </para>
/// </remarks>
[Trait("RequiresService", "Dapr")]
[Trait("RequiresService", "Flowable")]
[Collection(AutoNateE2ECollection.Name)]
public sealed class WorkflowSignalBusExecutionTests : E2ETestBase
{
    private readonly AutoNateE2EFixture _fixture;

    public WorkflowSignalBusExecutionTests(AutoNateE2EFixture fixture) : base(fixture) =>
        _fixture = fixture;

    /// <summary>
    /// A signal start event on the DEFAULT topic receives from the bus (#540).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The test that should already have existed, and whose absence is why #540
    /// survived: <c>workflow.signals</c> had no JetStream subject, so anything
    /// published to it failed at the sidecar with "nats: no response from
    /// stream" — and no test crossed that hop. Every other signal test reaches
    /// the engine or the dispatcher directly.
    /// </para>
    /// <para>
    /// The DEFAULT topic on purpose. An authored topic would prove the bus path
    /// while leaving the ordinary case — the one the studio writes when an author
    /// picks no topic — exactly as unexercised as it was.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_signal_start_event_on_the_default_topic_receives_from_the_bus()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var api = session.Page.APIRequest;

        var key = $"sb{Guid.NewGuid():N}"[..20];
        var signal = $"b{Guid.NewGuid():N}"[..18];
        await PublishAsync(api, key, StartedBySignal(key, signal));

        Assert.Equal(0, await InstanceCountAsync(key));

        Assert.True(
            _fixture.DaprHttpPort > 0,
            "No sidecar for this run, so nothing can be put on the bus (#487).");

        using var client = new HttpClient();
        var published = await client.PostAsync(
            $"http://127.0.0.1:{_fixture.DaprHttpPort}/v1.0/publish/pubsub/{DefaultSignalTopic}"
            + "?metadata.rawPayload=true",
            new StringContent(
                $$"""{"eventType":"{{signal}}"}""",
                System.Text.Encoding.UTF8,
                "application/json"));

        // THIS is the line #540 failed on, and it failed HERE rather than later:
        // the sidecar answers 500 "nats: no response from stream" when the topic
        // has no subject, so the symptom is a refused publish, not a quiet one.
        Assert.True(
            published.IsSuccessStatusCode,
            $"Publishing '{signal}' to '{DefaultSignalTopic}' failed: "
            + $"{(int)published.StatusCode} {await published.Content.ReadAsStringAsync()}");

        var started = await EventuallyAsync(
            () => InstanceCountAsync(key), n => n > 0,
            $"the bus event '{signal}' on the default topic to start an instance of '{key}'");

        // #636. SETTLE, THEN COUNT. The duplicate landed 66 ms after the first
        // instance when it was measured; a count taken on the first read that
        // returns > 0 passes with two. One is the claim, so the count is taken
        // again after every late arrival would have landed.
        Assert.True(started >= 1);
        await Task.Delay(TimeSpan.FromSeconds(3));
        Assert.Equal(1, await InstanceCountAsync(key));
    }

    // The E2E project does not reference AutoNate.Web, so this is a literal.
    // Pinned from the backend side by `The_E2E_copy_of_the_default_topic_matches_the_product`'s
    // sibling, for the same reason the message one is.
    private const string DefaultSignalTopic = "workflow.signals";

    private static string StartedBySignal(string key, string signalName) => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          xmlns:flowable="http://flowable.org/bpmn"
                          id="Definitions_1" targetNamespace="http://autonate.dev/workflows">
          <bpmn:signal id="Sig_1" name="{signalName}" />
          <bpmn:process id="{key}" name="Signal bus" isExecutable="true">
            <bpmn:startEvent id="s"><bpmn:signalEventDefinition signalRef="Sig_1"/></bpmn:startEvent>
            <bpmn:userTask id="t" name="Started" />
            <bpmn:endEvent id="e" />
            <bpmn:sequenceFlow id="f1" sourceRef="s" targetRef="t" />
            <bpmn:sequenceFlow id="f2" sourceRef="t" targetRef="e" />
          </bpmn:process>
          <bpmndi:BPMNDiagram id="Diagram_1"
                              xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI"
                              xmlns:dc="http://www.omg.org/spec/DD/20100524/DC">
            <bpmndi:BPMNPlane id="Plane_1" bpmnElement="{key}">
              <bpmndi:BPMNShape id="Shape_t" bpmnElement="t">
                <dc:Bounds x="200" y="80" width="100" height="80" />
              </bpmndi:BPMNShape>
            </bpmndi:BPMNPlane>
          </bpmndi:BPMNDiagram>
        </bpmn:definitions>
        """;

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
