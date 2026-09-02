using System.Security.Claims;
using System.Text.Json;
using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.Evaluator;
using AutoNate.Web.Authorization.Selectors;
using AutoNate.Web.Models;
using AutoNate.Web.Services.Agent.Skills;
using AutoNate.Web.Services.SystemIssues;
using AutoNate.Web.Services.Workflow;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AutoNate.Web.Tests.Security;

// archived-19 / archived-20: two agent skills read gated entities through stores that do not
// gate by actor, so a user the REST API answers with 403 could read the same
// data by asking the chatbot — full BPMN for every workflow model, and system
// issues whose FactsJson carries verbatim production exception text.
//
// The stores here throw if touched: the point is that a denied caller is
// refused *before* the read, not that the data is filtered afterwards.
public sealed class AgentSkillAuthorizationTests
{
    // ---- archived-19 explain-workflow -------------------------------------------

    [Fact]
    public async Task explain_workflow_denies_without_a_workflowmodel_view_grant()
    {
        var result = await InvokeAsync(
            new ExplainWorkflowSkill(), "explain_workflow",
            $$"""{"workflowId":"{{WorkflowId}}"}""",
            Deny, new ThrowingWorkflowStore());

        AssertRefused(result, "explain_workflow");
        Assert.DoesNotContain("bpmn", Raw(result), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task find_workflow_denies_without_a_workflowmodel_view_grant()
    {
        var result = await InvokeAsync(
            new ExplainWorkflowSkill(), "find_workflow", "{}",
            Deny, new ThrowingWorkflowStore());

        AssertRefused(result, "find_workflow");
    }

    [Fact]
    public async Task explain_workflow_returns_the_bpmn_when_the_grant_is_present()
    {
        var result = await InvokeAsync(
            new ExplainWorkflowSkill(), "explain_workflow",
            $$"""{"workflowId":"{{WorkflowId}}"}""",
            Allow, new StubWorkflowStore());

        Assert.Equal("workflow_model", Kind(result));
        var bpmn = result.GetProperty("data").GetProperty("bpmnXml").GetString();
        Assert.Contains("<bpmn:definitions", bpmn, StringComparison.Ordinal);
    }

    [Fact]
    public async Task find_workflow_lists_when_the_grant_is_present()
    {
        var result = await InvokeAsync(
            new ExplainWorkflowSkill(), "find_workflow", "{}",
            Allow, new StubWorkflowStore());

        Assert.Equal("workflow_search_results", Kind(result));
        Assert.Contains("order-intake", Raw(result), StringComparison.Ordinal);
    }

    // A denial and a genuine miss must read identically, so the tool cannot be
    // used to enumerate which workflow ids exist.
    [Fact]
    public async Task explain_workflow_denial_is_indistinguishable_from_a_miss()
    {
        var denied = await InvokeAsync(
            new ExplainWorkflowSkill(), "explain_workflow",
            $$"""{"workflowId":"{{WorkflowId}}"}""",
            Deny, new ThrowingWorkflowStore());

        var missing = await InvokeAsync(
            new ExplainWorkflowSkill(), "explain_workflow",
            $$"""{"workflowId":"{{WorkflowId}}"}""",
            Allow, new EmptyWorkflowStore());

        Assert.Equal(Message(missing), Message(denied));
    }

    // ---- archived-20 analyze-system-issue ---------------------------------------

    [Fact]
    public async Task list_system_issues_denies_without_a_systemissue_view_grant()
    {
        var result = await InvokeAsync(
            new AnalyzeSystemIssueSkill(), "list_system_issues", "{}",
            Deny, new ThrowingSystemIssueStore());

        AssertRefused(result, "list_system_issues");
    }

    [Fact]
    public async Task get_system_issue_denies_without_a_systemissue_view_grant()
    {
        var result = await InvokeAsync(
            new AnalyzeSystemIssueSkill(), "get_system_issue",
            $$"""{"issueId":"{{IssueId}}"}""",
            Deny, new ThrowingSystemIssueStore());

        AssertRefused(result, "get_system_issue");
        // The exception text the recorder stashes in FactsJson must not leak.
        Assert.DoesNotContain("NullReferenceException", Raw(result), StringComparison.Ordinal);
    }

    [Fact]
    public async Task system_issue_tools_return_data_when_the_grant_is_present()
    {
        var list = await InvokeAsync(
            new AnalyzeSystemIssueSkill(), "list_system_issues", "{}",
            Allow, new StubSystemIssueStore());
        Assert.Equal("system_issues", Kind(list));

        var single = await InvokeAsync(
            new AnalyzeSystemIssueSkill(), "get_system_issue",
            $$"""{"issueId":"{{IssueId}}"}""",
            Allow, new StubSystemIssueStore());
        Assert.Equal("system_issue", Kind(single));
        Assert.Contains("NullReferenceException", Raw(single), StringComparison.Ordinal);
    }

    // ---- the permanent gate ---------------------------------------------

    // Every skill in the assembly must be classified here. A new skill fails
    // this test until its author states how it is gated — which is the whole
    // point: archived-19 and archived-20 were both "nobody noticed this one reads gated data".
    private static readonly Dictionary<string, SkillGate> ExpectedGates = new(StringComparer.Ordinal)
    {
        // Consult IAuthorizer directly.
        ["AnalyzeSystemIssueSkill"] = SkillGate.Authorizer,
        ["DesignSurfacesLookupSkill"] = SkillGate.Authorizer,
        ["ExplainWorkflowSkill"] = SkillGate.Authorizer,
        ["ExternalConnectionsSkill"] = SkillGate.Authorizer,
        ["LookupDataStoresSkill"] = SkillGate.Authorizer,
        ["LookupDatasetsSkill"] = SkillGate.Authorizer,
        ["LookupDirectorySkill"] = SkillGate.Authorizer,
        ["LookupNotesSkill"] = SkillGate.Authorizer,
        ["LookupPermissionsSkill"] = SkillGate.Authorizer,
        ["LookupWorkflowExecutionsSkill"] = SkillGate.Authorizer,
        ["ManageDataStoresSkill"] = SkillGate.Authorizer,
        ["ManageDatasetsSkill"] = SkillGate.Authorizer,
        ["ManageNotesSkill"] = SkillGate.Authorizer,
        ["ManagePermissionsSkill"] = SkillGate.Authorizer,
        ["ManageRecordEdgesSkill"] = SkillGate.Authorizer,
        ["ManageRecordTypesSkill"] = SkillGate.Authorizer,
        ["OperateWorkflowExecutionsSkill"] = SkillGate.Authorizer,
        ["PluginContributedSkill"] = SkillGate.Authorizer,
        ["PluginsAdminSkill"] = SkillGate.Authorizer,
        ["ProjectionsSkill"] = SkillGate.Authorizer,
        ["SendNotificationsSkill"] = SkillGate.Authorizer,
        ["SiteSettingsSkill"] = SkillGate.Authorizer,

        // Read through a store that applies IAuthorizer itself — EfCoreRecordStore
        // takes IAuthorizer and folds BuildRecordSqlFilterAsync / FilterQueryAsync
        // into every query, which is the contract's "route reads through stores
        // that already gate" case.
        ["LookupRecordsSkill"] = SkillGate.GatedStore,
        ["ManageRecordsSkill"] = SkillGate.GatedStore,

        // Only touch stores that scope every read to the session's user id
        // (ListForActorAsync / GetForActorAsync / *ForUserAsync), which is the
        // exemption IAgentSkill's contract describes.
        ["LookupDashboardsSkill"] = SkillGate.ActorScopedStore,
        ["ManageDashboardsSkill"] = SkillGate.ActorScopedStore,
        ["LookupNotificationsSkill"] = SkillGate.ActorScopedStore,
        ["ManageSavedQueriesSkill"] = SkillGate.ActorScopedStore,

        // Touch no gated entity at all.
        ["AqlAssistSkill"] = SkillGate.NoGatedData,       // AQL syntax help
        ["LookupAqlSkill"] = SkillGate.NoGatedData,       // AQL schema/grammar reference
        ["InspectPageSkill"] = SkillGate.NoGatedData,     // the caller's own page snapshot
        ["WebFetchSkill"] = SkillGate.NoGatedData,        // external HTTP, SSRF-guarded
        ["WebSearchSkill"] = SkillGate.NoGatedData,       // external search provider
    };

    private enum SkillGate { Authorizer, GatedStore, ActorScopedStore, NoGatedData }

    [Fact]
    public void every_agent_skill_is_classified_by_how_it_is_gated()
    {
        var actual = SkillTypes().Select(t => t.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        var expected = ExpectedGates.Keys.OrderBy(n => n, StringComparer.Ordinal).ToArray();

        Assert.Equal(expected, actual);
    }

    // Reflection here (not the source scan) so a skill added anywhere in the
    // assembly is caught, including one that never lands in the Skills folder.
    private static IEnumerable<Type> SkillTypes() =>
        typeof(IAgentSkill).Assembly
            .GetTypes()
            .Where(t => typeof(IAgentSkill).IsAssignableFrom(t) && t is { IsAbstract: false, IsInterface: false });

    // The regression guard proper: a skill classified as authorizer-gated must
    // still consult an authorizer. Deleting the guard from ExplainWorkflowSkill
    // or AnalyzeSystemIssueSkill — the exact shape of archived-19 and archived-20 — fails here.
    //
    // This reads the source rather than reflecting, deliberately: an IL scan
    // silently under-reports (it missed the notes skills, which authorize
    // through IContentAuthorizer), and a guard test that can fail open is
    // worse than none.
    [Fact]
    public void skills_classified_as_authorizer_gated_still_consult_an_authorizer()
    {
        var skillsDir = SkillSourceDirectory();

        var missing = ExpectedGates
            .Where(kv => kv.Value == SkillGate.Authorizer)
            .Where(kv => !ConsultsAnAuthorizer(skillsDir, kv.Key))
            .Select(kv => kv.Key)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            missing.Length == 0,
            "These skills are classified as authorizer-gated but no longer call an authorizer: "
                + string.Join(", ", missing));
    }

    // Every skill classified as needing no authorizer must keep earning that:
    // if one starts calling an authorizer, its classification is now wrong and
    // somebody should say which category it belongs to.
    [Fact]
    public void skills_classified_as_not_needing_an_authorizer_still_do_not_call_one()
    {
        var skillsDir = SkillSourceDirectory();

        var unexpected = ExpectedGates
            .Where(kv => kv.Value is SkillGate.ActorScopedStore or SkillGate.NoGatedData)
            .Where(kv => ConsultsAnAuthorizer(skillsDir, kv.Key))
            .Select(kv => kv.Key)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            unexpected.Length == 0,
            "These skills now call an authorizer but are not classified as authorizer-gated: "
                + string.Join(", ", unexpected));
    }

    private static readonly string[] AuthorizerCalls =
    [
        "AuthorizeAsync",
        "GetRequiredService<IAuthorizer>",
        "GetRequiredService<IContentAuthorizer>",
        "IsAuthorizedAsync",
        "FilterQueryAsync",
        "ExistsAndAuthorizedAsync",
        "BuildRecordSqlFilterAsync",
    ];

    private static bool ConsultsAnAuthorizer(string skillsDirectory, string skillTypeName)
    {
        var path = Path.Combine(skillsDirectory, skillTypeName + ".cs");
        Assert.True(File.Exists(path), $"Could not find source for {skillTypeName} at {path}.");

        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.TrimStart();
            if (line.StartsWith("//", StringComparison.Ordinal)) continue;   // a comment is not a guard
            if (AuthorizerCalls.Any(call => line.Contains(call, StringComparison.Ordinal))) return true;
        }
        return false;
    }

    // Walk up from the test binaries to the repo, so this works from `dotnet
    // test` and from an IDE runner alike. Failing loudly beats skipping: a gate
    // test that quietly does nothing is the thing being guarded against.
    private static string SkillSourceDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(
                dir.FullName, "src", "AutoNate.Web", "Services", "Agent", "Skills");
            if (Directory.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException(
            "Could not locate src/AutoNate.Web/Services/Agent/Skills from " + AppContext.BaseDirectory);
    }

    // ---- harness ---------------------------------------------------------

    private static readonly Guid WorkflowId = Guid.Parse("aaaaaaaa-1111-2222-3333-444444444444");
    private static readonly Guid IssueId = Guid.Parse("bbbbbbbb-1111-2222-3333-444444444444");

    private static IAuthorizer Deny => new FixedAuthorizer(false);
    private static IAuthorizer Allow => new FixedAuthorizer(true);

    private static async Task<JsonElement> InvokeAsync<TStore>(
        IAgentSkill skill, string toolName, string argsJson, IAuthorizer authorizer, TStore store)
        where TStore : class
    {
        var services = new ServiceCollection();
        services.AddSingleton(authorizer);
        RegisterStore(services, store);
        var sp = services.BuildServiceProvider();

        var tool = skill.Tools.Single(t => t.Name == toolName);
        using var doc = JsonDocument.Parse(argsJson);
        return await tool.Invoke(
            doc.RootElement.Clone(),
            new AgentToolContext(new AgentSessionContext(new ClaimsPrincipal(), Guid.NewGuid(), "test"), sp),
            CancellationToken.None);
    }

    private static void RegisterStore<TStore>(IServiceCollection services, TStore store) where TStore : class
    {
        if (store is IWorkflowModelStore w) services.AddSingleton(w);
        if (store is ISystemIssueStore s) services.AddSingleton(s);
    }

    private static string Raw(JsonElement e) => e.GetRawText();

    private static string Kind(JsonElement e) => e.GetProperty("kind").GetString()!;

    private static string Message(JsonElement e) =>
        e.GetProperty("data").GetProperty("message").GetString()!;

    private static void AssertRefused(JsonElement result, string source)
    {
        Assert.Equal("error", Kind(result));
        Assert.Equal(source, result.GetProperty("source").GetString());
    }

    private sealed class FixedAuthorizer : IAuthorizer
    {
        private readonly bool _allow;
        public FixedAuthorizer(bool allow) => _allow = allow;

        public Task<AuthDecision> AuthorizeAsync(
            ClaimsPrincipal actor, string action, EntityRef target,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_allow ? AuthDecision.Allow("test") : AuthDecision.Deny("test"));

        public Task<IQueryable<T>> FilterQueryAsync<T>(
            AutoNate.Web.Persistence.AutoNateDbContext db, ClaimsPrincipal actor,
            string kind, string action, IQueryable<T> source,
            CancellationToken cancellationToken = default) where T : class =>
            Task.FromResult(_allow ? source : source.Where(_ => false));

        public Task<CapabilitySummary> GetCapabilitiesAsync(
            ClaimsPrincipal actor, CancellationToken cancellationToken = default) =>
            Task.FromResult(new CapabilitySummary
            {
                UserId = Guid.Empty,
                IsSuperAdmin = false,
                Capabilities = new Dictionary<string, IReadOnlyDictionary<string, bool>>()
            });

        public Task<bool> IsAuthorizedAsync(
            ClaimsPrincipal actor, string kind, string action,
            Func<SelectorAst, bool> selectorMatcher,
            CancellationToken cancellationToken = default) => Task.FromResult(_allow);

        public Task<RecordSqlFilter> BuildRecordSqlFilterAsync(
            ClaimsPrincipal actor, string action, int parameterOffset,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_allow ? RecordSqlFilter.Open : RecordSqlFilter.Closed);

        public Task<AuthExplanation> ExplainAsync(
            Guid asUserId, string action, EntityRef target,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new AuthExplanation
            {
                Effect = _allow ? AuthEffect.Allow : AuthEffect.Deny,
                Reason = "test",
                AsUserId = asUserId,
                IsSuperAdmin = false,
                GroupIds = Array.Empty<Guid>(),
                RoleIds = Array.Empty<Guid>(),
                Grants = Array.Empty<GrantConsideration>()
            });
    }

    private class ThrowingWorkflowStore : IWorkflowModelStore
    {
        private static Exception Boom() =>
            new InvalidOperationException("A denied caller must not reach IWorkflowModelStore.");

        public virtual Task<IReadOnlyList<WorkflowModel>> ListAsync(CancellationToken cancellationToken = default) => throw Boom();
        public virtual Task<WorkflowModel?> GetAsync(Guid id, CancellationToken cancellationToken = default) => throw Boom();
        public Task<WorkflowModel?> GetMostRecentAsync(CancellationToken cancellationToken = default) => throw Boom();
        public Task<WorkflowModel?> GetByProcessKeyAsync(string processKey, CancellationToken cancellationToken = default) => throw Boom();
        public Task<WorkflowModel> SaveAsync(WorkflowModel model, CancellationToken cancellationToken = default) => throw Boom();
        public Task<WorkflowModel> PublishAsync(WorkflowModel model, WorkflowDeploymentInfo deployment, CancellationToken cancellationToken = default) => throw Boom();
        public Task<IReadOnlyList<WorkflowModelVersion>> ListVersionsAsync(Guid id, CancellationToken cancellationToken = default) => throw Boom();
        public Task<WorkflowModel?> DeleteAsync(Guid id, CancellationToken cancellationToken = default) => throw Boom();
    }

    private sealed class StubWorkflowStore : ThrowingWorkflowStore
    {
        private static readonly WorkflowModel Model = new()
        {
            Id = WorkflowId,
            Name = "Order intake",
            ProcessKey = "order-intake",
            BpmnXml = "<bpmn:definitions><bpmn:process id=\"order-intake\" /></bpmn:definitions>",
            IsDraft = false,
            PublishedVersionNumber = 3,
        };

        public override Task<IReadOnlyList<WorkflowModel>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<WorkflowModel>>([Model]);

        public override Task<WorkflowModel?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult<WorkflowModel?>(id == WorkflowId ? Model : null);
    }

    private sealed class EmptyWorkflowStore : ThrowingWorkflowStore
    {
        public override Task<WorkflowModel?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult<WorkflowModel?>(null);
    }

    private class ThrowingSystemIssueStore : ISystemIssueStore
    {
        private static Exception Boom() =>
            new InvalidOperationException("A denied caller must not reach ISystemIssueStore.");

        public virtual Task<IReadOnlyList<SystemIssue>> ListAsync(SystemIssueListQuery query, CancellationToken cancellationToken = default) => throw Boom();
        public virtual Task<SystemIssue?> GetAsync(Guid id, CancellationToken cancellationToken = default) => throw Boom();
        public Task<IReadOnlyList<string>> ListOpenFingerprintsForDetectorAsync(string detectorId, CancellationToken cancellationToken = default) => throw Boom();
    }

    private sealed class StubSystemIssueStore : ThrowingSystemIssueStore
    {
        private static SystemIssue Issue() => new(
            Id: IssueId,
            DetectorId: "unhandled-exception",
            Category: "runtime",
            Severity: "high",
            Fingerprint: "fp",
            Title: "Unhandled exception",
            Summary: "boom",
            RelatedEntityKind: null,
            RelatedEntityId: null,
            FactsJson: """{"exception":"System.NullReferenceException: Object reference not set"}""",
            State: "open",
            FirstSeenAtUtc: DateTimeOffset.UnixEpoch,
            LastSeenAtUtc: DateTimeOffset.UnixEpoch,
            OccurrenceCount: 4,
            AcknowledgedAtUtc: null,
            AcknowledgedBy: null,
            ResolvedAtUtc: null,
            ResolutionKind: null,
            ResolutionNotes: null,
            AutoRemediationAttemptCount: 0,
            AutoRemediationLastError: null,
            NextRemediationAfterUtc: null);

        public override Task<IReadOnlyList<SystemIssue>> ListAsync(SystemIssueListQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SystemIssue>>([Issue()]);

        public override Task<SystemIssue?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult<SystemIssue?>(id == IssueId ? Issue() : null);
    }
}
