using System.Security.Claims;
using AutoNate.Web.Models;
using AutoNate.Web.Persistence;
using AutoNate.Web.Persistence.Scaffolded;
using AutoNate.Web.Services.Flowable.Cache;
using AutoNate.Web.Services.Projections;
using AutoNate.Web.Services.Query;
using AutoNate.Web.Services.Query.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AutoNate.Web.Tests;

[Trait("Category", "Integration")]
public sealed class FlowsQueryEntityTests
{
    private static readonly ClaimsPrincipal Actor = new(new ClaimsIdentity(new[]
    {
        new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
        new Claim(ClaimTypes.Name, "test"),
    }, "test"));

    [Fact]
    public async Task User_query_with_WHERE_status_and_CURRENTSTEP_columns()
    {
        // Matches the user-supplied query:
        //   FROM Flows WHERE Status = "In-progress" ORDER BY StartDate
        //     COLUMNS(FlowName, Status, CURRENTSTEP(Name) AS Step,
        //             CURRENTSTEP(assignee) AS AssignedTo)
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        _ = factory.CreateClient();

        var modelId = Guid.NewGuid();
        using (var seedScope = factory.Services.CreateScope())
        {
            var dbFactory = seedScope.ServiceProvider.GetRequiredService<IDbContextFactory<AutoNateDbContext>>();
            var execProjection = seedScope.ServiceProvider.GetRequiredService<FlowableExecutionProjection>();
            var taskProjection = seedScope.ServiceProvider.GetRequiredService<FlowableTaskProjection>();
            await using var db = await dbFactory.CreateDbContextAsync();

            // Seed a published workflow_models row so FlowName JOINs cleanly.
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO workflow_models (
                    id, name, process_key, bpmn_xml, is_draft,
                    draft_version_number, published_version_number,
                    created_at_utc, updated_at_utc)
                VALUES ({modelId}, 'Invoice Approval', 'invoice-approval', '<bpmn/>', false,
                        1, 1, NOW(), NOW())
                """);

            // One running execution + one completed (should be filtered out by WHERE).
            await execProjection.ApplyAsync(new[]
            {
                new ChangeEvent<WorkflowExecutionSummary>(
                    ChangeOp.Upsert, "inst-running",
                    new WorkflowExecutionSummary
                    {
                        Id = "inst-running",
                        StartedAtUtc = DateTimeOffset.UtcNow.AddHours(-1),
                        Status = "Running",
                        ProcessDefinitionId = "invoice-approval:1:abc",
                        StartUserId = "alice"
                    },
                    DateTimeOffset.UtcNow),
                new ChangeEvent<WorkflowExecutionSummary>(
                    ChangeOp.Upsert, "inst-done",
                    new WorkflowExecutionSummary
                    {
                        Id = "inst-done",
                        StartedAtUtc = DateTimeOffset.UtcNow.AddHours(-2),
                        Status = "Complete",
                        ProcessDefinitionId = "invoice-approval:1:abc",
                        StartUserId = "alice"
                    },
                    DateTimeOffset.UtcNow),
            }, db, CancellationToken.None);

            // Open task on the running instance — CURRENTSTEP() should
            // resolve to this row's Name + Assignee.
            await taskProjection.ApplyAsync(new[]
            {
                new ChangeEvent<FlowableTaskSummary>(
                    ChangeOp.Upsert, "tsk-approve",
                    new FlowableTaskSummary
                    {
                        Id = "tsk-approve",
                        Name = "Manager Approval",
                        TaskDefinitionKey = "managerApproval",
                        Assignee = "bob",
                        ProcessInstanceId = "inst-running",
                        ProcessDefinitionId = "invoice-approval:1:abc",
                        CreatedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10)
                    },
                    DateTimeOffset.UtcNow)
            }, db, CancellationToken.None);
        }

        using var scope = factory.Services.CreateScope();
        var entity = scope.ServiceProvider.GetRequiredService<FlowsQueryEntity>();

        var aql = new AqlQuery(
            Entity: "Flows",
            Where: new AqlCompare("Status", "=", new AqlString("In-progress")),
            OrderBy: new[] { new AqlOrderItem(new AqlSelectItem("StartDate", null, null), Descending: false) },
            Columns: new[]
            {
                new AqlSelectItem(Field: "FlowName", AggregateFn: null, AggregateField: null),
                new AqlSelectItem(Field: "Status", AggregateFn: null, AggregateField: null),
                new AqlSelectItem(Field: null, AggregateFn: "CURRENTSTEP", AggregateField: "Name", Alias: "Step"),
                new AqlSelectItem(Field: null, AggregateFn: "CURRENTSTEP", AggregateField: "assignee", Alias: "AssignedTo"),
            },
            Group: null,
            Limit: null);

        var prepared = await entity.PrepareAsync(aql, CancellationToken.None);
        Assert.Empty(prepared.ValidationErrors);

        var result = await prepared.ExecuteAsync(Actor, hardCap: 100, CancellationToken.None);

        Assert.Single(result.Rows);
        var row = result.Rows[0];
        Assert.Equal("Invoice Approval", (string?)row["FlowName"]);
        Assert.Equal("In-progress", (string?)row["Status"]);
        Assert.Equal("Manager Approval", (string?)row["Step"]);
        Assert.Equal("bob", (string?)row["AssignedTo"]);
    }

    [Fact]
    public async Task Status_WHERE_accepts_synonyms_and_normalizes_to_display_form()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        _ = factory.CreateClient();

        using (var seedScope = factory.Services.CreateScope())
        {
            var dbFactory = seedScope.ServiceProvider.GetRequiredService<IDbContextFactory<AutoNateDbContext>>();
            var projection = seedScope.ServiceProvider.GetRequiredService<FlowableExecutionProjection>();
            await using var db = await dbFactory.CreateDbContextAsync();

            await projection.ApplyAsync(new[]
            {
                new ChangeEvent<WorkflowExecutionSummary>(
                    ChangeOp.Upsert, "inst-syn",
                    new WorkflowExecutionSummary
                    {
                        Id = "inst-syn",
                        StartedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-30),
                        Status = "Running",
                        ProcessDefinitionId = "ad-hoc:1:xyz",
                        StartUserId = "carol"
                    },
                    DateTimeOffset.UtcNow)
            }, db, CancellationToken.None);
        }

        using var scope = factory.Services.CreateScope();
        var entity = scope.ServiceProvider.GetRequiredService<FlowsQueryEntity>();

        foreach (var literal in new[] { "In-progress", "active", "Running", "inprogress" })
        {
            var aql = new AqlQuery(
                Entity: "Flows",
                Where: new AqlCompare("Status", "=", new AqlString(literal)),
                OrderBy: Array.Empty<AqlOrderItem>(),
                Columns: null, Group: null, Limit: null);
            var prepared = await entity.PrepareAsync(aql, CancellationToken.None);
            var result = await prepared.ExecuteAsync(Actor, hardCap: 100, CancellationToken.None);
            Assert.True(
                result.Rows.Count >= 1,
                $"Status literal '{literal}' should have matched the running flow.");
        }
    }

    [Fact]
    public async Task First_seen_execution_coalesces_tasks_in_the_same_batch()
    {
        // Regression: a freshly-cached flow used to show empty CURRENTSTEP
        // values until the next FlowableTaskPollingFeed tick (~60s gap).
        // The execution projection now identifies first-seen instances and
        // pulls their tasks into workflow_task_cache in the same batch, so
        // CURRENTSTEP populates immediately.
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        _ = factory.CreateClient();

        // Pre-seed the Flowable stub so the coalesced fetch returns a task
        // for the new instance.
        factory.FlowableStub.TasksByProcess["inst-new"] = new List<FlowableTaskSummary>
        {
            new()
            {
                Id = "tsk-coalesced",
                Name = "Initial Step",
                TaskDefinitionKey = "initialStep",
                Assignee = "harry",
                ProcessInstanceId = "inst-new",
                ProcessDefinitionId = "coalesce-test:1:abc",
                CreatedAtUtc = DateTimeOffset.UtcNow.AddSeconds(-5)
            }
        };

        using (var scope = factory.Services.CreateScope())
        {
            var execProjection = scope.ServiceProvider.GetRequiredService<FlowableExecutionProjection>();
            var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AutoNateDbContext>>();
            await using var db = await dbFactory.CreateDbContextAsync();

            // Apply the execution change — this should trigger the
            // coalesce path because the cache has never seen this instance.
            await execProjection.ApplyAsync(new[]
            {
                new ChangeEvent<WorkflowExecutionSummary>(
                    ChangeOp.Upsert, "inst-new",
                    new WorkflowExecutionSummary
                    {
                        Id = "inst-new",
                        StartedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
                        Status = "Running",
                        ProcessDefinitionId = "coalesce-test:1:abc",
                        StartUserId = "harry"
                    },
                    DateTimeOffset.UtcNow)
            }, db, CancellationToken.None);

            // The coalesced fetch should have landed the task synchronously.
            var taskRow = await db.WorkflowTaskCache.AsNoTracking()
                .FirstOrDefaultAsync(t => t.FlowableTaskId == "tsk-coalesced");
            Assert.NotNull(taskRow);
            Assert.Equal("Initial Step", taskRow!.Name);
            Assert.Equal("harry", taskRow.Assignee);

            // Stub records every call — confirm GetTasksByProcessInstanceAsync
            // fired exactly once for the new id.
            Assert.Contains("TasksByInstance:inst-new", factory.FlowableStub.Calls);
        }

        // End-to-end: an AQL query through Flows with CURRENTSTEP() now
        // returns the populated values, not nulls.
        using var queryScope = factory.Services.CreateScope();
        var entity = queryScope.ServiceProvider.GetRequiredService<FlowsQueryEntity>();
        var aql = new AqlQuery(
            Entity: "Flows",
            Where: new AqlCompare("Id", "=", new AqlString("inst-new")),
            OrderBy: Array.Empty<AqlOrderItem>(),
            Columns: new[]
            {
                new AqlSelectItem(Field: "Id", AggregateFn: null, AggregateField: null),
                new AqlSelectItem(Field: null, AggregateFn: "CURRENTSTEP", AggregateField: "Name"),
                new AqlSelectItem(Field: null, AggregateFn: "CURRENTSTEP", AggregateField: "Assignee"),
            },
            Group: null,
            Limit: null);
        var prepared = await entity.PrepareAsync(aql, CancellationToken.None);
        var result = await prepared.ExecuteAsync(Actor, hardCap: 100, CancellationToken.None);
        Assert.Single(result.Rows);
        Assert.Equal("Initial Step", (string?)result.Rows[0]["CURRENTSTEP(Name)"]);
        Assert.Equal("harry",        (string?)result.Rows[0]["CURRENTSTEP(Assignee)"]);
    }

    [Fact]
    public async Task Coalesce_does_not_re_fetch_for_already_cached_instances()
    {
        // Coverage for the "only first-seen" guard: re-applying the same
        // execution change shouldn't trigger another GetTasksByProcessInstance
        // call, otherwise we'd thrash Flowable on every poll tick.
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        _ = factory.CreateClient();

        factory.FlowableStub.TasksByProcess["inst-repeat"] = new List<FlowableTaskSummary>
        {
            new()
            {
                Id = "tsk-repeat",
                Name = "Step",
                ProcessInstanceId = "inst-repeat",
                ProcessDefinitionId = "repeat-test:1:abc",
                CreatedAtUtc = DateTimeOffset.UtcNow.AddSeconds(-5)
            }
        };

        using var scope = factory.Services.CreateScope();
        var execProjection = scope.ServiceProvider.GetRequiredService<FlowableExecutionProjection>();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AutoNateDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();

        var change = new ChangeEvent<WorkflowExecutionSummary>(
            ChangeOp.Upsert, "inst-repeat",
            new WorkflowExecutionSummary
            {
                Id = "inst-repeat",
                StartedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
                Status = "Running",
                ProcessDefinitionId = "repeat-test:1:abc",
                StartUserId = "iris"
            },
            DateTimeOffset.UtcNow);

        await execProjection.ApplyAsync(new[] { change }, db, CancellationToken.None);
        var firstCallCount = factory.FlowableStub.Calls.Count(c => c == "TasksByInstance:inst-repeat");
        Assert.Equal(1, firstCallCount);

        // Re-apply: instance is now in cache, so coalesce should be a no-op.
        await execProjection.ApplyAsync(new[] { change }, db, CancellationToken.None);
        var afterReapply = factory.FlowableStub.Calls.Count(c => c == "TasksByInstance:inst-repeat");
        Assert.Equal(1, afterReapply);
    }

    [Fact]
    public async Task CURRENTSTEP_accepts_every_supported_arg_in_one_query()
    {
        // Regression: the validator used to reject ANY argument to a row
        // function because the original COUNTCHILDREN-style row functions
        // were parameterless. CURRENTSTEP opts in via
        // IQueryEntity.RowFunctionAcceptsArgument so all seven arg forms
        // pass validation and resolve to the right task field.
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        _ = factory.CreateClient();

        using (var seedScope = factory.Services.CreateScope())
        {
            var dbFactory = seedScope.ServiceProvider.GetRequiredService<IDbContextFactory<AutoNateDbContext>>();
            var execProjection = seedScope.ServiceProvider.GetRequiredService<FlowableExecutionProjection>();
            var taskProjection = seedScope.ServiceProvider.GetRequiredService<FlowableTaskProjection>();
            await using var db = await dbFactory.CreateDbContextAsync();

            await execProjection.ApplyAsync(new[]
            {
                new ChangeEvent<WorkflowExecutionSummary>(
                    ChangeOp.Upsert, "inst-currentstep",
                    new WorkflowExecutionSummary
                    {
                        Id = "inst-currentstep",
                        StartedAtUtc = DateTimeOffset.UtcNow.AddHours(-1),
                        Status = "Running",
                        ProcessDefinitionId = "currentstep-test:1:abc",
                        StartUserId = "freya"
                    },
                    DateTimeOffset.UtcNow)
            }, db, CancellationToken.None);

            await taskProjection.ApplyAsync(new[]
            {
                new ChangeEvent<FlowableTaskSummary>(
                    ChangeOp.Upsert, "tsk-currentstep",
                    new FlowableTaskSummary
                    {
                        Id = "tsk-currentstep",
                        Name = "Review",
                        TaskDefinitionKey = "reviewStep",
                        Assignee = "george",
                        ProcessInstanceId = "inst-currentstep",
                        ProcessDefinitionId = "currentstep-test:1:abc",
                        CreatedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5),
                        DueDate = DateTimeOffset.UtcNow.AddDays(1)
                    },
                    DateTimeOffset.UtcNow)
            }, db, CancellationToken.None);
        }

        using var scope = factory.Services.CreateScope();
        var entity = scope.ServiceProvider.GetRequiredService<FlowsQueryEntity>();

        // Matches the user-reported query that previously failed
        // validation with "CURRENTSTEP() does not take an argument".
        var aql = new AqlQuery(
            Entity: "Flows",
            Where: new AqlCompare("ProcessKey", "=", new AqlString("currentstep-test")),
            OrderBy: Array.Empty<AqlOrderItem>(),
            Columns: new[]
            {
                new AqlSelectItem(Field: "FlowName", AggregateFn: null, AggregateField: null),
                new AqlSelectItem(Field: null, AggregateFn: "CURRENTSTEP", AggregateField: "Name"),
                new AqlSelectItem(Field: null, AggregateFn: "CURRENTSTEP", AggregateField: "Assignee"),
                new AqlSelectItem(Field: null, AggregateFn: "CURRENTSTEP", AggregateField: "ActivityId"),
                new AqlSelectItem(Field: null, AggregateFn: "CURRENTSTEP", AggregateField: "TaskId"),
                new AqlSelectItem(Field: null, AggregateFn: "CURRENTSTEP", AggregateField: "DueDate"),
                new AqlSelectItem(Field: null, AggregateFn: "CURRENTSTEP", AggregateField: "CreatedTime"),
                new AqlSelectItem(Field: null, AggregateFn: "CURRENTSTEP", AggregateField: "Priority"),
            },
            Group: null,
            Limit: null);

        var prepared = await entity.PrepareAsync(aql, CancellationToken.None);
        Assert.Empty(prepared.ValidationErrors);

        var result = await prepared.ExecuteAsync(Actor, hardCap: 100, CancellationToken.None);
        Assert.Single(result.Rows);
        var row = result.Rows[0];
        Assert.Equal("Review",     (string?)row["CURRENTSTEP(Name)"]);
        Assert.Equal("george",     (string?)row["CURRENTSTEP(Assignee)"]);
        Assert.Equal("reviewStep", (string?)row["CURRENTSTEP(ActivityId)"]);
        Assert.Equal("tsk-currentstep", (string?)row["CURRENTSTEP(TaskId)"]);
        Assert.NotNull(row["CURRENTSTEP(DueDate)"]);
        Assert.NotNull(row["CURRENTSTEP(CreatedTime)"]);
        Assert.Null(row["CURRENTSTEP(Priority)"]);  // not set on the seeded task
    }

    [Fact]
    public async Task Errored_status_overlays_running_when_error_row_exists_but_cancelled_wins()
    {
        // Precedence per ExecutionEndpoints: Cancelled > Errored > base.
        // Seed three executions, attach error rows to two of them — the
        // running one should flip to "Errored", the cancelled one should
        // stay "Cancelled" even though it has an error.
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        _ = factory.CreateClient();

        using (var seedScope = factory.Services.CreateScope())
        {
            var dbFactory = seedScope.ServiceProvider.GetRequiredService<IDbContextFactory<AutoNateDbContext>>();
            var projection = seedScope.ServiceProvider.GetRequiredService<FlowableExecutionProjection>();
            await using var db = await dbFactory.CreateDbContextAsync();

            await projection.ApplyAsync(new[]
            {
                new ChangeEvent<WorkflowExecutionSummary>(
                    ChangeOp.Upsert, "inst-running-err",
                    new WorkflowExecutionSummary
                    {
                        Id = "inst-running-err",
                        StartedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-30),
                        Status = "Running",
                        ProcessDefinitionId = "err-test:1:abc",
                        StartUserId = "ed"
                    },
                    DateTimeOffset.UtcNow),
                new ChangeEvent<WorkflowExecutionSummary>(
                    ChangeOp.Upsert, "inst-cancelled-err",
                    new WorkflowExecutionSummary
                    {
                        Id = "inst-cancelled-err",
                        StartedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-40),
                        Status = "Cancelled",
                        ProcessDefinitionId = "err-test:1:abc",
                        StartUserId = "ed"
                    },
                    DateTimeOffset.UtcNow),
                new ChangeEvent<WorkflowExecutionSummary>(
                    ChangeOp.Upsert, "inst-running-clean",
                    new WorkflowExecutionSummary
                    {
                        Id = "inst-running-clean",
                        StartedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-20),
                        Status = "Running",
                        ProcessDefinitionId = "err-test:1:abc",
                        StartUserId = "ed"
                    },
                    DateTimeOffset.UtcNow),
            }, db, CancellationToken.None);

            // Insert error rows for two of the three instances.
            foreach (var instanceId in new[] { "inst-running-err", "inst-cancelled-err" })
            {
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO workflow_execution_errors (
                        id, process_instance_id, activity_id, occurred_at_utc)
                    VALUES ({Guid.NewGuid()}, {instanceId}, 'someActivity', NOW())
                    """);
            }
        }

        using var scope = factory.Services.CreateScope();
        var entity = scope.ServiceProvider.GetRequiredService<FlowsQueryEntity>();

        // Pull all three and assert per-id statuses.
        var aql = new AqlQuery(
            Entity: "Flows",
            Where: new AqlCompare("ProcessKey", "=", new AqlString("err-test")),
            OrderBy: Array.Empty<AqlOrderItem>(),
            Columns: new[]
            {
                new AqlSelectItem(Field: "Id", AggregateFn: null, AggregateField: null),
                new AqlSelectItem(Field: "Status", AggregateFn: null, AggregateField: null),
            },
            Group: null,
            Limit: null);

        var prepared = await entity.PrepareAsync(aql, CancellationToken.None);
        var result = await prepared.ExecuteAsync(Actor, hardCap: 100, CancellationToken.None);

        var byId = result.Rows.ToDictionary(r => (string)r["Id"]!, r => (string?)r["Status"]);
        Assert.Equal("Errored",   byId["inst-running-err"]);
        Assert.Equal("Cancelled", byId["inst-cancelled-err"]);  // Cancelled wins over Errored
        Assert.Equal("In-progress", byId["inst-running-clean"]);

        // WHERE Status = "Errored" should match only the running+errored one.
        var aqlFiltered = aql with
        {
            Where = new AqlBinary("AND",
                new AqlCompare("ProcessKey", "=", new AqlString("err-test")),
                new AqlCompare("Status", "=", new AqlString("Errored")))
        };
        var filtered = await (await entity.PrepareAsync(aqlFiltered, CancellationToken.None))
            .ExecuteAsync(Actor, hardCap: 100, CancellationToken.None);
        Assert.Single(filtered.Rows);
        Assert.Equal("inst-running-err", (string?)filtered.Rows[0]["Id"]);
    }

    [Fact]
    public async Task Group_by_status_with_COUNT_aggregate_and_aggregate_ORDER_BY()
    {
        // Matches the user-supplied aggregate query:
        //   FROM Flows ORDER BY COUNT() COLUMNS(Status, COUNT() As Count) GROUP(Status)
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        _ = factory.CreateClient();

        using (var seedScope = factory.Services.CreateScope())
        {
            var dbFactory = seedScope.ServiceProvider.GetRequiredService<IDbContextFactory<AutoNateDbContext>>();
            var projection = seedScope.ServiceProvider.GetRequiredService<FlowableExecutionProjection>();
            await using var db = await dbFactory.CreateDbContextAsync();

            // 3 running + 2 completed + 1 cancelled across one process.
            var seeds = new[]
            {
                ("inst-r-1", "Running"),
                ("inst-r-2", "Running"),
                ("inst-r-3", "Running"),
                ("inst-c-1", "Complete"),
                ("inst-c-2", "Complete"),
                ("inst-x-1", "Cancelled"),
            };
            await projection.ApplyAsync(
                seeds.Select((s, i) => new ChangeEvent<WorkflowExecutionSummary>(
                    ChangeOp.Upsert, s.Item1,
                    new WorkflowExecutionSummary
                    {
                        Id = s.Item1,
                        StartedAtUtc = DateTimeOffset.UtcNow.AddHours(-i),
                        Status = s.Item2,
                        ProcessDefinitionId = "agg-test:1:abc",
                        StartUserId = "dana"
                    },
                    DateTimeOffset.UtcNow)).ToArray(),
                db, CancellationToken.None);
        }

        using var scope = factory.Services.CreateScope();
        var entity = scope.ServiceProvider.GetRequiredService<FlowsQueryEntity>();

        var aql = new AqlQuery(
            Entity: "Flows",
            Where: null,
            OrderBy: new[]
            {
                new AqlOrderItem(
                    new AqlSelectItem(Field: null, AggregateFn: "COUNT", AggregateField: null),
                    Descending: false)
            },
            Columns: new[]
            {
                new AqlSelectItem(Field: "Status", AggregateFn: null, AggregateField: null),
                new AqlSelectItem(Field: null, AggregateFn: "COUNT", AggregateField: null, Alias: "Count"),
            },
            Group: new[] { "Status" },
            Limit: null);

        var prepared = await entity.PrepareAsync(aql, CancellationToken.None);
        Assert.Empty(prepared.ValidationErrors);

        var result = await prepared.ExecuteAsync(Actor, hardCap: 100, CancellationToken.None);
        Assert.Equal(3, result.Rows.Count);

        // ORDER BY COUNT() ASC → Cancelled(1), Completed(2), In-progress(3).
        Assert.Equal("Cancelled",   (string?)result.Rows[0]["Status"]);
        Assert.Equal(1L,            (long?)result.Rows[0]["Count"]);
        Assert.Equal("Completed",   (string?)result.Rows[1]["Status"]);
        Assert.Equal(2L,            (long?)result.Rows[1]["Count"]);
        Assert.Equal("In-progress", (string?)result.Rows[2]["Status"]);
        Assert.Equal(3L,            (long?)result.Rows[2]["Count"]);
    }
}
