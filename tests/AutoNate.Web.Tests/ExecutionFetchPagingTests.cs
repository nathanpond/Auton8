using System.Net;
using System.Web;
using AutoNate.Web.Configuration;
using AutoNate.Web.Services.Flowable;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AutoNate.Web.Tests;

/// <summary>
/// The execution fetch pages, so the cache can hold more than one page (#588).
/// </summary>
/// <remarks>
/// <para>
/// <c>GetWorkflowExecutionsAsync</c> issued four fixed <c>size=200</c> queries
/// with no <c>start</c> and no loop, and it is the only filler for
/// <c>workflow_execution_cache</c> — both the poll feed and the backfill call it.
/// So the cache was itself capped at 200 and #108's list could not show more
/// however well its query was written.
/// </para>
/// </remarks>
public sealed class ExecutionFetchPagingTests
{
    private const string BaseAddress = "http://flowable.test/flowable/";
    private const string Spine = "service/history/historic-process-instances";

    [Fact]
    public async Task It_pages_past_the_first_full_page()
    {
        // 250 instances at 200 per page: one full page then a short one. A
        // caller that stops after the first page sees 200 -- the old behaviour.
        var (client, stub) = CreateClient(spineCount: 250);

        var executions = await client.GetWorkflowExecutionsAsync(maxPages: 5, CancellationToken.None);

        Assert.Equal(250, executions.Count);
        Assert.Equal([0, 200], SpineStarts(stub));
    }

    /// <summary>
    /// A page that comes back FULL is not assumed to be the end (#588).
    /// </summary>
    /// <remarks>
    /// The first half of the complement. A collection holding exactly one page is
    /// indistinguishable from one holding more until you ask for the next page —
    /// so the loop asks, and stops on the empty answer.
    /// </remarks>
    [Fact]
    public async Task An_exactly_full_page_is_not_assumed_to_be_the_end()
    {
        var (client, stub) = CreateClient(spineCount: 200);

        var executions = await client.GetWorkflowExecutionsAsync(maxPages: 5, CancellationToken.None);

        Assert.Equal(200, executions.Count);

        // It asked for a second page and got nothing, rather than assuming.
        Assert.Equal([0, 200], SpineStarts(stub));
    }

    /// <summary>
    /// A SHORT page ends that collection, and is not treated as a failure (#588).
    /// </summary>
    /// <remarks>
    /// The other half. Getting this backwards would make every partial collection
    /// look like an error, which is a plausible-looking wrong answer in the
    /// opposite direction.
    /// </remarks>
    [Fact]
    public async Task A_short_page_ends_that_collection_without_erroring()
    {
        var (client, stub) = CreateClient(spineCount: 5);

        var executions = await client.GetWorkflowExecutionsAsync(maxPages: 5, CancellationToken.None);

        Assert.Equal(5, executions.Count);
        Assert.Equal([0], SpineStarts(stub));
    }

    /// <summary>
    /// `maxPages` bounds the work, which is what keeps a poll tick cheap (#588).
    /// </summary>
    /// <remarks>
    /// The ceiling is a parameter rather than a constant because the poll and the
    /// backfill want different answers — the poll runs every 60s, the backfill is
    /// a one-shot operator action. This asserts the poll's side of that.
    /// </remarks>
    [Fact]
    public async Task MaxPages_bounds_the_work_on_a_large_backlog()
    {
        var (client, stub) = CreateClient(spineCount: 5000);

        var executions = await client.GetWorkflowExecutionsAsync(maxPages: 2, CancellationToken.None);

        Assert.Equal(400, executions.Count);

        // Two pages, not twenty-five. The backlog does not set the tick's cost.
        Assert.Equal([0, 200], SpineStarts(stub));
    }

    /// <summary>
    /// A failed page throws rather than returning a short list (#588).
    /// </summary>
    /// <remarks>
    /// <para>This is the criterion that says a partial sweep must not silently
    /// truncate. Returning what it had would be indistinguishable, to the caller,
    /// from a collection that really did end there — and the caller here is the
    /// projection, which would then write a cache that is quietly missing rows.</para>
    /// </remarks>
    [Fact]
    public async Task A_failed_page_throws_rather_than_truncating()
    {
        var (client, _) = CreateClient(spineCount: 500, failSpineOnStart: 200);

        await Assert.ThrowsAnyAsync<Exception>(() =>
            client.GetWorkflowExecutionsAsync(maxPages: 5, CancellationToken.None));
    }

    // ---- helpers ----

    private static List<int> SpineStarts(StubHttpMessageHandler stub) =>
        stub.Requests
            .Where(r => r.Url.Contains(Spine, StringComparison.Ordinal))
            .Select(r => int.Parse(
                HttpUtility.ParseQueryString(new Uri(r.Url).Query)["start"] ?? "0",
                System.Globalization.CultureInfo.InvariantCulture))
            .OrderBy(v => v)
            .ToList();

    private static (FlowableClient client, StubHttpMessageHandler stub) CreateClient(
        int spineCount, int? failSpineOnStart = null)
    {
        var stub = new StubHttpMessageHandler();

        stub.When(HttpMethod.Get, Spine, request =>
        {
            var query = HttpUtility.ParseQueryString(request.RequestUri!.Query);
            var start = int.Parse(query["start"] ?? "0", System.Globalization.CultureInfo.InvariantCulture);
            var size = int.Parse(query["size"] ?? "200", System.Globalization.CultureInfo.InvariantCulture);

            if (failSpineOnStart is { } failAt && start == failAt)
            {
                return new HttpResponseMessage(HttpStatusCode.InternalServerError)
                {
                    Content = new StringContent("boom")
                };
            }

            var page = Enumerable.Range(start, Math.Max(0, Math.Min(size, spineCount - start)))
                .Select(i => new
                {
                    id = $"inst-{i}",
                    processDefinitionId = "invoice:1:1",
                    startTime = DateTimeOffset.UtcNow.AddMinutes(-i),
                    startUserId = "alice"
                })
                .ToArray();

            return StubHttpMessageHandler.JsonResponse(new { data = page, total = spineCount });
        });

        // The three enrichment collections are empty here: this suite is about
        // the spine's paging, and an empty lookup leaves the merge's behaviour
        // unchanged rather than adding a second variable.
        foreach (var path in new[]
                 {
                     "service/runtime/process-instances",
                     "service/runtime/tasks",
                     "service/history/historic-activity-instances"
                 })
        {
            stub.When(HttpMethod.Get, path,
                _ => StubHttpMessageHandler.JsonResponse(new { data = Array.Empty<object>(), total = 0 }));
        }

        stub.When(HttpMethod.Get, "service/repository/process-definitions",
            _ => StubHttpMessageHandler.JsonResponse(new { data = Array.Empty<object>(), total = 0 }));

        var http = new HttpClient(stub) { BaseAddress = new Uri(BaseAddress) };
        var client = new FlowableClient(
            http,
            Options.Create(new FlowableOptions { BaseUrl = BaseAddress }),
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<FlowableClient>.Instance);
        return (client, stub);
    }
}
