namespace AutoNate.Plugins.Abstractions;

// Plugin-facing helper for contributing IPluginDataConnector implementations
// to the host's data-connector handler registry. Mirrors IPluginBehaviors /
// IPluginProjections: registrations are tagged with the plugin's id and
// auto-removed on disable, so plugin authors re-register them every time
// their Configure(IPluginContext) runs.
public interface IPluginConnectors
{
    void Register(IPluginDataConnector connector);

    IReadOnlyList<IPluginDataConnector> Registered { get; }

    // Removes every connector this plugin previously registered. The host
    // calls this on disable; plugins can call it explicitly from Cleanup()
    // when they want them gone before host-driven teardown runs. Returns
    // the number of connectors removed.
    int RemoveAll();
}
