using System.Text.Json;
using AutoNate.E2E.Tests.Support;
using Microsoft.Playwright;
using Xunit;

namespace AutoNate.E2E.Tests;

/// <summary>
/// Run a step once per item in a collection (#159).
/// </summary>
/// <remarks>
/// Multi-instance is an activity MARKER, not a palette entry, which is why the
/// original inventory could not find it — it has no references in Auton8's own
/// code, only in the vendored bpmn-js. The engine has always been able to do it.
/// </remarks>
[Trait("RequiresService", "Flowable")]
[Collection(AutoNateE2ECollection.Name)]
public sealed class MultiInstanceExecutionTests : E2ETestBase
{
    public MultiInstanceExecutionTests(AutoNateE2EFixture fixture) : base(fixture) { }

    [Fact]
    public async Task Each_instance_sees_its_own_item_and_sequential_runs_them_in_order()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var api = session.Page.APIRequest;

        var key = $"mis{Guid.NewGuid():N}"[..20];
        await PublishAsync(api, key, ScriptDiagram(key, sequential: true));
        var instance = await StartAsync(api, key, ["alpha", "beta", "gamma"]);

        var variables = await EventuallyVariablesAsync(api, instance,
            v => v.TryGetValue("trail", out var t) && t.Count(c => c == ';') == 3,
            "all three instances to run");

        // The element variable is the whole feature. A test asserting only the
        // instance COUNT would pass without each instance ever seeing its item.
        // Sequential ordering is asserted here too, because it is invisible
        // otherwise.
        Assert.Equal("alpha;beta;gamma;", variables["trail"]);
    }

    [Fact]
    public async Task An_empty_collection_completes_immediately_instead_of_hanging()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var api = session.Page.APIRequest;

        var key = $"mie{Guid.NewGuid():N}"[..20];
        await PublishAsync(api, key, ScriptDiagram(key, sequential: true));
        var instance = await StartAsync(api, key, []);

        // The edge case most likely to reach production. It must complete the
        // activity without creating instances, not hang and not fail.
        var variables = await EventuallyVariablesAsync(api, instance,
            v => v.ContainsKey("finished"), "the process to complete past an empty collection");

        Assert.Equal("", variables.GetValueOrDefault("trail", ""));
        Assert.True(variables.ContainsKey("finished"));
    }

    [Fact]
    public async Task A_parallel_multi_instance_user_task_produces_one_task_per_item()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var api = session.Page.APIRequest;

        var key = $"mip{Guid.NewGuid():N}"[..20];
        await PublishAsync(api, key, UserTaskDiagram(key, sequential: false));
        var instance = await StartAsync(api, key, ["a", "b", "c"]);

        var names = await EventuallyAsync(api, instance, n => n.Count == 3,
            "three concurrent instances of the task");
        Assert.Equal(3, names.Count(n => n == "Approve"));
    }

    [Fact]
    public async Task A_sequential_multi_instance_user_task_produces_one_at_a_time()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var api = session.Page.APIRequest;

        var key = $"mit{Guid.NewGuid():N}"[..20];
        await PublishAsync(api, key, UserTaskDiagram(key, sequential: true));
        var instance = await StartAsync(api, key, ["a", "b", "c"]);

        var names = await EventuallyAsync(api, instance, n => n.Count >= 1, "the first instance");

        // The complement of the parallel test, and the pair is what makes either
        // meaningful — isSequential ignored in either direction passes one of them.
        Assert.Single(names);
    }

    [Fact]
    public async Task A_loop_marker_is_refused_at_publish_naming_the_step()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var api = session.Page.APIRequest;

        var key = $"mil{Guid.NewGuid():N}"[..20];
        var xml = UserTaskDiagram(key, sequential: true).Replace(
            "<bpmn:multiInstanceLoopCharacteristics isSequential=\"true\" " +
            "flowable:collection=\"${items}\" flowable:elementVariable=\"item\" />",
            "<bpmn:standardLoopCharacteristics loopMaximum=\"3\" />",
            StringComparison.Ordinal);
        Assert.Contains("standardLoopCharacteristics", xml, StringComparison.Ordinal);

        var id = Guid.NewGuid();
        var created = await api.PostAsync("/api/workflows/", new APIRequestContextOptions
        {
            DataObject = new { id, name = TestNames.Prefixed(key), processKey = key, bpmnXml = xml }
        });
        Assert.True(created.Ok, await created.TextAsync());

        var published = await api.PostAsync($"/api/workflows/{id}/publish", new APIRequestContextOptions
        {
            DataObject = new { id, name = TestNames.Prefixed(key), processKey = key, bpmnXml = xml }
        });

        // Flowable never repeats the activity — measured against a control on the
        // same task. Publishing it would give an author a loop that runs once.
        Assert.False(published.Ok);
        var body = await published.TextAsync();
        Assert.Contains("Loop Marker", body, StringComparison.Ordinal);
        Assert.Contains("Approve", body, StringComparison.Ordinal);
    }

    // ── diagrams ─────────────────────────────────────────────────────────────

    // ── #245: the criteria #159 ticked and did not implement ────────────────

    [Fact]
    public async Task A_fixed_count_creates_exactly_that_many_instances()
    {
        // No test anywhere set loopCardinality; the manifest asserted it worked on
        // a manual probe. There was also no field in the panel and no read or
        // write of it in workflow.js, so an author could not have set one.
        await using var session = await NewSignedInAsAdminAsync();
        var api = session.Page.APIRequest;

        var key = $"mic{Guid.NewGuid():N}"[..20];
        await PublishAsync(api, key, CardinalityDiagram(key, count: "3"));

        // Deliberately started with NO collection: a cardinality that quietly fell
        // back to a list would have nothing to iterate and create nothing.
        var instance = await StartAsync(api, key, null);

        var names = await EventuallyAsync(api, instance, n => n.Count >= 3,
            "three instances from a fixed count");
        Assert.Equal(3, names.Count(n => n == "Approve"));

        // And not a fourth a moment later.
        await Task.Delay(2_000);
        Assert.Equal(3, (await TaskNamesAsync(api, instance)).Count(n => n == "Approve"));
    }

    [Fact]
    public async Task A_multi_instance_user_task_is_independently_assignable_and_completable()
    {
        // #159's criterion asserted the COUNT and nothing else. One task per item
        // is also true of an implementation whose tasks all carry one assignee and
        // complete together, which is the opposite of what a per-approver step is
        // for.
        await using var session = await NewSignedInAsAdminAsync();
        var api = session.Page.APIRequest;

        var key = $"mia{Guid.NewGuid():N}"[..20];
        await PublishAsync(api, key, AssignedUserTaskDiagram(key));
        var instance = await StartAsync(api, key, ["ana", "ben", "cat"]);

        var tasks = await EventuallyTasksAsync(api, instance, t => t.Count == 3,
            "one task per approver");

        // Each carries ITS OWN item as assignee, not one shared value.
        var assignees = tasks.Select(t => t.Assignee).OrderBy(a => a, StringComparer.Ordinal).ToList();
        Assert.Equal(["ana", "ben", "cat"], assignees);

        // Completing one completes ONE. A shared-execution implementation would
        // take all three down together, and the count assertion above cannot see
        // the difference.
        var first = tasks.First(t => t.Assignee == "ben");
        var completed = await api.PostAsync($"/api/tasks/{first.Id}/complete",
            new APIRequestContextOptions { DataObject = new { } });
        Assert.True(completed.Ok, $"Completing failed: {completed.Status} {await completed.TextAsync()}");

        var remaining = await EventuallyTasksAsync(api, instance, t => t.Count == 2,
            "the other two approvals to be untouched");
        Assert.Equal(
            ["ana", "cat"],
            remaining.Select(t => t.Assignee).OrderBy(a => a, StringComparer.Ordinal));
    }

    [Fact]
    public async Task A_completion_condition_ends_the_loop_early_and_cancels_the_rest()
    {
        // The story's own key_link said outright that "the cancellation is the half
        // most likely to be missed". It was missed: nothing ran a completion
        // condition against the engine at all.
        await using var session = await NewSignedInAsAdminAsync();
        var api = session.Page.APIRequest;

        var key = $"miq{Guid.NewGuid():N}"[..20];
        await PublishAsync(api, key, AssignedUserTaskDiagram(
            key, completionCondition: "${nrOfCompletedInstances >= 2}"));
        var instance = await StartAsync(api, key, ["ana", "ben", "cat"]);

        var tasks = await EventuallyTasksAsync(api, instance, t => t.Count == 3,
            "one task per approver");

        foreach (var assignee in new[] { "ana", "ben" })
        {
            var task = tasks.First(t => t.Assignee == assignee);
            var done = await api.PostAsync($"/api/tasks/{task.Id}/complete",
                new APIRequestContextOptions { DataObject = new { } });
            Assert.True(done.Ok, $"Completing {assignee} failed: {done.Status}");
        }

        // Early completion: the process moved on with one approval outstanding.
        var after = await EventuallyAsync(api, instance,
            n => n.Contains("After approvals"), "the loop to finish early");
        Assert.Contains("After approvals", after);

        // The cancellation, which is the half that was missing. Cat's task is
        // GONE, not merely un-completed -- an implementation that let the loop
        // proceed while the parent moved on would leave it sitting there.
        Assert.DoesNotContain("Approve", after);

        var settled = await EventuallyTasksAsync(api, instance,
            t => t.All(task => task.Assignee != "cat"),
            "the outstanding approval to be cancelled");
        Assert.DoesNotContain(settled, task => task.Assignee == "cat");
    }

    [Fact]
    public async Task Each_runs_result_is_collected_into_one_list_on_the_process()
    {
        // "Results aggregate back" was ticked with zero implementation: no
        // flowable:variableAggregation anywhere, no field, nothing in docs.
        await using var session = await NewSignedInAsAdminAsync();
        var api = session.Page.APIRequest;

        var key = $"mig{Guid.NewGuid():N}"[..20];
        await PublishAsync(api, key, AggregatingDiagram(key));
        var instance = await StartAsync(api, key, ["alpha", "beta", "gamma"]);

        var variables = await EventuallyVariablesAsync(api, instance,
            v => v.ContainsKey("finished"), "the process to finish");

        Assert.True(variables.ContainsKey("scores"),
            "Nothing was collected: the parent has no 'scores' variable. Present: " +
            string.Join(", ", variables.Keys));

        var scores = variables["scores"];

        // One entry per run, each the value THAT run produced. Asserting only
        // that the variable exists would pass for an aggregation that collected
        // the last instance's value and called it a list.
        foreach (var expected in new[] { "alpha!", "beta!", "gamma!" })
        {
            Assert.Contains(expected, scores, StringComparison.Ordinal);
        }

        // Three entries, not one value that happens to contain the strings. A
        // "list" holding only the last instance's result would satisfy the
        // Contains assertions above if the earlier runs' values appeared
        // anywhere else in it.
        Assert.Equal(3, scores.Split("alpha!").Length - 1
            + scores.Split("beta!").Length - 1
            + scores.Split("gamma!").Length - 1);

        // Recorded rather than asserted as a defect: the per-run variable also
        // ends up on the process under its own name, because the script sandbox's
        // variables.set writes through to the instance. Aggregation is unaffected
        // -- `scores` is built from the per-instance scope, which is why it has
        // all three values and not three copies of the last one -- but an author
        // reading `score` on the parent gets whichever run finished last, so the
        // aggregated list is the one to read.
        Assert.True(variables.ContainsKey("scores"));
    }

    private sealed record TaskRow(string Id, string Name, string? Assignee);

    private static async Task<List<TaskRow>> TasksAsync(IAPIRequestContext api, string instanceId)
    {
        var response = await api.GetAsync($"/api/executions/{instanceId}/tasks");
        Assert.True(response.Ok, await response.TextAsync());
        using var document = JsonDocument.Parse(await response.TextAsync());
        return document.RootElement.EnumerateArray()
            .Select(e => new TaskRow(
                e.GetProperty("id").GetString()!,
                e.GetProperty("name").GetString()!,
                e.TryGetProperty("assignee", out var a) ? a.GetString() : null))
            .ToList();
    }

    private static async Task<List<TaskRow>> EventuallyTasksAsync(
        IAPIRequestContext api, string instanceId, Func<List<TaskRow>, bool> until, string what)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        List<TaskRow> tasks = [];
        while (DateTime.UtcNow < deadline)
        {
            tasks = await TasksAsync(api, instanceId);
            if (until(tasks)) return tasks;
            await Task.Delay(500);
        }

        Assert.Fail($"Timed out waiting for {what}. Tasks were: " +
            string.Join(", ", tasks.Select(t => $"{t.Name}/{t.Assignee}")));
        return tasks;
    }

    private static async Task<List<string>> TaskNamesAsync(IAPIRequestContext api, string instanceId) =>
        (await TasksAsync(api, instanceId)).Select(t => t.Name).ToList();

    /// <summary>A fixed number of runs, with no collection at all.</summary>
    private static string CardinalityDiagram(string key, string count) => $$"""
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          xmlns:flowable="http://flowable.org/bpmn"
                          xmlns:autonate="http://autonate.dev/workflows"
                          id="Definitions_1" targetNamespace="http://autonate.dev/workflows">
          <bpmn:process id="{{key}}" name="Fixed count" isExecutable="true">
            <bpmn:startEvent id="s" />
            <bpmn:sequenceFlow id="f0" sourceRef="s" targetRef="t" />
            <bpmn:userTask id="t" name="Approve">
              <bpmn:multiInstanceLoopCharacteristics isSequential="false"
                                                     autonate:loopCardinality="{{count}}" />
            </bpmn:userTask>
            <bpmn:sequenceFlow id="f1" sourceRef="t" targetRef="e" />
            <bpmn:endEvent id="e" />
          </bpmn:process>
          {{Di(key, "s", "t", "e")}}
        </bpmn:definitions>
        """;

    /// <summary>One task per item, each assigned to its own item.</summary>
    private static string AssignedUserTaskDiagram(string key, string? completionCondition = null) => $$"""
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          xmlns:flowable="http://flowable.org/bpmn"
                          xmlns:autonate="http://autonate.dev/workflows"
                          id="Definitions_1" targetNamespace="http://autonate.dev/workflows">
          <bpmn:process id="{{key}}" name="Approvals" isExecutable="true">
            <bpmn:startEvent id="s" />
            <bpmn:sequenceFlow id="f0" sourceRef="s" targetRef="t" />
            <bpmn:userTask id="t" name="Approve" flowable:assignee="${approver}">
              <bpmn:multiInstanceLoopCharacteristics isSequential="false"
                                                     flowable:collection="${items}"
                                                     flowable:elementVariable="approver"{{
                (completionCondition is null
                    ? ""
                    : $"\n                                                     autonate:completionCondition=\"{completionCondition}\"")}} />
            </bpmn:userTask>
            <bpmn:sequenceFlow id="f1" sourceRef="t" targetRef="after" />
            <bpmn:userTask id="after" name="After approvals" />
            <bpmn:sequenceFlow id="f2" sourceRef="after" targetRef="e" />
            <bpmn:endEvent id="e" />
          </bpmn:process>
          {{Di(key, "s", "t", "after", "e")}}
        </bpmn:definitions>
        """;

    /// <summary>Each run sets a variable; the loop collects them into one list.</summary>
    private static string AggregatingDiagram(string key) => $$"""
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          xmlns:flowable="http://flowable.org/bpmn"
                          xmlns:autonate="http://autonate.dev/workflows"
                          id="Definitions_1" targetNamespace="http://autonate.dev/workflows">
          <bpmn:process id="{{key}}" name="Collect" isExecutable="true">
            <bpmn:startEvent id="s" />
            <bpmn:sequenceFlow id="f0" sourceRef="s" targetRef="t" />
            <bpmn:scriptTask id="t" name="Score one" scriptFormat="javascript"
                             autonate:runAs="workflowAuthor">
              <bpmn:multiInstanceLoopCharacteristics isSequential="false"
                                                     flowable:collection="${items}"
                                                     flowable:elementVariable="item"
                                                     autonate:aggregateSource="score"
                                                     autonate:aggregateTarget="scores" />
              <bpmn:script>variables.set('score', variables.get('item') + '!');</bpmn:script>
            </bpmn:scriptTask>
            <bpmn:sequenceFlow id="f1" sourceRef="t" targetRef="done" />
            <bpmn:scriptTask id="done" name="Finish" scriptFormat="javascript"
                             autonate:runAs="workflowAuthor">
              <bpmn:script>variables.set('finished', true);</bpmn:script>
            </bpmn:scriptTask>
            <bpmn:sequenceFlow id="f2" sourceRef="done" targetRef="e" />
            <bpmn:endEvent id="e" />
          </bpmn:process>
          {{Di(key, "s", "t", "done", "e")}}
        </bpmn:definitions>
        """;

    private static string Marker(bool sequential) =>
        $"<bpmn:multiInstanceLoopCharacteristics isSequential=\"{(sequential ? "true" : "false")}\" " +
        "flowable:collection=\"${items}\" flowable:elementVariable=\"item\" />";

    private static string ScriptDiagram(string key, bool sequential) => $$"""
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          xmlns:flowable="http://flowable.org/bpmn"
                          xmlns:autonate="http://autonate.dev/workflows"
                          id="Definitions_1" targetNamespace="http://autonate.dev/workflows">
          <bpmn:process id="{{key}}" name="Each item" isExecutable="true">
            <bpmn:startEvent id="s" />
            <bpmn:sequenceFlow id="f0" sourceRef="s" targetRef="t" />
            <bpmn:scriptTask id="t" name="Handle item" scriptFormat="javascript"
                             autonate:runAs="workflowAuthor">
              {{Marker(sequential)}}
              <!-- variables.get, not a bare `item`: the sandbox exposes process
                   variables through the variables API and binds no bare
                   identifiers, so `item` alone is a ReferenceError. -->
              <bpmn:script>variables.set('trail', (variables.get('trail') || '') + variables.get('item') + ';');</bpmn:script>
            </bpmn:scriptTask>
            <bpmn:sequenceFlow id="f1" sourceRef="t" targetRef="done" />
            <bpmn:scriptTask id="done" name="Finish" scriptFormat="javascript"
                             autonate:runAs="workflowAuthor">
              <bpmn:script>variables.set('finished', true);</bpmn:script>
            </bpmn:scriptTask>
            <bpmn:sequenceFlow id="f2" sourceRef="done" targetRef="e" />
            <bpmn:endEvent id="e" />
          </bpmn:process>
          {{Di(key, "s", "t", "done", "e")}}
        </bpmn:definitions>
        """;

    private static string UserTaskDiagram(string key, bool sequential) => $$"""
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          xmlns:flowable="http://flowable.org/bpmn"
                          id="Definitions_1" targetNamespace="http://autonate.dev/workflows">
          <bpmn:process id="{{key}}" name="Approvals" isExecutable="true">
            <bpmn:startEvent id="s" />
            <bpmn:sequenceFlow id="f0" sourceRef="s" targetRef="t" />
            <bpmn:userTask id="t" name="Approve">
              {{Marker(sequential)}}
            </bpmn:userTask>
            <bpmn:sequenceFlow id="f1" sourceRef="t" targetRef="e" />
            <bpmn:endEvent id="e" />
          </bpmn:process>
          {{Di(key, "s", "t", "e")}}
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

    // ── helpers ──────────────────────────────────────────────────────────────

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

    private static async Task<string> StartAsync(
        IAPIRequestContext api, string key, string[]? items)
    {
        // #245. A null collection is not an empty one: the cardinality test must
        // start with no `items` variable at all, so that a cardinality quietly
        // falling back to a list has nothing to iterate and creates nothing.
        var response = await api.PostAsync($"/api/workflows/{key}/start", new APIRequestContextOptions
        {
            DataObject = items is null
                ? new { variables = new { } }
                : (object)new { variables = new { items } }
        });
        Assert.True(response.Ok, $"Starting failed: {response.Status} {await response.TextAsync()}");
        using var document = JsonDocument.Parse(await response.TextAsync());
        return document.RootElement.GetProperty("id").GetString()!;
    }

    private static async Task<Dictionary<string, string>> VariablesAsync(
        IAPIRequestContext api, string instanceId)
    {
        var response = await api.GetAsync($"/api/executions/{instanceId}/diagram");
        Assert.True(response.Ok, $"Reading the execution failed: {response.Status} {await response.TextAsync()}");
        using var document = JsonDocument.Parse(await response.TextAsync());

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in document.RootElement.GetProperty("variables").EnumerateArray())
        {
            result[entry.GetProperty("name").GetString()!] =
                entry.GetProperty("value").GetString() ?? string.Empty;
        }
        return result;
    }

    private static async Task<Dictionary<string, string>> EventuallyVariablesAsync(
        IAPIRequestContext api, string instanceId,
        Func<Dictionary<string, string>, bool> until, string what)
    {
        var deadline = DateTime.UtcNow.AddSeconds(45);
        var variables = new Dictionary<string, string>(StringComparer.Ordinal);
        while (DateTime.UtcNow < deadline)
        {
            variables = await VariablesAsync(api, instanceId);
            if (until(variables)) return variables;
            await Task.Delay(1000);
        }

        Assert.Fail($"Timed out waiting for {what}. Variables: " +
                    string.Join(", ", variables.Select(v => $"{v.Key}={v.Value}")));
        return variables;
    }

    private static async Task<List<string>> EventuallyAsync(
        IAPIRequestContext api, string instanceId, Func<List<string>, bool> until, string what)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        List<string> names = [];
        while (DateTime.UtcNow < deadline)
        {
            var response = await api.GetAsync($"/api/executions/{instanceId}/tasks");
            Assert.True(response.Ok, await response.TextAsync());
            using var document = JsonDocument.Parse(await response.TextAsync());
            names = document.RootElement.EnumerateArray()
                .Select(e => e.GetProperty("name").GetString()!).ToList();
            if (until(names)) return names;
            await Task.Delay(500);
        }

        Assert.Fail($"Timed out waiting for {what}. Tasks were: {string.Join(", ", names)}");
        return names;
    }
}
