using System.Text.Json;
using AutoNate.E2E.Tests.Support;
using Microsoft.Playwright;
using Xunit;

namespace AutoNate.E2E.Tests;

/// <summary>
/// An event-based gateway takes whichever path arrives first, and cancels the
/// rest (#164).
/// </summary>
/// <remarks>
/// The cancellation is the feature. A test that only checks the taken path cannot
/// tell whether the other alternatives are still subscribed and waiting — which
/// is exactly what a broken implementation looks like from the outside. So each
/// test asserts the loser was cancelled, not merely that the winner ran.
/// </remarks>
[Trait("RequiresService", "Flowable")]
[Collection(AutoNateE2ECollection.Name)]
public sealed class EventBasedGatewayExecutionTests : E2ETestBase
{
    public EventBasedGatewayExecutionTests(AutoNateE2EFixture fixture) : base(fixture) { }

    [Fact]
    public async Task The_message_wins_when_it_arrives_before_the_timer()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var api = session.Page.APIRequest;

        var key = $"ebg_m_{Guid.NewGuid():N}"[..22];
        await PublishAsync(api, key, Diagram(key, "PT30S"));
        var instance = await StartAsync(api, key, new { orderId = "ORD-1" });

        var sent = await api.PostAsync("/api/workflow-messages/", new APIRequestContextOptions
        {
            DataObject = new
            {
                processKey = key,
                messageName = $"{key}_confirm",
                correlationValue = "ORD-1"
            }
        });
        Assert.True(sent.Ok, $"Send failed: {sent.Status} {await sent.TextAsync()}");

        var names = await EventuallyAsync(api, instance,
            n => n.Contains("Message won"), "the message path to be taken");

        Assert.Contains("Message won", names);
        Assert.DoesNotContain("Timer won", names);

        // The loser is cancelled, not merely late: no timer job survives for this
        // instance. Without this the test passes against a gateway that took the
        // message path and left the timer armed to fire 30 seconds later.
        Assert.Equal(0, await TimerJobCountAsync(instance));
    }

    [Fact]
    public async Task The_timer_wins_when_no_message_arrives_and_the_message_stops_waiting()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var api = session.Page.APIRequest;

        var key = $"ebg_t_{Guid.NewGuid():N}"[..22];
        await PublishAsync(api, key, Diagram(key, "PT2S"));
        var instance = await StartAsync(api, key, new { orderId = "ORD-2" });

        var names = await EventuallyAsync(api, instance,
            n => n.Contains("Timer won"), "the timer path to be taken");

        Assert.Contains("Timer won", names);
        Assert.DoesNotContain("Message won", names);

        // And the message alternative genuinely stopped waiting. Sending it now
        // must find nothing — which is both the cancellation assertion and the
        // "a later event does not re-trigger a resolved gateway" criterion.
        var late = await api.PostAsync("/api/workflow-messages/", new APIRequestContextOptions
        {
            DataObject = new
            {
                processKey = key,
                messageName = $"{key}_confirm",
                correlationValue = "ORD-2"
            }
        });
        Assert.False(late.Ok, "A message arriving after the gateway resolved should match nothing.");

        // Still only the timer path — the late message changed nothing.
        var after = await TaskNamesAsync(api, instance);
        Assert.Contains("Timer won", after);
        Assert.DoesNotContain("Message won", after);
    }

    [Fact]
    public async Task A_pending_gateway_shows_every_alternative_it_is_waiting_for()
    {
        // "What is this process waiting for?" is the question this construct
        // generates most often, and while the gateway is pending there are no
        // tasks to look at — the alternatives are events, not work items.
        await using var session = await NewSignedInAsAdminAsync();
        var api = session.Page.APIRequest;

        var key = $"ebg_w_{Guid.NewGuid():N}"[..22];
        await PublishAsync(api, key, Diagram(key, "PT60S"));
        var instance = await StartAsync(api, key, new { orderId = "ORD-3" });

        // Nothing in the task list, which is exactly why the diagram has to carry
        // it. Asserted so the next assertion cannot be mistaken for redundant.
        Assert.Empty(await TaskNamesAsync(api, instance));

        var diagram = await api.GetAsync($"/api/executions/{instance}/diagram");
        Assert.True(diagram.Ok, $"Reading the diagram failed: {diagram.Status}");
        using var document = JsonDocument.Parse(await diagram.TextAsync());
        var current = document.RootElement.GetProperty("currentActivityIds")
            .EnumerateArray().Select(e => e.GetString()).ToList();

        // BOTH alternatives, not just one — an operator can see the whole choice
        // the process is sitting on.
        Assert.Contains("onMsg", current);
        Assert.Contains("onTimer", current);
    }

    [Fact]
    public async Task A_gateway_with_one_path_is_refused_at_publish()
    {
        // Verified against the engine: this deploys cleanly, so nothing but our
        // own validation stands between the author and a process that waits
        // forever while its diagram suggests a choice.
        await using var session = await NewSignedInAsAdminAsync();
        var api = session.Page.APIRequest;

        var key = $"ebg_1_{Guid.NewGuid():N}"[..22];
        var response = await TryPublishAsync(api, key, OnePathDiagram(key));

        Assert.False(response.Ok, "A gateway with a single path should be refused.");
        Assert.Contains("nothing for it to choose between", await response.TextAsync());
    }

    private static async Task<int> TimerJobCountAsync(string processInstanceId)
    {
        using var client = Support.FlowableDeploymentSweep.CreateClient(
            Environment.GetEnvironmentVariable("AUTONATE_FLOWABLE_URL") ?? "http://localhost:8080/flowable-rest",
            Environment.GetEnvironmentVariable("AUTONATE_FLOWABLE_USER") ?? "rest-admin",
            Environment.GetEnvironmentVariable("AUTONATE_FLOWABLE_PASSWORD") ?? "test");

        // Filtered here rather than by query parameter: #112 established that the
        // job endpoints ignore processInstanceId and return everything.
        var body = await client.GetStringAsync("service/management/timer-jobs?size=500");
        using var document = JsonDocument.Parse(body);
        return document.RootElement.GetProperty("data").EnumerateArray()
            .Count(row => row.TryGetProperty("processInstanceId", out var owner)
                          && owner.GetString() == processInstanceId);
    }

    private static string Diagram(string key, string timerDuration) => $$"""
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
                          xmlns:flowable="http://flowable.org/bpmn"
                          id="Definitions_1" targetNamespace="http://autonate.dev/workflows">
          <bpmn:message id="Msg_1" name="{{key}}_confirm" />
          <bpmn:process id="{{key}}" name="Event Gateway" isExecutable="true">
            <bpmn:startEvent id="s" />
            <bpmn:sequenceFlow id="f0" sourceRef="s" targetRef="gw" />
            <bpmn:eventBasedGateway id="gw" name="First one wins" />
            <bpmn:sequenceFlow id="fa" sourceRef="gw" targetRef="onMsg" />
            <bpmn:intermediateCatchEvent id="onMsg" name="Confirmed"
                                         flowable:autonateCorrelationKey="orderId">
              <bpmn:messageEventDefinition messageRef="Msg_1" />
            </bpmn:intermediateCatchEvent>
            <bpmn:sequenceFlow id="fa1" sourceRef="onMsg" targetRef="msgPath" />
            <bpmn:userTask id="msgPath" name="Message won" />
            <bpmn:sequenceFlow id="fb" sourceRef="gw" targetRef="onTimer" />
            <bpmn:intermediateCatchEvent id="onTimer" name="Timed out">
              <bpmn:timerEventDefinition>
                <bpmn:timeDuration xsi:type="bpmn:tFormalExpression">{{timerDuration}}</bpmn:timeDuration>
              </bpmn:timerEventDefinition>
            </bpmn:intermediateCatchEvent>
            <bpmn:sequenceFlow id="fb1" sourceRef="onTimer" targetRef="timerPath" />
            <bpmn:userTask id="timerPath" name="Timer won" />
          </bpmn:process>
          {{Di(key, "s", "gw", "onMsg", "msgPath", "onTimer", "timerPath")}}
        </bpmn:definitions>
        """;

    private static string OnePathDiagram(string key) => $$"""
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          xmlns:flowable="http://flowable.org/bpmn"
                          id="Definitions_1" targetNamespace="http://autonate.dev/workflows">
          <bpmn:message id="Msg_1" name="{{key}}_confirm" />
          <bpmn:process id="{{key}}" name="One Path" isExecutable="true">
            <bpmn:startEvent id="s" />
            <bpmn:sequenceFlow id="f0" sourceRef="s" targetRef="gw" />
            <bpmn:eventBasedGateway id="gw" name="Only one" />
            <bpmn:sequenceFlow id="fa" sourceRef="gw" targetRef="onMsg" />
            <bpmn:intermediateCatchEvent id="onMsg" name="Confirmed"
                                         flowable:autonateCorrelationKey="orderId">
              <bpmn:messageEventDefinition messageRef="Msg_1" />
            </bpmn:intermediateCatchEvent>
          </bpmn:process>
          {{Di(key, "s", "gw", "onMsg")}}
        </bpmn:definitions>
        """;

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

    private static async Task<IAPIResponse> TryPublishAsync(IAPIRequestContext api, string key, string xml)
    {
        var id = Guid.NewGuid();
        var displayName = TestNames.Prefixed(key);
        var created = await api.PostAsync("/api/workflows/", new APIRequestContextOptions
        {
            DataObject = new { id, name = displayName, processKey = key, bpmnXml = xml }
        });
        Assert.True(created.Ok, $"Creating the model failed: {created.Status} {await created.TextAsync()}");

        return await api.PostAsync($"/api/workflows/{id}/publish", new APIRequestContextOptions
        {
            DataObject = new { id, name = displayName, processKey = key, bpmnXml = xml }
        });
    }

    private static async Task PublishAsync(IAPIRequestContext api, string key, string xml)
    {
        var published = await TryPublishAsync(api, key, xml);
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
