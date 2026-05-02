using System.Collections.Concurrent;
using AutoNate.Plugins.Abstractions;

namespace AutoNate.Web.Services.Workflow.Behaviors;

// Singleton registry merging two sources:
//   * Built-ins: every IWorkflowBehavior registered in DI (host code).
//   * Plugin-contributed: registered at enable via IPluginBehaviors.Register
//     and tagged with the plugin's id so disable can sweep them.
//
// Lookup precedence on key collisions: built-ins beat plugins, earlier
// registrations beat later ones. Collisions are logged at warning level.
public sealed class WorkflowBehaviorRegistry : IWorkflowBehaviorRegistry
{
    private readonly ILogger<WorkflowBehaviorRegistry> _log;
    private readonly Dictionary<string, IWorkflowBehavior> _builtIns;

    private readonly ConcurrentDictionary<Guid, List<IWorkflowBehavior>> _pluginRegistrations = new();
    private readonly object _gate = new();

    public WorkflowBehaviorRegistry(
        IEnumerable<IWorkflowBehavior> builtIns,
        ILogger<WorkflowBehaviorRegistry> log)
    {
        _log = log;
        _builtIns = new Dictionary<string, IWorkflowBehavior>(StringComparer.Ordinal);
        foreach (var behavior in builtIns)
        {
            if (string.IsNullOrWhiteSpace(behavior.Key))
            {
                _log.LogWarning(
                    "Skipping workflow behavior {Type}: Key is required.",
                    behavior.GetType().FullName);
                continue;
            }
            if (!_builtIns.TryAdd(behavior.Key, behavior))
            {
                _log.LogWarning(
                    "Workflow behavior key '{Key}' is already registered as a built-in; ignoring duplicate {Type}.",
                    behavior.Key, behavior.GetType().FullName);
            }
        }
    }

    public IWorkflowBehavior? Get(string key)
    {
        if (string.IsNullOrEmpty(key)) return null;
        if (_builtIns.TryGetValue(key, out var builtIn))
        {
            return builtIn;
        }
        lock (_gate)
        {
            foreach (var list in _pluginRegistrations.Values)
            {
                foreach (var behavior in list)
                {
                    if (string.Equals(behavior.Key, key, StringComparison.Ordinal))
                    {
                        return behavior;
                    }
                }
            }
        }
        return null;
    }

    public IReadOnlyList<IWorkflowBehavior> GetAll()
    {
        var result = new List<IWorkflowBehavior>(_builtIns.Values);
        var seen = new HashSet<string>(_builtIns.Keys, StringComparer.Ordinal);
        lock (_gate)
        {
            foreach (var list in _pluginRegistrations.Values)
            {
                foreach (var behavior in list)
                {
                    if (seen.Add(behavior.Key))
                    {
                        result.Add(behavior);
                    }
                }
            }
        }
        return result;
    }

    public bool RegisterFromPlugin(Guid pluginId, IWorkflowBehavior behavior)
    {
        if (string.IsNullOrWhiteSpace(behavior.Key))
        {
            _log.LogWarning(
                "Plugin {PluginId} tried to register a workflow behavior with an empty Key; ignored.", pluginId);
            return false;
        }

        if (_builtIns.ContainsKey(behavior.Key))
        {
            _log.LogWarning(
                "Plugin {PluginId} tried to register workflow behavior '{Key}' which collides with a built-in; ignored.",
                pluginId, behavior.Key);
            return false;
        }

        lock (_gate)
        {
            foreach (var (otherPluginId, list) in _pluginRegistrations)
            {
                foreach (var existing in list)
                {
                    if (string.Equals(existing.Key, behavior.Key, StringComparison.Ordinal))
                    {
                        _log.LogWarning(
                            "Plugin {PluginId} tried to register workflow behavior '{Key}' which is already registered by plugin {OwnerPluginId}; ignored.",
                            pluginId, behavior.Key, otherPluginId);
                        return false;
                    }
                }
            }
            var bucket = _pluginRegistrations.GetOrAdd(pluginId, _ => new List<IWorkflowBehavior>());
            bucket.Add(behavior);
        }
        _log.LogInformation(
            "Plugin {PluginId} registered workflow behavior '{Key}'.", pluginId, behavior.Key);
        return true;
    }

    public int RemoveAllForPlugin(Guid pluginId)
    {
        if (!_pluginRegistrations.TryRemove(pluginId, out var list))
        {
            return 0;
        }
        if (list.Count > 0)
        {
            _log.LogInformation(
                "Removed {Count} workflow behavior(s) registered by plugin {PluginId}.",
                list.Count, pluginId);
        }
        return list.Count;
    }
}
