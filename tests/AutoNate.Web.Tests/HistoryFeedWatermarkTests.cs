using AutoNate.Web.Models;
using AutoNate.Web.Services.Flowable.Cache;
using AutoNate.Web.Services.Projections;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AutoNate.Web.Tests;

/// <summary>
/// The history feed's watermark actually bounds its work (#590).
/// </summary>
/// <remarks>
/// <para>
/// It did not. The feed sent <c>startedAfter</c> and paged ascending from the
/// beginning; Flowable ignores that parameter on this collection, so every tick
/// replayed all of history while the watermark advanced and the logs read as
/// incremental.
/// </para>
/// <para>
/// <b>No test caught it because the stub was more capable than the server.</b>
/// <c>StubFlowableClient</c> took a <c>sinceUtc</c> and honoured it, so every
/// assertion confirmed what the code believed rather than what the engine does.
/// These tests assert on <b>pages fetched and rows emitted</b>, against a stub
/// that now behaves as Flowable does: newest first, unfiltered.
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
public sealed class HistoryFeedWatermarkTests
{
    private static FlowableHistoricActivityEvent Event(string id, DateTimeOffset at) => new()
    {
        ProcessInstanceId = "inst-1",
        ActivityId = id,
        ActivityName = id,
        ActivityType = "userTask",
        StartTime = at
    };

    /// <summary>
    /// A second tick over unchanged history fetches strictly less than the first (#590).
    /// </summary>
    /// <remarks>
    /// The criterion that makes the watermark load-bearing. Before the fix both
    /// ticks fetched every page, and this assertion would have failed on the
    /// numbers while every existing test stayed green.
    /// </remarks>
    [Fact]
    public async Task A_second_tick_over_unchanged_history_fetches_strictly_less()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(
            extraConfig: new Dictionary<string, string?> { ["FlowableCache:HistoryPageSize"] = "2" });
        _ = factory.CreateClient();

        var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < 6; i++)
        {
            factory.FlowableStub.HistoricActivityEvents.Add(Event($"act-{i}", now.AddMinutes(-i)));
        }

        using var scope = factory.Services.CreateScope();
        var feed = scope.ServiceProvider.GetRequiredService<FlowableHistoryPollingFeed>();

        factory.FlowableStub.Calls.Clear();
        await feed.RunTickOnceAsync(CancellationToken.None);
        var firstPages = PageCount(factory);

        factory.FlowableStub.Calls.Clear();
        await feed.RunTickOnceAsync(CancellationToken.None);
        var secondPages = PageCount(factory);

        Assert.True(firstPages > 1, $"The first tick should walk history; it fetched {firstPages} page(s).");
        Assert.True(
            secondPages < firstPages,
            $"A second tick over unchanged history must do less work: first {firstPages} pages, second {secondPages}.");
    }

    /// <summary>
    /// A tick with nothing new does bounded work (#590).
    /// </summary>
    /// <remarks>
    /// Asserting the page count, not that the result set is empty. An empty
    /// result is what the old code produced too — after fetching every page.
    /// </remarks>
    [Fact]
    public async Task A_tick_with_nothing_new_fetches_one_page()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(
            extraConfig: new Dictionary<string, string?> { ["FlowableCache:HistoryPageSize"] = "2" });
        _ = factory.CreateClient();

        var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < 6; i++)
        {
            factory.FlowableStub.HistoricActivityEvents.Add(Event($"act-{i}", now.AddMinutes(-i)));
        }

        using var scope = factory.Services.CreateScope();
        var feed = scope.ServiceProvider.GetRequiredService<FlowableHistoryPollingFeed>();
        await feed.RunTickOnceAsync(CancellationToken.None);

        factory.FlowableStub.Calls.Clear();
        await feed.RunTickOnceAsync(CancellationToken.None);

        Assert.Equal(1, PageCount(factory));
    }

    /// <summary>
    /// New events are still picked up (#590).
    /// </summary>
    /// <remarks>
    /// The complement to bounding the work. A feed that stopped fetching
    /// altogether would satisfy both tests above and be entirely useless.
    /// </remarks>
    [Fact]
    public async Task An_event_newer_than_the_watermark_is_still_read()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(
            extraConfig: new Dictionary<string, string?> { ["FlowableCache:HistoryPageSize"] = "2" });
        _ = factory.CreateClient();

        var now = DateTimeOffset.UtcNow;
        factory.FlowableStub.HistoricActivityEvents.Add(Event("old", now.AddMinutes(-10)));

        using var scope = factory.Services.CreateScope();
        var feed = scope.ServiceProvider.GetRequiredService<FlowableHistoryPollingFeed>();
        await feed.RunTickOnceAsync(CancellationToken.None);

        factory.FlowableStub.HistoricActivityEvents.Add(Event("brand-new", now.AddMinutes(5)));

        factory.FlowableStub.Calls.Clear();
        await feed.RunTickOnceAsync(CancellationToken.None);

        // It looked, and it looked at the newest end.
        Assert.True(PageCount(factory) >= 1);
        Assert.Contains(factory.FlowableStub.Calls, c => c.StartsWith("HistoricActivities:start=0", StringComparison.Ordinal));
    }

    /// <summary>
    /// An event sharing the watermark's exact timestamp is NOT dropped (#590).
    /// </summary>
    /// <remarks>
    /// <para>The stop is on <b>strictly older</b>, and this is the test that makes
    /// that claim mean something. Several activities can start within the same
    /// millisecond; stopping at older-or-equal would silently drop any that a
    /// previous tick had not reached, and the feed would look perfectly healthy
    /// while losing events.</para>
    ///
    /// <para>The boundary second is therefore re-read on every tick. That is the
    /// cheap direction to be wrong in — the projection is idempotent on
    /// <c>event_id</c>, so a repeat is a no-op, while a miss is permanent.</para>
    /// </remarks>
    [Fact]
    public async Task An_event_sharing_the_watermarks_timestamp_is_not_dropped()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(
            extraConfig: new Dictionary<string, string?> { ["FlowableCache:HistoryPageSize"] = "10" });
        _ = factory.CreateClient();

        var boundary = DateTimeOffset.UtcNow;
        factory.FlowableStub.HistoricActivityEvents.Add(Event("first-at-boundary", boundary));
        factory.FlowableStub.HistoricActivityEvents.Add(Event("older", boundary.AddMinutes(-5)));

        using var scope = factory.Services.CreateScope();
        var feed = scope.ServiceProvider.GetRequiredService<FlowableHistoryPollingFeed>();

        var firstEmitted = await feed.RunTickOnceAsync(CancellationToken.None);
        Assert.Equal(2, firstEmitted);

        // A second activity that started in the same instant as the watermark.
        factory.FlowableStub.HistoricActivityEvents.Add(Event("second-at-boundary", boundary));

        var secondEmitted = await feed.RunTickOnceAsync(CancellationToken.None);

        // Both boundary events are re-read; the older one is not. Stopping at
        // older-or-equal would emit nothing here and lose "second-at-boundary"
        // for good.
        Assert.Equal(2, secondEmitted);
    }

    private static int PageCount(AutoNateWebApplicationFactory factory) =>
        factory.FlowableStub.Calls.Count(c => c.StartsWith("HistoricActivities:", StringComparison.Ordinal));
}
