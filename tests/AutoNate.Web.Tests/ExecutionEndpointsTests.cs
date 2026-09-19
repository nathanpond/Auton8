using System.Net;
using System.Net.Http.Json;
using AutoNate.Web.Endpoints;
using AutoNate.Web.Models;
using AutoNate.Web.Services.Flowable;
using AutoNate.Web.Services.Workflow;
using Xunit;

namespace AutoNate.Web.Tests;

[Trait("Category", "Integration")]
public sealed class ExecutionEndpointsTests
{
    [Fact]
    public async Task ListExecutions_ReturnsStubbedEmptyList()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();

        var executions = await client.GetFromJsonAsync<WorkflowExecutionSummary[]>(
            "/api/executions/");

        Assert.NotNull(executions);
        Assert.Empty(executions);
        // "ListExecutions:1" -- the stub records the page ceiling the caller asked
        // for (#588). This endpoint still asks for ONE page, which is today's
        // behaviour preserved by the `maxPages: 1` default; the poll feed and the
        // backfill are the two callers that ask for more. Asserting the ceiling
        // rather than the bare call means a change to what this endpoint fetches
        // shows up here instead of silently.
        Assert.Contains("ListExecutions:1", factory.FlowableStub.Calls);
    }

    [Fact]
    public async Task GetDiagram_DelegatesToFlowableClient()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/executions/inst-123/diagram");
        response.EnsureSuccessStatusCode();

        Assert.Contains("Diagram:inst-123", factory.FlowableStub.Calls);
    }

    [Fact]
    public async Task GetHistory_DelegatesToFlowableClient()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        factory.FlowableStub.HistoryByInstance["inst-hist"] = new List<WorkflowExecutionHistoryEvent>
        {
            new() { ActivityId = "startEvent_1", ActivityType = "startEvent", StartedAtUtc = DateTimeOffset.UtcNow },
            new() { ActivityId = "userTask_review", ActivityName = "Review", ActivityType = "userTask", Assignee = "alice" }
        };
        var client = factory.CreateClient();

        var events = await client.GetFromJsonAsync<WorkflowExecutionHistoryEvent[]>(
            "/api/executions/inst-hist/history");

        Assert.NotNull(events);
        Assert.Equal(2, events!.Length);
        Assert.Equal("startEvent_1", events[0].ActivityId);
        Assert.Equal("alice", events[1].Assignee);
        Assert.Contains("History:inst-hist", factory.FlowableStub.Calls);
    }

    [Fact]
    public async Task GetLog_DelegatesToFlowableClient()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        factory.FlowableStub.LogByInstance["inst-log"] = new List<WorkflowExecutionLogEntry>
        {
            new()
            {
                Kind = "variable-update",
                OccurredAtUtc = DateTimeOffset.UtcNow,
                VariableUpdate = new WorkflowExecutionLogVariableUpdate { Name = "amount", Value = "200", Revision = 2 }
            },
            new()
            {
                Kind = "task-completed",
                OccurredAtUtc = DateTimeOffset.UtcNow.AddMinutes(1),
                Task = new WorkflowExecutionLogTask { TaskId = "t-1", Name = "Approve", Assignee = "alice" }
            }
        };
        var client = factory.CreateClient();

        var entries = await client.GetFromJsonAsync<WorkflowExecutionLogEntry[]>(
            "/api/executions/inst-log/log");

        Assert.NotNull(entries);
        Assert.Equal(2, entries!.Length);
        Assert.Equal("variable-update", entries[0].Kind);
        Assert.Equal("amount", entries[0].VariableUpdate?.Name);
        Assert.Equal("task-completed", entries[1].Kind);
        Assert.Equal("alice", entries[1].Task?.Assignee);
        Assert.Contains("Log:inst-log", factory.FlowableStub.Calls);
    }

    [Fact]
    public async Task GetTasksByInstance_DelegatesToFlowableClient()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();

        var tasks = await client.GetFromJsonAsync<FlowableTaskSummary[]>(
            "/api/executions/inst-456/tasks");

        Assert.NotNull(tasks);
        Assert.Contains("TasksByInstance:inst-456", factory.FlowableStub.Calls);
    }

    [Fact]
    public async Task DeleteExecution_Returns204AndCallsClient()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();

        // Prime auth so the DELETE goes through.
        (await client.GetAsync("/api/executions/")).EnsureSuccessStatusCode();

        var response = await client.DeleteAsync("/api/executions/inst-789");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Contains("DeleteExecution:inst-789", factory.FlowableStub.Calls);
    }

    [Fact]
    public async Task CancelExecution_Returns204AndCallsClient()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        (await client.GetAsync("/api/executions/")).EnsureSuccessStatusCode();

        var response = await client.PostAsync("/api/executions/inst-cancel/cancel", content: null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Contains("CancelExecution:inst-cancel", factory.FlowableStub.Calls);
    }

    [Fact]
    public async Task DeleteAllExecutions_Returns200WithCount_AndCallsClient()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        factory.FlowableStub.DeleteAllWorkflowExecutionsResult = 7;
        var client = factory.CreateClient();
        (await client.GetAsync("/api/executions/")).EnsureSuccessStatusCode();

        var response = await client.PostAsync("/api/executions/delete-all", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<DeleteAllResponse>();
        Assert.NotNull(body);
        Assert.Equal(7, body!.Deleted);
        Assert.Contains("DeleteAllExecutions", factory.FlowableStub.Calls);
    }

    private sealed record DeleteAllResponse(int Deleted);

    [Fact]
    public async Task TasksAssignedToMe_PassesActorIdToClient()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();

        var tasks = await client.GetFromJsonAsync<FlowableTaskSummary[]>(
            "/api/tasks/assigned-to-me");

        Assert.NotNull(tasks);
        Assert.Contains(factory.FlowableStub.Calls, c => c.StartsWith("TasksForUser:"));
    }

    [Fact]
    public async Task CompleteTask_Returns204AndCallsClient()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        (await client.GetAsync("/api/tasks/assigned-to-me")).EnsureSuccessStatusCode();

        var response = await client.PostAsJsonAsync(
            "/api/tasks/task-1/complete",
            new ExecutionEndpoints.CompleteTaskRequest(null));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Contains("CompleteTask:task-1", factory.FlowableStub.Calls);
    }

    [Fact]
    public async Task AddProcessVariables_AsksTheEngineToReevaluateConditions()
    {
        // The add path is a separate endpoint from the update path — Flowable's
        // REST API splits create and update — so it needs its own assertion or the
        // two drift.
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        (await client.GetAsync("/api/executions/")).EnsureSuccessStatusCode();

        var response = await client.PostAsJsonAsync(
            "/api/executions/inst-eval/variables",
            new ExecutionEndpoints.UpdateProcessVariablesRequest(new[]
            {
                new ProcessVariableUpdate { Name = "approved", Value = true, Type = "boolean" }
            }));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var calls = factory.FlowableStub.Calls;
        var added = calls.IndexOf("AddVariables:inst-eval");
        var evaluated = calls.IndexOf("EvaluateConditionalEvents:inst-eval");
        Assert.True(evaluated >= 0, "The conditional events were never re-evaluated after the write.");
        Assert.True(added < evaluated, "Conditions were evaluated before the variable was written.");
    }

    [Fact]
    public async Task UpdateProcessVariables_Returns204AndCallsClient()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        (await client.GetAsync("/api/executions/")).EnsureSuccessStatusCode();

        var response = await client.PutAsJsonAsync(
            "/api/executions/inst-vars/variables",
            new ExecutionEndpoints.UpdateProcessVariablesRequest(new[]
            {
                new ProcessVariableUpdate { Name = "amount", Value = 42, Type = "integer" },
                new ProcessVariableUpdate { Name = "label", Value = "ok" }
            }));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Contains("UpdateVariables:inst-vars", factory.FlowableStub.Calls);

        // #158: and the engine is asked to re-evaluate conditional events, in that
        // order. Flowable does not do it on a variable change, so without this a
        // process parked on `${approved == true}` stays parked after someone sets
        // approved here — the feature looks broken and nothing says why.
        //
        // Asserted as an ORDER, not just presence: evaluating before the write
        // would evaluate the old values and be silently useless.
        var calls = factory.FlowableStub.Calls;
        var wrote = calls.IndexOf("UpdateVariables:inst-vars");
        var evaluated = calls.IndexOf("EvaluateConditionalEvents:inst-vars");
        Assert.True(evaluated >= 0, "The conditional events were never re-evaluated after the write.");
        Assert.True(wrote < evaluated, "Conditions were evaluated before the variable was written.");

        Assert.True(factory.FlowableStub.VariableUpdatesByInstance.TryGetValue("inst-vars", out var captured));
        Assert.Equal(2, captured!.Count);
        Assert.Equal("amount", captured[0].Name);
        Assert.Equal("integer", captured[0].Type);
    }

    [Fact]
    public async Task AddProcessVariables_Returns204AndCallsClient()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        (await client.GetAsync("/api/executions/")).EnsureSuccessStatusCode();

        var response = await client.PostAsJsonAsync(
            "/api/executions/inst-add/variables",
            new ExecutionEndpoints.UpdateProcessVariablesRequest(new[]
            {
                new ProcessVariableUpdate { Name = "newAmount", Value = 7, Type = "integer" },
                new ProcessVariableUpdate { Name = "newLabel", Value = "hello", Type = "string" }
            }));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Contains("AddVariables:inst-add", factory.FlowableStub.Calls);
        Assert.True(factory.FlowableStub.VariableAdditionsByInstance.TryGetValue("inst-add", out var captured));
        Assert.Equal(2, captured!.Count);
        Assert.Equal("newAmount", captured[0].Name);
        Assert.Equal("integer", captured[0].Type);
    }

    [Fact]
    public async Task ForceCompleteTask_Returns204AndCallsClient()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        (await client.GetAsync("/api/executions/")).EnsureSuccessStatusCode();

        var response = await client.PostAsJsonAsync(
            "/api/executions/inst-fc/tasks/task-fc/force-complete",
            new ExecutionEndpoints.CompleteTaskRequest(null));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Contains("CompleteTask:task-fc", factory.FlowableStub.Calls);
    }

    [Fact]
    public async Task ReassignTask_Returns204AndCallsClient()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        (await client.GetAsync("/api/executions/")).EnsureSuccessStatusCode();

        var response = await client.PostAsJsonAsync(
            "/api/executions/inst-rs/tasks/task-rs/reassign",
            new ExecutionEndpoints.ReassignTaskRequest("alice"));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Contains("UpdateTaskAssignee:task-rs:alice", factory.FlowableStub.Calls);
        Assert.Equal("alice", factory.FlowableStub.TaskAssigneesByTaskId["task-rs"]);
    }

    [Fact]
    public async Task ReassignTask_NullAssignee_DelegatesAsClear()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        (await client.GetAsync("/api/executions/")).EnsureSuccessStatusCode();

        var response = await client.PostAsJsonAsync(
            "/api/executions/inst-clr/tasks/task-clr/reassign",
            new ExecutionEndpoints.ReassignTaskRequest(null));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Contains("UpdateTaskAssignee:task-clr:(null)", factory.FlowableStub.Calls);
        Assert.Null(factory.FlowableStub.TaskAssigneesByTaskId["task-clr"]);
    }

    [Fact]
    public async Task UpdateTaskDueDate_Returns204AndCallsClient()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        (await client.GetAsync("/api/executions/")).EnsureSuccessStatusCode();

        var due = new DateTimeOffset(2030, 5, 6, 7, 8, 9, TimeSpan.Zero);
        var response = await client.PostAsJsonAsync(
            "/api/executions/inst-dd/tasks/task-dd/due-date",
            new ExecutionEndpoints.UpdateTaskDueDateRequest(due));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Contains(factory.FlowableStub.Calls, c => c.StartsWith("UpdateTaskDueDate:task-dd:"));
        Assert.Equal(due, factory.FlowableStub.TaskDueDatesByTaskId["task-dd"]);
    }

    [Fact]
    public async Task UpdateTaskDueDate_NullDate_DelegatesAsClear()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        (await client.GetAsync("/api/executions/")).EnsureSuccessStatusCode();

        var response = await client.PostAsJsonAsync(
            "/api/executions/inst-dd2/tasks/task-dd2/due-date",
            new ExecutionEndpoints.UpdateTaskDueDateRequest(null));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Contains("UpdateTaskDueDate:task-dd2:(null)", factory.FlowableStub.Calls);
        Assert.Null(factory.FlowableStub.TaskDueDatesByTaskId["task-dd2"]);
    }

    [Fact]
    public async Task MoveExecutionState_Returns204AndCallsClient()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        (await client.GetAsync("/api/executions/")).EnsureSuccessStatusCode();

        var response = await client.PostAsJsonAsync(
            "/api/executions/inst-mv/move-state",
            new ExecutionEndpoints.MoveExecutionStateRequest("userTask_review"));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Contains("MoveExecutionState:inst-mv:userTask_review", factory.FlowableStub.Calls);
        Assert.Contains(factory.FlowableStub.MoveExecutionStateCalls,
            c => c.ProcessInstanceId == "inst-mv" && c.TargetActivityId == "userTask_review");
    }

    [Fact]
    public async Task MoveExecutionState_EmptyTarget_Returns400()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        (await client.GetAsync("/api/executions/")).EnsureSuccessStatusCode();

        var response = await client.PostAsJsonAsync(
            "/api/executions/inst-mv2/move-state",
            new ExecutionEndpoints.MoveExecutionStateRequest(""));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain(factory.FlowableStub.Calls, c => c.StartsWith("MoveExecutionState:"));
    }

    [Fact]
    public async Task GetCompletedAssignees_DelegatesToFlowableClient()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        factory.FlowableStub.CompletedAssigneesByActivity[("inst-q", "userTask_review")]
            = new List<string> { "alice", "bob" };
        var client = factory.CreateClient();

        var assignees = await client.GetFromJsonAsync<string[]>(
            "/api/executions/inst-q/activities/userTask_review/completed-assignees");

        Assert.NotNull(assignees);
        Assert.Equal(new[] { "alice", "bob" }, assignees);
        Assert.Contains("CompletedAssignees:inst-q:userTask_review", factory.FlowableStub.Calls);
    }

    // ── #226: caller errors on the variable endpoints ────────────────────────
    //
    // This is the surface an operator uses to unstick a process — #112 points
    // people straight at it. A 500 while doing that is the wrong signal, and a
    // 500 is what pages someone.

    [Fact]
    public async Task PostVariables_WithNoVariablesInTheBody_Returns400()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await PrimeAuthAsync(client);

        // `variables` deserialises to null and the handler dereferenced it, so a
        // malformed body came back as a 500 NullReferenceException.
        var response = await client.PostAsJsonAsync(
            "/api/executions/proc-1/variables", new { escalate = true });

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("variables", await response.Content.ReadAsStringAsync(),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PostVariables_WhenFlowableReportsAConflict_PassesThe409Through()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        factory.FlowableStub.AddVariablesFailure = new FlowableRequestException(
            System.Net.HttpStatusCode.Conflict,
            "create the process variables",
            "Flowable could not create the process variables. HTTP 409 Conflict. " +
            "Variable 'escalate' is already present on execution 'proc-1'.");

        var client = factory.CreateClient();
        await PrimeAuthAsync(client);

        var response = await client.PostAsJsonAsync(
            "/api/executions/proc-1/variables",
            new { variables = new[] { new { name = "escalate", value = "true", type = "string" } } });

        // Flowable classified this correctly. Re-wrapping it as a 500 threw that
        // away and turned a typo into an incident. That is what this row is for,
        // and it still holds.
        Assert.Equal(System.Net.HttpStatusCode.Conflict, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();

        // #350 changed what the BODY carries. It used to be the engine's entire
        // HTTP response -- the same field that has been observed carrying a JDBC
        // URL with its password -- so it is described rather than forwarded.
        Assert.DoesNotContain("Flowable could not", body, StringComparison.Ordinal);

        // And #354 gave that description real content again. For one round this
        // read "the reason is in the server log", which was the price of the
        // sanitisation and was worse than what operators had.
        Assert.Contains("already set on this step", body, StringComparison.Ordinal);

        // Still nothing the engine wrote -- not even the execution id, which the
        // caller already knows because it is in their own request URL.
        Assert.DoesNotContain("proc-1'", body, StringComparison.Ordinal);
        Assert.DoesNotContain("escalate'", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PostVariables_WhenFlowableItselfFaults_StaysA500()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        factory.FlowableStub.AddVariablesFailure = new FlowableRequestException(
            System.Net.HttpStatusCode.InternalServerError,
            "create the process variables",
            "Flowable could not create the process variables. HTTP 500 .");

        var client = factory.CreateClient();
        await PrimeAuthAsync(client);

        var response = await client.PostAsJsonAsync(
            "/api/executions/proc-1/variables",
            new { variables = new[] { new { name = "escalate", value = "true", type = "string" } } });

        // The complement. Passing every Flowable failure through would relabel a
        // genuine engine fault as the caller's fault and stop it paging anyone —
        // the same defect pointing the other way.
        Assert.Equal(System.Net.HttpStatusCode.InternalServerError, response.StatusCode);
    }

    // The mutating endpoints below are gated; the GETs in this file are not, so
    // this file had no priming until #226 added a POST.
    private static async Task PrimeAuthAsync(HttpClient client)
    {
        (await client.GetAsync("/api/workflows/")).EnsureSuccessStatusCode();
    }

    /// <summary>
    /// The write is recorded even when the nudge that follows it explodes (#376, #382).
    /// </summary>
    /// <remarks>
    /// <para>
    /// #376 moved <c>auditPublisher.PublishAsync</c> ahead of the throwing
    /// conditional-event nudge on both <c>/variables</c> routes, because ordered
    /// the other way a nudge failure meant the variables were written, the caller
    /// got a 500, and <b>nothing recorded that the write happened</b>.
    /// </para>
    /// <para>
    /// It shipped with no guard at all: reverting the reorder left 30/30 green.
    /// The two assertions nearby pin <c>write &lt; evaluate</c>, which is true in
    /// BOTH orderings, and the stub's nudge could not fail, so the scenario the
    /// fix exists for could not be expressed (#382).
    /// </para>
    /// <para>
    /// This is the complement: not "the audit event was published" (true either
    /// way on the happy path) but "the audit event survives the failure of the
    /// step that comes after it". Both routes, because #293 and #360 each found a
    /// path an enumeration had missed by checking one of a pair.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("PUT", "inst-boom-put", WorkflowAdminEventTypes.ExecutionVariablesSet)]
    [InlineData("POST", "inst-boom-post", WorkflowAdminEventTypes.ExecutionVariablesAdded)]
    public async Task A_failing_nudge_does_not_lose_the_record_of_the_write(
        string method, string instanceId, string expectedEventType)
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        (await client.GetAsync("/api/executions/")).EnsureSuccessStatusCode();

        factory.RecordedAuditEvents.Clear();
        factory.FlowableStub.EvaluateConditionalEventsThrows =
            new InvalidOperationException("the engine refused to re-evaluate");

        var body = new ExecutionEndpoints.UpdateProcessVariablesRequest(new[]
        {
            new ProcessVariableUpdate { Name = "approved", Value = true, Type = "boolean" }
        });

        var response = method == "PUT"
            ? await client.PutAsJsonAsync($"/api/executions/{instanceId}/variables", body)
            : await client.PostAsJsonAsync($"/api/executions/{instanceId}/variables", body);

        // The nudge is the throwing overload on purpose (Outcome 10), so the
        // caller is told. What must NOT happen is the write going unrecorded.
        Assert.NotEqual(HttpStatusCode.NoContent, response.StatusCode);

        var calls = factory.FlowableStub.Calls;
        Assert.Contains($"EvaluateConditionalEvents:{instanceId}", calls);

        var recorded = factory.RecordedAuditEvents.Events
            .Where(e => e.EventType == expectedEventType)
            .ToList();

        Assert.True(
            recorded.Count == 1,
            $"The {method} /variables route wrote the variables and then failed on the "
            + $"nudge, and published {recorded.Count} '{expectedEventType}' audit events. "
            + "It must publish exactly one BEFORE the nudge: the event is the record "
            + "that the write happened, and the write has already happened by then. "
            + "If this is red, the audit publish moved back below "
            + "EvaluateConditionalEventsAsync (#376, #382).");
    }

}
