using System.Security.Claims;
using System.Text.Json;
using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.Evaluator;
using AutoNate.Web.Authorization.Selectors;
using AutoNate.Web.Services.Agent.Skills;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AutoNate.Web.Tests;

// Phase 5 catalog + auth-gate smoke coverage. Like the Phase 1 / Phase 3
// suites, this exercises the parts unique to the chatbot wrapper — catalog
// assembly, missing-arg rejections, and the kind-level authorizer
// shortcuts that short-circuit before any downstream store call.
public sealed class Phase5SkillsTests
{
    private static readonly Guid SessionUserId = Guid.Parse("12121212-1212-1212-1212-121212121212");

    [Fact]
    public void all_phase5_skills_register_without_tool_name_collisions_alongside_phase1_3()
    {
        var skills = new IAgentSkill[]
        {
            // Phase 5a
            new ProjectionsSkill(),
            new PluginsAdminSkill(),
            new ExternalConnectionsSkill(),
            new SiteSettingsSkill(),
            new ManageRecordEdgesSkill(),
            // Phase 5b
            new DesignSurfacesLookupSkill(),
            // Pre-existing — verify no name collisions with the established catalog.
            new LookupRecordsSkill(),
            new ManageRecordsSkill(),
            new LookupAqlSkill(),
            new AqlAssistSkill(),
            new LookupWorkflowExecutionsSkill(),
            new OperateWorkflowExecutionsSkill(),
            new LookupPermissionsSkill(),
            new ManagePermissionsSkill(),
            new LookupDirectorySkill(),
            new LookupNotesSkill(),
            new ManageNotesSkill(),
            new ManageSavedQueriesSkill(),
            new LookupNotificationsSkill(),
            new SendNotificationsSkill()
        };
        var registry = new SkillRegistry(skills);
        Assert.Equal(skills.Length, registry.All.Count);
        Assert.True(registry.ChatTools.Count > 0);
    }

    [Fact]
    public void every_phase5_skill_has_non_empty_metadata()
    {
        var skills = new IAgentSkill[]
        {
            new ProjectionsSkill(),
            new PluginsAdminSkill(),
            new ExternalConnectionsSkill(),
            new SiteSettingsSkill(),
            new ManageRecordEdgesSkill(),
            new DesignSurfacesLookupSkill()
        };
        foreach (var skill in skills)
        {
            Assert.False(string.IsNullOrWhiteSpace(skill.Name));
            Assert.False(string.IsNullOrWhiteSpace(skill.Description));
            Assert.NotEmpty(skill.Tools);
            foreach (var tool in skill.Tools)
            {
                Assert.False(string.IsNullOrWhiteSpace(tool.Name));
                Assert.False(string.IsNullOrWhiteSpace(tool.Description));
            }
        }
    }

    [Fact]
    public async Task Projections_list_denies_when_caller_lacks_SiteConfig_edit()
    {
        var skill = new ProjectionsSkill();
        var tool = skill.Tools.Single(t => t.Name == "list_projections");
        var sp = ServicesWith<IAuthorizer>(new AlwaysDenyAuthorizer());
        var result = await tool.Invoke(Parse("{}"),
            new AgentToolContext(NewSession(), sp), CancellationToken.None);
        Assert.Equal("error", result.GetProperty("kind").GetString());
        Assert.Contains("SiteConfig", result.GetProperty("data").GetProperty("message").GetString()!);
    }

    [Fact]
    public async Task PluginsAdmin_list_denies_when_caller_lacks_Plugin_manage()
    {
        var skill = new PluginsAdminSkill();
        var tool = skill.Tools.Single(t => t.Name == "list_plugins");
        var sp = ServicesWith<IAuthorizer>(new AlwaysDenyAuthorizer());
        var result = await tool.Invoke(Parse("{}"),
            new AgentToolContext(NewSession(), sp), CancellationToken.None);
        Assert.Equal("error", result.GetProperty("kind").GetString());
        Assert.Contains("Plugin", result.GetProperty("data").GetProperty("message").GetString()!);
    }

    [Fact]
    public async Task ExternalConnections_list_denies_when_caller_lacks_view()
    {
        var skill = new ExternalConnectionsSkill();
        var tool = skill.Tools.Single(t => t.Name == "list_external_connections");
        var sp = ServicesWith<IAuthorizer>(new AlwaysDenyAuthorizer());
        var result = await tool.Invoke(Parse("{}"),
            new AgentToolContext(NewSession(), sp), CancellationToken.None);
        Assert.Equal("error", result.GetProperty("kind").GetString());
        Assert.Contains("ExternalConnection", result.GetProperty("data").GetProperty("message").GetString()!);
    }

    [Fact]
    public async Task SiteSettings_set_unknown_key_is_rejected_before_store_call()
    {
        var skill = new SiteSettingsSkill();
        var tool = skill.Tools.Single(t => t.Name == "set_site_setting");
        var sp = ServicesWith<IAuthorizer>(new AlwaysDenyAuthorizer());
        var args = Parse("""{ "key": "totally-bogus-key", "value": true, "confirmed": true }""");
        var result = await tool.Invoke(args, new AgentToolContext(NewSession(), sp), CancellationToken.None);
        Assert.Equal("manage_change_rejected", result.GetProperty("kind").GetString());
        Assert.Contains("Unknown setting", result.GetProperty("data").GetProperty("error").GetString()!);
    }

    [Fact]
    public async Task ManageRecordEdges_create_missing_args_is_rejected()
    {
        var skill = new ManageRecordEdgesSkill();
        var tool = skill.Tools.Single(t => t.Name == "create_record_edge");
        var args = Parse("""{ "fromRecordId": "00000000-0000-0000-0000-000000000001" }""");
        var sp = new ServiceCollection().BuildServiceProvider();
        var result = await tool.Invoke(args, new AgentToolContext(NewSession(), sp), CancellationToken.None);
        Assert.Equal("manage_change_rejected", result.GetProperty("kind").GetString());
    }

    [Fact]
    public async Task DesignSurfaces_get_workflow_model_missing_id_returns_error()
    {
        var skill = new DesignSurfacesLookupSkill();
        var tool = skill.Tools.Single(t => t.Name == "get_workflow_model");
        var sp = new ServiceCollection().BuildServiceProvider();
        var result = await tool.Invoke(Parse("{}"),
            new AgentToolContext(NewSession(), sp), CancellationToken.None);
        Assert.Equal("error", result.GetProperty("kind").GetString());
        Assert.Contains("id is required", result.GetProperty("data").GetProperty("message").GetString()!);
    }

    // ---- helpers ----

    private static AgentSessionContext NewSession() =>
        new(new ClaimsPrincipal(), SessionUserId, "test");

    private static JsonElement Parse(string raw)
    {
        using var doc = JsonDocument.Parse(raw);
        return doc.RootElement.Clone();
    }

    private static IServiceProvider ServicesWith<T>(T instance) where T : class
    {
        var services = new ServiceCollection();
        services.AddSingleton(instance);
        return services.BuildServiceProvider();
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
