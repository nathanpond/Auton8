using AutoNate.Plugins.Abstractions;

namespace AutoNate.Web.Plugins;

// Stand-in for IPluginBehaviors used by test setups that don't wire a
// behavior registry into PluginRuntime. Mirrors the shape of NoopPluginMenus.
internal sealed class NoopPluginBehaviors : IPluginBehaviors
{
    public void Register(IWorkflowBehavior behavior)
    {
        // Silently accept. The plugin's Configure() can still complete; the
        // host just won't see the behavior in the registry — fine for tests
        // that don't exercise the workflow surface.
    }

    public IReadOnlyList<IWorkflowBehavior> Registered => Array.Empty<IWorkflowBehavior>();

    public int RemoveAll() => 0;
}
