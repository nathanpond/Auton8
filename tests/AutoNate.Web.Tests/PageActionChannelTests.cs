using System.Text.Json;
using AutoNate.Web.Services.Agent.Loop;
using AutoNate.Web.Services.Agent.PageQuery;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AutoNate.Web.Tests;

public sealed class PageActionChannelTests
{
    [Fact]
    public async Task ApplyAsync_returns_page_unreachable_when_not_activated()
    {
        var channel = new PageActionChannel(new PageActionRouter(), NullLogger<PageActionChannel>.Instance);

        var result = await channel.ApplyAsync("update_node", null, CancellationToken.None);

        Assert.False(result.Ok);
        Assert.IsType<PageActionResult.Failure>(result);
        Assert.Equal("page_unreachable", ((PageActionResult.Failure)result).ErrorCode);
    }

    [Fact]
    public async Task ApplyAsync_emits_PageActionRequested_and_resolves_via_router()
    {
        var router = new PageActionRouter();
        var channel = new PageActionChannel(router, NullLogger<PageActionChannel>.Instance);
        var conversationId = Guid.NewGuid();
        var emitted = new List<AgentEvent>();
        channel.Activate(conversationId, ev =>
        {
            emitted.Add(ev);
            return ValueTask.CompletedTask;
        });

        var ask = channel.ApplyAsync("update_node",
            JsonSerializer.SerializeToElement(new { id = "X", properties = new { name = "renamed" } }),
            CancellationToken.None);

        await Task.Delay(20);
        var emit = Assert.Single(emitted);
        var request = Assert.IsType<AgentEvent.PageActionRequested>(emit);
        Assert.Equal("update_node", request.Action);
        Assert.NotNull(request.Args);

        var resolved = router.TryResolve(conversationId, request.ActionId,
            new PageActionResult.Success("Renamed X.", JsonSerializer.SerializeToElement(new { id = "X" })));
        Assert.True(resolved);

        var result = await ask;
        var ok = Assert.IsType<PageActionResult.Success>(result);
        Assert.Equal("Renamed X.", ok.Summary);
    }

    [Fact]
    public async Task ApplyAsync_returns_cancelled_when_ct_fires()
    {
        var router = new PageActionRouter();
        var channel = new PageActionChannel(router, NullLogger<PageActionChannel>.Instance);
        channel.Activate(Guid.NewGuid(), _ => ValueTask.CompletedTask);

        using var cts = new CancellationTokenSource();
        var ask = channel.ApplyAsync("noop", null, cts.Token);
        await cts.CancelAsync();

        var result = await ask;
        var fail = Assert.IsType<PageActionResult.Failure>(result);
        Assert.Equal("cancelled", fail.ErrorCode);
    }

    [Fact]
    public void Router_resolve_returns_false_for_unknown_action()
    {
        var router = new PageActionRouter();
        var resolved = router.TryResolve(Guid.NewGuid(), "no_such",
            new PageActionResult.Success("ok"));
        Assert.False(resolved);
    }

    [Fact]
    public void Router_register_throws_on_duplicate_id()
    {
        var router = new PageActionRouter();
        var conv = Guid.NewGuid();
        router.Register(conv, "dup");
        Assert.Throws<InvalidOperationException>(() => router.Register(conv, "dup"));
    }
}
