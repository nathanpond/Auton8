# Recipe: plugin-contributed scheduled job

Plugins use a leaner contract than host code. Instead of registering
`IProjection<TSource>` / `IChangeFeed<TSource>` against the framework's
DI container (which they can't reach at app startup time), plugins call
`IPluginContext.Projections.RegisterScheduled(...)` from inside their
`Configure(IPluginContext)` method.

The host wraps each registration in a drain loop that ticks on the
declared interval and records into `IProjectionHealthService` — so
plugin jobs show up on `/api/admin/projections` alongside host
projections.

## When to use this

- Periodically refreshing a plugin-owned cache table from the plugin's
  own data source.
- Recomputing an expensive aggregate that the plugin's other code reads.
- Wiring a plugin's polling integration with an external SaaS into a
  framework that automatically exposes pause/resume/health.

For sub-second freshness or anything that needs the full
`IProjection<TSource>` machinery, you'd subscribe to events directly in
your plugin and write to your own tables — the scheduled-job API is the
"easy path" specifically for periodic refresh.

## Limitation to know about

Jobs registered after host startup don't begin draining until the next
app restart. The `PluginScheduledJobsHostedService` snapshots the
registry once on `ExecuteAsync`. Dynamic runtime spin-up of newly
registered jobs is a planned enhancement; the contract won't change.

So: plugin enable-at-startup → jobs run on the next interval. Plugin
enable-at-runtime → jobs are registered (the entry appears on the admin
page) but don't tick until the process restarts. Disable always sweeps
immediately.

## Skeleton

Inside your plugin's `Configure`:

```csharp
public void Configure(IPluginContext context)
{
    context.Projections.RegisterScheduled(
        name: $"{context.Code}.refresh_inventory",
        interval: TimeSpan.FromMinutes(15),
        tick: async ct =>
        {
            await using var conn = context.Data.OpenConnection();
            // Read from your plugin's source — could be the plugin's
            // own DB schema, an external API, anything.
            var inventory = await FetchInventoryAsync(conn, ct);

            // Write into your plugin's schema (which is what
            // context.Data.OpenConnection grants write access to).
            foreach (var item in inventory)
            {
                await UpsertInventoryRowAsync(conn, item, ct);
            }
        });
}
```

### Naming

The `name` argument must be globally unique across all plugins and host
projections. Convention: prefix with your plugin's `Code` so collisions
are impossible.

```csharp
$"{context.Code}.refresh_inventory"   // ✅
"refresh_inventory"                    // ❌ would collide with any other plugin's job of the same name
```

The host throws `InvalidOperationException` at registration time if
the name is already taken.

### Interval

Must be positive. Choose generously — a 15-minute interval on a job
that takes 2 seconds is 0.2% CPU; a 30-second interval on the same job
is 6.7%. Sub-minute intervals are usually wrong for plugin jobs (the
framework's polling feeds are a better fit when you need sub-minute
freshness, but those aren't available to plugins yet).

### Failure handling

Exceptions thrown from your `tick` delegate are caught, logged via the
host's logger factory, and recorded against
`projection.batch_failures_total`. The next tick fires on schedule
regardless. The host never propagates plugin exceptions into its own
service-host lifecycle.

If you want backoff on failure (rather than fixed cadence), implement
it yourself inside `tick` — sleep + return, or track a "last failure at"
and skip subsequent ticks until enough time has passed.

## Cleanup

The host calls `RemoveAll()` on your `IPluginProjections` when your
plugin is disabled or deleted. You don't need to manage this yourself.
If you want to drop a single job mid-life, there's no public API for
that today — just re-register on every `Configure` and let
disable/enable cycles do the resetting.

## Cleanup hook

If your plugin has its own `Cleanup` implementation, you don't need to
call `RemoveAll` from it — the host already sweeps before any plugin
Cleanup runs. But you can call it explicitly if Cleanup mirrors your
Configure and you want a self-contained teardown.

```csharp
public void Cleanup(IPluginContext context)
{
    // Optional — host already sweeps. Mirrors the Menus.RemoveAll() /
    // Behaviors patterns if you prefer symmetric Configure/Cleanup.
    context.Projections.RemoveAll();
}
```

## Visibility in admin UI

Plugin jobs appear in `GET /api/admin/projections/` and the
`/admin/config/projections` SPA page with:

- `name` = your job name
- `feeds[].feedName` = `"plugin.scheduled"` (constant — all plugin jobs
  share the same feed-name label)
- `eventsAppliedTotal` = number of successful ticks
- `applyFailuresTotal` = number of ticks that threw

Pause/resume from the admin page works against your job by name. There
is no per-plugin "rebuild" action — that's a host-projection concept tied
to `BackfillRunner`.

## Testing

The framework ships `PluginScheduledJobRegistry` as a singleton you can
resolve in tests without needing a real plugin loaded. See
`ProjectionFrameworkPhase4Tests.Plugin_scheduled_job_registry_dedupes_by_name`
for a minimal example.

To test the tick path itself, you can construct a `PluginProjections`
manually with a real `PluginScheduledJobRegistry` and a fake plugin id,
register a tick, then drive it directly:

```csharp
var registry = new PluginScheduledJobRegistry();
var projections = new PluginProjections(registry, pluginId: Guid.NewGuid());

var ticks = 0;
projections.RegisterScheduled("my.test.job", TimeSpan.FromSeconds(1),
    _ => { ticks++; return Task.CompletedTask; });

var job = registry.Snapshot().Single();
await job.Tick(CancellationToken.None);
Assert.Equal(1, ticks);
```

The `PluginScheduledJobsHostedService` only matters for the runtime
drain loop; tests can exercise the `Tick` delegate directly.
