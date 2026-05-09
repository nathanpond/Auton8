using System.Text.Json;
using AutoNate.Web.Services.Agent.PageQuery;
using Xunit;

namespace AutoNate.Web.Tests;

public sealed class PageQueryRouterTests
{
    [Fact]
    public async Task Resolve_completes_pending_register()
    {
        var router = new PageQueryRouter();
        var conversationId = Guid.NewGuid();
        var queryId = "q1";

        var tcs = router.Register(conversationId, queryId);
        var data = JsonSerializer.SerializeToElement(new { ok = true });

        var resolved = router.TryResolve(conversationId, queryId, new PageQueryResult.Success(data));
        Assert.True(resolved);

        var result = await tcs.Task;
        Assert.True(result.Ok);
        Assert.IsType<PageQueryResult.Success>(result);
    }

    [Fact]
    public void Resolve_returns_false_for_unknown_query()
    {
        var router = new PageQueryRouter();
        var resolved = router.TryResolve(Guid.NewGuid(), "no_such", new PageQueryResult.Success(JsonElementOf("{}")));
        Assert.False(resolved);
    }

    [Fact]
    public void Cleanup_removes_the_pending_entry()
    {
        var router = new PageQueryRouter();
        var conv = Guid.NewGuid();

        router.Register(conv, "q");
        router.Cleanup(conv, "q");

        var late = router.TryResolve(conv, "q", new PageQueryResult.Success(JsonElementOf("{}")));
        Assert.False(late);
    }

    [Fact]
    public void Register_throws_on_duplicate_id()
    {
        var router = new PageQueryRouter();
        var conv = Guid.NewGuid();
        router.Register(conv, "dup");
        Assert.Throws<InvalidOperationException>(() => router.Register(conv, "dup"));
    }

    private static JsonElement JsonElementOf(string raw)
    {
        using var doc = JsonDocument.Parse(raw);
        return doc.RootElement.Clone();
    }
}
