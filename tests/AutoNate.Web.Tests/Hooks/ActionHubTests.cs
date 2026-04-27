using AutoNate.Plugins.Abstractions;
using AutoNate.Web.Hooks;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AutoNate.Web.Tests.Hooks;

public sealed class ActionHubTests
{
    [Fact]
    public void Do_FiresAllSubscribers()
    {
        var registrar = NewRegistrar();
        var calls = new List<string>();
        registrar.AddAction("test", priority: 10, _ => calls.Add("a"));
        registrar.AddAction("test", priority: 10, _ => calls.Add("b"));

        registrar.Actions.Do("test");

        Assert.Equal(new[] { "a", "b" }, calls);
    }

    [Fact]
    public void Do_RespectsPriorityOrder()
    {
        var registrar = NewRegistrar();
        var calls = new List<string>();
        registrar.AddAction("test", priority: 20, _ => calls.Add("late"));
        registrar.AddAction("test", priority: 5, _ => calls.Add("early"));
        registrar.AddAction("test", priority: 10, _ => calls.Add("mid"));

        registrar.Actions.Do("test");

        Assert.Equal(new[] { "early", "mid", "late" }, calls);
    }

    [Fact]
    public void Do_SamePriorityPreservesRegistrationOrder()
    {
        var registrar = NewRegistrar();
        var calls = new List<string>();
        registrar.AddAction("test", priority: 10, _ => calls.Add("first"));
        registrar.AddAction("test", priority: 10, _ => calls.Add("second"));
        registrar.AddAction("test", priority: 10, _ => calls.Add("third"));

        registrar.Actions.Do("test");

        Assert.Equal(new[] { "first", "second", "third" }, calls);
    }

    [Fact]
    public void Do_OneCallbackThrows_OthersStillRun()
    {
        var registrar = NewRegistrar();
        var calls = new List<string>();
        registrar.AddAction("test", priority: 10, _ => calls.Add("a"));
        registrar.AddAction("test", priority: 10, _ => throw new InvalidOperationException("boom"));
        registrar.AddAction("test", priority: 10, _ => calls.Add("c"));

        registrar.Actions.Do("test");

        Assert.Equal(new[] { "a", "c" }, calls);
    }

    [Fact]
    public async Task DoAsync_PassesCancellationToken()
    {
        var registrar = NewRegistrar();
        CancellationToken seen = default;
        registrar.AddActionAsync("test", priority: 10, async (_, ct) =>
        {
            seen = ct;
            await Task.Yield();
        });

        using var cts = new CancellationTokenSource();
        await registrar.Actions.DoAsync("test", cts.Token);

        Assert.Equal(cts.Token, seen);
    }

    [Fact]
    public void HasAction_TrueForSyncOrAsync()
    {
        var registrar = NewRegistrar();
        Assert.False(registrar.Actions.HasAction("nope"));
        registrar.AddAction("a", 10, _ => { });
        Assert.True(registrar.Actions.HasAction("a"));
        registrar.AddActionAsync("b", 10, (_, _) => Task.CompletedTask);
        Assert.True(registrar.Actions.HasAction("b"));
    }

    private static HookRegistrar NewRegistrar() => new(NullLogger<ActionHub>.Instance);
}
