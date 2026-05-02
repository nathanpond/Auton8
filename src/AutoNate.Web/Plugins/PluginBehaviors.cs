using AutoNate.Plugins.Abstractions;
using AutoNate.Web.Services.Workflow.Behaviors;

namespace AutoNate.Web.Plugins;

// Host-side IPluginBehaviors. Forwards Register calls to the central
// WorkflowBehaviorRegistry tagged with this plugin's id; lifecycle code
// in PluginRuntime invokes RemoveAllForPlugin on disable.
internal sealed class PluginBehaviors : IPluginBehaviors
{
    private readonly IWorkflowBehaviorRegistry _registry;
    private readonly Guid _pluginId;
    private readonly List<IWorkflowBehavior> _accepted = new();
    private readonly object _gate = new();

    public PluginBehaviors(IWorkflowBehaviorRegistry registry, Guid pluginId)
    {
        _registry = registry;
        _pluginId = pluginId;
    }

    public void Register(IWorkflowBehavior behavior)
    {
        ArgumentNullException.ThrowIfNull(behavior);
        if (_registry.RegisterFromPlugin(_pluginId, behavior))
        {
            lock (_gate)
            {
                _accepted.Add(behavior);
            }
        }
    }

    public IReadOnlyList<IWorkflowBehavior> Registered
    {
        get
        {
            lock (_gate)
            {
                return _accepted.ToArray();
            }
        }
    }

    public int RemoveAll()
    {
        var removed = _registry.RemoveAllForPlugin(_pluginId);
        lock (_gate)
        {
            _accepted.Clear();
        }
        return removed;
    }
}
