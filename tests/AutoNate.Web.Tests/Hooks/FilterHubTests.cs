using AutoNate.Web.Hooks;
using Xunit;

namespace AutoNate.Web.Tests.Hooks;

public sealed class FilterHubTests
{
    [Fact]
    public void Apply_NoSubscribers_ReturnsInputUnchanged()
    {
        var hub = new FilterHub();
        var result = hub.Apply<string>("nope", "hello");
        Assert.Equal("hello", result);
    }

    [Fact]
    public void Apply_ChainsValueThroughSubscribers()
    {
        var hub = new HookRegistrar(Microsoft.Extensions.Logging.Abstractions.NullLogger<ActionHub>.Instance);
        hub.AddFilter<string>("test", priority: 10, (val, _) => val + "-a");
        hub.AddFilter<string>("test", priority: 10, (val, _) => val + "-b");

        var result = hub.Filters.Apply<string>("test", "x");

        Assert.Equal("x-a-b", result);
    }

    [Fact]
    public void Apply_RespectsPriorityOrder()
    {
        var hub = new HookRegistrar(Microsoft.Extensions.Logging.Abstractions.NullLogger<ActionHub>.Instance);
        hub.AddFilter<string>("test", priority: 20, (val, _) => val + ".late");
        hub.AddFilter<string>("test", priority: 5, (val, _) => val + ".early");
        hub.AddFilter<string>("test", priority: 10, (val, _) => val + ".mid");

        var result = hub.Filters.Apply<string>("test", "v");

        Assert.Equal("v.early.mid.late", result);
    }

    [Fact]
    public void Apply_OneCallbackThrows_PropagatesAndStopsChain()
    {
        var hub = new HookRegistrar(Microsoft.Extensions.Logging.Abstractions.NullLogger<ActionHub>.Instance);
        hub.AddFilter<string>("test", priority: 10, (val, _) => val + "-a");
        hub.AddFilter<string>("test", priority: 10, (val, _) => throw new InvalidOperationException("boom"));
        hub.AddFilter<string>("test", priority: 10, (val, _) => val + "-c");

        Assert.Throws<InvalidOperationException>(() => hub.Filters.Apply<string>("test", "x"));
    }

    [Fact]
    public async Task ApplyAsync_PassesCancellationToken()
    {
        var hub = new HookRegistrar(Microsoft.Extensions.Logging.Abstractions.NullLogger<ActionHub>.Instance);
        CancellationToken seen = default;
        hub.AddFilterAsync<string>("test", priority: 10, async (val, _, ct) =>
        {
            seen = ct;
            await Task.Yield();
            return val + "!";
        });

        using var cts = new CancellationTokenSource();
        var result = await hub.Filters.ApplyAsync<string>("test", "hi", cts.Token);

        Assert.Equal("hi!", result);
        Assert.Equal(cts.Token, seen);
    }

    [Fact]
    public void HasFilter_FalseWhenNoSubscribers_TrueAfterAdd()
    {
        var hub = new HookRegistrar(Microsoft.Extensions.Logging.Abstractions.NullLogger<ActionHub>.Instance);
        Assert.False(hub.Filters.HasFilter("test"));
        hub.AddFilter<string>("test", priority: 10, (val, _) => val);
        Assert.True(hub.Filters.HasFilter("test"));
    }
}
