using AutoNate.Plugins.Abstractions;

namespace AutoNate.Web.Plugins;

// Used when the plugin runtime is constructed without a PluginAgentSkillRegistry
// (notably during Cleanup() / unprovisioned paths). Mirrors NoopPluginProjections.
internal sealed class NoopPluginAgentSkills : IPluginAgentSkills
{
    public void Register(
        string skillName,
        string skillDescription,
        IReadOnlyList<PluginAgentTool> tools,
        Func<PluginAgentSessionContext, string?>? systemPromptFragment = null)
    {
        // Silent. Cleanup() may try to re-register if the plugin's
        // Configure/Cleanup are symmetric; we don't want to throw inside
        // the host's plugin teardown loop.
    }

    public int RemoveAll() => 0;
}
