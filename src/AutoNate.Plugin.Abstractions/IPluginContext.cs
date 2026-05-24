namespace AutoNate.Plugins.Abstractions;

// Hand-off the host gives a plugin during Configure(). Replaces the previous
// (IHookRegistrar, IServiceProvider) pair so plugin-specific context (the
// 8-char Code, the schema name, the data-access surface) can be expressed as
// first-class properties instead of resolved through a global service locator
// that has no way to know which plugin is asking.
public interface IPluginContext
{
    Guid PluginId { get; }

    // 8-char namespace identifier, e.g. "a1b2c3d4". Used as the prefix on the
    // plugin's schema (and role) name so its tables are visually owner-tagged
    // when referenced by other plugins or in psql.
    string Code { get; }

    // Fully-qualified name of the plugin's owned schema, "plg_<code>". All of
    // the plugin's own tables live here; cross-plugin reads use this same
    // schema name on the producer side.
    string SchemaName { get; }

    IHookRegistrar Hooks { get; }

    // Read/write surface for the plugin's own schema, read-only surface for
    // everything else. Connections opened through this object are
    // authenticated as the per-plugin Postgres role, so isolation is enforced
    // by the database — not by this wrapper.
    IPluginDataAccess Data { get; }

    // Helpers for registering menu items inside the host's menu system.
    // Items added here are tagged with the plugin's ID and auto-cleaned on
    // disable/delete; plugins re-register them inside Configure() on each
    // enable.
    IPluginMenus Menus { get; }

    // Helpers for registering IWorkflowBehavior implementations the workflow
    // studio surfaces in the service-task picker. Same lifecycle as Menus:
    // tagged by plugin id and auto-removed on disable.
    IPluginBehaviors Behaviors { get; }

    // Helpers for contributing projections (scheduled cache populators) to
    // the host's projection framework. Each registered job appears on
    // /api/admin/projections with the same health / pause surface as
    // built-in projections.
    IPluginProjections Projections { get; }

    // Host services for cross-cutting needs (logging, etc.). The host wraps
    // its root provider in an allowlist before handing it over, so only a
    // curated set of safe types resolve — currently ILoggerFactory,
    // ILogger<T>, TimeProvider, IHostEnvironment. GetService on anything
    // else returns null on purpose (it prevents a plugin from reaching
    // IConfiguration, connection strings, data-protection keys, or shared
    // secrets through this surface). Use `Data` for storage and ask the
    // host to expand the allowlist if a legitimate cross-cutting need
    // surfaces.
    IServiceProvider HostServices { get; }
}
