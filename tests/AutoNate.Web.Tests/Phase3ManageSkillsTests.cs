using System.Security.Claims;
using System.Text.Json;
using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.Evaluator;
using AutoNate.Web.Authorization.Selectors;
using AutoNate.Web.Services.Agent.Skills;
using AutoNate.Web.Services.Agent.Skills.Internal;
using AutoNate.Web.Services.Notes;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AutoNate.Web.Tests;

// Smoke-level coverage for the Phase 3 manage-skills. Per-store integration
// is intentionally not exercised here — the tools delegate to the same
// stores their REST counterparts use, which have their own tests. The
// surface unique to the chatbot wrapper is: tool catalog, ConfirmGate
// envelope, missing-arg rejections, and the kind-level authorization
// shortcuts that short-circuit before any store call.
public sealed class Phase3ManageSkillsTests
{
    private static readonly Guid SessionUserId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");

    [Fact]
    public void all_phase3_manage_skills_register_without_tool_name_collisions()
    {
        var skills = new IAgentSkill[]
        {
            new ManageNotesSkill(),
            new ManageSavedQueriesSkill(),
            new OperateWorkflowExecutionsSkill(),
            new ManagePermissionsSkill(),
            new SendNotificationsSkill(),
            // Mix in the pre-existing manage-skill + the Phase 1 lookup-skills
            // to ensure we don't collide with anything already in the catalog.
            new ManageRecordsSkill(),
            new LookupRecordsSkill(),
            new LookupAqlSkill(),
            new LookupPermissionsSkill(),
            new LookupNotificationsSkill(),
            new AqlAssistSkill()
        };
        var registry = new SkillRegistry(skills);
        Assert.Equal(skills.Length, registry.All.Count);
        Assert.True(registry.ChatTools.Count > 0);
    }

    [Fact]
    public void every_manage_tool_advertises_confirmed_in_its_schema()
    {
        // Skills MUST give the model a `confirmed: bool` arg so the
        // confirm-gate is discoverable from the schema alone.
        var skills = new IAgentSkill[]
        {
            new ManageNotesSkill(),
            new ManageSavedQueriesSkill(),
            new OperateWorkflowExecutionsSkill(),
            new ManagePermissionsSkill()
            // SendNotificationsSkill includes confirm on the send tool but not
            // on mark-read tools (read-state isn't dangerous enough to gate).
        };
        foreach (var skill in skills)
        {
            foreach (var tool in skill.Tools)
            {
                var schema = tool.JsonSchema;
                Assert.True(schema.TryGetProperty("properties", out var properties),
                    $"{skill.Name}.{tool.Name} has no properties.");
                Assert.True(properties.TryGetProperty("confirmed", out _),
                    $"{skill.Name}.{tool.Name} must expose a `confirmed` arg.");
            }
        }
    }

    [Fact]
    public void confirm_gate_proposal_envelope_advertises_needsConfirmation()
    {
        var envelope = ConfirmGate.Proposal("test_proposal", "do_thing", new { x = 1 });
        var data = envelope.GetProperty("data");
        Assert.False(data.GetProperty("confirmed").GetBoolean());
        Assert.True(data.GetProperty("needsConfirmation").GetBoolean());
        Assert.Equal("do_thing", data.GetProperty("action").GetString());
    }

    [Fact]
    public void confirm_gate_isConfirmed_only_true_when_arg_is_boolean_true()
    {
        Assert.False(ConfirmGate.IsConfirmed(Parse("{}")));
        Assert.False(ConfirmGate.IsConfirmed(Parse("""{ "confirmed": false }""")));
        Assert.False(ConfirmGate.IsConfirmed(Parse("""{ "confirmed": "true" }""")));
        Assert.True(ConfirmGate.IsConfirmed(Parse("""{ "confirmed": true }""")));
    }

    [Fact]
    public async Task ManageNotes_create_page_missing_notebookId_returns_rejected_envelope()
    {
        var skill = new ManageNotesSkill();
        var tool = skill.Tools.Single(t => t.Name == "create_page_from_markdown");
        var args = Parse("""{ "title": "T", "markdown": "x" }""");
        var sp = new ServiceCollection().BuildServiceProvider();

        var result = await tool.Invoke(args,
            new AgentToolContext(NewSession(), sp), CancellationToken.None);

        Assert.Equal("manage_change_rejected", result.GetProperty("kind").GetString());
        var msg = result.GetProperty("data").GetProperty("error").GetString();
        Assert.Contains("notebookId", msg!);
    }

    [Fact]
    public async Task ManagePermissions_grant_without_SiteConfig_edit_is_rejected_before_store_call()
    {
        var skill = new ManagePermissionsSkill();
        var tool = skill.Tools.Single(t => t.Name == "grant_permission");
        var args = Parse("""
            {
              "principalKind": "user",
              "principalId": "00000000-0000-0000-0000-000000000001",
              "action": "view",
              "selectorString": "*",
              "effect": "allow",
              "confirmed": true
            }
            """);
        var sp = new ServiceCollection()
            .AddSingleton<IAuthorizer>(new AlwaysDenyAuthorizer())
            .BuildServiceProvider();

        var result = await tool.Invoke(args,
            new AgentToolContext(NewSession(), sp), CancellationToken.None);

        Assert.Equal("manage_change_rejected", result.GetProperty("kind").GetString());
        Assert.Contains("SiteConfig", result.GetProperty("data").GetProperty("error").GetString()!);
    }

    [Fact]
    public async Task SendNotifications_send_without_SiteConfig_edit_is_rejected()
    {
        var skill = new SendNotificationsSkill();
        var tool = skill.Tools.Single(t => t.Name == "send_notification");
        var args = Parse("""
            {
              "userId": "00000000-0000-0000-0000-000000000002",
              "title": "Hi",
              "body": "Body",
              "confirmed": true
            }
            """);
        var sp = new ServiceCollection()
            .AddSingleton<IAuthorizer>(new AlwaysDenyAuthorizer())
            .BuildServiceProvider();

        var result = await tool.Invoke(args,
            new AgentToolContext(NewSession(), sp), CancellationToken.None);
        Assert.Equal("manage_change_rejected", result.GetProperty("kind").GetString());
        Assert.Contains("SiteConfig", result.GetProperty("data").GetProperty("error").GetString()!);
    }

    [Fact]
    public async Task ManageSavedQueries_save_missing_name_is_rejected()
    {
        var skill = new ManageSavedQueriesSkill();
        var tool = skill.Tools.Single(t => t.Name == "save_query");
        var args = Parse("""{ "queryText": "FROM Records" }""");
        var sp = new ServiceCollection().BuildServiceProvider();

        var result = await tool.Invoke(args,
            new AgentToolContext(NewSession(), sp), CancellationToken.None);
        Assert.Equal("manage_change_rejected", result.GetProperty("kind").GetString());
        Assert.Contains("name", result.GetProperty("data").GetProperty("error").GetString()!);
    }

    [Fact]
    public async Task OperateWorkflowExecutions_cancel_missing_processInstanceId_is_rejected()
    {
        var skill = new OperateWorkflowExecutionsSkill();
        var tool = skill.Tools.Single(t => t.Name == "cancel_execution");
        var args = Parse("{}");
        var sp = new ServiceCollection().BuildServiceProvider();

        var result = await tool.Invoke(args,
            new AgentToolContext(NewSession(), sp), CancellationToken.None);
        Assert.Equal("manage_change_rejected", result.GetProperty("kind").GetString());
    }

    [Fact]
    public void markdown_converter_is_registered_as_a_singleton()
    {
        // Mirror the Program.cs registration to confirm the converter contract
        // is satisfiable from DI in tests too.
        var services = new ServiceCollection();
        services.AddSingleton<IMarkdownToBlockNoteConverter, MarkdownToBlockNoteConverter>();
        var sp = services.BuildServiceProvider();
        var converter = sp.GetRequiredService<IMarkdownToBlockNoteConverter>();
        Assert.NotNull(converter);
        var sameAgain = sp.GetRequiredService<IMarkdownToBlockNoteConverter>();
        Assert.Same(converter, sameAgain);
    }

    // --- helpers / fakes ---

    private static AgentSessionContext NewSession() =>
        new(new ClaimsPrincipal(), SessionUserId, "test");

    private static JsonElement Parse(string raw)
    {
        using var doc = JsonDocument.Parse(raw);
        return doc.RootElement.Clone();
    }

    private sealed class AlwaysDenyAuthorizer : IAuthorizer
    {
        public Task<AuthDecision> AuthorizeAsync(
            ClaimsPrincipal actor, string action, EntityRef target,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(AuthDecision.Deny("test"));

        public Task<IQueryable<T>> FilterQueryAsync<T>(
            AutoNate.Web.Persistence.AutoNateDbContext db, ClaimsPrincipal actor,
            string kind, string action, IQueryable<T> source,
            CancellationToken cancellationToken = default) where T : class =>
            Task.FromResult(source.Where(_ => false));

        public Task<CapabilitySummary> GetCapabilitiesAsync(ClaimsPrincipal actor, CancellationToken cancellationToken = default) =>
            Task.FromResult(new CapabilitySummary
            {
                UserId = Guid.Empty,
                IsSuperAdmin = false,
                Capabilities = new Dictionary<string, IReadOnlyDictionary<string, bool>>()
            });

        public Task<bool> IsAuthorizedAsync(ClaimsPrincipal actor, string kind, string action,
            Func<SelectorAst, bool> selectorMatcher,
            CancellationToken cancellationToken = default) => Task.FromResult(false);

        public Task<RecordSqlFilter> BuildRecordSqlFilterAsync(
            ClaimsPrincipal actor, string action, int parameterOffset,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(RecordSqlFilter.Closed);

        public Task<AuthExplanation> ExplainAsync(
            Guid asUserId, string action, EntityRef target,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new AuthExplanation
            {
                Effect = AuthEffect.Deny,
                Reason = "test",
                AsUserId = asUserId,
                IsSuperAdmin = false,
                GroupIds = Array.Empty<Guid>(),
                RoleIds = Array.Empty<Guid>(),
                Grants = Array.Empty<GrantConsideration>()
            });
    }
}
