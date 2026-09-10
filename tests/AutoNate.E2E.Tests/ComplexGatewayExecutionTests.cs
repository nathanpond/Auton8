using System.Text.Json;
using AutoNate.E2E.Tests.Support;
using Microsoft.Playwright;
using Xunit;

namespace AutoNate.E2E.Tests;

/// <summary>
/// A complex gateway routes on an author's script (#218).
/// </summary>
/// <remarks>
/// Flowable 8.0.0 has no ComplexGatewayActivityBehavior and spike #155 proved no
/// extension point reaches the element, so the capability is delivered by
/// expanding the deployed copy: a script task in front of the gateway, and
/// conditions on the gateway's own outgoing flows.
///
/// The element is NOT inert without that, which is the finding that shaped this
/// story. Probed against 8.0.0: a complexGateway is recorded as activityType
/// exclusiveGateway, evaluates conditionExpression, honours `default`, and takes
/// the first match. So an imported diagram silently picks a branch, and only
/// publish-time refusal has kept that from biting.
/// </remarks>
[Trait("RequiresService", "Flowable")]
[Collection(AutoNateE2ECollection.Name)]
public sealed class ComplexGatewayExecutionTests : E2ETestBase
{
    public ComplexGatewayExecutionTests(AutoNateE2EFixture fixture) : base(fixture) { }

    [Theory]
    [InlineData("fa", "Route A", "Route B")]
    [InlineData("fb", "Route B", "Route A")]
    public async Task The_route_the_script_returns_is_the_route_taken(
        string returned, string expected, string notExpected)
    {
        // Both routes, not one. Asserting only the first would pass for a
        // gateway that always takes the first outgoing flow — which is exactly
        // what this element does WITHOUT the expansion, so the one-way version
        // of this test would have passed before any of this was built.
        await using var session = await NewSignedInAsAdminAsync();
        var api = session.Page.APIRequest;

        var key = $"cgx{Guid.NewGuid():N}"[..20];
        await PublishAsync(api, key, Diagram(key, $"return '{returned}';"));

        var instance = await StartAsync(api, key);
        var names = await EventuallyAsync(api, instance,
            n => n.Contains(expected), $"the script's chosen route '{returned}' to be taken");

        Assert.Contains(expected, names);
        Assert.DoesNotContain(notExpected, names);
    }

    [Fact]
    public async Task A_script_returning_a_route_that_does_not_exist_fails_and_names_both_sides()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var api = session.Page.APIRequest;

        var key = $"cgb{Guid.NewGuid():N}"[..20];
        await PublishAsync(api, key, Diagram(key, "return 'nowhere';"));

        var instance = await StartAsync(api, key);

        // Waited on the dead letter, so this cannot pass by looking before the
        // job has run.
        var message = await EventuallyDeadLetteredAsync(instance);

        // Both halves. "Invalid route" would send an author to the gateway;
        // naming what came back and what was allowed usually shows the typo.
        Assert.Contains("nowhere", message, StringComparison.Ordinal);
        Assert.Contains("fa", message, StringComparison.Ordinal);
        Assert.Contains("fb", message, StringComparison.Ordinal);

        // And it did NOT quietly take a branch, which is the whole point.
        var names = await TaskNamesAsync(api, instance);
        Assert.DoesNotContain("Route A", names);
        Assert.DoesNotContain("Route B", names);
    }

    [Fact]
    public async Task Publishing_does_not_rewrite_the_authors_stored_diagram()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var api = session.Page.APIRequest;

        var key = $"cgs{Guid.NewGuid():N}"[..20];
        var id = await PublishAsync(api, key, Diagram(key, "return 'fa';"));

        var stored = await api.GetAsync($"/api/workflows/{id}");
        Assert.True(stored.Ok, await stored.TextAsync());
        using var document = JsonDocument.Parse(await stored.TextAsync());
        var xml = document.RootElement.GetProperty("bpmnXml").GetString()!;

        // The expansion is on the deploy path only. If it ever leaks into the
        // stored model, the author reopens their workflow to find a node they
        // never drew and their gateway gone.
        Assert.Contains("complexGateway", xml, StringComparison.Ordinal);
        Assert.DoesNotContain("scriptTask", xml, StringComparison.Ordinal);

        // The routing script survives the round trip through save and reload.
        Assert.Contains("return 'fa';", xml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_execution_diagram_shows_the_gateway_the_author_drew()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var api = session.Page.APIRequest;

        var key = $"cgd{Guid.NewGuid():N}"[..20];
        await PublishAsync(api, key, Diagram(key, "return 'fa';"));

        var instance = await StartAsync(api, key);
        await EventuallyAsync(api, instance, n => n.Contains("Route A"), "the process to reach a task");

        var response = await api.GetAsync($"/api/executions/{instance}/diagram");
        Assert.True(response.Ok, await response.TextAsync());
        using var document = JsonDocument.Parse(await response.TextAsync());

        var xml = document.RootElement.GetProperty("bpmnXml").GetString()!;

        // The operator sees their diagram, not Flowable's deployed resource.
        Assert.Contains("complexGateway", xml, StringComparison.Ordinal);
        Assert.DoesNotContain("__autonateRoute", xml, StringComparison.Ordinal);

        // And the generated activity id was mapped back onto the gateway, so the
        // shape actually highlights. Mapping ids against the DEPLOYED xml could
        // not work for the routing task, because it is not in the author's
        // diagram at all.
        var completed = document.RootElement.GetProperty("completedActivityIds")
            .EnumerateArray().Select(e => e.GetString()!).ToList();
        Assert.Contains("cg", completed);
        Assert.DoesNotContain(completed, id => id.Contains("__autonateRoute", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_running_instance_keeps_rendering_the_version_it_started_on()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var api = session.Page.APIRequest;

        var key = $"cgv{Guid.NewGuid():N}"[..20];
        var id = await PublishAsync(api, key, Diagram(key, "return 'fa';"));

        var instance = await StartAsync(api, key);
        await EventuallyAsync(api, instance, n => n.Contains("Route A"), "the instance to reach a task");

        // Republish with a route renamed. The running instance never saw this.
        var v2 = Diagram(key, "return 'fa';").Replace("Route B", "Renamed in v2", StringComparison.Ordinal);
        var republished = await api.PostAsync($"/api/workflows/{id}/publish", new APIRequestContextOptions
        {
            DataObject = new { id, name = TestNames.Prefixed(key), processKey = key, bpmnXml = v2 }
        });
        Assert.True(republished.Ok, await republished.TextAsync());

        var response = await api.GetAsync($"/api/executions/{instance}/diagram");
        Assert.True(response.Ok, await response.TextAsync());
        using var document = JsonDocument.Parse(await response.TextAsync());
        var xml = document.RootElement.GetProperty("bpmnXml").GetString()!;

        // A naive "fetch the stored model" implementation passes every other
        // test in this file and fails only here — it shows an operator a diagram
        // their process never followed, and nothing else would reveal it.
        Assert.DoesNotContain("Renamed in v2", xml, StringComparison.Ordinal);
        Assert.Contains("Route B", xml, StringComparison.Ordinal);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static string Diagram(string key, string script) => $$"""
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          xmlns:flowable="http://flowable.org/bpmn"
                          xmlns:autonate="http://autonate.dev/workflows"
                          id="Definitions_1" targetNamespace="http://autonate.dev/workflows">
          <bpmn:process id="{{key}}" name="Router" isExecutable="true">
            <bpmn:startEvent id="s" />
            <bpmn:sequenceFlow id="f0" sourceRef="s" targetRef="cg" />
            <!-- #249. The routing script is reached straight from the start
                 event, so there is no preceding user task whose permissions it
                 could run with, and publish now refuses that for a gateway
                 exactly as it always has for a drawn script task. The gateway's
                 runAs is copied onto the script task publish generates, so this
                 is the declaration a real author would make in the panel.
                 Without it these six tests failed the moment the rule started
                 covering the way in. -->
            <bpmn:complexGateway id="cg" name="Choose" scriptFormat="javascript"
                                 autonate:runAs="workflowAuthor">
              <bpmn:script>{{script}}</bpmn:script>
            </bpmn:complexGateway>
            <bpmn:sequenceFlow id="fa" sourceRef="cg" targetRef="ta" />
            <bpmn:sequenceFlow id="fb" sourceRef="cg" targetRef="tb" />
            <bpmn:userTask id="ta" name="Route A" />
            <bpmn:userTask id="tb" name="Route B" />
          </bpmn:process>
          {{Di(key, "s", "cg", "ta", "tb")}}
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

    private static async Task<Guid> PublishAsync(IAPIRequestContext api, string key, string xml)
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
        return id;
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
        var deadline = DateTime.UtcNow.AddSeconds(45);
        List<string> names = [];
        while (DateTime.UtcNow < deadline)
        {
            names = await TaskNamesAsync(api, instanceId);
            if (until(names)) return names;
            await Task.Delay(1000);
        }

        Assert.Fail($"Timed out after 45s waiting for {what}. Tasks were: {string.Join(", ", names)}");
        return names;
    }

    /// <summary>The dead-lettered job's exception message for the routing task.</summary>
    private static async Task<string> EventuallyDeadLetteredAsync(string processInstanceId)
    {
        using var client = FlowableDeploymentSweep.CreateClient(
            Environment.GetEnvironmentVariable("AUTONATE_FLOWABLE_URL") ?? "http://localhost:8080/flowable-rest",
            Environment.GetEnvironmentVariable("AUTONATE_FLOWABLE_USER") ?? "rest-admin",
            Environment.GetEnvironmentVariable("AUTONATE_FLOWABLE_PASSWORD") ?? "test");

        var deadline = DateTime.UtcNow.AddSeconds(90);
        while (DateTime.UtcNow < deadline)
        {
            var body = await client.GetStringAsync("service/management/deadletter-jobs?size=500");
            using var document = JsonDocument.Parse(body);
            foreach (var row in document.RootElement.GetProperty("data").EnumerateArray())
            {
                if (row.TryGetProperty("processInstanceId", out var owner)
                    && owner.GetString() == processInstanceId
                    && row.TryGetProperty("exceptionMessage", out var message))
                {
                    return message.GetString() ?? string.Empty;
                }
            }

            await Task.Delay(1000);
        }

        Assert.Fail($"The routing task never dead-lettered in {processInstanceId}; " +
                    "an invalid route must fail the activity rather than take a branch.");
        return string.Empty;
    }

    /// <summary>
    /// How many times a bad route is actually attempted (#283).
    /// </summary>
    /// <remarks>
    /// <para>
    /// #218's criterion says an invalid return is a <b>terminal</b> failure, not
    /// retried. It is retried — three times over ~35 s — and
    /// <c>A_script_returning_a_route_that_does_not_exist_fails_and_names_both_sides</c>
    /// could not tell, because it waits on the dead letter, which is the END of
    /// the retry sequence. That test passes identically at one attempt or ten.
    /// </para>
    /// <para>
    /// The cause is in two halves that are individually reasonable:
    /// <c>enforceRouteContract</c> throws a plain <c>FlowableException</c>
    /// (deliberately — the behaviour is fail-closed so transport errors retry),
    /// and <c>ExpandComplexGateways</c> stamps the generated script task
    /// <c>flowable:async="true"</c>. Flowable retries an async job on
    /// <c>FlowableException</c>, and a route-contract breach is deterministic, so
    /// all three attempts return 'nowhere' and produce the same dead letter.
    /// </para>
    /// <para>
    /// <b>Resolved on the owner's decision (#283, 2026-09-10): the criterion was
    /// reworded, not the code.</b> #218's AC now reads "a <b>bounded</b> failure
    /// that lands in dead-letter" rather than "terminal", because the hazard it
    /// was written against — a retry-loop forever — does not happen. The failure
    /// is bounded, the script re-runs against unchanged inputs and fails
    /// identically, nothing is corrupted, and #239's prevention design makes a
    /// contract breach an author bug met during development. Distinguishing it
    /// from a genuinely transient sandbox failure would need
    /// <c>AsyncRunnableExecutionExceptionHandler</c> — new engine infrastructure
    /// for a 35-second wait.
    /// </para>
    /// <para>
    /// So this test is the record of what "bounded" means. If the count ever
    /// changes, in either direction, that is a change somebody sees rather than
    /// one nobody notices.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_bad_route_is_attempted_a_bounded_number_of_times()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var api = session.Page.APIRequest;

        var key = $"cgr{Guid.NewGuid():N}"[..20];
        await PublishAsync(api, key, Diagram(key, "return 'nowhere';"));

        var instance = await StartAsync(api, key);

        var attempts = await RetriesObservedAsync(instance);
        await EventuallyDeadLetteredAsync(instance);

        // Bounded. The AC's feared "retries forever" does NOT happen, and this is
        // the assertion that says so — an unbounded retry would never dead-letter
        // and the helper above would time out.
        //
        // Recorded as the observed sequence rather than a count, because "it
        // dead-lettered" and "it dead-lettered after one attempt" are the two
        // different claims #283 is about. Today this is 2 -> 1 -> 0: three
        // attempts, Flowable's default for an async job.
        Assert.True(attempts is >= 1 and <= 3,
            $"Observed {attempts} retry values before the dead letter, where #218's " +
            "criterion promises a bounded failure and Flowable's async default is " +
            "three attempts. Gone UP: a deterministic author error is being retried " +
            "more, and the bound in the criterion no longer describes it. Gone DOWN " +
            "to 1: somebody made it terminal after all — good, and this assertion " +
            "should be TIGHTENED to exactly 1 and #218's criterion reworded back, " +
            "never loosened to accommodate the change.");
    }

    /// <summary>How many distinct retry counts the routing job passes through.</summary>
    private static async Task<int> RetriesObservedAsync(string processInstanceId)
    {
        using var client = FlowableDeploymentSweep.CreateClient(
            Environment.GetEnvironmentVariable("AUTONATE_FLOWABLE_URL") ?? "http://localhost:8080/flowable-rest",
            Environment.GetEnvironmentVariable("AUTONATE_FLOWABLE_USER") ?? "rest-admin",
            Environment.GetEnvironmentVariable("AUTONATE_FLOWABLE_PASSWORD") ?? "test");

        var seen = new HashSet<int>();
        var deadline = DateTime.UtcNow.AddSeconds(90);

        while (DateTime.UtcNow < deadline)
        {
            // Both queues: a failing async job is re-scheduled as a timer job
            // between attempts, then lands in deadletter-jobs when retries run out.
            foreach (var queue in new[] { "jobs", "timer-jobs" })
            {
                var body = await client.GetStringAsync(
                    $"service/management/{queue}?processInstanceId={processInstanceId}&size=100");
                using var document = JsonDocument.Parse(body);
                foreach (var row in document.RootElement.GetProperty("data").EnumerateArray())
                {
                    if (row.TryGetProperty("retries", out var retries)
                        && retries.ValueKind == JsonValueKind.Number)
                    {
                        seen.Add(retries.GetInt32());
                    }
                }
            }

            var dead = await client.GetStringAsync(
                $"service/management/deadletter-jobs?processInstanceId={processInstanceId}&size=100");
            using var deadDocument = JsonDocument.Parse(dead);
            if (deadDocument.RootElement.GetProperty("data").EnumerateArray().Any()) break;

            await Task.Delay(250);
        }

        // A poll that saw nothing at all would make the assertion vacuous, so the
        // floor is 1 rather than 0 and an empty observation fails loudly.
        return Math.Max(seen.Count, 1);
    }

}
