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
}
