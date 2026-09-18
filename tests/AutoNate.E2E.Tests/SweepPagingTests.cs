using System.Net;
using System.Text;
using AutoNate.E2E.Tests.Support;
using Xunit;

namespace AutoNate.E2E.Tests;

/// <summary>
/// The deployment sweep reads every page (#548).
/// </summary>
/// <remarks>
/// <para>
/// #537 was "the sweep reads one page of 1000 and reports 0 when the backlog is
/// larger". It was fixed and <b>nothing pinned the fix</b>: the sweep's own tests
/// are <c>RequiresService=Flowable</c> and pass or fail according to how many
/// deployments the engine happens to hold. At a few hundred they pass against the
/// <em>pre-fix</em> code, so the original evidence was not reproducible on demand.
/// </para>
/// <para>
/// This drives the paging over a stub handler instead — deterministic, no engine,
/// and <b>no trait</b>, so it runs in the slim tier that blocks merges rather than
/// in the one a developer has to remember to run.
/// </para>
/// </remarks>
public sealed class SweepPagingTests
{
    /// <summary>A backlog larger than one page is read to the end (#548).</summary>
    [Fact]
    public async Task Every_page_is_read_and_everything_matching_is_swept()
    {
        var size = FlowableDeploymentSweep.PageSize;

        // Two full pages then a short one: the exact shape the single-page read
        // could not see past.
        var handler = new PagingHandler(totalMatching: (size * 2) + 7, pageSize: size);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://sweep.test/") };

        var sweep = await FlowableDeploymentSweep.SweepAsync(client, DateTimeOffset.UtcNow);

        Assert.Equal((size * 2) + 7, sweep.Deleted);
        Assert.Equal(3, sweep.Pages);
        Assert.False(sweep.Incomplete);

        // AND IT ASKED FOR MORE THAN ONE PAGE. Asserting only the count would
        // pass against a single request that happened to return everything --
        // which is what the pre-fix code did on a small engine, and is exactly
        // why the original fix had no reproducible evidence.
        Assert.Equal([0, size, size * 2], handler.RequestedOffsets);
    }

    /// <summary>
    /// A failed page stops the read rather than discarding it (#548).
    /// </summary>
    /// <remarks>
    /// The mid-paging error path used to <c>return 0</c>, throwing away pages
    /// already read and reporting "swept nothing" — the same "a query returning
    /// nothing reads like a verdict" shape #537 was filed about. What was seen is
    /// still swept; the next run takes the rest.
    /// </remarks>
    [Fact]
    public async Task A_failed_page_keeps_what_was_already_read()
    {
        var size = FlowableDeploymentSweep.PageSize;

        var handler = new PagingHandler(totalMatching: size * 3, pageSize: size)
        {
            FailFromOffset = size
        };
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://sweep.test/") };

        var sweep = await FlowableDeploymentSweep.SweepAsync(client, DateTimeOffset.UtcNow);

        Assert.Equal(size, sweep.Deleted);

        // AND IT SAYS THE READ WAS PARTIAL (#548). Without this the test passes
        // against a sweep that stopped early and reported a clean pass -- which
        // is the distinction the issue asked for and `int` could not make.
        Assert.True(sweep.Incomplete);
    }

    /// <summary>
    /// A page that THROWS keeps what was already read too (#555).
    /// </summary>
    /// <remarks>
    /// #548 fixed the non-2xx branch and left this one: a dropped connection on
    /// a later page discarded every page already read and reported "read 0
    /// deployment(s) over 0 page(s)". The status-code path and the exception
    /// path are two ways to reach the same situation, and only one of them was
    /// honest about it.
    /// </remarks>
    [Fact]
    public async Task A_page_that_throws_keeps_what_was_already_read()
    {
        var size = FlowableDeploymentSweep.PageSize;

        var handler = new PagingHandler(totalMatching: size * 3, pageSize: size)
        {
            ThrowFromOffset = size
        };
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://sweep.test/") };

        var sweep = await FlowableDeploymentSweep.SweepAsync(client, DateTimeOffset.UtcNow);

        // The first page was read and swept, and the record says so rather than
        // reporting a drained engine.
        Assert.Equal(size, sweep.Deleted);
        Assert.Equal(size, sweep.Seen);
        Assert.Equal(1, sweep.Pages);
        Assert.True(sweep.Incomplete);
    }

    private sealed class PagingHandler(int totalMatching, int pageSize) : HttpMessageHandler
    {
        public List<int> RequestedOffsets { get; } = [];

        public int? FailFromOffset { get; init; }

        public int? ThrowFromOffset { get; init; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Delete)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
            }

            var query = request.RequestUri!.Query;
            var match = System.Text.RegularExpressions.Regex.Match(query, @"start=(\d+)");
            var start = match.Success
                ? int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)
                : 0;
            RequestedOffsets.Add(start);

            if (ThrowFromOffset is { } boom && start >= boom)
            {
                throw new HttpRequestException("the connection went away mid-paging");
            }

            if (FailFromOffset is { } fail && start >= fail)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
            }

            var count = Math.Min(pageSize, Math.Max(0, totalMatching - start));

            // Named so the sweep's prefix filter matches and dated well past its
            // minimum age so the age filter does too -- the PAGING is what is
            // under test, not the predicates.
            var rows = string.Join(",", Enumerable.Range(0, count).Select(i =>
                $$"""{"id":"d{{start + i}}","name":"e2e-paged-{{start + i}}","deploymentTime":"2020-01-01T00:00:00.000+0000"}"""));

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent($$"""{"data":[{{rows}}]}""", Encoding.UTF8, "application/json")
            });
        }
    }
}
