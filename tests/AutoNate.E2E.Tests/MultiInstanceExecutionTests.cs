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

    // ── #173: the operator-facing half ───────────────────────────────────────

    [Fact]
    public async Task The_history_collapses_a_parallel_multi_instance_to_one_row_with_progress()
    {
        // Against the REAL engine, because everything this asserts was derived
        // from probing Flowable 8.0.0 and a stub would only replay what I already
        // believe. Three instances, one completed, and the row must say so.
        await using var session = await NewSignedInAsAdminAsync();
        var api = session.Page.APIRequest;

        var key = $"mih{Guid.NewGuid():N}"[..20];
        await PublishAsync(api, key, UserTaskDiagram(key, sequential: false));
        var instance = await StartAsync(api, key, ["alpha", "beta", "gamma"]);

        var tasks = await EventuallyTasksAsync(api, instance, t => t.Count == 3,
            "three parallel instances");

        var completed = await api.PostAsync($"/api/tasks/{tasks[0].Id}/complete",
            new APIRequestContextOptions { DataObject = new { } });
        Assert.True(completed.Ok, await completed.TextAsync());

        await EventuallyTasksAsync(api, instance, t => t.Count == 2, "two instances left");

        var rows = await HistoryAsync(api, instance);
        var review = rows.Where(r => r.GetProperty("activityId").GetString() == "t").ToList();

        // ONE row, not three. This is the whole point: 500 instances must not
        // become 500 rows.
        var one = Assert.Single(review);
        var progress = one.GetProperty("multiInstance");

        Assert.Equal(3, progress.GetProperty("total").GetInt32());
        Assert.Equal(1, progress.GetProperty("completed").GetInt32());
        Assert.Equal(2, progress.GetProperty("active").GetInt32());
        Assert.False(progress.GetProperty("isSequential").GetBoolean());

        // The instances are NOT in that payload -- the lazy-loading criterion,
        // asserted on the wire rather than on what the browser happens to render.
        Assert.Equal(JsonValueKind.Null, one.GetProperty("elementValue").ValueKind);

        // ...and are there when asked for, each carrying its own collection item.
        var instancesResponse = await api.GetAsync(
            $"/api/executions/{instance}/activities/t/instances");
        Assert.True(instancesResponse.Ok, await instancesResponse.TextAsync());

        using var document = JsonDocument.Parse(await instancesResponse.TextAsync());
        var expanded = document.RootElement.EnumerateArray().ToList();
        Assert.Equal(3, expanded.Count);

        var items = expanded
            .Select(e => e.GetProperty("elementValue").GetString())
            .OrderBy(v => v, StringComparer.Ordinal)
            .ToList();

        // Each instance knows WHICH item it has. A shared or empty value would
        // pass a count assertion and tell an operator nothing.
        Assert.Equal(["alpha", "beta", "gamma"], items);
    }

    [Fact]
    public async Task A_sequential_loop_in_flight_reports_the_engine_total_not_its_one_row()
    {
        // The measurement that changed the design: a sequential loop creates one
        // instance at a time, so Flowable writes ONE historic activity row for a
        // loop over three. Counting rows reports "1 of 1" -- a finished-looking
        // activity with two runs to go. This is the spec that fails if the engine
        // counters are ever dropped, and it cannot run without a live engine.
        await using var session = await NewSignedInAsAdminAsync();
        var api = session.Page.APIRequest;

        var key = $"mis3{Guid.NewGuid():N}"[..20];
        await PublishAsync(api, key, UserTaskDiagram(key, sequential: true));
        var instance = await StartAsync(api, key, ["alpha", "beta", "gamma"]);

        await EventuallyTasksAsync(api, instance, t => t.Count == 1, "the first instance");

        var rows = await HistoryAsync(api, instance);
        var one = Assert.Single(rows.Where(r => r.GetProperty("activityId").GetString() == "t"));
        var progress = one.GetProperty("multiInstance");

        Assert.True(progress.GetProperty("isSequential").GetBoolean());
        Assert.Equal(3, progress.GetProperty("total").GetInt32());
        Assert.Equal(0, progress.GetProperty("completed").GetInt32());
    }

    [Fact]
    public async Task The_executions_list_shows_one_row_however_many_instances_a_process_has()
    {
        // The AC, asserted against the LIST endpoint rather than the detail view,
        // because that is where a per-instance row would break #108's paging.
        await using var session = await NewSignedInAsAdminAsync();
        var api = session.Page.APIRequest;

        var key = $"mil2{Guid.NewGuid():N}"[..20];
        await PublishAsync(api, key, UserTaskDiagram(key, sequential: false));
        var instance = await StartAsync(api, key, ["a", "b", "c", "d", "e"]);

        await EventuallyTasksAsync(api, instance, t => t.Count == 5, "five instances");

        var response = await api.GetAsync("/api/executions/");
        Assert.True(response.Ok, await response.TextAsync());

        using var document = JsonDocument.Parse(await response.TextAsync());
        var mine = document.RootElement.EnumerateArray()
            .Where(e => e.GetProperty("id").GetString() == instance)
            .ToList();

        // Five instances, one row. Not five, and not zero.
        Assert.Single(mine);
    }

    private static async Task<List<JsonElement>> HistoryAsync(IAPIRequestContext api, string instanceId)
    {
        var response = await api.GetAsync($"/api/executions/{instanceId}/history");
        Assert.True(response.Ok, await response.TextAsync());
        using var document = JsonDocument.Parse(await response.TextAsync());
        return document.RootElement.EnumerateArray().Select(e => e.Clone()).ToList();
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

    // ── #173's a11y half, which #628 found unasserted at every tier ──────────
    //
    // #173's AC reads: the expand control is keyboard-operable, the progress is
    // announced to assistive technology, and the a11y ratchet is not weakened.
    // The ratchet half held. The other two had no executable coverage anywhere:
    // this class drove `APIRequest` only, and the SPA's vitest runs
    // `environment: "node"` with no DOM to assert against. The completion
    // evidence said "browser-answerable facts stayed for E2E", which implied
    // they were covered here. They were covered nowhere.
    //
    // These three are here rather than in a slim spec because the row does not
    // exist without a real multi-instance execution behind it. The arithmetic
    // and the wording live in `multiInstanceProgress.ts` and ARE slim-tested;
    // what only a browser answers is focus, the ARIA state, and the region.

    /// <summary>
    /// The expand control is reachable and operable by keyboard (#173, #628).
    /// </summary>
    /// <remarks>
    /// Focus is driven by TABBING rather than by <c>FocusAsync()</c>, which
    /// would prove the element can hold focus without proving a user can get to
    /// it. The second Enter is the complement and is the half that matters: a
    /// control that only ever opens satisfies every assertion above it while
    /// being half a toggle.
    /// </remarks>
    [Fact]
    public async Task The_multi_instance_expand_control_is_reachable_and_operable_by_keyboard()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;
        var api = page.APIRequest;

        var key = $"mik{Guid.NewGuid():N}"[..20];
        await OpenCollapsedRowAsync(page, api, key);

        // Located by `aria-controls` rather than by its label, because the label
        // is "Hide instances" once it is open and a name-based locator would
        // stop matching the element it just operated.
        var opener = page.GetByRole(AriaRole.Button, new() { Name = "Show 3 instances" });
        var panelId = await opener.GetAttributeAsync("aria-controls");
        Assert.False(string.IsNullOrWhiteSpace(panelId),
            "The expand control must name the region it controls.");

        var toggle = page.Locator($"[aria-controls='{panelId}']");
        var panel = page.Locator($"[id='{panelId}']");

        Assert.Equal("false", await toggle.GetAttributeAsync("aria-expanded"));

        var reached = false;
        for (var i = 0; i < 150 && !reached; i++)
        {
            await page.Keyboard.PressAsync("Tab");
            reached = await toggle.EvaluateAsync<bool>("el => el === document.activeElement");
        }

        Assert.True(reached,
            "The multi-instance expand control must be reachable by keyboard alone.");

        await page.Keyboard.PressAsync("Enter");

        await Assertions.Expect(toggle).ToHaveAttributeAsync("aria-expanded", "true");
        await Assertions.Expect(panel).ToBeVisibleAsync(new() { Timeout = 15_000 });
        // The panel carries the instances, so "expanded" is not just an attribute
        // flip over an empty box.
        await Assertions.Expect(panel).ToContainTextAsync("alpha", new() { Timeout = 15_000 });

        await page.Keyboard.PressAsync("Enter");

        await Assertions.Expect(toggle).ToHaveAttributeAsync("aria-expanded", "false");
        await Assertions.Expect(panel).Not.ToBeVisibleAsync();
    }

    /// <summary>
    /// The progress reaches assistive technology through a persistent polite
    /// region (#173, #628).
    /// </summary>
    /// <remarks>
    /// <para>Whether a screen reader speaks is not observable from Playwright —
    /// the same reasoning <c>ExecutionFreshnessIndicatorTests</c> settled on for
    /// #109. What IS observable, and what decides it, is that a region with a
    /// polite live setting carries the whole sentence and SURVIVES the
    /// interaction. A region torn down and recreated with its text already in it
    /// is never announced, so the node is marked and required to still be there
    /// afterwards; a remount loses the mark.</para>
    /// <para><c>polite</c> rather than <c>assertive</c> is asserted deliberately:
    /// interrupting whatever the user is reading to say a loop is 2 of 5 done
    /// would be worse than saying nothing.</para>
    /// </remarks>
    [Fact]
    public async Task The_multi_instance_progress_is_announced_by_a_persistent_polite_region()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;
        var api = page.APIRequest;

        var key = $"mia{Guid.NewGuid():N}"[..20];
        await OpenCollapsedRowAsync(page, api, key);

        var region = AnnouncementRegion(page);
        await region.WaitForAsync(new() { State = WaitForSelectorState.Attached, Timeout = 15_000 });

        Assert.Equal("status", await region.GetAttributeAsync("role"));
        Assert.Equal("polite", await region.GetAttributeAsync("aria-live"));

        // The WHOLE sentence, not the bar's number. A screen-reader user gets no
        // badge layout, so the parts have to be prose rather than adjacency.
        Assert.Equal("Approve: 0 of 3 complete.", (await region.TextContentAsync())?.Trim());

        await region.EvaluateAsync("el => el.setAttribute('data-e2e-live-region', 'kept')");
        await page.GetByRole(AriaRole.Button, new() { Name = "Show 3 instances" }).ClickAsync();

        var kept = page.Locator("[data-e2e-live-region='kept']");
        await Assertions.Expect(kept).ToHaveCountAsync(1);
        Assert.Equal("Approve: 0 of 3 complete.", (await kept.TextContentAsync())?.Trim());
    }

    /// <summary>
    /// The announcement changes IN PLACE when an instance completes (#173, #628).
    /// </summary>
    /// <remarks>
    /// <para>The assertion that makes the previous one mean something. A live
    /// region rendered already-populated on mount typically announces nothing —
    /// assistive technology speaks a CHANGE — so "the progress is announced"
    /// is only true if the sentence can change while the region stays put.</para>
    /// <para>Waiting long enough cannot fake this. The node is marked before the
    /// task is completed and the marked node is required to carry the new
    /// sentence: a remounted row loses the mark and fails here, which is exactly
    /// the shape that would look announced and be silent.</para>
    /// <para>This is also why <c>ExecutionContent</c> now carries the detail
    /// channel subscription itself. It lived in the LIST page, so
    /// <c>/executions/:id</c> — a real route, linked from Called Workflows —
    /// never refreshed at all: nothing could change, in the region or anywhere
    /// else on it.</para>
    /// </remarks>
    [Fact]
    public async Task The_announcement_changes_in_place_when_an_instance_completes()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;
        var api = page.APIRequest;

        var key = $"mic{Guid.NewGuid():N}"[..20];
        var instance = await OpenCollapsedRowAsync(page, api, key);

        var region = AnnouncementRegion(page);
        await region.WaitForAsync(new() { State = WaitForSelectorState.Attached, Timeout = 15_000 });
        Assert.Equal("Approve: 0 of 3 complete.", (await region.TextContentAsync())?.Trim());
        await region.EvaluateAsync("el => el.setAttribute('data-e2e-live-region', 'kept')");

        var tasks = await EventuallyTasksAsync(api, instance, t => t.Count == 3,
            "three parallel instances");
        var completed = await api.PostAsync($"/api/tasks/{tasks[0].Id}/complete",
            new APIRequestContextOptions { DataObject = new { } });
        Assert.True(completed.Ok, await completed.TextAsync());

        await Assertions.Expect(page.Locator("[data-e2e-live-region='kept']"))
            .ToHaveTextAsync("Approve: 1 of 3 complete.", new() { Timeout = 60_000 });
    }

    // ---- a11y helpers --------------------------------------------------------

    /// <summary>
    /// The one visually hidden live region the collapsed row renders. Scoped to
    /// the execution page so a status region belonging to the shell cannot
    /// stand in for it.
    /// </summary>
    private static ILocator AnnouncementRegion(IPage page) =>
        page.Locator(".workflow-execution-page [role='status'][aria-live='polite']")
            .Filter(new() { HasText = "Approve:" });

    /// <summary>
    /// Publish a parallel three-item approval, start it, wait until the SERVER
    /// has the collapsed row, then open the History tab.
    /// </summary>
    /// <remarks>
    /// The history query has no refetch interval, so a tab mounted before the
    /// projection caught up would show an empty history and stay that way. The
    /// wait is on the API for that reason — it makes the browser half
    /// deterministic instead of racing a poll.
    /// </remarks>
    private static async Task<string> OpenCollapsedRowAsync(IPage page, IAPIRequestContext api, string key)
    {
        await PublishAsync(api, key, UserTaskDiagram(key, sequential: false));
        var instance = await StartAsync(api, key, ["alpha", "beta", "gamma"]);
        await EventuallyTasksAsync(api, instance, t => t.Count == 3, "three parallel instances");

        await EventuallyMultiInstanceRowAsync(api, instance,
            progress => progress.GetProperty("total").GetInt32() == 3
                     && progress.GetProperty("completed").GetInt32() == 0);

        await page.GotoAsync($"/executions/{instance}");
        await page.GetByRole(AriaRole.Tab, new() { Name = "History" }).ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Show 3 instances" })
            .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30_000 });

        return instance;
    }

    private static async Task EventuallyMultiInstanceRowAsync(
        IAPIRequestContext api, string instance, Func<JsonElement, bool> until)
    {
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < deadline)
        {
            var rows = await HistoryAsync(api, instance);
            var row = rows.FirstOrDefault(r => r.GetProperty("activityId").GetString() == "t");
            if (row.ValueKind == JsonValueKind.Object
                && row.TryGetProperty("multiInstance", out var progress)
                && progress.ValueKind == JsonValueKind.Object
                && until(progress))
            {
                return;
            }

            await Task.Delay(500);
        }

        Assert.Fail($"The collapsed multi-instance row for '{instance}' never reached the expected progress.");
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
