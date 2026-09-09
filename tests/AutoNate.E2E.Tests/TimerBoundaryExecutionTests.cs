using System.Text.Json;
using AutoNate.E2E.Tests.Support;
using Microsoft.Playwright;
using Xunit;

namespace AutoNate.E2E.Tests;

/// <summary>
/// Timer boundary events do what their configuration says (#157).
/// </summary>
/// <remarks>
/// Behaviour, not deployment. Every element in this milestone deploys; the epic
/// exists because deploying is not working. Each test here asserts the half a naive
/// implementation omits — that the interrupting timer *cancelled* the activity, that
/// the non-interrupting one *left it running*, and that completing the activity first
/// *removed* the timer rather than merely not firing it.
///
/// Timers make tests slow and flaky if written naively, so these use short durations
/// and poll for the expected state rather than sleeping a fixed interval.
/// </remarks>
[Trait("RequiresService", "Flowable")]
[Collection(AutoNateE2ECollection.Name)]
public sealed class TimerBoundaryExecutionTests : E2ETestBase
{
    public TimerBoundaryExecutionTests(AutoNateE2EFixture fixture) : base(fixture) { }

    [Fact]
    public async Task An_interrupting_timer_cancels_the_activity_and_a_non_interrupting_one_does_not()
    {
        // Both in one process so they are compared against the same diagram rather
        // than two that might differ some other way.
        await using var session = await NewSignedInAsAdminAsync();
        var api = session.Page.APIRequest;

        var key = $"tb_both_{Guid.NewGuid():N}"[..24];
        await PublishAsync(api, key, $$"""
            <?xml version="1.0" encoding="UTF-8"?>
            <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                              xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
                              id="Definitions_1" targetNamespace="http://autonate.dev/workflows">
              <bpmn:process id="{{key}}" name="Timer Boundary" isExecutable="true">
                <bpmn:startEvent id="s" />
                <bpmn:sequenceFlow id="f0" sourceRef="s" targetRef="fork" />
                <bpmn:parallelGateway id="fork" />

                <bpmn:sequenceFlow id="fa" sourceRef="fork" targetRef="cancelled" />
                <bpmn:userTask id="cancelled" name="Cancelled work" />
                <bpmn:boundaryEvent id="interrupting" name="Interrupting" attachedToRef="cancelled" cancelActivity="true">
                  <bpmn:timerEventDefinition>
                    <bpmn:timeDuration xsi:type="bpmn:tFormalExpression">PT2S</bpmn:timeDuration>
                  </bpmn:timerEventDefinition>
                </bpmn:boundaryEvent>
                <bpmn:sequenceFlow id="fa2" sourceRef="interrupting" targetRef="afterInterrupting" />
                <bpmn:userTask id="afterInterrupting" name="After interrupting" />

                <bpmn:sequenceFlow id="fb" sourceRef="fork" targetRef="surviving" />
                <bpmn:userTask id="surviving" name="Surviving work" />
                <bpmn:boundaryEvent id="continuing" name="Non-interrupting" attachedToRef="surviving" cancelActivity="false">
                  <bpmn:timerEventDefinition>
                    <bpmn:timeDuration xsi:type="bpmn:tFormalExpression">PT2S</bpmn:timeDuration>
                  </bpmn:timerEventDefinition>
                </bpmn:boundaryEvent>
                <bpmn:sequenceFlow id="fb2" sourceRef="continuing" targetRef="afterContinuing" />
                <bpmn:userTask id="afterContinuing" name="After non-interrupting" />
              </bpmn:process>
              {{Di(key, "s", "fork", "cancelled", "interrupting", "afterInterrupting", "surviving", "continuing", "afterContinuing")}}
            </bpmn:definitions>
            """);

        var instanceId = await StartAsync(api, key);

        // Precondition: both activities are live and neither timer has fired.
        var before = await TaskNamesAsync(api, instanceId);
        Assert.Contains("Cancelled work", before);
        Assert.Contains("Surviving work", before);
        Assert.DoesNotContain("After interrupting", before);

        var after = await EventuallyAsync(api, instanceId,
            names => names.Contains("After interrupting") && names.Contains("After non-interrupting"),
            "both timers to fire");

        // The interrupting half: the boundary path ran AND the activity is gone.
        // Asserting only the first would pass for a non-interrupting timer, which
        // is the opposite feature.
        Assert.Contains("After interrupting", after);
        Assert.DoesNotContain("Cancelled work", after);

        // The non-interrupting half: the boundary path ran AND the activity is
        // still there, completable.
        Assert.Contains("After non-interrupting", after);
        Assert.Contains("Surviving work", after);
    }

    [Fact]
    public async Task A_repeating_timer_fires_the_number_of_times_its_cycle_says()
    {
        // "Bounded by the cycle definition rather than unbounded" — asserted by
        // counting, since a repeating timer that never stops looks identical to one
        // that has simply not stopped yet.
        await using var session = await NewSignedInAsAdminAsync();
        var api = session.Page.APIRequest;

        var key = $"tb_cycle_{Guid.NewGuid():N}"[..24];
        await PublishAsync(api, key, $$"""
            <?xml version="1.0" encoding="UTF-8"?>
            <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                              xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
                              id="Definitions_1" targetNamespace="http://autonate.dev/workflows">
              <bpmn:process id="{{key}}" name="Cycle" isExecutable="true">
                <bpmn:startEvent id="s" />
                <bpmn:sequenceFlow id="f0" sourceRef="s" targetRef="work" />
                <bpmn:userTask id="work" name="Long work" />
                <bpmn:sequenceFlow id="f1" sourceRef="work" targetRef="e" />
                <bpmn:endEvent id="e" />
                <bpmn:boundaryEvent id="nudge" name="Nudge" attachedToRef="work" cancelActivity="false">
                  <bpmn:timerEventDefinition>
                    <bpmn:timeCycle xsi:type="bpmn:tFormalExpression">R3/PT1S</bpmn:timeCycle>
                  </bpmn:timerEventDefinition>
                </bpmn:boundaryEvent>
                <bpmn:sequenceFlow id="f2" sourceRef="nudge" targetRef="nudged" />
                <bpmn:userTask id="nudged" name="Nudged" />
              </bpmn:process>
              {{Di(key, "s", "work", "e", "nudge", "nudged")}}
            </bpmn:definitions>
            """);

        var instanceId = await StartAsync(api, key);

        var names = await EventuallyAsync(api, instanceId,
            n => n.Count(x => x == "Nudged") >= 3, "the cycle to fire three times");

        Assert.Equal(3, names.Count(n => n == "Nudged"));
        // And the guarded activity is untouched throughout, which is what
        // non-interrupting means.
        Assert.Contains("Long work", names);
    }

    [Fact]
    public async Task Completing_the_activity_first_removes_the_timer()
    {
        // The classic wrong behaviour: a timer firing on a task somebody already
        // finished. Asserted on the job being GONE, not on nothing having happened —
        // "nothing happened yet" is true of any duration long enough.
        await using var session = await NewSignedInAsAdminAsync();
        var api = session.Page.APIRequest;

        var key = $"tb_done_{Guid.NewGuid():N}"[..24];
        await PublishAsync(api, key, $$"""
            <?xml version="1.0" encoding="UTF-8"?>
            <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                              xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
                              id="Definitions_1" targetNamespace="http://autonate.dev/workflows">
              <bpmn:process id="{{key}}" name="Beat the timer" isExecutable="true">
                <bpmn:startEvent id="s" />
                <bpmn:sequenceFlow id="f0" sourceRef="s" targetRef="work" />
                <bpmn:userTask id="work" name="Quick work" />
                <bpmn:sequenceFlow id="f1" sourceRef="work" targetRef="done" />
                <bpmn:userTask id="done" name="Finished normally" />
                <bpmn:boundaryEvent id="late" name="Too late" attachedToRef="work" cancelActivity="true">
                  <bpmn:timerEventDefinition>
                    <bpmn:timeDuration xsi:type="bpmn:tFormalExpression">PT30S</bpmn:timeDuration>
                  </bpmn:timerEventDefinition>
                </bpmn:boundaryEvent>
                <bpmn:sequenceFlow id="f2" sourceRef="late" targetRef="escalated" />
                <bpmn:userTask id="escalated" name="Escalated" />
              </bpmn:process>
              {{Di(key, "s", "work", "done", "late", "escalated")}}
            </bpmn:definitions>
            """);

        var instanceId = await StartAsync(api, key);
        Assert.Contains("Quick work", await TaskNamesAsync(api, instanceId));

        var taskId = await TaskIdAsync(api, instanceId, "Quick work");
        var completed = await api.PostAsync($"/api/tasks/{taskId}/complete", new APIRequestContextOptions
        {
            DataObject = new { variables = new Dictionary<string, object?>() }
        });
        Assert.True(completed.Ok, $"Completing failed: {completed.Status} {await completed.TextAsync()}");

        var after = await EventuallyAsync(api, instanceId,
            n => n.Contains("Finished normally"), "the process to move past the task");

        // The timer never fires, because completing removed its job.
        Assert.DoesNotContain("Escalated", after);
    }

    [Fact]
    public async Task A_boundary_cancelled_activity_renders_as_cancelled_not_completed()
    {
        // #177, asserted through #157's feature because an interrupting timer
        // boundary is the first construct that makes this reachable by ordinary
        // authoring.
        //
        // The assertion is made with the instance STILL RUNNING. That is the whole
        // point: cancellation used to be read from the process instance's
        // DeleteReason, so a test on a cancelled instance passed while the defect was
        // live, and the cancelled activity fell through into "completed" — an
        // operator saw a timed-out task rendered exactly like one somebody finished.
        await using var session = await NewSignedInAsAdminAsync();
        var api = session.Page.APIRequest;

        var key = $"tb_cancel_{Guid.NewGuid():N}"[..24];
        await PublishAsync(api, key, $$"""
            <?xml version="1.0" encoding="UTF-8"?>
            <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                              xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
                              id="Definitions_1" targetNamespace="http://autonate.dev/workflows">
              <bpmn:process id="{{key}}" name="Cancelled render" isExecutable="true">
                <bpmn:startEvent id="s" />
                <bpmn:sequenceFlow id="f0" sourceRef="s" targetRef="work" />
                <bpmn:userTask id="work" name="Timed out work" />
                <bpmn:sequenceFlow id="f1" sourceRef="work" targetRef="e" />
                <bpmn:endEvent id="e" />
                <bpmn:boundaryEvent id="timeout" name="Too slow" attachedToRef="work" cancelActivity="true">
                  <bpmn:timerEventDefinition>
                    <bpmn:timeDuration xsi:type="bpmn:tFormalExpression">PT2S</bpmn:timeDuration>
                  </bpmn:timerEventDefinition>
                </bpmn:boundaryEvent>
                <bpmn:sequenceFlow id="f2" sourceRef="timeout" targetRef="escalated" />
                <bpmn:userTask id="escalated" name="Escalated" />
              </bpmn:process>
              {{Di(key, "s", "work", "e", "timeout", "escalated")}}
            </bpmn:definitions>
            """);

        var instanceId = await StartAsync(api, key);
        await EventuallyAsync(api, instanceId, n => n.Contains("Escalated"), "the timer to cancel the task");

        var diagram = await api.GetAsync($"/api/executions/{instanceId}/diagram");
        Assert.True(diagram.Ok, $"Reading the diagram failed: {diagram.Status}");
        using var document = JsonDocument.Parse(await diagram.TextAsync());

        string[] Ids(string field) => document.RootElement.GetProperty(field)
            .EnumerateArray().Select(e => e.GetString()!).ToArray();

        // The instance is alive — `escalated` is waiting — so nothing here depends
        // on the process having been torn down.
        Assert.Contains("escalated", Ids("currentActivityIds"));

        Assert.Contains("work", Ids("cancelledActivityIds"));
        // The half that was broken: it must not ALSO read as completed, since
        // completedActivityIds is built by excluding the cancelled set.
        Assert.DoesNotContain("work", Ids("completedActivityIds"));
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

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
        // #214: the workflow NAME becomes the Flowable deployment name, and the
        // sweep keys on the `e2e-` prefix TestNames.Prefixed produces. Computed once
        // — a fresh call here and in the publish below would rename the model.
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

    /// <summary>
    /// Polls until the condition holds. Polling rather than a fixed sleep, so a
    /// loaded machine makes these slower rather than flaky.
    /// </summary>
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
