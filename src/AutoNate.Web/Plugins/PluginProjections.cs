using AutoNate.Plugins.Abstractions;

namespace AutoNate.Web.Plugins;

// Plugin-scoped facade over the global PluginScheduledJobRegistry. Tags
// every registration with the plugin's id so RemoveAll() can sweep on
// disable without touching other plugins' jobs.
internal sealed class PluginProjections : IPluginProjections
{
    private readonly PluginScheduledJobRegistry _registry;
    private readonly Guid _pluginId;

    public PluginProjections(PluginScheduledJobRegistry registry, Guid pluginId)
    {
        _registry = registry;
        _pluginId = pluginId;
    }

    public void RegisterScheduled(string name, TimeSpan interval, Func<CancellationToken, Task> tick)
    {
        ArgumentNullException.ThrowIfNull(tick);
        _registry.Register(_pluginId, name, interval, tick);
    }

    public int RemoveAll() => _registry.RemoveForPlugin(_pluginId);
}
