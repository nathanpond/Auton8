using AutoNate.Plugins.Abstractions;

namespace AutoNate.Web.Plugins;

// Plugin-scoped facade over the global PluginAgentSkillRegistry. Tags every
// registration with the plugin's id so RemoveAll() can sweep on disable
// without touching other plugins' skills. Mirrors PluginProjections exactly.
internal sealed class PluginAgentSkills : IPluginAgentSkills
{
    private readonly PluginAgentSkillRegistry _registry;
    private readonly Guid _pluginId;

    public PluginAgentSkills(PluginAgentSkillRegistry registry, Guid pluginId)
    {
        _registry = registry;
        _pluginId = pluginId;
    }

    public void Register(
        string skillName,
        string skillDescription,
        IReadOnlyList<PluginAgentTool> tools,
        Func<PluginAgentSessionContext, string?>? systemPromptFragment = null)
    {
        _registry.Register(_pluginId, skillName, skillDescription, tools, systemPromptFragment);
    }

    public int RemoveAll() => _registry.RemoveForPlugin(_pluginId);
}
