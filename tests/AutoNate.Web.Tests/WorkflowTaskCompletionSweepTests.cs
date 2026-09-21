using AutoNate.Web.Models;
using AutoNate.Web.Persistence;
using AutoNate.Web.Persistence.Scaffolded;
using AutoNate.Web.Services.Flowable.Cache;
using AutoNate.Web.Services.Projections;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AutoNate.Web.Tests;

/// <summary>
/// The task cache learns that a task finished (#586).
/// </summary>
/// <remarks>
/// <para>
/// Before this, nothing ever told it. <c>FlowableTaskProjection.MapRow</c> writes
/// <c>CompletedTime = null</c> and <c>Status = "active"</c> unconditionally and no
/// producer emits a task delete, so a completed task's row sat in the
/// <c>active</c> state until retention removed it by process age — 2555 days.
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
public sealed class WorkflowTaskCompletionSweepTests
{
    [Fact]
    public async Task A_finished_task_is_marked_completed_in_the_cache()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        _ = factory.CreateClient();

        await SeedActiveTaskAsync(factory, "task-done", "inst-1");
        var endedAt = DateTimeOffset.UtcNow.AddMinutes(-2);
        factory.FlowableStub.FinishedTasks.Add(new FlowableFinishedTask
        {
            Id = "task-done", EndedAtUtc = endedAt, ProcessInstanceId = "inst-1"
        });

        var marked = await SweepAsync(factory);

        Assert.Equal(1, marked);
        var row = await ReadTaskAsync(factory, "task-done");
        Assert.Equal("completed", row.Status);
        Assert.NotNull(row.CompletedTime);
    }

    /// <summary>
    /// An open task is untouched, and a failed sweep cannot change that (#586).
    /// </summary>
    /// <remarks>
    /// <para>This is the complement the story asks for, and it holds here <b>by
    /// construction</b> rather than by care: completion is read from Flowable's
    /// history as a positive fact, so a sweep that stops early has simply marked
    /// fewer things. Nothing is inferred from a task's absence, which is the
    /// inference the cheaper "diff the runtime sweep" design would have rested
    /// on.</para>
    ///
    /// <para>The test drives it anyway — "by construction" is a claim a test
    /// should hold the code to, and a future change to a diff-based sweep would
    /// break here rather than in production.</para>
    /// </remarks>
    [Fact]
    public async Task A_sweep_that_fails_midway_leaves_open_tasks_open()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        _ = factory.CreateClient();

        await SeedActiveTaskAsync(factory, "task-open", "inst-1");
        await SeedActiveTaskAsync(factory, "task-also-open", "inst-1");

        // Flowable reports NOTHING as finished, and then falls over.
        factory.FlowableStub.GetFinishedTasksThrows = new HttpRequestException("connection reset");
        factory.FlowableStub.GetFinishedTasksThrowsOnPage = 0;

        using var scope = factory.Services.CreateScope();
        var sweep = scope.ServiceProvider.GetRequiredService<WorkflowTaskCompletionSweep>();
        await Assert.ThrowsAsync<HttpRequestException>(() => sweep.RunOnceAsync(CancellationToken.None));

        foreach (var id in new[] { "task-open", "task-also-open" })
        {
            var row = await ReadTaskAsync(factory, id);
            Assert.Equal("active", row.Status);
            Assert.Null(row.CompletedTime);
        }
    }

    /// <summary>
    /// The sweep stops once a page tells it nothing new, and does not walk on (#586).
    /// </summary>
    /// <remarks>
    /// <para>This is what keeps a steady-state tick cheap without a server-side
    /// time filter — which Flowable does not offer here: `finishedAfter` is
    /// ignored on this endpoint, verified against a live engine.
    /// `sort=endTime&amp;order=desc` is honoured, so a page that marks nothing
    /// means everything beyond it finished earlier still and is already known.</para>
    ///
    /// <para><b>The page size is forced down to 2, and that is the point of the
    /// test.</b> The first version seeded a single finished task and asserted one
    /// page was fetched — which held whatever the stop condition did, because a
    /// 1-row page is shorter than the 200-row default and the short-page break
    /// fired first. Removing the stop condition entirely did not fail it. With a
    /// FULL page that marks nothing, only the stop condition can end the sweep.</para>
    /// </remarks>
    [Fact]
    public async Task A_second_sweep_stops_on_a_full_page_that_marks_nothing()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(
            extraConfig: new Dictionary<string, string?> { ["FlowableCache:TaskPageSize"] = "2" });
        _ = factory.CreateClient();

        // Three finished tasks, so page 0 comes back FULL (2 of 2) and a sweep
        // that ignores its stop condition would go on to ask for page 1.
        for (var i = 0; i < 3; i++)
        {
            await SeedActiveTaskAsync(factory, $"task-{i}", "inst-1");
            factory.FlowableStub.FinishedTasks.Add(new FlowableFinishedTask
            {
                Id = $"task-{i}",
                EndedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-i),
                ProcessInstanceId = "inst-1"
            });
        }

        Assert.Equal(3, await SweepAsync(factory));

        factory.FlowableStub.Calls.Clear();
        var second = await SweepAsync(factory);

        // Idempotent: the UPDATE's `completed_time IS NULL` guard means an
        // already-completed row matches nothing, so the count means "newly
        // marked" rather than "seen".
        Assert.Equal(0, second);

        // And it stopped after the first full page, rather than walking back
        // through history. Without the stop condition this is 2.
        var pages = factory.FlowableStub.Calls
            .Count(c => c.StartsWith("GetFinishedTasks:", StringComparison.Ordinal));
        Assert.Equal(1, pages);
    }

    /// <summary>
    /// `FlowsQueryEntity`'s open-task filter now excludes a row it could not (#586).
    /// </summary>
    /// <remarks>
    /// <para>That query filters <c>Status == "active" &amp;&amp; CompletedTime == null</c>
    /// and takes the oldest match as the instance's current step. Because nothing
    /// ever wrote either value to anything else, the filter matched every row —
    /// it read as correctness and could not exclude anything, so
    /// <c>CURRENTSTEP()</c> reported an instance's first task forever.</para>
    ///
    /// <para>The assertion is on the predicate itself rather than on a rendered
    /// query, so it fails for the reason it names.</para>
    /// </remarks>
    [Fact]
    public async Task The_open_task_filter_becomes_load_bearing()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        _ = factory.CreateClient();

        await SeedActiveTaskAsync(factory, "task-first", "inst-1");
        await SeedActiveTaskAsync(factory, "task-second", "inst-1");

        var dbFactory = factory.Services.GetRequiredService<IDbContextFactory<AutoNateDbContext>>();

        async Task<List<string>> OpenTasksAsync()
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            return await db.WorkflowTaskCache.AsNoTracking()
                .Where(t => t.FlowableInstanceId == "inst-1"
                         && t.Status == "active"
                         && t.CompletedTime == null)
                .OrderBy(t => t.CreatedTime)
                .Select(t => t.FlowableTaskId)
                .ToListAsync();
        }

        // Before: the filter excludes neither, so the "current step" is the first.
        Assert.Equal(["task-first", "task-second"], await OpenTasksAsync());

        factory.FlowableStub.FinishedTasks.Add(new FlowableFinishedTask
        {
            Id = "task-first", EndedAtUtc = DateTimeOffset.UtcNow, ProcessInstanceId = "inst-1"
        });
        Assert.Equal(1, await SweepAsync(factory));

        // After: the first task is gone from the open set, so the current step
        // advances. This is the assertion that was impossible before #586.
        Assert.Equal(["task-second"], await OpenTasksAsync());
    }

    // ---- helpers ----

    /// <summary>
    /// A re-poll cannot un-complete a task the sweep already marked (#629).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every producer emits only OPEN tasks, so <c>MapRow</c> writes
    /// <c>completed_time = null</c> and <c>status = 'active'</c> unconditionally.
    /// Safe in the steady state; not safe in a race.
    /// <c>FlowableTaskPollingFeed</c> emits into a channel this projection drains
    /// asynchronously, so a page captured BEFORE a completion can be applied
    /// AFTER the sweep marked it.
    /// </para>
    /// <para>
    /// And the sweep would not heal it: it walks <c>endTime DESC</c> and breaks
    /// on the first page that marks nothing, so once newer completions push the
    /// resurrected task off page 0 it is never re-marked — and
    /// <c>CURRENTSTEP()</c> goes back to reporting the stale task, which is the
    /// symptom #586 exists to remove.
    /// </para>
    /// <para>
    /// Every other test in this file re-projects only BEFORE marking, which is
    /// why none of them could see it.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_late_poll_page_cannot_un_complete_a_swept_task()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        (await client.GetAsync("/api/workflows/")).EnsureSuccessStatusCode();

        const string TaskId = "task-629";
        const string Instance = "inst-629";
        await SeedActiveTaskAsync(factory, TaskId, Instance);

        // The engine reports it finished, and the sweep marks it.
        factory.FlowableStub.FinishedTasks.Add(new FlowableFinishedTask
        {
            Id = TaskId,
            EndedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
            ProcessInstanceId = Instance
        });

        Assert.Equal(1, await SweepAsync(factory));

        var marked = await ReadTaskAsync(factory, TaskId);
        Assert.NotNull(marked.CompletedTime);
        Assert.Equal("completed", marked.Status);

        // NOW THE LATE PAGE LANDS — the same open-task summary a poll captured
        // before the completion, applied after it.
        await SeedActiveTaskAsync(factory, TaskId, Instance);

        var after = await ReadTaskAsync(factory, TaskId);

        Assert.NotNull(after.CompletedTime);
        Assert.Equal("completed", after.Status);
        Assert.Equal(marked.CompletedTime, after.CompletedTime);
    }

    private static async Task<int> SweepAsync(AutoNateWebApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var sweep = scope.ServiceProvider.GetRequiredService<WorkflowTaskCompletionSweep>();
        return await sweep.RunOnceAsync(CancellationToken.None);
    }

    private static async Task<WorkflowTaskCache> ReadTaskAsync(
        AutoNateWebApplicationFactory factory, string taskId)
    {
        var dbFactory = factory.Services.GetRequiredService<IDbContextFactory<AutoNateDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.WorkflowTaskCache.AsNoTracking()
            .FirstAsync(t => t.FlowableTaskId == taskId);
    }

    // Seeded through the real projection, so these rows are exactly what the
    // runtime poll would have written -- including the unconditional
    // Status = "active" this story is about.
    private static async Task SeedActiveTaskAsync(
        AutoNateWebApplicationFactory factory, string taskId, string instanceId)
    {
        using var scope = factory.Services.CreateScope();
        var projection = scope.ServiceProvider.GetRequiredService<FlowableTaskProjection>();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AutoNateDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();

        await projection.ApplyAsync(
            [new ChangeEvent<FlowableTaskSummary>(
                ChangeOp.Upsert, taskId,
                new FlowableTaskSummary
                {
                    Id = taskId,
                    Name = taskId,
                    ProcessInstanceId = instanceId,
                    ProcessDefinitionId = "invoice:1:1",
                    CreatedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10)
                },
                DateTimeOffset.UtcNow)],
            db, CancellationToken.None);
    }
}
