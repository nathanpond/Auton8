using AutoNate.Web.Models;
using AutoNate.Web.Persistence;
using AutoNate.Web.Services.Flowable.Cache;
using AutoNate.Web.Services.Projections;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace AutoNate.Web.Tests;

/// <summary>
/// The execution cache carries the run's display name and its model's name (#583).
/// </summary>
/// <remarks>
/// <para>
/// Both fields arrive on <c>WorkflowExecutionSummary</c> from the poll
/// (<c>FlowableClient.cs:365-366</c>) and were discarded by <c>MapRow</c>. The
/// executions list renders <c>name ?? id</c>, so serving it from this table
/// would have shown every run's Flowable id in place of its name — the reason
/// #104, #108 and #579 were all blocked on this.
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
public sealed class ExecutionCacheNameProjectionTests
{
    [Fact]
    public async Task A_projected_row_carries_the_run_name_and_the_model_name()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        _ = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        var projection = scope.ServiceProvider.GetRequiredService<FlowableExecutionProjection>();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AutoNateDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();

        await projection.ApplyAsync(
            [Upsert("named-1", name: "Invoice for Acme", modelName: "Invoice Approval")],
            db, CancellationToken.None);

        var row = await db.WorkflowExecutionCache.AsNoTracking()
            .FirstAsync(c => c.FlowableInstanceId == "named-1");

        Assert.Equal("Invoice for Acme", row.Name);
        Assert.Equal("Invoice Approval", row.WorkflowModelName);
    }

    /// <summary>
    /// "Never had a name" and "not yet re-projected" are different conditions and
    /// are distinguishable in the data (#583).
    /// </summary>
    /// <remarks>
    /// <para>Both render as an id in the UI, which is exactly why they must not be
    /// represented identically in the table: one is the truth about the run, the
    /// other is a row waiting for the next poll. <c>projection_version</c> tells
    /// them apart — a version-2 row was written by code that knows about
    /// <c>name</c>, so its null is authoritative.</para>
    ///
    /// <para>Asserting the version is the point. Without it this test would pass
    /// against a projection that simply never wrote the column.</para>
    /// </remarks>
    [Fact]
    public async Task A_run_with_no_name_is_distinguishable_from_a_row_not_yet_reprojected()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        _ = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        var projection = scope.ServiceProvider.GetRequiredService<FlowableExecutionProjection>();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<FlowableCacheOptions>>().Value;
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AutoNateDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();

        // A run Flowable gives no name for.
        await projection.ApplyAsync(
            [Upsert("unnamed-1", name: null, modelName: "Invoice Approval")],
            db, CancellationToken.None);

        // A row as the previous projection version left it: null name, version 1.
        // `{}` cannot be a literal here: EF parses braces in raw SQL as format
        // placeholders, and in an interpolated raw string they open an
        // interpolation. Passing it as a parameter sidesteps both.
        var emptyTags = "{}";
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO workflow_execution_cache (
                flowable_instance_id, process_definition_key, process_definition_id,
                status, start_time, auth_tags, projection_version, last_sync_at)
            VALUES ('stale-1', 'invoice', 'invoice:1:1', 'active', NOW(),
                    {emptyTags}::jsonb, 1, NOW())
            """);

        var unnamed = await db.WorkflowExecutionCache.AsNoTracking()
            .FirstAsync(c => c.FlowableInstanceId == "unnamed-1");
        var stale = await db.WorkflowExecutionCache.AsNoTracking()
            .FirstAsync(c => c.FlowableInstanceId == "stale-1");

        // Both have a null name — that is the condition being disambiguated.
        Assert.Null(unnamed.Name);
        Assert.Null(stale.Name);

        // And they are still distinguishable, by the only thing that can carry
        // the difference.
        Assert.Equal(options.ExecutionProjectionVersion, unnamed.ProjectionVersion);
        Assert.Equal(1, stale.ProjectionVersion);
        Assert.NotEqual(stale.ProjectionVersion, unnamed.ProjectionVersion);

        // The execution cache versions independently of the other three, which is
        // what makes the assertion above mean anything. If this ever equals the
        // shared version, a bump elsewhere would silently relabel these rows.
        Assert.NotEqual(options.CurrentProjectionVersion, options.ExecutionProjectionVersion);
    }

    /// <summary>
    /// A re-poll fills in a name the previous projection version left null (#583).
    /// </summary>
    /// <remarks>
    /// This is the whole of the migration story. The poll upserts EVERY instance
    /// it returns on every tick, not only new ones, so an existing row gains its
    /// name within one poll interval without any backfill — the backfill source
    /// calls the very same <c>GetWorkflowExecutionsAsync</c> and would re-emit an
    /// identical set.
    /// </remarks>
    [Fact]
    public async Task A_repoll_fills_in_a_name_the_previous_version_left_null()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        _ = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        var projection = scope.ServiceProvider.GetRequiredService<FlowableExecutionProjection>();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AutoNateDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();

        var emptyTags = "{}";
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO workflow_execution_cache (
                flowable_instance_id, process_definition_key, process_definition_id,
                status, start_time, auth_tags, projection_version, last_sync_at)
            VALUES ('repoll-1', 'invoice', 'invoice:1:1', 'active', NOW(),
                    {emptyTags}::jsonb, 1, NOW())
            """);

        var before = await db.WorkflowExecutionCache.AsNoTracking()
            .FirstAsync(c => c.FlowableInstanceId == "repoll-1");
        Assert.Null(before.Name);

        await projection.ApplyAsync(
            [Upsert("repoll-1", name: "Invoice for Acme", modelName: "Invoice Approval")],
            db, CancellationToken.None);

        var after = await db.WorkflowExecutionCache.AsNoTracking()
            .FirstAsync(c => c.FlowableInstanceId == "repoll-1");

        Assert.Equal("Invoice for Acme", after.Name);
        Assert.Equal(2, after.ProjectionVersion);
    }

    /// <summary>
    /// An empty name from Flowable is stored as null, not as "" (#583).
    /// </summary>
    /// <remarks>
    /// A run with no name and a run named "" are the same thing to a reader, and
    /// the UI's `name ?? id` fallback only fires on null — an empty string would
    /// render as a blank label rather than the id.
    /// </remarks>
    [Fact]
    public async Task An_empty_name_is_stored_as_null_so_the_id_fallback_fires()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        _ = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        var projection = scope.ServiceProvider.GetRequiredService<FlowableExecutionProjection>();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AutoNateDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();

        await projection.ApplyAsync(
            [Upsert("blank-1", name: "   ", modelName: "")],
            db, CancellationToken.None);

        var row = await db.WorkflowExecutionCache.AsNoTracking()
            .FirstAsync(c => c.FlowableInstanceId == "blank-1");

        Assert.Null(row.Name);
        Assert.Null(row.WorkflowModelName);
    }

    /// <summary>
    /// The read-through write path carries the name too, and does not blank the
    /// model name the poll wrote (#583).
    /// </summary>
    /// <remarks>
    /// <para>The two write paths must not disagree. They very nearly did: the
    /// projection's upsert writes EVERY column, so anything
    /// <c>FlowableReadThrough</c> leaves unset is written as null over whatever
    /// the poll had put there. <c>FlowableProcessInstanceSummary</c> — the
    /// runtime shape the read-through reads — carries <c>Name</c> but has no
    /// model-name field at all, so a detail view going stale would have silently
    /// erased <c>workflow_model_name</c>.</para>
    ///
    /// <para>The fix carries the cached value forward. This test is the one that
    /// would have caught the erasure, which is why it seeds the row from the poll
    /// path first and then forces a read-through over it.</para>
    /// </remarks>
    [Fact]
    public async Task A_read_through_carries_the_name_and_preserves_the_model_name()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        _ = factory.CreateClient();

        using var scope = factory.Services.CreateScope();
        var projection = scope.ServiceProvider.GetRequiredService<FlowableExecutionProjection>();
        var readThrough = scope.ServiceProvider.GetRequiredService<IFlowableReadThrough>();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AutoNateDbContext>>();

        // 1. The poll path writes the row, model name and all.
        await using (var seed = await dbFactory.CreateDbContextAsync())
        {
            await projection.ApplyAsync(
                [Upsert("both-paths", name: "Invoice for Acme", modelName: "Invoice Approval")],
                seed, CancellationToken.None);

            // Age it past ReadThroughFreshness so the next read is forced live
            // rather than served from cache.
            await seed.Database.ExecuteSqlRawAsync(
                "UPDATE workflow_execution_cache SET last_sync_at = NOW() - INTERVAL '1 hour' "
                + "WHERE flowable_instance_id = 'both-paths'");
        }

        // 2. The runtime shape the read-through will see. Note it has no
        //    model-name field to give.
        factory.FlowableStub.InstancesById["both-paths"] = new FlowableProcessInstanceSummary
        {
            Id = "both-paths",
            Name = "Invoice for Acme",
            ProcessDefinitionId = "invoice:1:1",
            Suspended = false,
            StartUserId = "user-1"
        };

        var row = await readThrough.GetInstanceAsync("both-paths");

        Assert.NotNull(row);
        Assert.Equal("Invoice for Acme", row!.Name);

        // The complement, and the whole reason this test exists: the model name
        // survived a write path that has no way to supply it.
        Assert.Equal("Invoice Approval", row.WorkflowModelName);
    }

    private static ChangeEvent<WorkflowExecutionSummary> Upsert(
        string id, string? name, string? modelName) =>
        new(ChangeOp.Upsert, id,
            new WorkflowExecutionSummary
            {
                Id = id,
                Name = name,
                WorkflowModelName = modelName,
                ProcessDefinitionId = "invoice:1:1",
                Status = "Running",
                StartUserId = "user-1",
                StartedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
                LastActivityAtUtc = DateTimeOffset.UtcNow
            },
            DateTimeOffset.UtcNow);
}
