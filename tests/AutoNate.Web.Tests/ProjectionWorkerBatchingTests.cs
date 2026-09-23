using System.Runtime.CompilerServices;
using AutoNate.Web.Persistence;
using AutoNate.Web.Services.Projections;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace AutoNate.Web.Tests;

/// <summary>
/// #672. <see cref="ProjectionWorker"/> only ever flushed a batch when it
/// reached <see cref="ProjectionOptions.MaxBatchSize"/> or the feed's stream
/// completed (shutdown). <c>MaxBatchWindow</c> was configured, documented
/// ("the MaxBatchWindow flush happens implicitly... when the source pauses")
/// and never implemented -- <c>BatchAsync</c> was a bare pass-through
/// enumerator. A feed emitting fewer items per tick than <c>MaxBatchSize</c>
/// (the normal case: a live engine rarely has 250 instances) accumulated
/// forever, undelivered, for as long as the app ran -- which is exactly the
/// shape of "an instance the engine starts on its own never reaches the
/// executions list": nothing but a synchronous write-through (#609) ever
/// wrote a row, because the background poller's batch never flushed.
/// </summary>
[Trait("Category", "Integration")]
public sealed class ProjectionWorkerBatchingTests
{
    [Fact]
    public async Task A_small_batch_flushes_once_MaxBatchWindow_elapses_without_reaching_MaxBatchSize()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        _ = factory.CreateClient();

        var applied = new List<IReadOnlyList<ChangeEvent<string>>>();
        var projection = new RecordingProjection(applied);
        var feed = new TwoItemsThenQuietFeed();

        var services = new ServiceCollection();
        services.AddSingleton(factory.Services.GetRequiredService<IDbContextFactory<AutoNateDbContext>>());
        services.AddSingleton<IEnumerable<IChangeFeed<string>>>(new IChangeFeed<string>[] { feed });
        await using var provider = services.BuildServiceProvider();

        var options = Options.Create(new ProjectionOptions
        {
            // Large enough that 2 items never reaches it on their own --
            // the only thing that can flush them is the time window.
            MaxBatchSize = 100,
            MaxBatchWindow = TimeSpan.FromMilliseconds(150)
        });
        var registry = new SingleProjectionRegistry(projection);
        var health = new ProjectionHealthService();
        var logger = new CapturingLogger<ProjectionWorker>();

        var worker = new ProjectionWorker(provider, registry, health, options, logger);

        await worker.StartAsync(CancellationToken.None);
        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (applied.Count == 0 && DateTime.UtcNow < deadline)
            {
                await Task.Delay(50);
            }
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }

        Assert.Single(applied);
        Assert.Equal(2, applied[0].Count);
        Assert.Equal(new[] { "a", "b" }, applied[0].Select(c => c.SourceId));
    }

    /// <summary>
    /// Complement: a batch that DOES reach MaxBatchSize flushes on that path,
    /// not the window -- the fix must not turn every flush into a window wait.
    /// </summary>
    [Fact]
    public async Task A_full_batch_flushes_on_reaching_MaxBatchSize_without_waiting_for_the_window()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        _ = factory.CreateClient();

        var applied = new List<IReadOnlyList<ChangeEvent<string>>>();
        var projection = new RecordingProjection(applied);
        var feed = new ThreeItemsThenQuietFeed();

        var services = new ServiceCollection();
        services.AddSingleton(factory.Services.GetRequiredService<IDbContextFactory<AutoNateDbContext>>());
        services.AddSingleton<IEnumerable<IChangeFeed<string>>>(new IChangeFeed<string>[] { feed });
        await using var provider = services.BuildServiceProvider();

        var options = Options.Create(new ProjectionOptions
        {
            MaxBatchSize = 3,
            // A window long enough that, if the size path didn't fire, the
            // test would time out waiting rather than pass for the wrong
            // reason.
            MaxBatchWindow = TimeSpan.FromSeconds(30)
        });
        var registry = new SingleProjectionRegistry(projection);
        var health = new ProjectionHealthService();
        var logger = new CapturingLogger<ProjectionWorker>();

        var worker = new ProjectionWorker(provider, registry, health, options, logger);

        var started = DateTime.UtcNow;
        await worker.StartAsync(CancellationToken.None);
        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (applied.Count == 0 && DateTime.UtcNow < deadline)
            {
                await Task.Delay(50);
            }
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }

        Assert.Single(applied);
        Assert.Equal(3, applied[0].Count);
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(10), "flushed on size, not after waiting out the 30s window");
    }

    private sealed class RecordingProjection(List<IReadOnlyList<ChangeEvent<string>>> applied) : IProjection<string>
    {
        public string Name => "test.recording";
        public int Version => 1;
        public Type SourceType => typeof(string);

        public Task ApplyAsync(IReadOnlyList<ChangeEvent<string>> batch, AutoNateDbContext db, CancellationToken cancellationToken)
        {
            // Snapshot: the caller reuses and clears its buffer list after this
            // returns, so holding the reference itself would retroactively
            // empty whatever we recorded.
            applied.Add(batch.ToList());
            return Task.CompletedTask;
        }
    }

    private sealed class SingleProjectionRegistry(IProjection projection) : IProjectionRegistry
    {
        public IReadOnlyList<IProjection> Projections { get; } = new[] { projection };
        public IProjection? TryGet(string name) => name == projection.Name ? projection : null;
    }

    /// <summary>Emits "a" then "b" once each, then never emits again (mirrors a
    /// real polling feed's infinite stream that just has nothing new to say).</summary>
    private sealed class TwoItemsThenQuietFeed : IChangeFeed<string>
    {
        public string FeedName => "test.two-then-quiet";

        public async IAsyncEnumerable<ChangeEvent<string>> StreamAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            yield return new ChangeEvent<string>(ChangeOp.Upsert, "a", "a", DateTimeOffset.UtcNow);
            yield return new ChangeEvent<string>(ChangeOp.Upsert, "b", "b", DateTimeOffset.UtcNow);
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
    }

    private sealed class ThreeItemsThenQuietFeed : IChangeFeed<string>
    {
        public string FeedName => "test.three-then-quiet";

        public async IAsyncEnumerable<ChangeEvent<string>> StreamAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            yield return new ChangeEvent<string>(ChangeOp.Upsert, "a", "a", DateTimeOffset.UtcNow);
            yield return new ChangeEvent<string>(ChangeOp.Upsert, "b", "b", DateTimeOffset.UtcNow);
            yield return new ChangeEvent<string>(ChangeOp.Upsert, "c", "c", DateTimeOffset.UtcNow);
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
    }
}
