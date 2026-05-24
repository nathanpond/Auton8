using System.Collections.Concurrent;

namespace AutoNate.Web.Plugins;

// Singleton registry of plugin-contributed scheduled jobs. Each entry is
// (pluginId, name, interval, tick) — the host-side adapter for the
// IPluginProjections contract. PluginScheduledJobsHostedService snapshots
// the registry at app start and spawns one drain loop per entry.
//
// Multi-plugin concurrency safety: keyed by global job name so two plugins
// can't accidentally collide. Registration after host start is recorded
// here but won't drain until the next restart (documented limitation).
public sealed class PluginScheduledJobRegistry
{
    private readonly ConcurrentDictionary<string, PluginScheduledJob> _byName = new(StringComparer.OrdinalIgnoreCase);

    public PluginScheduledJob Register(
        Guid pluginId,
        string name,
        TimeSpan interval,
        Func<CancellationToken, Task> tick)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(interval), "Interval must be positive.");
        }
        var job = new PluginScheduledJob(pluginId, name, interval, tick);
        if (!_byName.TryAdd(name, job))
        {
            throw new InvalidOperationException(
                $"A plugin scheduled job named '{name}' is already registered.");
        }
        return job;
    }

    public int RemoveForPlugin(Guid pluginId)
    {
        var removed = 0;
        foreach (var (key, job) in _byName.ToArray())
        {
            if (job.PluginId == pluginId && _byName.TryRemove(key, out _))
            {
                removed++;
            }
        }
        return removed;
    }

    public IReadOnlyList<PluginScheduledJob> Snapshot() =>
        _byName.Values.OrderBy(j => j.Name, StringComparer.OrdinalIgnoreCase).ToList();
}

public sealed record PluginScheduledJob(
    Guid PluginId,
    string Name,
    TimeSpan Interval,
    Func<CancellationToken, Task> Tick);
