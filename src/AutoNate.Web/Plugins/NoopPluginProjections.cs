using AutoNate.Plugins.Abstractions;

namespace AutoNate.Web.Plugins;

// Used when the plugin runtime is constructed in a context without a
// scheduled-job registry (notably during Cleanup() on uninstall, where the
// host wants the same surface area but no further registrations are valid).
// Mirrors NoopPluginMenus / NoopPluginBehaviors.
internal sealed class NoopPluginProjections : IPluginProjections
{
    public void RegisterScheduled(string name, TimeSpan interval, Func<CancellationToken, Task> tick)
    {
        // Intentionally silent — Cleanup() may try to re-register if a
        // plugin's Configure/Cleanup are symmetric, and we don't want to
        // throw inside the host's plugin teardown loop.
    }

    public int RemoveAll() => 0;
}
