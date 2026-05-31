using AutoNate.Plugins.Abstractions;

namespace AutoNate.Web.Plugins;

// Stand-in for IPluginConnectors used by test setups that don't wire a
// connector registry into PluginRuntime. Mirrors NoopPluginBehaviors.
internal sealed class NoopPluginConnectors : IPluginConnectors
{
    public void Register(IPluginDataConnector connector)
    {
        // Silently accept. The plugin's Configure() completes; the host
        // just won't see the registration — fine for tests that don't
        // exercise the data-connector surface.
    }

    public IReadOnlyList<IPluginDataConnector> Registered => Array.Empty<IPluginDataConnector>();

    public int RemoveAll() => 0;
}
