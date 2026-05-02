namespace AutoNate.Plugins.Abstractions;

public interface IAutoNatePlugin
{
    string Name { get; }
    string Version { get; }

    // Called once at enable time. Use the context to register hooks and to
    // talk to the plugin's own per-plugin schema. The host has already run
    // any pending migration files from the plugin's `migrations/` folder
    // before this is invoked.
    void Configure(IPluginContext context);

    // Called once when the host is about to delete this plugin, BEFORE the
    // plugin's per-plugin schema/role, on-disk files, and database row are
    // torn down. Runs even if the plugin was disabled at the time of delete:
    // the host loads the assembly into a fresh AssemblyLoadContext just for
    // this call. Use it to remove artifacts the plugin created OUTSIDE the
    // host's automatic cleanup paths — e.g. menu items the plugin wants to
    // sweep explicitly, record types it registered, files it dropped under
    // shared folders, third-party state it owns.
    //
    // The host already (1) sweeps menu items tagged with this plugin's id
    // via FK CASCADE on the plugins row delete, (2) DROP SCHEMA CASCADEs the
    // entire plg_<code> schema, and (3) deletes the plugin's bin folder. So
    // a no-op default is correct for plugins that don't reach beyond those.
    // Any exception thrown here is logged and swallowed; deletion still
    // proceeds.
    void Cleanup(IPluginContext context) { }
}
