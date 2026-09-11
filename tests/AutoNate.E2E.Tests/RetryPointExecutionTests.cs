using System.Text.Json;
using AutoNate.E2E.Tests.Support;
using Microsoft.Playwright;
using Xunit;

namespace AutoNate.E2E.Tests;

/// <summary>
/// Marking a step as an independent retry point changes where the workflow
/// checkpoints (#168).
/// </summary>
/// <remarks>
/// The attribute serialising correctly proves nothing about behaviour, so these
/// assert the behaviour and they assert it BOTH ways. The two processes below are
/// identical except for flowable:async on the failing step; the difference between
/// the two runs is the entire feature, and a test that only exercised the marked
/// case would pass against an attribute the engine ignores.
///
/// Verified against Flowable 8.0.0 before this was written: unmarked, a failing step
/// rolls its transaction back to the previous checkpoint and the work in front of it
/// is undone; marked, that work stays committed and only the failing step is retried.
/// </remarks>
[Trait("RequiresService", "Flowable")]
[Collection(AutoNateE2ECollection.Name)]
public sealed class RetryPointExecutionTests : E2ETestBase
{
    public RetryPointExecutionTests(AutoNateE2EFixture fixture) : base(fixture) { }

    // A behaviour key nothing registers. The executor rejects it, which is the
    // realistic failure this feature exists for — an external call that does not
    // work — without needing a fault-injection hook in production code.
    private const string FailingBehaviorKey = "autonate.no-such-behavior-for-tests";

    [Fact]
    public async Task Without_a_retry_point_the_work_before_the_failing_step_is_undone()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var api = session.Page.APIRequest;

        var key = $"rp_off_{Guid.NewGuid():N}"[..24];
        await PublishAsync(api, key, ProcessXml(key, retryPoint: false));

        var instanceId = await StartAsync(api, key);
        Assert.Contains("Step one", await TaskNamesAsync(api, instanceId));

        // Completing the user task runs the failing service task in the SAME
        // transaction, so the failure takes the completion down with it.
        var taskId = await TaskIdAsync(api, instanceId, "Step one");
        var completed = await api.PostAsync(
            $"/api/tasks/{taskId}/complete",
            new APIRequestContextOptions { DataObject = new { } });

        Assert.False(
            completed.Ok,
            "Completing the task in front of an unmarked failing step should have failed with it. " +
            $"It returned {completed.Status}.");

        // The half that matters, and the half a naive test omits: the completed
        // work is BACK. That is what "the whole preceding stretch is redone"
        // means, and it is the thing the retry point buys you out of.
        Assert.Contains("Step one", await TaskNamesAsync(api, instanceId));
    }

    [Fact]
    public async Task With_a_retry_point_the_work_before_the_failing_step_stands()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var api = session.Page.APIRequest;

        var key = $"rp_on_{Guid.NewGuid():N}"[..24];
        await PublishAsync(api, key, ProcessXml(key, retryPoint: true));

        var instanceId = await StartAsync(api, key);
        Assert.Contains("Step one", await TaskNamesAsync(api, instanceId));

        var taskId = await TaskIdAsync(api, instanceId, "Step one");
        var completed = await api.PostAsync(
            $"/api/tasks/{taskId}/complete",
            new APIRequestContextOptions { DataObject = new { } });

        Assert.True(
            completed.Ok,
            "The retry point should have committed the completion before the failing step ran. " +
            $"Completing returned {completed.Status}: {await completed.TextAsync()}");

        // The completion is durable: the engine commits at the retry point, so
        // the failure that follows cannot take it back.
        await EventuallyAsync(api, instanceId,
            names => !names.Contains("Step one"),
            "the completed task to stay completed");

        // The completion is on the record, not merely absent from the task list —
        // history is where "the work before the retry point was not redone" is
        // actually visible, and it survives the failing step.
        var history = await api.GetAsync($"/api/executions/{instanceId}/history");
        Assert.True(history.Ok, $"Reading history failed: {history.Status} {await history.TextAsync()}");
        Assert.Contains("Step one", await history.TextAsync());

        // AC: the exhausted step lands in the dead-letter state rather than
        // disappearing. Asserted against the engine, because that is where the
        // state lives — the operator-facing job list is M5's story, and this
        // story's obligation is only that the job is produced.
        //
        // Note: /management/deadletter-jobs ignores a processInstanceId query
        // parameter and returns everything, so the filter is applied here. A test
        // that trusted that parameter would be asserting over every other test's
        // leftovers.
        var deadLettered = await EventuallyDeadLetteredAsync(instanceId, "boom");
        Assert.Equal(0, deadLettered.Retries);
        Assert.False(
            string.IsNullOrWhiteSpace(deadLettered.ExceptionMessage),
            "A dead-lettered job should carry the exception that exhausted it, or the failure is invisible.");
    }

    [Fact]
    public async Task A_retry_point_changes_nothing_when_the_step_succeeds()
    {
        // "Marking a step does not change the behaviour of a process that never
        // fails, asserted so the setting is provably free in the happy path."
        await using var session = await NewSignedInAsAdminAsync();
        var api = session.Page.APIRequest;

        var marked = $"rp_hpy_{Guid.NewGuid():N}"[..24];
        var plain = $"rp_hpn_{Guid.NewGuid():N}"[..24];
        await PublishAsync(api, marked, HappyPathXml(marked, retryPoint: true));
        await PublishAsync(api, plain, HappyPathXml(plain, retryPoint: false));

        var markedInstance = await StartAsync(api, marked);
        var plainInstance = await StartAsync(api, plain);

        // Both have to be walked forward; "Step two" is only reachable once
        // "Step one" is completed.
        foreach (var instanceId in new[] { markedInstance, plainInstance })
        {
            var taskId = await TaskIdAsync(api, instanceId, "Step one");
            var completed = await api.PostAsync($"/api/tasks/{taskId}/complete",
                new APIRequestContextOptions { DataObject = new { } });
            Assert.True(completed.Ok,
                $"Completing step one failed: {completed.Status} {await completed.TextAsync()}");
        }

        // Same reachable state from both, which is what "provably free" means:
        // the checkpoint changes when work is committed, never what work runs.
        var markedNames = await EventuallyAsync(api, markedInstance,
            names => names.Contains("Step two"), "the marked process to reach step two");
        var plainNames = await EventuallyAsync(api, plainInstance,
            names => names.Contains("Step two"), "the unmarked process to reach step two");

        Assert.Equal(plainNames.OrderBy(n => n), markedNames.OrderBy(n => n));

        // #319. The assertion above cannot fail on its own: both lists can only ever
        // be ["Step two"], so it passes whether the attribute did anything or
        // nothing. Combined with the fixture marking a userTask -- an element the
        // retry-point control is never offered on and the backend never dispatches
        // for -- it asserted "provably free" for something that was never applied.
        //
        // So: assert the mark REACHED the engine. A marked async step becomes a job;
        // an unmarked one does not. If the attribute is inert the counts are equal
        // and this fails, which is the whole claim.
        // The mark reached the ENGINE. Asserted against the deployed resource rather
        // than by counting jobs: a succeeding async step completes before any poll
        // can see its job, so a job count is zero for both and proves nothing.
        var markedXml = await DeployedXmlAsync(markedInstance);
        var plainXml = await DeployedXmlAsync(plainInstance);

        Assert.Contains("flowable:async=\"true\"", markedXml, StringComparison.Ordinal);
        Assert.DoesNotContain("flowable:async=\"true\"", plainXml, StringComparison.Ordinal);
    }

    /// <summary>The BPMN Flowable is actually running for this instance.</summary>
    /// <remarks>
    /// #319. The equality assertion above cannot fail on its own — both task lists
    /// can only ever be ["Step two"] — so it passes whether the mark did anything
    /// or nothing. Reading the deployed resource is what makes "provably free"
    /// a claim about a setting that was actually applied.
    /// </remarks>
    private static async Task<string> DeployedXmlAsync(string processInstanceId)
    {
        using var client = Support.FlowableDeploymentSweep.CreateClient(
            Environment.GetEnvironmentVariable("AUTONATE_FLOWABLE_URL") ?? "http://localhost:8080/flowable-rest",
            Environment.GetEnvironmentVariable("AUTONATE_FLOWABLE_USER") ?? "rest-admin",
            Environment.GetEnvironmentVariable("AUTONATE_FLOWABLE_PASSWORD") ?? "test");

        var instance = await client.GetStringAsync(
            $"service/history/historic-process-instances/{Uri.EscapeDataString(processInstanceId)}");
        using var instanceDocument = JsonDocument.Parse(instance);
        var definitionId = instanceDocument.RootElement.GetProperty("processDefinitionId").GetString()!;

        return await client.GetStringAsync(
            $"service/repository/process-definitions/{Uri.EscapeDataString(definitionId)}/resourcedata");
    }

    // start -> "Step one" (user task) -> failing service task -> end.
    //
    // The user task in front is the instrument: whether its completion survives
    // the failure is exactly what the retry point decides.
    private static string ProcessXml(string key, bool retryPoint)
    {
        var async = retryPoint ? " flowable:async=\"true\"" : string.Empty;
        return $$"""
            <?xml version="1.0" encoding="UTF-8"?>
            <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                              xmlns:flowable="http://flowable.org/bpmn"
                              id="Definitions_1" targetNamespace="http://autonate.dev/workflows">
              <bpmn:process id="{{key}}" name="Retry Point" isExecutable="true">
                <bpmn:startEvent id="s" />
                <bpmn:sequenceFlow id="f0" sourceRef="s" targetRef="one" />
                <bpmn:userTask id="one" name="Step one" />
                <bpmn:sequenceFlow id="f1" sourceRef="one" targetRef="boom" />
                <bpmn:serviceTask id="boom" name="Failing step"
                                  flowable:delegateExpression="${autonateBehaviorDelegate}"
                                  flowable:autonateServiceKind="behavior"
                                  flowable:behaviorKey="{{FailingBehaviorKey}}"{{async}} />
                <bpmn:sequenceFlow id="f2" sourceRef="boom" targetRef="e" />
                <bpmn:endEvent id="e" />
              </bpmn:process>
              {{Di(key, "s", "one", "boom", "e")}}
            </bpmn:definitions>
            """;
    }

    // start -> "Step one" -> a SUCCEEDING service task -> "Step two" -> end.
    //
    // #319. The marked element is a serviceTask, not a userTask. The retry-point
    // control is offered only on `bpmn:ServiceTask` (WorkflowStudio.tsx) and the
    // backend dispatches RetryPoint only for `localName == "serviceTask"`
    // (WorkflowBpmnXml.cs), so a marked userTask is silently ignored. This fixture
    // used one, which meant "the setting is provably free in the happy path" was
    // asserted for an element an author cannot mark, with an assertion comparing
    // two lists that could only ever be equal.
    //
    // The service task succeeds (`${true}`), so the happy path is still a happy
    // path, and the mark now has an observable effect: async makes it a job.
    private static string HappyPathXml(string key, bool retryPoint)
    {
        var async = retryPoint ? " flowable:async=\"true\"" : string.Empty;
        return $$"""
            <?xml version="1.0" encoding="UTF-8"?>
            <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                              xmlns:flowable="http://flowable.org/bpmn"
                              id="Definitions_1" targetNamespace="http://autonate.dev/workflows">
              <bpmn:process id="{{key}}" name="Retry Point Happy" isExecutable="true">
                <bpmn:startEvent id="s" />
                <bpmn:sequenceFlow id="f0" sourceRef="s" targetRef="one" />
                <bpmn:userTask id="one" name="Step one" />
                <bpmn:sequenceFlow id="f1" sourceRef="one" targetRef="mark" />
                <bpmn:serviceTask id="mark" name="Marked step"
                                  flowable:expression="${true}"{{async}} />
                <bpmn:sequenceFlow id="fm" sourceRef="mark" targetRef="two" />
                <bpmn:userTask id="two" name="Step two" />
                <bpmn:sequenceFlow id="f2" sourceRef="two" targetRef="e" />
                <bpmn:endEvent id="e" />
              </bpmn:process>
              {{Di(key, "s", "one", "mark", "two", "e")}}
            </bpmn:definitions>
            """;
    }

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
        // #214: computed once — a second call would rename the model between
        // create and publish and leak the deployment past the sweep.
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

    private static async Task<List<string>> TaskNamesAsync(IAPIRequestContext api, string instanceId)
    {
        var response = await api.GetAsync($"/api/executions/{instanceId}/tasks");
        Assert.True(response.Ok, $"Reading tasks failed: {response.Status} {await response.TextAsync()}");
        using var document = JsonDocument.Parse(await response.TextAsync());
        return document.RootElement.EnumerateArray()
            .Select(element => element.GetProperty("name").GetString()!)
            .ToList();
    }

    private static async Task<string> TaskIdAsync(IAPIRequestContext api, string instanceId, string name)
    {
        var response = await api.GetAsync($"/api/executions/{instanceId}/tasks");
        using var document = JsonDocument.Parse(await response.TextAsync());
        return document.RootElement.EnumerateArray()
            .First(element => element.GetProperty("name").GetString() == name)
            .GetProperty("id").GetString()!;
    }

    private sealed record DeadLetterJob(int Retries, string? ExceptionMessage);

    /// <summary>
    /// Polls Flowable for a dead-lettered job on <paramref name="elementId"/> in
    /// this instance. Retries are exhausted in the background, so this waits
    /// rather than sampling once.
    /// </summary>
    private static async Task<DeadLetterJob> EventuallyDeadLetteredAsync(
        string processInstanceId, string elementId)
    {
        using var client = Support.FlowableDeploymentSweep.CreateClient(
            Environment.GetEnvironmentVariable("AUTONATE_FLOWABLE_URL") ?? "http://localhost:8080/flowable-rest",
            Environment.GetEnvironmentVariable("AUTONATE_FLOWABLE_USER") ?? "rest-admin",
            Environment.GetEnvironmentVariable("AUTONATE_FLOWABLE_PASSWORD") ?? "test");

        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < deadline)
        {
            var body = await client.GetStringAsync("service/management/deadletter-jobs?size=200");
            using var document = JsonDocument.Parse(body);
            foreach (var row in document.RootElement.GetProperty("data").EnumerateArray())
            {
                if (row.TryGetProperty("processInstanceId", out var owner)
                    && owner.GetString() == processInstanceId
                    && row.TryGetProperty("elementId", out var element)
                    && element.GetString() == elementId)
                {
                    return new DeadLetterJob(
                        row.GetProperty("retries").GetInt32(),
                        row.TryGetProperty("exceptionMessage", out var message)
                            ? message.GetString()
                            : null);
                }
            }

            await Task.Delay(1000);
        }

        Assert.Fail(
            $"Timed out after 60s waiting for '{elementId}' to be dead-lettered in instance {processInstanceId}. " +
            "Without a retry point the step never becomes a job at all, so this is the assertion that " +
            "distinguishes the feature from a no-op attribute.");
        return new DeadLetterJob(-1, null);
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
