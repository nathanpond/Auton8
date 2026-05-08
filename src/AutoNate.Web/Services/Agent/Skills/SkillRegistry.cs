using AutoNate.Web.Services.Agent.Providers;

namespace AutoNate.Web.Services.Agent.Skills;

public interface ISkillRegistry
{
    IReadOnlyList<IAgentSkill> All { get; }

    IReadOnlyList<ChatTool> ChatTools { get; }

    bool TryGetTool(string toolName, out AgentTool? tool, out IAgentSkill? owner);
}

public sealed class SkillRegistry : ISkillRegistry
{
    private readonly Dictionary<string, (AgentTool Tool, IAgentSkill Skill)> _tools;

    public SkillRegistry(IEnumerable<IAgentSkill> skills)
    {
        All = skills.ToList();
        _tools = new Dictionary<string, (AgentTool, IAgentSkill)>(StringComparer.Ordinal);
        foreach (var skill in All)
        {
            foreach (var tool in skill.Tools)
            {
                if (_tools.ContainsKey(tool.Name))
                {
                    throw new InvalidOperationException(
                        $"Duplicate tool name '{tool.Name}'. Each registered skill must declare unique tools.");
                }
                _tools[tool.Name] = (tool, skill);
            }
        }
        ChatTools = All.SelectMany(s => s.Tools).Select(t => t.ToChatTool()).ToList();
    }

    public IReadOnlyList<IAgentSkill> All { get; }
    public IReadOnlyList<ChatTool> ChatTools { get; }

    public bool TryGetTool(string toolName, out AgentTool? tool, out IAgentSkill? owner)
    {
        if (_tools.TryGetValue(toolName, out var pair))
        {
            tool = pair.Tool;
            owner = pair.Skill;
            return true;
        }
        tool = null;
        owner = null;
        return false;
    }
}
