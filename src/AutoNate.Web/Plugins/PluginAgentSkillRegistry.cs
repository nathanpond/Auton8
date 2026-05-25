using System.Collections.Concurrent;
using AutoNate.Plugins.Abstractions;

namespace AutoNate.Web.Plugins;

// Singleton registry of plugin-contributed chatbot skills. Mirrors
// PluginScheduledJobRegistry: keyed for global uniqueness, tagged by
// pluginId so a disable can sweep one plugin's registrations cleanly.
//
// Lifecycle: a plugin calls IPluginAgentSkills.Register during its
// Configure(), the per-plugin PluginAgentSkills facade forwards into this
// registry. On disable, the host invokes RemoveAll on the facade, which
// calls RemoveForPlugin here.
//
// Visibility: PluginContributedSkill (a single IAgentSkill instance
// registered in DI) returns Snapshot() per access, so the host's
// per-request SkillRegistry sees every currently-registered plugin tool
// the next time the agent loop builds its catalog.
public sealed class PluginAgentSkillRegistry
{
    private readonly ConcurrentDictionary<string, RegisteredPluginSkill> _byToolName =
        new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _skillNamesInUse =
        new(StringComparer.Ordinal);

    public void Register(
        Guid pluginId,
        string skillName,
        string skillDescription,
        IReadOnlyList<PluginAgentTool> tools,
        Func<PluginAgentSessionContext, string?>? systemPromptFragment)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(skillName);
        ArgumentNullException.ThrowIfNull(tools);
        if (tools.Count == 0)
        {
            throw new ArgumentException("At least one tool is required.", nameof(tools));
        }

        if (!_skillNamesInUse.TryAdd(skillName, 0))
        {
            throw new InvalidOperationException(
                $"A plugin agent skill named '{skillName}' is already registered.");
        }

        // Pre-validate tool-name uniqueness so a partial registration doesn't
        // leave half the tools in the dictionary and the other half rejected.
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tool in tools)
        {
            if (tool is null) throw new ArgumentException("Null tool in list.", nameof(tools));
            if (string.IsNullOrWhiteSpace(tool.Name))
                throw new ArgumentException("Tool name cannot be empty.", nameof(tools));
            if (!seen.Add(tool.Name))
                throw new ArgumentException($"Duplicate tool name '{tool.Name}' within skill '{skillName}'.", nameof(tools));
            if (_byToolName.ContainsKey(tool.Name))
            {
                _skillNamesInUse.TryRemove(skillName, out _);
                throw new InvalidOperationException(
                    $"A plugin agent tool named '{tool.Name}' is already registered.");
            }
        }

        foreach (var tool in tools)
        {
            _byToolName[tool.Name] = new RegisteredPluginSkill(
                pluginId, skillName, skillDescription, tool, systemPromptFragment);
        }
    }

    public int RemoveForPlugin(Guid pluginId)
    {
        var removed = 0;
        var removedSkillNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (key, reg) in _byToolName.ToArray())
        {
            if (reg.PluginId == pluginId && _byToolName.TryRemove(key, out _))
            {
                removed++;
                removedSkillNames.Add(reg.SkillName);
            }
        }
        foreach (var skillName in removedSkillNames)
        {
            _skillNamesInUse.TryRemove(skillName, out _);
        }
        return removed;
    }

    // Snapshot of every registered plugin tool, grouped by skill. Returns a
    // copy so the consumer can iterate without holding the dictionary lock.
    public IReadOnlyList<RegisteredPluginSkillGroup> SnapshotGrouped()
    {
        var byKey = new Dictionary<string, List<RegisteredPluginSkill>>(StringComparer.Ordinal);
        foreach (var reg in _byToolName.Values)
        {
            if (!byKey.TryGetValue(reg.SkillName, out var list))
            {
                list = new List<RegisteredPluginSkill>();
                byKey[reg.SkillName] = list;
            }
            list.Add(reg);
        }
        return byKey
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => new RegisteredPluginSkillGroup(
                kv.Key,
                kv.Value[0].SkillDescription,
                kv.Value[0].SystemPromptFragment,
                kv.Value
                    .OrderBy(r => r.Tool.Name, StringComparer.Ordinal)
                    .ToList()))
            .ToList();
    }
}

// Internal row representing one tool registered by one plugin. The
// SystemPromptFragment is owned at the skill level; every tool from the
// same skill carries the same delegate reference so the aggregator can use
// any of them when building the host's IAgentSkill view.
public sealed record RegisteredPluginSkill(
    Guid PluginId,
    string SkillName,
    string SkillDescription,
    PluginAgentTool Tool,
    Func<PluginAgentSessionContext, string?>? SystemPromptFragment);

public sealed record RegisteredPluginSkillGroup(
    string SkillName,
    string SkillDescription,
    Func<PluginAgentSessionContext, string?>? SystemPromptFragment,
    IReadOnlyList<RegisteredPluginSkill> Registrations);
