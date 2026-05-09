using System.Security.Claims;
using System.Text.Json;
using AutoNate.Web.Services.Agent.Loop;
using AutoNate.Web.Services.Agent.Skills;
using Xunit;

namespace AutoNate.Web.Tests;

public sealed class SystemPromptBuilderTests
{
    [Fact]
    public void Without_page_context_only_pageKey_appears()
    {
        var builder = new SystemPromptBuilder();
        var ctx = new AgentSessionContext(new ClaimsPrincipal(), Guid.Empty, "workflow", ConversationId: Guid.NewGuid());

        var prompt = builder.Build(ctx, Array.Empty<IAgentSkill>(), userDisplayName: null, userRoles: Array.Empty<string>());

        Assert.Contains("pageKey: workflow", prompt);
        Assert.DoesNotContain("Page snapshot", prompt);
        Assert.DoesNotContain("inspect_page", prompt);
    }

    [Fact]
    public void With_page_context_includes_summary_and_inspect_hint()
    {
        var builder = new SystemPromptBuilder();
        var snapshot = new PageContextSnapshot(
            PageKey: "workflow",
            SchemaVersion: 1,
            Summary: "Editing draft workflow 'Order Approval' (12 nodes); selection: User Task 'Manager Approval' (id: UserTask_3).",
            Version: 7,
            Data: JsonSerializer.SerializeToElement(new { workflow = new { id = "abc" } }));
        var ctx = new AgentSessionContext(new ClaimsPrincipal(), Guid.Empty, "workflow",
            ConversationId: Guid.NewGuid(), PageContext: snapshot);

        var prompt = builder.Build(ctx, Array.Empty<IAgentSkill>(), userDisplayName: null, userRoles: Array.Empty<string>());

        Assert.Contains("Editing draft workflow 'Order Approval'", prompt);
        Assert.Contains("schemaVersion=1", prompt);
        Assert.Contains("inspect_page", prompt);
        Assert.Contains("query_page", prompt);
    }
}
