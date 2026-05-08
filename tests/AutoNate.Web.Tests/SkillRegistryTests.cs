using AutoNate.Web.Services.Agent.Skills;
using Xunit;

namespace AutoNate.Web.Tests;

public sealed class SkillRegistryTests
{
    [Fact]
    public void Registers_the_three_phase_5_skills_with_unique_tool_names()
    {
        var skills = new IAgentSkill[]
        {
            new ExplainWorkflowSkill(),
            new LookupRecordsSkill(),
            new AnalyzeSystemIssueSkill()
        };

        var registry = new SkillRegistry(skills);

        Assert.Equal(3, registry.All.Count);

        // Spot-check tool names so the agent loop's ChatRequest.Tools list lines up.
        var names = registry.ChatTools.Select(t => t.Name).ToHashSet();
        Assert.Contains("find_workflow", names);
        Assert.Contains("explain_workflow", names);
        Assert.Contains("list_record_types", names);
        Assert.Contains("search_records", names);
        Assert.Contains("get_record", names);
        Assert.Contains("list_system_issues", names);
        Assert.Contains("get_system_issue", names);
    }

    [Fact]
    public void TryGetTool_returns_owning_skill_for_known_name()
    {
        var registry = new SkillRegistry(new IAgentSkill[]
        {
            new ExplainWorkflowSkill()
        });

        var found = registry.TryGetTool("find_workflow", out var tool, out var owner);

        Assert.True(found);
        Assert.NotNull(tool);
        Assert.NotNull(owner);
        Assert.Equal("explain-workflow", owner!.Name);
    }

    [Fact]
    public void TryGetTool_returns_false_for_unknown()
    {
        var registry = new SkillRegistry(new IAgentSkill[]
        {
            new ExplainWorkflowSkill()
        });

        var found = registry.TryGetTool("does_not_exist", out var tool, out var owner);

        Assert.False(found);
        Assert.Null(tool);
        Assert.Null(owner);
    }

    [Fact]
    public void Duplicate_tool_names_throw_at_construction()
    {
        Assert.Throws<InvalidOperationException>(() =>
            new SkillRegistry(new IAgentSkill[]
            {
                new ExplainWorkflowSkill(),
                new ExplainWorkflowSkill()
            }));
    }
}
