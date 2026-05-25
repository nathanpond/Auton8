using System.Security.Claims;
using System.Text.Json;
using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.Evaluator;
using AutoNate.Web.Models.Notifications;
using AutoNate.Web.Services.Agent.Skills;
using AutoNate.Web.Services.Notifications;
using AutoNate.Web.Services.Query.Entities;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AutoNate.Web.Tests;

// Smoke-level tests for the Phase 1 read-coverage skills. Per-skill business
// logic for the heavier stores (IContentAuthorizer, IFlowableClient,
// IPermissionGrantStore, IRoleAssignmentStore) is intentionally not exercised
// here — those skills are thin envelopes over already-tested infrastructure;
// the tests below cover the parts unique to the chatbot wrapper: tool
// catalog assembly, error-envelope shape, the admin-gate behavior, and the
// in-memory grammar surface.
public sealed class Phase1ReadSkillsTests
{
    private static readonly Guid SessionUserId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    [Fact]
    public void All_phase1_skills_register_without_tool_name_collisions()
    {
        var skills = new IAgentSkill[]
        {
            new LookupNotesSkill(),
            new LookupAqlSkill(),
            new LookupWorkflowExecutionsSkill(),
            new LookupPermissionsSkill(),
            new LookupDirectorySkill(),
            new LookupNotificationsSkill()
        };

        // SkillRegistry's constructor throws on duplicate tool names. Adding
        // any pre-existing in-tree skills (LookupRecords/Manage*/Inspect)
        // proves the new tool names don't collide with the live catalog.
        var withCore = skills
            .Concat(new IAgentSkill[]
            {
                new LookupRecordsSkill(),
                new ManageRecordsSkill()
            })
            .ToArray();

        var registry = new SkillRegistry(withCore);

        Assert.Equal(withCore.Length, registry.All.Count);
        Assert.True(registry.ChatTools.Count > 0);
    }

    [Fact]
    public async Task LookupAql_get_aql_grammar_returns_clauses_aggregates_and_entities()
    {
        var skill = new LookupAqlSkill();
        var registry = new FakeQueryRegistry(new[] { "Records", "Flows", "Notes" });
        var sp = ServicesWith<IQueryEntityRegistry>(registry);
        var tool = skill.Tools.Single(t => t.Name == "get_aql_grammar");

        using var doc = JsonDocument.Parse("{}");
        var result = await tool.Invoke(doc.RootElement,
            new AgentToolContext(NewSession(), sp), CancellationToken.None);

        Assert.Equal("aql_grammar", result.GetProperty("kind").GetString());
        var data = result.GetProperty("data");

        var clauseKeywords = data.GetProperty("clauseKeywords").EnumerateArray()
            .Select(e => e.GetString()).ToArray();
        Assert.Contains("FROM", clauseKeywords);
        Assert.Contains("WHERE", clauseKeywords);
        Assert.Contains("ORDER BY", clauseKeywords);

        var aggregates = data.GetProperty("aggregates").EnumerateArray().ToArray();
        Assert.Contains(aggregates, a => a.GetProperty("name").GetString() == "COUNT");

        var entities = data.GetProperty("entityNames").EnumerateArray()
            .Select(e => e.GetString()).ToArray();
        Assert.Contains("Records", entities);
        Assert.Contains("Flows", entities);
    }

    [Fact]
    public async Task LookupNotifications_list_uses_session_userId_and_returns_unread_count()
    {
        var store = new FakeNotificationStore
        {
            Items = new List<Notification>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    UserId = SessionUserId,
                    Kind = "workflow_task",
                    Title = "Task assigned",
                    Body = "You have a new task",
                    IsRead = false,
                    CreatedAtUtc = DateTimeOffset.UtcNow
                },
                new()
                {
                    Id = Guid.NewGuid(),
                    UserId = SessionUserId,
                    Kind = "mention",
                    Title = "You were mentioned",
                    Body = "Look at this page",
                    IsRead = true,
                    CreatedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5),
                    ReadAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1)
                }
            },
            UnreadCount = 1
        };
        var skill = new LookupNotificationsSkill();
        var sp = ServicesWith<INotificationStore>(store);

        using var doc = JsonDocument.Parse("{}");
        var tool = skill.Tools.Single(t => t.Name == "list_notifications");
        var result = await tool.Invoke(doc.RootElement,
            new AgentToolContext(NewSession(), sp), CancellationToken.None);

        Assert.Equal("notifications", result.GetProperty("kind").GetString());
        var data = result.GetProperty("data");
        Assert.Equal(2, data.GetProperty("totalCount").GetInt32());
        Assert.Equal(1, data.GetProperty("unreadCount").GetInt32());
        Assert.Equal(2, data.GetProperty("items").GetArrayLength());

        Assert.Equal(SessionUserId, Assert.Single(store.ListPagedCalls).UserId);
    }

    [Fact]
    public async Task LookupPermissions_list_grants_denies_when_caller_lacks_SiteConfig_view()
    {
        var skill = new LookupPermissionsSkill();
        var sp = ServicesWith<IAuthorizer>(new FakeAuthorizer(allowSiteConfig: false));

        using var doc = JsonDocument.Parse("{}");
        var tool = skill.Tools.Single(t => t.Name == "list_permission_grants");
        var result = await tool.Invoke(doc.RootElement,
            new AgentToolContext(NewSession(), sp), CancellationToken.None);

        Assert.Equal("error", result.GetProperty("kind").GetString());
        Assert.Contains("SiteConfig", result.GetProperty("data").GetProperty("message").GetString()!);
    }

    [Fact]
    public void All_phase1_skill_descriptions_are_non_empty()
    {
        // System prompt cost matters; if a skill ships with an empty name or
        // description the model can't disambiguate it. Cheap to enforce.
        var skills = new IAgentSkill[]
        {
            new LookupNotesSkill(),
            new LookupAqlSkill(),
            new LookupWorkflowExecutionsSkill(),
            new LookupPermissionsSkill(),
            new LookupDirectorySkill(),
            new LookupNotificationsSkill()
        };
        foreach (var skill in skills)
        {
            Assert.False(string.IsNullOrWhiteSpace(skill.Name), $"Skill name empty for {skill.GetType().Name}");
            Assert.False(string.IsNullOrWhiteSpace(skill.Description), $"Description empty for {skill.Name}");
            foreach (var tool in skill.Tools)
            {
                Assert.False(string.IsNullOrWhiteSpace(tool.Name), $"Tool name empty in {skill.Name}");
                Assert.False(string.IsNullOrWhiteSpace(tool.Description), $"Tool description empty: {skill.Name}.{tool.Name}");
            }
        }
    }

    // --- helpers / fakes ---

    private static AgentSessionContext NewSession() =>
        new(new ClaimsPrincipal(), SessionUserId, "test");

    private static IServiceProvider ServicesWith<T>(T instance) where T : class
    {
        var services = new ServiceCollection();
        services.AddSingleton(instance);
        return services.BuildServiceProvider();
    }

    private sealed class FakeAuthorizer : IAuthorizer
    {
        private readonly bool _allowSiteConfig;
        public FakeAuthorizer(bool allowSiteConfig) { _allowSiteConfig = allowSiteConfig; }

        public Task<AuthDecision> AuthorizeAsync(
            ClaimsPrincipal actor, string action, EntityRef target,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(target.Kind == EntityKinds.SiteConfig && _allowSiteConfig
                ? AuthDecision.Allow("test")
                : AuthDecision.Deny("test"));

        public Task<IQueryable<T>> FilterQueryAsync<T>(
            AutoNate.Web.Persistence.AutoNateDbContext db, ClaimsPrincipal actor,
            string kind, string action, IQueryable<T> source,
            CancellationToken cancellationToken = default) where T : class =>
            Task.FromResult(source);

        public Task<CapabilitySummary> GetCapabilitiesAsync(ClaimsPrincipal actor, CancellationToken cancellationToken = default) =>
            Task.FromResult(new CapabilitySummary
            {
                UserId = Guid.Empty,
                IsSuperAdmin = false,
                Capabilities = new Dictionary<string, IReadOnlyDictionary<string, bool>>()
            });

        public Task<bool> IsAuthorizedAsync(ClaimsPrincipal actor, string kind, string action,
            Func<AutoNate.Web.Authorization.Selectors.SelectorAst, bool> selectorMatcher,
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

    private sealed class FakeNotificationStore : INotificationStore
    {
        public List<Notification> Items { get; set; } = new();
        public int UnreadCount { get; set; }
        public List<(Guid UserId, ListNotificationsRequest Request)> ListPagedCalls { get; } = new();

        public Task<Notification> CreateAsync(CreateNotificationInput input, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<Notification>> ListForUserAsync(Guid userId, int? limit, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Notification>>(Items);

        public Task<NotificationPage> ListPagedForUserAsync(Guid userId, ListNotificationsRequest request, CancellationToken cancellationToken = default)
        {
            ListPagedCalls.Add((userId, request));
            return Task.FromResult(new NotificationPage(Items, Items.Count, UnreadCount));
        }

        public Task<int> GetUnreadCountAsync(Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(UnreadCount);

        public Task<Notification?> MarkReadAsync(Guid notificationId, Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult<Notification?>(null);

        public Task<int> MarkAllReadAsync(Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(0);

        public Task<IReadOnlyList<Notification>> DeleteByRelatedEntityAsync(Guid? userId, string relatedEntityKind, string relatedEntityId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Notification>>(Array.Empty<Notification>());

        public Task<IReadOnlyList<Notification>> DeleteByParentEntityAsync(string parentEntityKind, string parentEntityId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Notification>>(Array.Empty<Notification>());
    }

    private sealed class FakeQueryRegistry : IQueryEntityRegistry
    {
        public FakeQueryRegistry(IReadOnlyList<string> entityNames) { EntityNames = entityNames; }
        public IReadOnlyList<string> EntityNames { get; }
        public bool TryGet(string name, out IQueryEntity entity)
        {
            entity = null!;
            return false;
        }
    }
}
