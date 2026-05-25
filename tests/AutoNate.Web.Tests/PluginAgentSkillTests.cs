using System.Security.Claims;
using System.Text.Json;
using AutoNate.Plugins.Abstractions;
using AutoNate.Web.Plugins;
using AutoNate.Web.Services.Agent.Skills;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AutoNate.Web.Tests;

// Phase 4: plugin-contributed chatbot skills. Tests focus on the host-side
// adapter (PluginAgentSkillRegistry + PluginContributedSkill) — the parts
// the host owns. End-to-end ALC + plugin-loader tests live in the existing
// plugin-runtime test surface; here we exercise the registry contract and
// the IAgentSkill aggregator that turns plugin DTOs into host tools.
public sealed class PluginAgentSkillTests
{
    private static readonly Guid PluginA = Guid.Parse("aaaaaaaa-1111-1111-1111-aaaaaaaaaaaa");
    private static readonly Guid PluginB = Guid.Parse("bbbbbbbb-2222-2222-2222-bbbbbbbbbbbb");
    private static readonly Guid SessionUserId = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");

    [Fact]
    public void registry_register_then_snapshot_returns_the_registered_tool()
    {
        var registry = new PluginAgentSkillRegistry();
        registry.Register(
            PluginA, "skill-a", "Description A",
            new[] { MakeTool("tool_a") }, systemPromptFragment: null);

        var snapshot = registry.SnapshotGrouped();
        var group = Assert.Single(snapshot);
        Assert.Equal("skill-a", group.SkillName);
        Assert.Equal("tool_a", Assert.Single(group.Registrations).Tool.Name);
    }

    [Fact]
    public void registry_rejects_duplicate_skill_name_across_plugins()
    {
        var registry = new PluginAgentSkillRegistry();
        registry.Register(PluginA, "shared", "from A", new[] { MakeTool("from_a") }, null);
        var ex = Assert.Throws<InvalidOperationException>(() =>
            registry.Register(PluginB, "shared", "from B", new[] { MakeTool("from_b") }, null));
        Assert.Contains("shared", ex.Message);
    }

    [Fact]
    public void registry_rejects_duplicate_tool_name_across_plugins()
    {
        var registry = new PluginAgentSkillRegistry();
        registry.Register(PluginA, "skill-a", "A", new[] { MakeTool("dup_tool") }, null);
        var ex = Assert.Throws<InvalidOperationException>(() =>
            registry.Register(PluginB, "skill-b", "B", new[] { MakeTool("dup_tool") }, null));
        Assert.Contains("dup_tool", ex.Message);
    }

    [Fact]
    public void registry_RemoveForPlugin_sweeps_only_that_plugins_skills()
    {
        var registry = new PluginAgentSkillRegistry();
        registry.Register(PluginA, "skill-a", "A", new[] { MakeTool("a1"), MakeTool("a2") }, null);
        registry.Register(PluginB, "skill-b", "B", new[] { MakeTool("b1") }, null);

        var removed = registry.RemoveForPlugin(PluginA);
        Assert.Equal(2, removed);

        var snapshot = registry.SnapshotGrouped();
        var group = Assert.Single(snapshot);
        Assert.Equal("skill-b", group.SkillName);

        // Skill name freed — same plugin can re-register cleanly after a
        // disable + re-enable cycle.
        registry.Register(PluginA, "skill-a", "A", new[] { MakeTool("a1-redo") }, null);
        Assert.Equal(2, registry.SnapshotGrouped().Count);
    }

    [Fact]
    public void registry_partial_failure_does_NOT_register_any_of_a_skills_tools()
    {
        var registry = new PluginAgentSkillRegistry();
        registry.Register(PluginA, "skill-a", "A", new[] { MakeTool("a1") }, null);

        // Second tool collides; the whole call should reject and leave nothing
        // behind from PluginB's attempted registration.
        Assert.Throws<InvalidOperationException>(() =>
            registry.Register(PluginB, "skill-b", "B",
                new[] { MakeTool("b1_ok"), MakeTool("a1") }, null));

        // skill-a from PluginA still present; no skill-b leaked.
        var snapshot = registry.SnapshotGrouped();
        var skills = snapshot.Select(g => g.SkillName).ToHashSet();
        Assert.Contains("skill-a", skills);
        Assert.DoesNotContain("skill-b", skills);

        // b1_ok must not be in the registry either — pre-flight check is
        // expected to reject the whole call atomically.
        Assert.DoesNotContain(snapshot.SelectMany(g => g.Registrations.Select(r => r.Tool.Name)), n => n == "b1_ok");
    }

    [Fact]
    public void PluginContributedSkill_aggregates_registered_tools_into_one_IAgentSkill()
    {
        var registry = new PluginAgentSkillRegistry();
        registry.Register(PluginA, "skill-a", "A", new[] { MakeTool("a_tool_1"), MakeTool("a_tool_2") }, null);
        registry.Register(PluginB, "skill-b", "B", new[] { MakeTool("b_tool_1") }, null);

        var services = new ServiceCollection().BuildServiceProvider();
        var skill = new PluginContributedSkill(registry, services);

        Assert.Equal("plugin-skills", skill.Name);
        Assert.Equal(3, skill.Tools.Count);
        var toolNames = skill.Tools.Select(t => t.Name).ToHashSet();
        Assert.Contains("a_tool_1", toolNames);
        Assert.Contains("a_tool_2", toolNames);
        Assert.Contains("b_tool_1", toolNames);
    }

    [Fact]
    public void PluginContributedSkill_with_empty_registry_has_zero_tools_and_no_prompt_fragment()
    {
        var registry = new PluginAgentSkillRegistry();
        var services = new ServiceCollection().BuildServiceProvider();
        var skill = new PluginContributedSkill(registry, services);
        Assert.Empty(skill.Tools);
        var session = new AgentSessionContext(new ClaimsPrincipal(), SessionUserId, "test");
        Assert.Null(skill.SystemPromptFragment(session));
    }

    [Fact]
    public async Task PluginContributedSkill_invoke_routes_args_to_the_registered_handler()
    {
        var registry = new PluginAgentSkillRegistry();
        registry.Register(
            PluginA, "skill-a", "A",
            new[]
            {
                new PluginAgentTool(
                    Name: "echo",
                    Description: "echo",
                    JsonSchema: ParseSchema("""{"type":"object"}"""),
                    Invoke: (args, ctx, ct) =>
                    {
                        var message = args.TryGetProperty("message", out var m) ? m.GetString() : "";
                        var envelope = JsonSerializer.SerializeToElement(new
                        {
                            kind = "echo",
                            source = "test",
                            data = new { message, userId = ctx.Session.UserId }
                        });
                        return Task.FromResult(envelope);
                    })
            },
            systemPromptFragment: null);

        var services = new ServiceCollection().BuildServiceProvider();
        var skill = new PluginContributedSkill(registry, services);
        var tool = skill.Tools.Single(t => t.Name == "echo");
        var args = ParseSchema("""{ "message": "hi" }""");
        var ctx = new AgentToolContext(
            new AgentSessionContext(new ClaimsPrincipal(), SessionUserId, "test"),
            services);

        var result = await tool.Invoke(args, ctx, CancellationToken.None);
        Assert.Equal("echo", result.GetProperty("kind").GetString());
        Assert.Equal("hi", result.GetProperty("data").GetProperty("message").GetString());
        Assert.Equal(SessionUserId.ToString(), result.GetProperty("data").GetProperty("userId").GetString());
    }

    [Fact]
    public async Task PluginContributedSkill_catches_plugin_exceptions_and_returns_error_envelope()
    {
        var registry = new PluginAgentSkillRegistry();
        registry.Register(
            PluginA, "skill-a", "A",
            new[]
            {
                new PluginAgentTool(
                    Name: "boom",
                    Description: "throws",
                    JsonSchema: ParseSchema("""{"type":"object"}"""),
                    Invoke: (_, _, _) => throw new InvalidOperationException("plugin failed"))
            },
            systemPromptFragment: null);

        var services = new ServiceCollection().BuildServiceProvider();
        var skill = new PluginContributedSkill(registry, services);
        var tool = skill.Tools.Single(t => t.Name == "boom");
        var args = ParseSchema("{}");
        var ctx = new AgentToolContext(
            new AgentSessionContext(new ClaimsPrincipal(), SessionUserId, "test"),
            services);

        var result = await tool.Invoke(args, ctx, CancellationToken.None);
        Assert.Equal("error", result.GetProperty("kind").GetString());
        Assert.Contains("plugin failed", result.GetProperty("data").GetProperty("message").GetString()!);
    }

    [Fact]
    public void multi_cycle_register_and_RemoveForPlugin_does_not_leak_tools()
    {
        // Simulates the ALC-style enable/disable churn called out in the plan:
        // ten enable→disable cycles must each end with zero tools and the same
        // skill name freed for re-registration.
        var registry = new PluginAgentSkillRegistry();
        for (var i = 0; i < 10; i++)
        {
            registry.Register(
                PluginA, "skill-cycle", $"A round {i}",
                new[] { MakeTool($"tool_{i}_a"), MakeTool($"tool_{i}_b") },
                null);
            Assert.Equal(2, registry.SnapshotGrouped().Sum(g => g.Registrations.Count));
            var removed = registry.RemoveForPlugin(PluginA);
            Assert.Equal(2, removed);
            Assert.Empty(registry.SnapshotGrouped());
        }
    }

    [Fact]
    public void PluginContributedSkill_descriptions_include_each_skill_name_and_description()
    {
        var registry = new PluginAgentSkillRegistry();
        registry.Register(PluginA, "skill-a", "ADESC", new[] { MakeTool("a1") }, null);
        registry.Register(PluginB, "skill-b", "BDESC", new[] { MakeTool("b1") }, null);

        var services = new ServiceCollection().BuildServiceProvider();
        var skill = new PluginContributedSkill(registry, services);
        Assert.Contains("skill-a", skill.Description);
        Assert.Contains("ADESC", skill.Description);
        Assert.Contains("skill-b", skill.Description);
        Assert.Contains("BDESC", skill.Description);
    }

    // --- helpers ---

    private static PluginAgentTool MakeTool(string name) =>
        new(
            Name: name,
            Description: $"description for {name}",
            JsonSchema: ParseSchema("""{"type":"object"}"""),
            Invoke: (_, _, _) => Task.FromResult(ParseSchema("""{"kind":"ok"}""")));

    private static JsonElement ParseSchema(string raw)
    {
        using var doc = JsonDocument.Parse(raw);
        return doc.RootElement.Clone();
    }
}
