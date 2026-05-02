using AutoNate.Plugins.Abstractions;
using AutoNate.Web.Plugins;
using AutoNate.Web.Services.Workflow.Behaviors;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AutoNate.Web.Tests.Plugins;

public sealed class PluginBehaviorsTests
{
    [Fact]
    public void Register_AddsBehaviorToRegistryTaggedByPluginId()
    {
        var registry = new WorkflowBehaviorRegistry(
            Array.Empty<IWorkflowBehavior>(),
            NullLogger<WorkflowBehaviorRegistry>.Instance);
        var pluginId = Guid.NewGuid();
        var menus = new TestPluginBehaviors(registry, pluginId);

        var behavior = new TestBehavior("plugin.test", "Test");
        menus.Register(behavior);

        Assert.Same(behavior, registry.Get("plugin.test"));
        Assert.Single(menus.Registered);
    }

    [Fact]
    public void RemoveAll_ClearsBothLocalAndRegistryEntries()
    {
        var registry = new WorkflowBehaviorRegistry(
            Array.Empty<IWorkflowBehavior>(),
            NullLogger<WorkflowBehaviorRegistry>.Instance);
        var pluginId = Guid.NewGuid();
        var menus = new TestPluginBehaviors(registry, pluginId);
        menus.Register(new TestBehavior("a", "A"));
        menus.Register(new TestBehavior("b", "B"));

        var removed = menus.RemoveAll();

        Assert.Equal(2, removed);
        Assert.Empty(menus.Registered);
        Assert.Null(registry.Get("a"));
        Assert.Null(registry.Get("b"));
    }

    [Fact]
    public void Register_KeysCollidingWithBuiltIn_AreRejectedSilently()
    {
        var builtIn = new TestBehavior("autonate.builtin", "Built-in");
        var registry = new WorkflowBehaviorRegistry(
            new[] { (IWorkflowBehavior)builtIn },
            NullLogger<WorkflowBehaviorRegistry>.Instance);
        var menus = new TestPluginBehaviors(registry, Guid.NewGuid());

        menus.Register(new TestBehavior("autonate.builtin", "Plugin override"));

        Assert.Same(builtIn, registry.Get("autonate.builtin"));
        Assert.Empty(menus.Registered);
    }

    // PluginBehaviors is internal — re-implement the tiny surface for tests
    // rather than touching InternalsVisibleTo.
    private sealed class TestPluginBehaviors : IPluginBehaviors
    {
        private readonly IWorkflowBehaviorRegistry _registry;
        private readonly Guid _pluginId;
        private readonly List<IWorkflowBehavior> _accepted = new();

        public TestPluginBehaviors(IWorkflowBehaviorRegistry registry, Guid pluginId)
        {
            _registry = registry;
            _pluginId = pluginId;
        }

        public void Register(IWorkflowBehavior behavior)
        {
            if (_registry.RegisterFromPlugin(_pluginId, behavior))
            {
                _accepted.Add(behavior);
            }
        }

        public IReadOnlyList<IWorkflowBehavior> Registered => _accepted.ToArray();

        public int RemoveAll()
        {
            var removed = _registry.RemoveAllForPlugin(_pluginId);
            _accepted.Clear();
            return removed;
        }
    }

    private sealed class TestBehavior : IWorkflowBehavior
    {
        public TestBehavior(string key, string displayName)
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
