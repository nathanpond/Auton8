using System.Text.Json;
using AutoNate.Web.Services.Agent.Loop;
using AutoNate.Web.Services.Agent.PageQuery;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AutoNate.Web.Tests;

public sealed class PageQueryChannelTests
{
    [Fact]
    public async Task AskAsync_returns_page_unreachable_when_not_activated()
    {
        var channel = new PageQueryChannel(new PageQueryRouter(), NullLogger<PageQueryChannel>.Instance);

        var result = await channel.AskAsync("topic", null, CancellationToken.None);

        Assert.False(result.Ok);
        Assert.IsType<PageQueryResult.Failure>(result);
        Assert.Equal("page_unreachable", ((PageQueryResult.Failure)result).ErrorCode);
    }

    [Fact]
    public async Task AskAsync_emits_PageQueryRequested_and_resolves_via_router()
    {
        var router = new PageQueryRouter();
        var channel = new PageQueryChannel(router, NullLogger<PageQueryChannel>.Instance);
        var conversationId = Guid.NewGuid();
        var emitted = new List<AgentEvent>();
        channel.Activate(conversationId, ev =>
        {
            emitted.Add(ev);
            return ValueTask.CompletedTask;
        });

        // Race a resolver against the AskAsync call: the test plays the role
        // of the SPA replying to the page-query event.
        var ask = channel.AskAsync("bpmn.xml", null, CancellationToken.None);

        // Wait a tick for the emit to land.
        await Task.Delay(20);
        var pq = Assert.Single(emitted);
        var request = Assert.IsType<AgentEvent.PageQueryRequested>(pq);
        Assert.Equal("bpmn.xml", request.Topic);

        var resolved = router.TryResolve(conversationId, request.QueryId,
            new PageQueryResult.Success(JsonSerializer.SerializeToElement(new { xml = "<bpmn />" })));
        Assert.True(resolved);

        var result = await ask;
        var ok = Assert.IsType<PageQueryResult.Success>(result);
        Assert.Equal("<bpmn />", ok.Data.GetProperty("xml").GetString());
    }

    [Fact]
    public async Task AskAsync_returns_cancelled_when_ct_fires()
    {
        var router = new PageQueryRouter();
        var channel = new PageQueryChannel(router, NullLogger<PageQueryChannel>.Instance);
        channel.Activate(Guid.NewGuid(), _ => ValueTask.CompletedTask);

        using var cts = new CancellationTokenSource();
        var ask = channel.AskAsync("topic", null, cts.Token);
        cts.Cancel();

        var result = await ask;
        var fail = Assert.IsType<PageQueryResult.Failure>(result);
        Assert.Equal("cancelled", fail.ErrorCode);
    }

    [Fact]
    public async Task Deactivate_makes_subsequent_calls_unreachable()
    {
        var router = new PageQueryRouter();
        var channel = new PageQueryChannel(router, NullLogger<PageQueryChannel>.Instance);
        channel.Activate(Guid.NewGuid(), _ => ValueTask.CompletedTask);
        channel.Deactivate();

        var result = await channel.AskAsync("topic", null, CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Equal("page_unreachable", ((PageQueryResult.Failure)result).ErrorCode);
    }
}
