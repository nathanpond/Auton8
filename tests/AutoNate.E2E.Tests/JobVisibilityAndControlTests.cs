using System.Text.Json;
using AutoNate.E2E.Tests.Support;
using Microsoft.Playwright;
using Xunit;

namespace AutoNate.E2E.Tests;

/// <summary>
/// An operator can see what is stuck, and make it move (#172).
/// </summary>
/// <remarks>
/// <para>
/// Every assertion here is on the <b>effect</b>, not on the call returning.
/// Retrying a dead-lettered job is a <c>move</c> back to the executable queue, so
/// a 204 means "it is queued again" and nothing more — a test that stopped there
/// would pass against an endpoint that did nothing at all. What proves the retry
/// is the process advancing past the step that had exhausted its retries.
/// </para>
/// <para>
/// The same for reschedule: the timer firing at the new time is the feature, and
/// a successful call against an unchanged due date looks identical.
/// </para>
/// </remarks>
[Trait("RequiresService", "Flowable")]
[Collection(AutoNateE2ECollection.Name)]
public sealed class JobVisibilityAndControlTests : E2ETestBase
{
    public JobVisibilityAndControlTests(AutoNateE2EFixture fixture) : base(fixture) { }

    /// <summary>A behaviour key nothing registers: a realistic external failure.</summary>
    private const string FailingBehaviorKey = "autonate.no-such-behavior-for-tests";

    [Fact]
    public async Task A_dead_lettered_job_is_listed_with_the_failure_that_put_it_there()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var api = session.Page.APIRequest;

        var key = $"job_dl_{Guid.NewGuid():N}"[..22];
        await PublishAsync(api, key, FailingProcess(key));
        var instanceId = await StartAsync(api, key);

        await CompleteFirstTaskAsync(api, instanceId, "Step one");

        var job = await EventuallyJobAsync(api, instanceId,
            j => j.Queue == "DeadLetter",
            "the failing step to exhaust its retries");

        // "An operator who cannot see why it failed cannot decide whether
        // retrying is sensible" -- the AC's words, and the reason these three are
        // asserted rather than just the row existing.
        Assert.Equal(0, job.Retries);
        Assert.Equal("boom", job.ElementId);
        Assert.False(
            string.IsNullOrWhiteSpace(job.ExceptionMessage),
            "A dead-lettered job with no exception message tells an operator nothing.");

        // And the stack, on demand. Null is a legitimate answer for a job the
        // engine kept no stack for, so this asserts it is reachable and shaped
        // right rather than that it is non-empty.
        var exception = await api.GetAsync(
            $"/api/executions/{instanceId}/jobs/{job.Id}/exception?queue=deadletter");
        Assert.True(exception.Ok, $"Reading the stack failed: {exception.Status} {await exception.TextAsync()}");
        using var stackBody = JsonDocument.Parse(await exception.TextAsync());
        Assert.Equal(job.Id, stackBody.RootElement.GetProperty("jobId").GetString());
    }

    [Fact]
    public async Task Retrying_a_dead_lettered_job_advances_the_process_past_it()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var api = session.Page.APIRequest;

        // The failing step is a script task reading a variable, not an
        // unregistered behaviour: this test has to be able to FIX the cause
        // between the failure and the retry, and "register a behaviour
        // mid-test" is not something an operator can do either. Setting the
        // variable is.
        var key = $"job_rt_{Guid.NewGuid():N}"[..22];
        await PublishAsync(api, key, FixableFailingProcess(key));
        var instanceId = await StartAsync(api, key);

        await CompleteFirstTaskAsync(api, instanceId, "Step one");

        var job = await EventuallyJobAsync(api, instanceId,
            j => j.Queue == "DeadLetter",
            "the failing step to exhaust its retries");

        // The process has NOT advanced. Asserted before the retry, so "Step two
        // exists afterwards" cannot be satisfied by it having been there all
        // along -- which is the way this test would otherwise pass against a
        // retry endpoint that did nothing.
        Assert.DoesNotContain("Step two", await TaskNamesAsync(api, instanceId));

        // Fix the cause, the way an operator would before deciding to retry.
        var fixedUp = await api.PutAsync(
            $"/api/executions/{instanceId}/variables",
            new APIRequestContextOptions
            {
                DataObject = new { variables = new[] { new { name = "ok", value = "yes", type = "string" } } }
            });
        Assert.True(fixedUp.Ok, $"Setting the variable failed: {fixedUp.Status} {await fixedUp.TextAsync()}");

        var retried = await api.PostAsync(
            $"/api/executions/{instanceId}/jobs/{job.Id}/retry",
            new APIRequestContextOptions { DataObject = new { queue = "deadletter" } });
        Assert.True(retried.Ok, $"Retry failed: {retried.Status} {await retried.TextAsync()}");

        // THE ASSERTION. Not that the retry returned 204 -- that means the job was
        // moved back to the executable queue, which an endpoint doing nothing
        // could also report.
        // THE ASSERTION, on the ENGINE'S history rather than the task list.
        //
        // Two reasons, and the second was measured. First, the AC says it: "the
        // process resumes from that step. Asserted on the process advancing, not
        // on the retry call returning" -- and a retry that returned 204 and did
        // nothing would leave `two` absent here.
        //
        // Second, the cached task list is not the right instrument. #592 serves
        // task reads from workflow_execution_cache, and in a probe of this exact
        // sequence it still listed the force-completed "Step one" while history
        // already showed `boom` errored and `two` OPEN. Asserting on the cache
        // would have made this test a measure of projection latency.
        await EventuallyActivityAsync(api, instanceId, "two",
            "the activity after the retried step to become active");
    }

    [Fact]
    public async Task Rescheduling_a_timer_makes_it_fire_at_the_new_time()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var api = session.Page.APIRequest;

        // Two hours out, so it cannot fire on its own within this test. If the
        // assertion below ever passes against an unchanged due date, the timer
        // would have had to wait two hours -- which is what makes this a test of
        // the reschedule rather than of the engine's clock.
        var key = $"job_ts_{Guid.NewGuid():N}"[..22];
        await PublishAsync(api, key, TimerProcess(key));
        var instanceId = await StartAsync(api, key);

        var job = await EventuallyJobAsync(api, instanceId,
            j => j.Queue == "Timer",
            "the timer job to be scheduled");

        var originalDue = job.DueAtUtc;
        Assert.NotNull(originalDue);
        Assert.True(
            originalDue > DateTimeOffset.UtcNow.AddMinutes(30),
            $"The timer should be hours out, not {originalDue}. "
            + "A near-term timer would make the assertion below meaningless.");

        var rescheduled = await api.PostAsync(
            $"/api/executions/{instanceId}/jobs/{job.Id}/reschedule",
            new APIRequestContextOptions
            {
                DataObject = new { dueAtUtc = DateTimeOffset.UtcNow.AddSeconds(2) }
            });
        Assert.True(rescheduled.Ok, $"Reschedule failed: {rescheduled.Status} {await rescheduled.TextAsync()}");

        // On history, for the same reason as the retry test: the engine's record
        // of what ran, not the cache's view of it.
        await EventuallyActivityAsync(api, instanceId, "after",
            "the rescheduled timer to fire and the next step to become active");
    }

    [Fact]
    public async Task The_cross_execution_list_shows_what_is_stuck()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var api = session.Page.APIRequest;

        var key = $"job_xl_{Guid.NewGuid():N}"[..22];
        await PublishAsync(api, key, FailingProcess(key));
        var instanceId = await StartAsync(api, key);
        await CompleteFirstTaskAsync(api, instanceId, "Step one");

        await EventuallyJobAsync(api, instanceId, j => j.Queue == "DeadLetter",
            "the failing step to exhaust its retries");

        // The stuck list defaults to dead-lettered only, because "what is stuck
        // right now" is the question -- a list that also carried every healthy
        // scheduled timer would bury it.
        var stuck = await api.GetAsync("/api/executions/jobs");
        Assert.True(stuck.Ok, $"Reading the stuck list failed: {stuck.Status} {await stuck.TextAsync()}");

        var rows = ParseJobs(await stuck.TextAsync());
        Assert.Contains(rows, j => j.ProcessInstanceId == instanceId && j.Queue == "DeadLetter");
        Assert.All(rows, j => Assert.Equal("DeadLetter", j.Queue));

        // The complement: asking for everything really does widen it, so the
        // default is a filter rather than the only thing the endpoint can do.
        var all = await api.GetAsync("/api/executions/jobs?all=true");
        Assert.True(all.Ok, $"Reading the full list failed: {all.Status} {await all.TextAsync()}");
        Assert.True(
            ParseJobs(await all.TextAsync()).Count >= rows.Count,
            "Asking for every queue returned fewer jobs than the dead-lettered filter.");
    }

    // ── fixtures ────────────────────────────────────────────────────────────

    /// <summary>start → "Step one" → a step that always fails → end.</summary>
    private static string FailingProcess(string key) => $$"""
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          xmlns:flowable="http://flowable.org/bpmn"
                          id="Definitions_1" targetNamespace="http://autonate.dev/workflows">
          <bpmn:process id="{{key}}" name="Stuck Job" isExecutable="true">
            <bpmn:startEvent id="s" />
            <bpmn:sequenceFlow id="f0" sourceRef="s" targetRef="one" />
            <bpmn:userTask id="one" name="Step one" />
            <bpmn:sequenceFlow id="f1" sourceRef="one" targetRef="boom" />
            <bpmn:serviceTask id="boom" name="Failing step"
                              flowable:delegateExpression="${autonateBehaviorDelegate}"
                              flowable:autonateServiceKind="behavior"
                              flowable:behaviorKey="{{FailingBehaviorKey}}"
                              flowable:async="true" />
            <bpmn:sequenceFlow id="f2" sourceRef="boom" targetRef="two" />
            <bpmn:userTask id="two" name="Step two" />
            <bpmn:sequenceFlow id="f3" sourceRef="two" targetRef="e" />
            <bpmn:endEvent id="e" />
          </bpmn:process>
          {{Di(key, "s", "one", "boom", "two", "e")}}
        </bpmn:definitions>
        """;

    /// <summary>
    /// The same shape, but the failure has a cause an operator can remove.
    /// </summary>
    /// <remarks>
    /// <c>${ok == 'yes'}</c> throws while <c>ok</c> is unset, and succeeds once it
    /// is. That is what lets the retry test fix the cause first — without it, a
    /// retry can only fail again, and "the process advanced" would be unassertable.
    /// </remarks>
    private static string FixableFailingProcess(string key) => $$"""
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          xmlns:flowable="http://flowable.org/bpmn"
                          id="Definitions_1" targetNamespace="http://autonate.dev/workflows">
          <bpmn:process id="{{key}}" name="Fixable Job" isExecutable="true">
            <bpmn:startEvent id="s" />
            <bpmn:sequenceFlow id="f0" sourceRef="s" targetRef="one" />
            <bpmn:userTask id="one" name="Step one" />
            <bpmn:sequenceFlow id="f1" sourceRef="one" targetRef="boom" />
            <bpmn:serviceTask id="boom" name="Needs a variable"
                              flowable:expression="${ok == 'yes'}"
                              flowable:async="true" />
            <bpmn:sequenceFlow id="f2" sourceRef="boom" targetRef="two" />
            <bpmn:userTask id="two" name="Step two" />
            <bpmn:sequenceFlow id="f3" sourceRef="two" targetRef="e" />
            <bpmn:endEvent id="e" />
          </bpmn:process>
          {{Di(key, "s", "one", "boom", "two", "e")}}
        </bpmn:definitions>
        """;

    /// <summary>start → a two-hour timer → "After the timer" → end.</summary>
    private static string TimerProcess(string key) => $$"""
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          id="Definitions_1" targetNamespace="http://autonate.dev/workflows">
          <bpmn:process id="{{key}}" name="Scheduled" isExecutable="true">
            <bpmn:startEvent id="s" />
            <bpmn:sequenceFlow id="f0" sourceRef="s" targetRef="wait" />
            <bpmn:intermediateCatchEvent id="wait" name="Wait">
              <bpmn:timerEventDefinition>
                <bpmn:timeDuration xsi:type="bpmn:tFormalExpression"
                                   xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">PT2H</bpmn:timeDuration>
              </bpmn:timerEventDefinition>
            </bpmn:intermediateCatchEvent>
            <bpmn:sequenceFlow id="f1" sourceRef="wait" targetRef="after" />
            <bpmn:userTask id="after" name="After the timer" />
            <bpmn:sequenceFlow id="f2" sourceRef="after" targetRef="e" />
            <bpmn:endEvent id="e" />
          </bpmn:process>
          {{Di(key, "s", "wait", "after", "e")}}
        </bpmn:definitions>
        """;

    // ── harness ─────────────────────────────────────────────────────────────

    private sealed record JobRow(
        string Id, string Queue, string? ProcessInstanceId, string? ElementId,
        int Retries, string? ExceptionMessage, DateTimeOffset? DueAtUtc);

    private static List<JobRow> ParseJobs(string body)
    {
        using var document = JsonDocument.Parse(body);
        return document.RootElement.EnumerateArray()
            .Select(e => new JobRow(
                e.GetProperty("id").GetString()!,
                e.GetProperty("queue").GetString()!,
                e.TryGetProperty("processInstanceId", out var pi) ? pi.GetString() : null,
                e.TryGetProperty("elementId", out var el) ? el.GetString() : null,
                e.GetProperty("retries").GetInt32(),
                e.TryGetProperty("exceptionMessage", out var msg) ? msg.GetString() : null,
                e.TryGetProperty("dueAtUtc", out var due) && due.ValueKind != JsonValueKind.Null
                    ? due.GetDateTimeOffset()
                    : null))
            .ToList();
    }

    private static async Task<JobRow> EventuallyJobAsync(
        IAPIRequestContext api, string instanceId, Func<JobRow, bool> until, string what)
    {
        var deadline = DateTime.UtcNow.AddSeconds(90);
        var seen = new List<JobRow>();
        while (DateTime.UtcNow < deadline)
        {
            var response = await api.GetAsync($"/api/executions/{instanceId}/jobs");
            Assert.True(response.Ok, $"Reading jobs failed: {response.Status} {await response.TextAsync()}");
            seen = ParseJobs(await response.TextAsync());
            var match = seen.FirstOrDefault(until);
            if (match is not null) return match;
            await Task.Delay(1000);
        }

        Assert.Fail($"Timed out after 90s waiting for {what}. Jobs were: "
            + (seen.Count == 0 ? "(none)" : string.Join(", ", seen.Select(j => $"{j.Queue}/{j.ElementId}"))));
        throw new InvalidOperationException("unreachable");
    }

    private static string Di(string processKey, params string[] elementIds)
    {
        var shapes = string.Join("\n", elementIds.Select((id, index) =>
            $"""
                  <bpmndi:BPMNShape id="Shape_{id}" bpmnElement="{id}">
                    <dc:Bounds x="{100 + (index * 160)}" y="100" width="120" height="90" />
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

    private static async Task<string> StartAsync(IAPIRequestContext api, string key)
    {
        var response = await api.PostAsync($"/api/workflows/{key}/start",
            new APIRequestContextOptions { DataObject = new { } });
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

    private static async Task CompleteFirstTaskAsync(
        IAPIRequestContext api, string instanceId, string name)
    {
        var response = await api.GetAsync($"/api/executions/{instanceId}/tasks");
        Assert.True(response.Ok, $"Reading tasks failed: {response.Status} {await response.TextAsync()}");
        using var document = JsonDocument.Parse(await response.TextAsync());
        var taskId = document.RootElement.EnumerateArray()
            .First(e => e.GetProperty("name").GetString() == name)
            .GetProperty("id").GetString()!;

        // force-complete, because the failing step runs in the completion's own
        // transaction on the unmarked path -- the async mark is what keeps the
        // completion from being rolled back with it, and force-complete is what
        // the admin surface offers anyway.
        var completed = await api.PostAsync(
            $"/api/executions/{instanceId}/tasks/{taskId}/force-complete",
            new APIRequestContextOptions { DataObject = new { } });
        Assert.True(completed.Ok,
            $"Completing '{name}' failed: {completed.Status} {await completed.TextAsync()}");
    }

    /// <summary>
    /// Waits for a BPMN activity to appear in the engine's own history.
    /// </summary>
    /// <remarks>
    /// The engine's record rather than the cached task list. Measured during this
    /// story: after a successful retry, history showed `boom` errored and `two`
    /// open while `/tasks` still listed the force-completed "Step one" -- so an
    /// assertion on the task list measures projection latency, not the feature.
    /// </remarks>
    private static async Task EventuallyActivityAsync(
        IAPIRequestContext api, string instanceId, string activityId, string what)
    {
        var deadline = DateTime.UtcNow.AddSeconds(90);
        var trail = "(none)";
        while (DateTime.UtcNow < deadline)
        {
            var response = await api.GetAsync($"/api/executions/{instanceId}/history");
            Assert.True(response.Ok,
                $"Reading history failed: {response.Status} {await response.TextAsync()}");

            using var document = JsonDocument.Parse(await response.TextAsync());
            var rows = document.RootElement.EnumerateArray().ToList();
            if (rows.Any(e => e.GetProperty("activityId").GetString() == activityId))
            {
                return;
            }

            trail = string.Join(" | ", rows.Select(e => e.GetProperty("activityId").GetString()));
            await Task.Delay(1000);
        }

        Assert.Fail($"Timed out after 90s waiting for {what}. History was: {trail}");
    }

    private static async Task<List<string>> EventuallyAsync(
        IAPIRequestContext api, string instanceId, Func<List<string>, bool> until, string what)
    {
        var deadline = DateTime.UtcNow.AddSeconds(90);
        List<string> names = [];
        while (DateTime.UtcNow < deadline)
        {
            names = await TaskNamesAsync(api, instanceId);
            if (until(names)) return names;
            await Task.Delay(1000);
        }

        Assert.Fail($"Timed out after 90s waiting for {what}. Tasks were: {string.Join(", ", names)}");
        return names;
    }
}
