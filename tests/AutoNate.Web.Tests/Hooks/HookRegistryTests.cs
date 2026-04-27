using AutoNate.Web.Hooks;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AutoNate.Web.Tests.Hooks;

public sealed class HookRegistryTests
{
    [Fact]
    public void ScopedRegistrar_RemoveAll_ClearsEverySubscriptionItRegistered()
    {
        var root = new HookRegistrar(NullLogger<ActionHub>.Instance);
        var scoped = new ScopedHookRegistrar(root);

        scoped.AddAction("evt", 10, _ => { });
        scoped.AddActionAsync("evt", 10, (_, _) => Task.CompletedTask);
        scoped.AddFilter<string>("flt", 10, (v, _) => v);
        scoped.AddFilterAsync<string>("flt", 10, (v, _, _) => Task.FromResult(v));

        Assert.True(root.Actions.HasAction("evt"));
        Assert.True(root.Filters.HasFilter("flt"));

        scoped.RemoveAllForPlugin();

        Assert.False(root.Actions.HasAction("evt"));
        Assert.False(root.Filters.HasFilter("flt"));
    }

    [Fact]
    public void Add_IsThreadSafe_UnderConcurrency()
    {
        var root = new HookRegistrar(NullLogger<ActionHub>.Instance);
        var scoped = new ScopedHookRegistrar(root);

        const int total = 200;
        var counter = 0;
        Parallel.For(0, total, _ =>
        {
            scoped.AddAction("evt", priority: 10, _ => Interlocked.Increment(ref counter));
        });

        root.Actions.Do("evt");

        Assert.Equal(total, counter);
    }
}
