using System.Text.Json;
using AutoNate.E2E.Tests.Support;
using Microsoft.Playwright;
using Xunit;

namespace AutoNate.E2E.Tests;

/// <summary>
/// The operator's execution controls change execution state, not just render
/// (E2E-060, #79).
/// </summary>
/// <remarks>
/// <para>Every fact here reads state BACK, and reads it from the engine or the
/// engine-facing API — never from a toast. The failure this class guards
/// against is a control that reports success and does nothing, which is
/// precisely what an untested admin surface accumulates.</para>
///
/// <para><b>Each control is asserted with its complement.</b> Reassigning to B is
/// "B holds it and A does not"; moving state is "the target is current and the
/// origin is not"; force-completing one task is "the next task exists and the
/// other one is untouched". A positive-only assertion passes against a control
/// that fires on every task.</para>
///
/// <para><b>Six controls are driven through the operator endpoints, one through
/// the UI.</b> The SPA reaches reassign, due date, force-complete, move-state and
/// the variable editors through the bpmn-js context menu, which has no stable
/// accessible target for Playwright. Bulk-delete has a real button and a real
/// confirm dialog, and it is the control with the least forgiving failure mode —
/// so the dialog, the part a mistake would bypass, is the part this drives.</para>
///
/// <para><b>Seeding goes through the engine, not the cache.</b> The seed helpers
/// publish and start real definitions; seeding <c>workflow_execution_cache</c>
/// would test the read model against itself.</para>
/// </remarks>
[Trait("RequiresService", "Flowable")]
[Collection(AutoNateE2ECollection.Name)]
public sealed class WorkflowExecutionAdminTests : E2ETestBase
{
    private const string FirstAssignee = "alice";
    private const string SecondAssignee = "bob";

    public WorkflowExecutionAdminTests(AutoNateE2EFixture fixture) : base(fixture) { }

    [Fact]
    public async Task Reassign_moves_the_task_to_the_new_assignee_and_off_the_old_one()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var api = session.Page.APIRequest;
        var (instance, reviewTask) = await SeedAtReviewAsync(api);

        Assert.Equal(FirstAssignee, reviewTask.Assignee);

        var response = await api.PostAsync(
            $"/api/executions/{instance.Id}/tasks/{reviewTask.Id}/reassign",
            new APIRequestContextOptions { DataObject = new { assignee = SecondAssignee } });
        Assert.True(response.Ok, await response.TextAsync());

        // The API's view: the row the operator sees.
        var tasks = await EventuallyTasksAsync(api, instance.Id,
            t => t.Any(x => x.Id == reviewTask.Id && x.Assignee == SecondAssignee),
            $"the task to show assignee {SecondAssignee}");
        var moved = Assert.Single(tasks, t => t.Id == reviewTask.Id);
        Assert.Equal(SecondAssignee, moved.Assignee);
        Assert.NotEqual(FirstAssignee, moved.Assignee);

        // The engine's view: the row the API is a projection OF. A projection that
        // wrote "bob" without the engine agreeing would pass the block above.
        using var engine = EngineClient();
        var engineTask = await EngineJsonAsync(engine, $"service/runtime/tasks/{reviewTask.Id}");
        Assert.Equal(SecondAssignee, engineTask.GetProperty("assignee").GetString());
    }

    [Fact]
    public async Task Changing_a_due_date_is_visible_on_the_task_from_both_sides()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var api = session.Page.APIRequest;
        var (instance, reviewTask) = await SeedAtReviewAsync(api);
        Assert.Null(reviewTask.DueDate);

        var dueDate = new DateTimeOffset(2027, 1, 15, 10, 30, 0, TimeSpan.Zero);
        var response = await api.PostAsync(
            $"/api/executions/{instance.Id}/tasks/{reviewTask.Id}/due-date",
            new APIRequestContextOptions { DataObject = new { dueDate } });
        Assert.True(response.Ok, await response.TextAsync());

        var tasks = await EventuallyTasksAsync(api, instance.Id,
            t => t.Any(x => x.Id == reviewTask.Id && x.DueDate is not null),
            "the task to carry a due date");
        var dated = Assert.Single(tasks, t => t.Id == reviewTask.Id);
        Assert.Equal(dueDate, dated.DueDate);

        using var engine = EngineClient();
        var engineTask = await EngineJsonAsync(engine, $"service/runtime/tasks/{reviewTask.Id}");
        var engineDue = DateTimeOffset.Parse(engineTask.GetProperty("dueDate").GetString()!);
        Assert.Equal(dueDate, engineDue);
    }

    [Fact]
    public async Task Force_completing_the_current_task_advances_to_the_next_step()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var api = session.Page.APIRequest;
        var (instance, reviewTask) = await SeedAtReviewAsync(api);

        var response = await api.PostAsync(
            $"/api/executions/{instance.Id}/tasks/{reviewTask.Id}/force-complete",
            new APIRequestContextOptions { DataObject = new { } });
        Assert.True(response.Ok, await response.TextAsync());

        // Advanced: the next task exists, the completed one is gone.
        var tasks = await EventuallyTasksAsync(api, instance.Id,
            t => t.Any(x => x.Name == "Approve"), "the Approve task to appear");
        Assert.Contains(tasks, t => t.Name == "Approve");
        Assert.DoesNotContain(tasks, t => t.Id == reviewTask.Id);

        // And the diagram agrees about WHERE the execution is.
        var current = await EventuallyCurrentActivitiesAsync(api, instance.Id,
            ids => ids.Contains("UserTask_2"), "UserTask_2 to become current");
        Assert.Equal(["UserTask_2"], current);
    }

    [Fact]
    public async Task Moving_execution_state_jumps_to_the_target_activity_and_leaves_the_origin()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var api = session.Page.APIRequest;
        var (instance, reviewTask) = await SeedAtReviewAsync(api);

        var before = await CurrentActivitiesAsync(api, instance.Id);
        Assert.Equal(["UserTask_1"], before);

        var response = await api.PostAsync(
            $"/api/executions/{instance.Id}/move-state",
            new APIRequestContextOptions { DataObject = new { targetActivityId = "UserTask_2" } });
        Assert.True(response.Ok, await response.TextAsync());

        var current = await EventuallyCurrentActivitiesAsync(api, instance.Id,
            ids => ids.Contains("UserTask_2"), "the execution to arrive at UserTask_2");
        Assert.Equal(["UserTask_2"], current);
        Assert.DoesNotContain("UserTask_1", current);

        // Moving state is not completing: the Review task was CANCELLED, not done.
        // A task list that still shows it would be a move that moved nothing.
        var tasks = await EventuallyTasksAsync(api, instance.Id,
            t => t.All(x => x.Id != reviewTask.Id), "the Review task to be withdrawn");
        Assert.DoesNotContain(tasks, t => t.Id == reviewTask.Id);
        Assert.Contains(tasks, t => t.Name == "Approve");
    }

    [Fact]
    public async Task Variables_written_by_the_operator_are_readable_from_the_engine()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var api = session.Page.APIRequest;
        var (instance, _) = await SeedAtReviewAsync(api);

        using var engine = EngineClient();
        Assert.Equal(42, await EngineIntVariableAsync(engine, instance.Id, "amount"));

        // Override an existing variable...
        var update = await api.PutAsync(
            $"/api/executions/{instance.Id}/variables",
            new APIRequestContextOptions
            {
                DataObject = new { variables = new[] { new { name = "amount", value = 99, type = "integer" } } }
            });
        Assert.True(update.Ok, await update.TextAsync());
        Assert.Equal(99, await EngineIntVariableAsync(engine, instance.Id, "amount"));

        // ...and add one that did not exist. Two different endpoints, because
        // Flowable rejects POST against a name that is already there.
        var add = await api.PostAsync(
            $"/api/executions/{instance.Id}/variables",
            new APIRequestContextOptions
            {
                DataObject = new { variables = new[] { new { name = "reviewer", value = "carol", type = "string" } } }
            });
        Assert.True(add.Ok, await add.TextAsync());

        var vars = await EngineJsonAsync(engine, $"service/runtime/process-instances/{instance.Id}/variables");
        var reviewer = vars.EnumerateArray().Single(v => v.GetProperty("name").GetString() == "reviewer");
        Assert.Equal("carol", reviewer.GetProperty("value").GetString());
    }

    [Fact]
    public async Task History_and_the_execution_log_record_what_the_operator_did()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var api = session.Page.APIRequest;
        var (instance, reviewTask) = await SeedAtReviewAsync(api);

        var response = await api.PostAsync(
            $"/api/executions/{instance.Id}/tasks/{reviewTask.Id}/force-complete",
            new APIRequestContextOptions { DataObject = new { } });
        Assert.True(response.Ok, await response.TextAsync());
        await EventuallyTasksAsync(api, instance.Id, t => t.Any(x => x.Name == "Approve"), "the next step");

        // History: the completed activity has a row that ENDED, the current one
        // has a row that has not. Both halves, so "history exists" cannot pass on
        // a feed that never closes anything. (`endedAtUtc`, not `endTime`: the
        // route projects the engine model to its own shape -- measured by
        // dumping the rows, after the first version of this guessed.)
        var history = await EventuallyJsonArrayAsync(api, $"/api/executions/{instance.Id}/history",
            rows => rows.Any(r => Str(r, "activityId") == "UserTask_1" && Has(r, "endedAtUtc"))
                    && rows.Any(r => Str(r, "activityId") == "UserTask_2"),
            "history to show Review ended and Approve begun");
        var review = Assert.Single(history, r => Str(r, "activityId") == "UserTask_1");
        Assert.True(Has(review, "endedAtUtc"));
        var approve = Assert.Single(history, r => Str(r, "activityId") == "UserTask_2");
        Assert.False(Has(approve, "endedAtUtc"));

        // The force-complete is recorded AS an override, on the row itself, with
        // who did it -- distinguishable from a user completing their own task.
        // That is the operator's action in the history, which is the whole
        // point of the control leaving a trace.
        Assert.True(Bool(review, "isOverride"));
        Assert.True(Has(review, "completedByUserId"));

        // And the execution log records the completion AS an override too. Log
        // rows nest the task -- `{ kind: "task-completed", task: { taskDefinitionKey,
        // isOverride, completedByUserId } }` -- measured by dumping them, after the
        // first version of this asserted on a flat `activityId` that is not there.
        var log = await EventuallyJsonArrayAsync(api, $"/api/executions/{instance.Id}/log",
            rows => rows.Any(r => Str(r, "kind") == "task-completed"
                                  && Str(Prop(r, "task"), "taskDefinitionKey") == "UserTask_1"),
            "the log to carry the completed step");
        var completed = Assert.Single(log, r => Str(r, "kind") == "task-completed"
                                                && Str(Prop(r, "task"), "taskDefinitionKey") == "UserTask_1");
        Assert.True(Bool(Prop(completed, "task"), "isOverride"));
        Assert.True(Has(Prop(completed, "task"), "completedByUserId"));

        // The complement: the step the operator did NOT touch is created but not
        // completed, and carries no override flag.
        var next = Assert.Single(log, r => Str(r, "kind") == "task-created"
                                           && Str(Prop(r, "task"), "taskDefinitionKey") == "UserTask_2");
        Assert.False(Bool(Prop(next, "task"), "isOverride"));
        Assert.DoesNotContain(log, r => Str(r, "kind") == "task-completed"
                                        && Str(Prop(r, "task"), "taskDefinitionKey") == "UserTask_2");
    }

    /// <summary>
    /// Bulk-delete is wired end to end, and the confirm dialog is the safeguard
    /// (#79).
    /// </summary>
    /// <remarks>
    /// <para><b>Why this does not actually delete anything.</b>
    /// <c>POST /api/executions/delete-all</c> is ENGINE-WIDE:
    /// <c>FlowableClient.DeleteAllWorkflowExecutionsAsync</c> pages every historic
    /// instance Flowable has and deletes each — not "Auton8's", all of them. Six
    /// engine-touching E2E classes run outside <c>AutoNateE2ECollection</c>, in
    /// parallel, against the same engine (#643). A spec that confirmed the real
    /// dialog on a full-local run would wipe whatever <c>WorkflowExecutionTests</c>
    /// had mid-flight next door — an intermittent red that would look like THEIR
    /// flake. So the request is intercepted at the browser and never reaches the
    /// server.</para>
    ///
    /// <para><b>What is asserted instead, and why it is the half that matters.</b>
    /// The control's least forgiving failure is not "the endpoint deleted the
    /// wrong rows"; it is "the button did it without asking" or "the dialog's
    /// confirm was wired to nothing". Both are proven here: cancelling the dialog
    /// sends <b>zero</b> requests, confirming it sends <b>exactly one</b>, and the
    /// one carries the right method and path. The endpoint's own effect —
    /// paging and deleting every instance — is the engine's contract, exercised
    /// per instance by <c>WorkflowExecutionTests</c>' single delete and not
    /// re-provable in-suite without the collateral above. Stated as a narrowing,
    /// not hidden as coverage.</para>
    /// </remarks>
    [Fact]
    public async Task Bulk_delete_asks_first_and_sends_exactly_one_request_on_confirm()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;
        var api = page.APIRequest;

        // At least one execution, or the button is disabled and there is nothing
        // to wire. Seeded for real; only the DELETE is intercepted.
        await SeedAtReviewAsync(api);

        var requests = new List<IRequest>();
        await page.RouteAsync("**/api/executions/delete-all", async route =>
        {
            requests.Add(route.Request);
            await route.FulfillAsync(new()
            {
                Status = 200,
                ContentType = "application/json",
                Body = """{"deleted":0}"""
            });
        });

        await page.GotoAsync("/workflow-executions");
        var deleteAll = page.GetByRole(AriaRole.Button, new() { Name = "Delete All Executions" });
        await Assertions.Expect(deleteAll).ToBeEnabledAsync(new() { Timeout = 15_000 });

        // THE COMPLEMENT FIRST: open, then cancel. A button that fires on click
        // and shows a dialog for decoration passes every confirm-path assertion.
        await deleteAll.ClickAsync();
        var dialog = page.GetByRole(AriaRole.Dialog);
        await Assertions.Expect(dialog).ToBeVisibleAsync(new() { Timeout = 10_000 });
        // "Keep", not "Cancel": the dialog names what cancelling DOES to the
        // executions, which is the right label for a destructive confirm.
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Keep", Exact = true }).ClickAsync();
        await Assertions.Expect(dialog).Not.ToBeVisibleAsync();
        await page.WaitForTimeoutAsync(500);
        Assert.Empty(requests);

        // Then confirm: exactly one request, and the right one.
        await deleteAll.ClickAsync();
        await Assertions.Expect(dialog).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Delete all", Exact = true }).ClickAsync();

        await EventuallyAsync(() => Task.FromResult(requests.Count >= 1), "the delete-all request to be sent");
        await page.WaitForTimeoutAsync(500);
        var sent = Assert.Single(requests);
        Assert.Equal("POST", sent.Method);
        Assert.EndsWith("/api/executions/delete-all", new Uri(sent.Url).AbsolutePath);
    }

    // ---- seeding -------------------------------------------------------------

    private sealed record TaskRow(string Id, string Name, string? Assignee, DateTimeOffset? DueDate);

    /// <summary>
    /// A fresh two-step execution parked at Review, assigned, carrying a variable.
    /// Returns the instance and the Review task the controls act on.
    /// </summary>
    private static async Task<(ExecutionDto Instance, TaskRow ReviewTask)> SeedAtReviewAsync(IAPIRequestContext api)
    {
        var seeder = new ApiSeeder(api);
        var key = $"adm{Guid.NewGuid():N}"[..20];
        await seeder.CreateAndPublishTwoStepWorkflowAsync(key, TestNames.Prefixed("admin"), FirstAssignee);
        var instance = await seeder.StartExecutionAsync(
            key, TestNames.Prefixed("run"),
            new Dictionary<string, object?> { ["amount"] = 42 });

        var tasks = await EventuallyTasksAsync(api, instance.Id,
            t => t.Any(x => x.Name == "Review"), "the Review task to exist");
        return (instance, tasks.Single(t => t.Name == "Review"));
    }

    /// <summary>
    /// #652. Bulk-delete on its EFFECT: the seeded runs are gone from Auton8's
    /// list and from the engine's history. Possible now because every
    /// engine-touching class runs in the sequential collection (#643), so
    /// nothing else is mid-flight when this wipes the engine. It IS
    /// engine-wide -- that is what the control promises -- and the wiring fact
    /// above still proves the dialog's two paths without it.
    /// </summary>
    [Fact]
    public async Task Bulk_delete_removes_every_execution_from_the_list_and_from_the_engine()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;
        var api = page.APIRequest;
        var (first, _) = await SeedAtReviewAsync(api);
        var (second, _) = await SeedAtReviewAsync(api);

        await page.GotoAsync("/workflow-executions");
        var deleteAll = page.GetByRole(AriaRole.Button, new() { Name = "Delete All Executions" });
        await Assertions.Expect(deleteAll).ToBeEnabledAsync(new() { Timeout = 15_000 });
        await deleteAll.ClickAsync();
        var dialog = page.GetByRole(AriaRole.Dialog);
        await Assertions.Expect(dialog).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Delete all", Exact = true }).ClickAsync();

        // Gone from Auton8...
        await EventuallyAsync(async () =>
        {
            var response = await api.GetAsync("/api/executions/");
            if (!response.Ok) return false;
            using var document = JsonDocument.Parse(await response.TextAsync());
            var ids = document.RootElement.EnumerateArray().Select(e => Str(e, "id")).ToHashSet(StringComparer.Ordinal);
            return !ids.Contains(first.Id) && !ids.Contains(second.Id);
        }, "both seeded runs to leave the executions list");

        // ...AND from the engine, by id, in history: 404 is "never existed or
        // deleted", which is the claim.
        using var engine = EngineClient();
        foreach (var id in new[] { first.Id, second.Id })
        {
            var gone = await engine.GetAsync($"service/history/historic-process-instances/{id}");
            Assert.Equal(System.Net.HttpStatusCode.NotFound, gone.StatusCode);
        }
    }

    // ---- read-backs ----------------------------------------------------------

    private static async Task<List<TaskRow>> TasksAsync(IAPIRequestContext api, string instanceId)
    {
        var response = await api.GetAsync($"/api/executions/{instanceId}/tasks");
        Assert.True(response.Ok, await response.TextAsync());
        using var document = JsonDocument.Parse(await response.TextAsync());
        return document.RootElement.EnumerateArray()
            .Select(e => new TaskRow(
                Str(e, "id")!,
                Str(e, "name")!,
                Str(e, "assignee"),
                Has(e, "dueDate") ? DateTimeOffset.Parse(Str(e, "dueDate")!) : null))
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

        Assert.Fail($"Timed out waiting for {what}. Tasks were: "
            + string.Join(", ", tasks.Select(t => $"{t.Name}/{t.Assignee}")));
        return tasks;
    }

    private static async Task<List<string>> CurrentActivitiesAsync(IAPIRequestContext api, string instanceId)
    {
        var response = await api.GetAsync($"/api/executions/{instanceId}/diagram");
        Assert.True(response.Ok, await response.TextAsync());
        using var document = JsonDocument.Parse(await response.TextAsync());
        return document.RootElement.GetProperty("currentActivityIds")
            .EnumerateArray().Select(e => e.GetString()!).OrderBy(s => s, StringComparer.Ordinal).ToList();
    }

    private static async Task<List<string>> EventuallyCurrentActivitiesAsync(
        IAPIRequestContext api, string instanceId, Func<List<string>, bool> until, string what)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        List<string> current = [];
        while (DateTime.UtcNow < deadline)
        {
            current = await CurrentActivitiesAsync(api, instanceId);
            if (until(current)) return current;
            await Task.Delay(500);
        }

        Assert.Fail($"Timed out waiting for {what}. Current activities were: {string.Join(", ", current)}");
        return current;
    }

    private static async Task<List<JsonElement>> EventuallyJsonArrayAsync(
        IAPIRequestContext api, string path, Func<List<JsonElement>, bool> until, string what)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        List<JsonElement> rows = [];
        while (DateTime.UtcNow < deadline)
        {
            var response = await api.GetAsync(path);
            Assert.True(response.Ok, await response.TextAsync());
            rows = JsonDocument.Parse(await response.TextAsync()).RootElement
                .EnumerateArray().Select(e => e.Clone()).ToList();
            if (until(rows)) return rows;
            await Task.Delay(500);
        }

        // The rows themselves, not just their count: a timeout here is almost
        // always a field name the predicate got wrong, and the payload says which.
        Assert.Fail($"Timed out waiting for {what} at {path}. Rows ({rows.Count}):\n"
            + string.Join("\n", rows.Select(r => r.GetRawText())));
        return rows;
    }

    private static async Task EventuallyAsync(Func<Task<bool>> condition, string what)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            if (await condition()) return;
            await Task.Delay(500);
        }

        Assert.Fail($"Timed out waiting for {what}.");
    }

    // ---- the engine, read directly -------------------------------------------

    private static HttpClient EngineClient() => FlowableDeploymentSweep.CreateClient(
        Environment.GetEnvironmentVariable("AUTONATE_FLOWABLE_URL") ?? "http://localhost:8080/flowable-rest",
        Environment.GetEnvironmentVariable("AUTONATE_FLOWABLE_USER") ?? "rest-admin",
        Environment.GetEnvironmentVariable("AUTONATE_FLOWABLE_PASSWORD") ?? "test");

    private static async Task<JsonElement> EngineJsonAsync(HttpClient engine, string path)
    {
        var response = await engine.GetAsync(path);
        Assert.True(response.IsSuccessStatusCode,
            $"{path} -> {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    private static async Task<int> EngineIntVariableAsync(HttpClient engine, string instanceId, string name)
    {
        var variable = await EngineJsonAsync(engine,
            $"service/runtime/process-instances/{instanceId}/variables/{name}");
        return variable.GetProperty("value").GetInt32();
    }

    // ---- JSON helpers, case-tolerant so a casing change fails loudly, not silently

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    private static bool Has(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v)
        && v.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined;

    private static JsonElement Prop(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) ? v : default;

    private static bool Bool(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
}
