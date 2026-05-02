using AutoNate.Plugins.Abstractions;
using AutoNate.Web.Services.Workflow.Behaviors;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AutoNate.Web.Tests.Workflow;

public sealed class WorkflowBehaviorRegistryTests
{
    [Fact]
    public void GetAll_ReturnsBuiltInsAndPluginRegistrations()
    {
        var builtIn = new StubBehavior("autonate.builtin", "Built-in");
        var pluginBehavior = new StubBehavior("plugin.thing", "Plugin Thing");
        var registry = NewRegistry(builtIn);
        var pluginId = Guid.NewGuid();

        Assert.True(registry.RegisterFromPlugin(pluginId, pluginBehavior));

        var all = registry.GetAll();

        Assert.Contains(all, b => b.Key == "autonate.builtin");
        Assert.Contains(all, b => b.Key == "plugin.thing");
    }

    [Fact]
    public void Get_PrefersBuiltInOverPluginCollision()
    {
        var builtIn = new StubBehavior("autonate.thing", "Built-in");
        var pluginVersion = new StubBehavior("autonate.thing", "Plugin override attempt");
        var registry = NewRegistry(builtIn);

        var registered = registry.RegisterFromPlugin(Guid.NewGuid(), pluginVersion);

        Assert.False(registered);
        Assert.Same(builtIn, registry.Get("autonate.thing"));
    }

    [Fact]
    public void RegisterFromPlugin_RejectsCollisionAcrossPlugins()
    {
        var registry = NewRegistry();
        var first = new StubBehavior("shared.key", "first");
        var second = new StubBehavior("shared.key", "second");

        Assert.True(registry.RegisterFromPlugin(Guid.NewGuid(), first));
        Assert.False(registry.RegisterFromPlugin(Guid.NewGuid(), second));
        Assert.Same(first, registry.Get("shared.key"));
    }

    [Fact]
    public void RemoveAllForPlugin_DropsOnlyThatPluginsBehaviors()
    {
        var registry = NewRegistry();
        var pluginA = Guid.NewGuid();
        var pluginB = Guid.NewGuid();
        registry.RegisterFromPlugin(pluginA, new StubBehavior("a.one", "A1"));
        registry.RegisterFromPlugin(pluginA, new StubBehavior("a.two", "A2"));
        registry.RegisterFromPlugin(pluginB, new StubBehavior("b.one", "B1"));

        var removed = registry.RemoveAllForPlugin(pluginA);

        Assert.Equal(2, removed);
        Assert.Null(registry.Get("a.one"));
        Assert.Null(registry.Get("a.two"));
        Assert.NotNull(registry.Get("b.one"));
    }

    [Fact]
    public void Get_ReturnsNull_ForUnknownKey()
    {
        var registry = NewRegistry();
        Assert.Null(registry.Get("not.registered"));
        Assert.Null(registry.Get(string.Empty));
    }

    private static WorkflowBehaviorRegistry NewRegistry(params IWorkflowBehavior[] builtIns) =>
        new(builtIns, NullLogger<WorkflowBehaviorRegistry>.Instance);

    private sealed class StubBehavior : IWorkflowBehavior
    {
        public StubBehavior(string key, string displayName)
        {
            Key = key;
            DisplayName = displayName;
        }

        public string Key { get; }
        public string DisplayName { get; }
        public string? Description => null;

        public Task<BehaviorResult> ExecuteAsync(BehaviorContext context, CancellationToken cancellationToken) =>
            Task.FromResult(BehaviorResult.Ok());
    }
}
