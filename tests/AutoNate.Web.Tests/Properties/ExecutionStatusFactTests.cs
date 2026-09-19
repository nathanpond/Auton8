using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.EntityTypes;
using AutoNate.Web.Authorization.Evaluator;
using AutoNate.Web.Authorization.Selectors;
using AutoNate.Web.Endpoints;
using AutoNate.Web.Models;
using AutoNate.Web.Persistence;
using AutoNate.Web.Persistence.Scaffolded;
using AutoNate.Web.Services.Flowable;
using AutoNate.Web.Tests.Properties.Generators;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AutoNate.Web.Tests.Properties;

/// <summary>
/// `status` means the same thing on both evaluation paths, and `tenant` no
/// longer means anything at all (#576).
/// </summary>
/// <remarks>
/// <para>
/// These assert the PRODUCTION fact builders, which is why both were made
/// internal. A test that rebuilt the dictionary itself would pass while
/// <c>BuildFacts</c> still omitted the tag — the exact defect being closed.
/// </para>
/// </remarks>
public sealed class ExecutionStatusFactTests
{
    [Fact]
    public void The_list_endpoint_supplies_status_normalized()
    {
        var facts = ExecutionEndpoints.BuildFacts(new WorkflowExecutionSummary
        {
            Id = "exec-1",
            ProcessDefinitionId = "onboarding:1:1",
            StartUserId = "alice",

            // Flowable's raw vocabulary, deliberately: the projection maps
            // "running" to "active" before writing the column, so a fact builder
            // passing this through untouched is how `[status=running]` would
            // match in memory and nothing in SQL.
            Status = "running"
        });

        Assert.True(facts.ContainsKey("status"), "status must be supplied; before #576 it was not.");
        Assert.Equal(WorkflowExecutionStatuses.Active, facts["status"]);
    }

    /// <summary>
    /// The instance authorizer supplies `status` from the cache row (#576, then #579).
    /// </summary>
    /// <remarks>
    /// <para><b>This assertion was rewritten, not weakened.</b> #576 made this
    /// path supply <c>status</c> by INFERRING it from
    /// <c>FlowableProcessInstanceSummary.Suspended</c>, because the runtime shape
    /// the authorizer read carries no status string and anything that endpoint
    /// returns is still running. Only two states were reachable: suspended and
    /// active.</para>
    ///
    /// <para>#579 moved the authorizer onto <c>IFlowableReadThrough</c>, so it now
    /// reads <c>workflow_execution_cache</c>, which carries the projection's
    /// normalized status. The inference is gone and so is its two-state ceiling —
    /// <c>completed</c>, <c>cancelled</c> and <c>terminated</c> are reachable on
    /// this path for the first time. The old test could not have expressed that,
    /// which is why it is replaced rather than adjusted.</para>
    /// </remarks>
    [Fact]
    public void The_instance_authorizer_supplies_status_from_the_cache_row()
    {
        var completed = WorkflowExecutionInstanceAuthorizer.BuildFacts(
            Row("exec-done", WorkflowExecutionStatuses.Completed));
        var active = WorkflowExecutionInstanceAuthorizer.BuildFacts(
            Row("exec-live", WorkflowExecutionStatuses.Active));

        Assert.Equal(WorkflowExecutionStatuses.Completed, completed["status"]);
        Assert.Equal(WorkflowExecutionStatuses.Active, active["status"]);

        // The complement that matters, and the one the old suspension-flag
        // version could not make: a terminal state is now distinguishable here.
        // Before #579 every instance this path saw was active or suspended.
        Assert.NotEqual(active["status"], completed["status"]);
    }

    private static WorkflowExecutionCache Row(string id, string status) => new()
    {
        FlowableInstanceId = id,
        ProcessDefinitionKey = "onboarding",
        ProcessDefinitionId = "onboarding:1:1",
        Status = status,
        StartedBy = "alice",
        StartTime = DateTime.UtcNow,
        LastSyncAtUtc = DateTime.UtcNow
    };

    /// <summary>
    /// A `[status=…]` grant selects the same rows in SQL as in memory, and
    /// REFUSES the same ones (#576).
    /// </summary>
    /// <remarks>
    /// The complement is the whole test. In-memory denied every row before this
    /// story — no status fact meant no match — so a test asserting only that
    /// the active row passes would have passed against the old code on the SQL
    /// side and failed to notice the in-memory side answering nothing.
    /// </remarks>
    [Fact]
    public async Task A_status_grant_agrees_on_both_paths_in_both_directions()
    {
        await using var app = await AutoNateWebApplicationFactory.CreateAsync();
        _ = app.CreateClient();
        var factory = app.Services.GetRequiredService<IDbContextFactory<AutoNateDbContext>>();

        var active = NewRow("exec-active", "active");
        var completed = NewRow("exec-completed", "completed");

        await using (var seed = await factory.CreateDbContextAsync())
        {
            seed.WorkflowExecutionCache.AddRange(active, completed);
            await seed.SaveChangesAsync();
        }

        var selector = SelectorParser.Parse("/workflowexecution[status=active]");

        await using var db = await factory.CreateDbContextAsync();
        var context = new CompilationContext(db, SelectorGenerators.ActorUserId);
        var predicate = new WorkflowExecutionCacheSelectorCompiler().Compile(selector, context);

        var fromSql = await db.WorkflowExecutionCache.Where(predicate)
            .Select(e => e.FlowableInstanceId).OrderBy(id => id).ToListAsync();

        // The in-memory side goes through the production fact builder, so this
        // compares the two real paths rather than SQL against a local copy.
        var evaluator = new InMemorySelectorEvaluator(SelectorGenerators.ActorUserId);
        var summaries = new[]
        {
            new WorkflowExecutionSummary
            {
                Id = "exec-active", ProcessDefinitionId = "onboarding:1:1",
                StartUserId = "alice", Status = "running"
            },
            new WorkflowExecutionSummary
            {
                Id = "exec-completed", ProcessDefinitionId = "onboarding:1:1",
                StartUserId = "alice", Status = "completed"
            },
        };

        var fromMemory = summaries
            .Where(x => evaluator.Matches(selector, x.Id, ExecutionEndpoints.BuildFacts(x)))
            .Select(x => x.Id)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(["exec-active"], fromSql);
        Assert.Equal(fromMemory, fromSql);

        // Refused, on both paths. This is the direction that was broken.
        Assert.DoesNotContain("exec-completed", fromSql);
        Assert.DoesNotContain("exec-completed", fromMemory);

        // And the in-memory path returns SOMETHING. Before #576 it returned the
        // empty set for every status selector, which trivially satisfies
        // "excludes the completed row".
        Assert.NotEmpty(fromMemory);
    }

    /// <summary>
    /// `tenant` is neither advertised nor compiled (#576).
    /// </summary>
    /// <remarks>
    /// It targeted a column FlowableExecutionProjection always writes as null,
    /// and the model it maps from has no tenant field at all — so
    /// <c>[tenant=x]</c> matched nothing and <c>[tenant=null]</c> matched
    /// everything. Removing it turns a grant that could not mean what it said
    /// into a loud refusal.
    /// </remarks>
    [Fact]
    public async Task The_tenant_tag_is_neither_advertised_nor_compiled()
    {
        Assert.DoesNotContain("tenant", CoreEntityTypes.WorkflowExecution.Tags);

        // Still advertised, so this test cannot pass by the whole tag set
        // having gone missing.
        Assert.Contains("status", CoreEntityTypes.WorkflowExecution.Tags);

        await using var app = await AutoNateWebApplicationFactory.CreateAsync();
        _ = app.CreateClient();
        var factory = app.Services.GetRequiredService<IDbContextFactory<AutoNateDbContext>>();
        await using var db = await factory.CreateDbContextAsync();

        var selector = SelectorParser.Parse("/workflowexecution[tenant=acme]");
        var context = new CompilationContext(db, SelectorGenerators.ActorUserId);

        var ex = Assert.Throws<SelectorCompilationException>(
            () => new WorkflowExecutionCacheSelectorCompiler().Compile(selector, context));

        Assert.Contains("tenant", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static WorkflowExecutionCache NewRow(string id, string status) => new()
    {
        FlowableInstanceId = id,
        ProcessDefinitionKey = "onboarding",
        ProcessDefinitionId = "onboarding:1:1",
        Status = status,
        StartedBy = "alice",
        StartTime = DateTime.UtcNow,
        LastSyncAtUtc = DateTime.UtcNow
    };
}
