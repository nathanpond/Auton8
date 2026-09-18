using System.Text.Json;
using AutoNate.E2E.Tests.Support;
using Microsoft.Playwright;
using Xunit;

namespace AutoNate.E2E.Tests;

/// <summary>
/// Firing a signal into a running engine through Auton8's API (#523).
/// </summary>
/// <remarks>
/// <para>
/// <c>BroadcastSignalAsync</c> existed since the bus path was built and had zero
/// production callers — every reference was a test. This is the route that
/// reaches it, proven against a live engine rather than against a stub.
/// </para>
/// <para>
/// The decision rules — what counts as a declaration, what is refused — are
/// unit-tested in <c>WorkflowSignalBroadcasterTests</c>, which runs in slim.
/// What needs an engine, and is only here, is that a broadcast actually moves
/// instances.
/// </para>
/// </remarks>
[Trait("RequiresService", "Flowable")]
[Collection(AutoNateE2ECollection.Name)]
public sealed class WorkflowSignalApiExecutionTests : E2ETestBase
{
    public WorkflowSignalApiExecutionTests(AutoNateE2EFixture fixture) : base(fixture) { }

    [Fact]
    public async Task A_signal_start_event_starts_a_workflow_that_had_no_instances()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var api = session.Page.APIRequest;

        var key = $"sig{Guid.NewGuid():N}"[..20];
        var signal = $"s{Guid.NewGuid():N}"[..18];
        await PublishAsync(api, key, StartedBySignal(key, signal));

        // NONE BEFORE. Without this the assertion below is satisfiable by any
        // instance that happens to exist, including one this suite started for
        // something else.
        Assert.Equal(0, await InstanceCountAsync(key));

        var response = await api.PostAsync("/api/workflow-signals/", new APIRequestContextOptions
        {
            DataObject = new { signalName = signal }
        });
        Assert.True(response.Ok, $"Broadcast failed: {response.Status} {await response.TextAsync()}");

        var started = await EventuallyAsync(
            () => InstanceCountAsync(key), n => n > 0,
            $"the broadcast of '{signal}' to start an instance of '{key}'");

        Assert.Equal(1, started);
    }

    /// <summary>
    /// The complement, and it asserts the COUNT rather than the absence of a throw (#523).
    /// </summary>
    /// <remarks>
    /// Flowable accepts a broadcast for a name nothing subscribes to and answers
    /// 204. A test that only checked "nothing blew up" would pass against an
    /// endpoint that silently did nothing at all — which is the whole failure
    /// mode this refusal exists to prevent.
    /// </remarks>
    [Fact]
    public async Task A_signal_nothing_declares_starts_nothing_and_names_the_signal()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var api = session.Page.APIRequest;

        var key = $"sig{Guid.NewGuid():N}"[..20];
        var declared = $"s{Guid.NewGuid():N}"[..18];
        var undeclared = $"u{Guid.NewGuid():N}"[..18];
        await PublishAsync(api, key, StartedBySignal(key, declared));

        var response = await api.PostAsync("/api/workflow-signals/", new APIRequestContextOptions
        {
            DataObject = new { signalName = undeclared }
        });

        Assert.Equal(404, response.Status);

        var body = await response.TextAsync();
        Assert.Contains(undeclared, body, StringComparison.Ordinal);

        // AND NOTHING RAN. Give the engine the same window the positive case is
        // allowed, so "nothing started" cannot mean "not yet".
        await Task.Delay(3_000);
        Assert.Equal(0, await InstanceCountAsync(key));
    }

    /// <summary>
    /// Wake-all, not wake-one (#523).
    /// </summary>
    /// <remarks>
    /// One waiter advancing is the single-waiter case passing by accident. The
    /// owner's decision was to wake all, matching the bus path, so that one
    /// signal does not behave two ways depending on how it arrived.
    /// </remarks>
    [Fact]
    public async Task Every_instance_waiting_on_the_name_advances()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var api = session.Page.APIRequest;

        var key = $"sig{Guid.NewGuid():N}"[..20];
        var signal = $"s{Guid.NewGuid():N}"[..18];
        await PublishAsync(api, key, WaitsForSignal(key, signal));

        var first = await StartAsync(api, key);
        var second = await StartAsync(api, key);

        foreach (var instance in new[] { first, second })
        {
            await EventuallyAsync(
                async () => (await TaskNamesAsync(api, instance)).Contains("Waiting") ? 1 : 0,
                n => n == 1, $"instance {instance} to park on its catch");
        }

        var response = await api.PostAsync("/api/workflow-signals/", new APIRequestContextOptions
        {
            DataObject = new { signalName = signal }
        });
        Assert.True(response.Ok, $"Broadcast failed: {response.Status} {await response.TextAsync()}");

        foreach (var instance in new[] { first, second })
        {
            await EventuallyAsync(
                async () => (await TaskNamesAsync(api, instance)).Contains("Woken") ? 1 : 0,
                n => n == 1,
                $"instance {instance} to advance past its catch -- one waking and not the other "
                + "is wake-one wearing wake-all's name");
        }
    }

    /// <summary>
    /// An instance-scoped catch is not this caller's audience (#243, #523).
    /// </summary>
    /// <remarks>
    /// <para>
    /// #243 established that an external signal arrives from OUTSIDE every
    /// instance, so an instance-scoped catch — one that exists to be raised from
    /// within its own run — is not its audience. Waking one from outside is the
    /// cross-instance leak #156 exists to prevent.
    /// </para>
    /// <para>
    /// The bus path enforces that with an explicit <c>IsSignalGlobalAsync</c>
    /// filter, because it signals executions DIRECTLY and that bypasses the
    /// engine's own scope enforcement. This route does not need the filter: it
    /// broadcasts, and Flowable will not deliver a global broadcast to an
    /// instance-scoped subscription. <c>SignalScopeExecutionTests</c> states the
    /// same mechanism from the other side — scope is "enforced by Flowable's own
    /// flowable:scope rather than by filtering a broadcast here".
    /// </para>
    /// <para>
    /// That is a claim about the engine, so it is asserted rather than assumed.
    /// Without this test, the two paths could diverge on exactly the axis #243
    /// was filed about and nothing would say so.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task An_instance_scoped_catch_is_not_woken_by_a_broadcast()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var api = session.Page.APIRequest;

        var key = $"sig{Guid.NewGuid():N}"[..20];
        var signal = $"s{Guid.NewGuid():N}"[..18];
        await PublishAsync(api, key, WaitsForSignal(key, signal, instanceScoped: true));

        var instance = await StartAsync(api, key);
        await EventuallyAsync(
            async () => (await TaskNamesAsync(api, instance)).Contains("Waiting") ? 1 : 0,
            n => n == 1, $"instance {instance} to park on its instance-scoped catch");

        // The broadcast is accepted -- the workflow DOES declare a catch for the
        // name, so refusing it would be wrong. What must not happen is the wake.
        var response = await api.PostAsync("/api/workflow-signals/", new APIRequestContextOptions
        {
            DataObject = new { signalName = signal }
        });
        Assert.True(response.Ok, $"Broadcast failed: {response.Status} {await response.TextAsync()}");

        // The same window the positive fan-out case is allowed, so "did not wake"
        // cannot mean "not yet".
        await Task.Delay(5_000);

        var names = await TaskNamesAsync(api, instance);
        Assert.DoesNotContain("Woken", names);
        Assert.Contains("Waiting", names);
    }

    // ---- diagrams ------------------------------------------------------------

    private static string StartedBySignal(string key, string signalName) => Wrap(key, signalName,
        """<bpmn:startEvent id="s"><bpmn:signalEventDefinition signalRef="Sig_1"/></bpmn:startEvent>"""
        + """<bpmn:userTask id="t" name="Started" /><bpmn:endEvent id="e" />"""
        + """<bpmn:sequenceFlow id="f1" sourceRef="s" targetRef="t" />"""
        + """<bpmn:sequenceFlow id="f2" sourceRef="t" targetRef="e" />""");

    private static string WaitsForSignal(string key, string signalName, bool instanceScoped = false) =>
        Wrap(key, signalName, instanceScoped,
        """<bpmn:startEvent id="s" /><bpmn:userTask id="t" name="Waiting" />"""
        + """<bpmn:boundaryEvent id="b" attachedToRef="t"><bpmn:signalEventDefinition signalRef="Sig_1"/></bpmn:boundaryEvent>"""
        + """<bpmn:userTask id="w" name="Woken" /><bpmn:endEvent id="e" /><bpmn:endEvent id="e2" />"""
        + """<bpmn:sequenceFlow id="f1" sourceRef="s" targetRef="t" />"""
        + """<bpmn:sequenceFlow id="f2" sourceRef="t" targetRef="e" />"""
        + """<bpmn:sequenceFlow id="f3" sourceRef="b" targetRef="w" />"""
        + """<bpmn:sequenceFlow id="f4" sourceRef="w" targetRef="e2" />""");

    private static string Wrap(string key, string signalName, string body) =>
        Wrap(key, signalName, instanceScoped: false, body);

    private static string Wrap(string key, string signalName, bool instanceScoped, string body) => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          xmlns:flowable="http://flowable.org/bpmn"
                          id="Definitions_1" targetNamespace="http://autonate.dev/workflows">
          <bpmn:signal id="Sig_1" name="{signalName}"{(instanceScoped ? " flowable:scope=\"processInstance\"" : "")} />
          <bpmn:process id="{key}" name="Signals" isExecutable="true">
            {body}
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

    // ---- helpers -------------------------------------------------------------

    /// <summary>How many instances of this process key the engine knows about.</summary>
    /// <remarks>
    /// Asked of Flowable's history directly, so a run that started and finished is
    /// still counted. "No instance" is the load-bearing half of the refusal test,
    /// and a runtime-only query would report it for an instance that simply ended.
    /// </remarks>
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

    private static async Task<int> EventuallyAsync(
        Func<Task<int>> read, Func<int, bool> until, string what)
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

    private static async Task<List<string>> TaskNamesAsync(IAPIRequestContext api, string instanceId)
    {
        var response = await api.GetAsync($"/api/executions/{instanceId}/tasks");
        if (!response.Ok) return [];
        using var document = JsonDocument.Parse(await response.TextAsync());
        return document.RootElement.EnumerateArray()
            .Select(element => element.GetProperty("name").GetString() ?? "")
            .ToList();
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

    private static async Task<string> StartAsync(IAPIRequestContext api, string key)
    {
        var response = await api.PostAsync($"/api/workflows/{key}/start", new APIRequestContextOptions
        {
            DataObject = new { }
        });
        Assert.True(response.Ok, $"Starting failed: {response.Status} {await response.TextAsync()}");
        using var document = JsonDocument.Parse(await response.TextAsync());
        return document.RootElement.GetProperty("id").GetString()!;
    }
}
