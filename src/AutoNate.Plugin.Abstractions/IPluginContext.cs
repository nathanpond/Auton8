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

    // Host services for cross-cutting needs (logging, etc.). Avoid using this
    // for data access; use `Data` instead.
    IServiceProvider HostServices { get; }
}
