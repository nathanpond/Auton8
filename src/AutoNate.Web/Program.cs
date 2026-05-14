using AutoNate.Plugins.Abstractions;
using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.Edges;
using AutoNate.Web.Authorization.EntityTypes;
using AutoNate.Web.Authorization.Evaluator;
using AutoNate.Web.Authorization.Selectors;
using AutoNate.Web.Configuration;
using AutoNate.Web.Endpoints;
using AutoNate.Web.Hooks;
using AutoNate.Web.Models;
using AutoNate.Web.Persistence;
using AutoNate.Web.Plugins;
using AutoNate.Web.Services.ApplicationEvents;
using AutoNate.Web.Services.Audit;
using AutoNate.Web.Services.Auth;
using AutoNate.Web.Services.Authorization;
using AutoNate.Web.Services.BusWatcher;
using AutoNate.Web.Services.Dapr;
using AutoNate.Web.Services.Agent;
using AutoNate.Web.Services.Agent.Conversations;
using AutoNate.Web.Services.Agent.Loop;
using AutoNate.Web.Services.Agent.PageQuery;
using AutoNate.Web.Services.Agent.Providers;
using AutoNate.Web.Services.Agent.Search;
using AutoNate.Web.Services.Agent.Skills;
using AutoNate.Web.Services.Events;
using AutoNate.Web.Services.ExternalConnections;
using AutoNate.Web.Services.Flowable;
using AutoNate.Web.Services.Forms;
using AutoNate.Web.Services.Menus;
using AutoNate.Web.Services.Nats;
using AutoNate.Web.Services.Notifications;
using AutoNate.Web.Services.Records;
using AutoNate.Web.Services.Records.Fields;
using AutoNate.Web.Services.Signals;
using AutoNate.Web.Services.SiteSettings;
using AutoNate.Web.Services.SystemIssues;
using AutoNate.Web.Services.SystemIssues.Detectors;
using AutoNate.Web.Services.SystemIssues.Remediators;
using AutoNate.Web.Services.Workflow;
using AutoNate.Web.Services.Workflow.Behaviors;
using AutoNate.Web.Storage;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.FileProviders;
using Dapr.Messaging.PublishSubscribe.Extensions;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

const string DevelopmentAutoLoginClaimType = "dev_auto_login";
const string DevelopmentAutoLoginUsernameClaimType = "dev_auto_login_username";
const string AuthenticationSourceClaimType = "auth_source";
const string ManualAuthenticationSource = "manual";
const string DevelopmentAutoLoginAuthenticationSource = "development_auto_login";

#if DEBUG
// Rider's plain ".NET Project" launcher runs the built executable directly, which skips
// launchSettings.json. Mirror the local development defaults unless the user already
// supplied explicit values.
if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")))
{
    Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", Environments.Development);
}

if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")))
{
    Environment.SetEnvironmentVariable("ASPNETCORE_URLS", "http://localhost:5108");
}
#endif

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseStaticWebAssets();

if (builder.Environment.IsDevelopment())
{
    builder.Configuration["ReloadStaticAssetsAtRuntime"] = bool.FalseString;
}

// Add services to the container.
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/";
        options.AccessDeniedPath = "/";
        options.SlidingExpiration = true;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);

        // CSRF posture: Strict so a cross-site link/form/POST can't replay the
        // auth cookie. Same-origin navigations (login redirect, SPA links,
        // address-bar entry, bookmark clicks) still carry it. The trade-off
        // is that a link from another site (Slack, email, internal docs)
        // lands the user unauthenticated and bounces them through login —
        // acceptable for an internal workflow tool, and login CSRF gets a
        // belt-and-braces fix via the antiforgery middleware on /account/login.
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.HttpOnly = true;
        // SameAsRequest in Development so http://localhost:5108 keeps the
        // dev cookie flow working; Always in every other environment so the
        // production deployment (which the README requires to sit behind
        // TLS) refuses to leak the cookie over an accidental HTTP fallback.
        options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;

        // Without these overrides the cookie middleware 302-redirects every
        // unauthenticated request — including AJAX calls — to LoginPath. The
        // SPA's axios then follows the redirect and parses index.html as the
        // response body. For /api routes we want plain 401/403 status codes
        // so the SPA's response interceptor can do the right thing.
        options.Events.OnRedirectToLogin = context =>
        {
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            }
            context.Response.Redirect(context.RedirectUri);
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            }
            context.Response.Redirect(context.RedirectUri);
            return Task.CompletedTask;
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddAntiforgery(options =>
{
    // Match the auth cookie's posture (see AddCookie above). The antiforgery
    // cookie is purely same-origin (issued by GET /api/antiforgery/token,
    // consumed by the matching POST), so Strict is correct and doesn't
    // break any cross-context flow. Secure follows the same dev/prod split.
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;
});
builder.Services.AddHttpContextAccessor();
// IRequestContext is a thin facade over IHttpContextAccessor (singleton) — no
// per-request state of its own, so it's safe as a singleton and avoids
// lifetime-mismatch errors with the singleton event publishers.
builder.Services.AddSingleton<IRequestContext, RequestContext>();
builder.Services.AddSingleton<IAuditEventPublisher, DaprAuditEventPublisher>();
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<ViewEventCoalescer>();
builder.Services.AddOptions<AuditOutboxOptions>()
    .BindConfiguration(AuditOutboxOptions.SectionName);
// Phase 5 of the audit-events plan: live publishers go through the outbox.
// EfCoreAuditEventOutbox writes a row, AuditOutboxDispatcher polls + posts to
// Dapr. When AuditOutbox:Enabled = false, the publishers fall back to direct
// posting via DirectPublishAuditEventOutbox (the pre-Phase-5 behavior).
builder.Services.AddSingleton<EfCoreAuditEventOutbox>();
builder.Services.AddSingleton<DirectPublishAuditEventOutbox>();
builder.Services.AddSingleton<IAuditEventOutbox>(sp =>
{
    var enabled = sp.GetRequiredService<IOptions<AuditOutboxOptions>>().Value.Enabled;
    return enabled
        ? sp.GetRequiredService<EfCoreAuditEventOutbox>()
        : sp.GetRequiredService<DirectPublishAuditEventOutbox>();
});
builder.Services.AddHostedService<AuditOutboxDispatcher>();
builder.Services.AddOptions<DevelopmentAutoLoginOptions>()
    .BindConfiguration(DevelopmentAutoLoginOptions.SectionName);
builder.Services.AddOptions<FlowableOptions>()
    .BindConfiguration(FlowableOptions.SectionName);
builder.Services.AddOptions<DaprOptions>()
    .BindConfiguration(DaprOptions.SectionName);
builder.Services.AddOptions<NatsOptions>()
    .BindConfiguration(NatsOptions.SectionName);
builder.Services.AddSingleton<NatsStreamProvisioner>();
builder.Services.AddSingleton<BusWatcherStreamService>();
builder.Services.AddSingleton<DaprSidecarProbe>();
builder.Services.AddSingleton<AutoNate.Web.Services.SystemHealth.SystemHealthService>();
builder.Services.AddSingleton<AutoNate.Web.Services.SystemHealth.ISystemHealthProbe>(
    sp => sp.GetRequiredService<AutoNate.Web.Services.SystemHealth.SystemHealthService>());
builder.Services.AddSingleton<IWorkflowSignalRegistry, EfCoreWorkflowSignalRegistry>();
builder.Services.AddSingleton<RecordTypeShortCodeCache>();
builder.Services.AddSingleton<IRecordTypeShortCodeResolver>(
    sp => sp.GetRequiredService<RecordTypeShortCodeCache>());
builder.Services.AddHostedService<RecordTypeShortCodeCacheInitializer>();
builder.Services.AddSingleton<WorkflowSignalDispatcher>();
builder.Services.AddDaprPubSubClient((sp, b) =>
{
    var options = sp.GetRequiredService<IOptions<DaprOptions>>().Value;
    if (!string.IsNullOrWhiteSpace(options.GrpcEndpoint))
    {
        b.UseGrpcEndpoint(options.GrpcEndpoint);
    }
    if (!string.IsNullOrWhiteSpace(options.HttpEndpoint))
    {
        b.UseHttpEndpoint(options.HttpEndpoint);
    }
});
builder.Services.AddSingleton<DaprStreamingSubscriber>();
builder.Services.AddSingleton<IDaprStreamingSubscriber>(sp => sp.GetRequiredService<DaprStreamingSubscriber>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<DaprStreamingSubscriber>());
builder.Services.AddHostedService<AutoNate.Web.Services.Workflow.WorkflowExecutionErrorRecorder>();
builder.Services.AddSingleton<AutoNate.Web.Services.Workflow.WorkflowTaskCompletionRecorder>();
builder.Services.AddSingleton<AutoNate.Web.Persistence.DbConnectionFailureLoggingInterceptor>();
builder.Services.AddDbContextFactory<AutoNateDbContext>((sp, options) =>
{
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("Default")
        ?? throw new InvalidOperationException("Connection string 'Default' is required."));
    options.AddInterceptors(
        sp.GetRequiredService<AutoNate.Web.Persistence.DbConnectionFailureLoggingInterceptor>());
    // EF Core's RelationalEventId.ConnectionError fires for any exception
    // during connection-open, including TaskCanceledException when a SPA
    // request is aborted mid-handshake (tab close / navigation / refetch
    // bursts). Those produce the bulk of the `fail: 20004` noise but aren't
    // real faults. Silence the diagnostic event entirely; our interceptor
    // logs real (non-cancelled) failures at Warning with full detail.
    options.ConfigureWarnings(w =>
        w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.ConnectionError));
});
builder.Services.AddOptions<AutoNate.Web.Authorization.AuthorizationOptions>()
    .BindConfiguration(AutoNate.Web.Authorization.AuthorizationOptions.SectionName);
foreach (var entityType in CoreEntityTypes.All)
{
    builder.Services.AddSingleton<IEntityType>(entityType);
}
builder.Services.AddSingleton<IEntityRegistry, EntityRegistry>();
builder.Services.AddSingleton<IEntityEdgeWriter, EntityEdgeWriter>();

builder.Services.AddSingleton<ISelectorCompiler, RecordSelectorCompiler>();
builder.Services.AddSingleton<ISelectorCompiler, RoleSelectorCompiler>();
builder.Services.AddSingleton<ISelectorCompiler, GroupSelectorCompiler>();
builder.Services.AddSingleton<ISelectorCompiler, RecordTypeSelectorCompiler>();
builder.Services.AddSingleton<ISelectorCompiler, WorkflowModelSelectorCompiler>();
builder.Services.AddSingleton<ISelectorCompiler, FormSelectorCompiler>();
// User and ExternalConnection don't expose tag predicates today; a path-only
// compiler keeps `/<kind>/<id>` and `/<kind>/*` grants working without
// silently denying instance gates per Authorizer.FilterQueryAsync.
builder.Services.AddSingleton<ISelectorCompiler>(_ =>
    new PathOnlySelectorCompiler<AutoNate.Web.Persistence.Scaffolded.LocalUser>(
        EntityKinds.User, u => u.UserId));
builder.Services.AddSingleton<ISelectorCompiler>(_ =>
    new PathOnlySelectorCompiler<AutoNate.Web.Persistence.Scaffolded.ExternalConnection>(
        EntityKinds.ExternalConnection, c => c.Id));
builder.Services.AddSingleton<ISelectorCompilerRegistry, SelectorCompilerRegistry>();

builder.Services.AddScoped<IInstanceAuthorizer, RecordInstanceAuthorizer>();
builder.Services.AddScoped<IInstanceAuthorizer, RoleInstanceAuthorizer>();
builder.Services.AddScoped<IInstanceAuthorizer, GroupInstanceAuthorizer>();
builder.Services.AddScoped<IInstanceAuthorizer, RecordTypeInstanceAuthorizer>();
builder.Services.AddScoped<IInstanceAuthorizer, WorkflowModelInstanceAuthorizer>();
builder.Services.AddScoped<IInstanceAuthorizer, WorkflowTaskInstanceAuthorizer>();
builder.Services.AddScoped<IInstanceAuthorizer, WorkflowExecutionInstanceAuthorizer>();
builder.Services.AddScoped<IInstanceAuthorizer, FormInstanceAuthorizer>();
builder.Services.AddScoped<IInstanceAuthorizer, UserInstanceAuthorizer>();
builder.Services.AddScoped<IInstanceAuthorizer, ExternalConnectionInstanceAuthorizer>();

builder.Services.AddScoped<IAuthorizer, Authorizer>();
// Content hierarchy — separate authorization path (project-role baseline +
// closest-ancestor overrides + deletions_locked enforcement). RequirePermission
// filter dispatches to this for project/cabinet/notebook/page kinds.
builder.Services.AddScoped<AutoNate.Web.Services.Content.IContentAuthorizer,
    AutoNate.Web.Services.Content.ContentAuthorizer>();
builder.Services.AddScoped<AutoNate.Web.Services.Content.IContentTreeService,
    AutoNate.Web.Services.Content.ContentTreeService>();
builder.Services.AddScoped<AutoNate.Web.Services.Content.IProjectMembershipService,
    AutoNate.Web.Services.Content.ProjectMembershipService>();
builder.Services.AddScoped<AutoNate.Web.Services.Content.IContentVersionService,
    AutoNate.Web.Services.Content.ContentVersionService>();
builder.Services.AddOptions<AutoNate.Web.Services.Content.ContentAttachmentOptions>()
    .BindConfiguration("ContentAttachments");
builder.Services.AddSingleton<AutoNate.Web.Services.Content.IContentAttachmentStore,
    AutoNate.Web.Services.Content.FilesystemContentAttachmentStore>();
builder.Services.AddScoped<AuthCacheBumper>();
builder.Services.AddScoped<EntityEdgeReconciler>();
builder.Services.AddScoped<IRoleStore, EfCoreRoleStore>();
builder.Services.AddScoped<IGroupStore, EfCoreGroupStore>();
builder.Services.AddScoped<IRoleAssignmentStore, EfCoreRoleAssignmentStore>();
builder.Services.AddScoped<IPermissionGrantStore, EfCorePermissionGrantStore>();
builder.Services.AddSingleton<AutoNate.Web.Services.Menus.PageRegistrySnapshotCache>();
builder.Services.AddScoped<IMenuStore, EfCoreMenuStore>();
builder.Services.AddScoped<IPageTemplateStore, EfCorePageTemplateStore>();
builder.Services.AddScoped<ILocalUserStore, EfCoreLocalUserStore>();
builder.Services.AddScoped<IWorkflowModelStore, EfCoreWorkflowModelStore>();
builder.Services.AddScoped<IFormStore, EfCoreFormStore>();

// External Connections — admin-managed config for outbound integrations
// (LLM providers today, future SMTP/S3/IdP). DataProtection encrypts the
// stored secret; the keyring lives under the host's content root by default
// so a single-host deploy works without ceremony. Tests register a stub
// IDataProtectionProvider through the standard test factory.
builder.Services.AddDataProtection();
builder.Services.AddSingleton<IConnectionSecretProtector, DataProtectionConnectionSecretProtector>();
builder.Services.AddScoped<IExternalConnectionStore, EfCoreExternalConnectionStore>();
// Phase 4 replaces this with kind-routed Anthropic/OpenAI testers; until then
// the stub just confirms the secret decrypts cleanly.
builder.Services.AddScoped<ITestConnectionService, StubTestConnectionService>();
builder.Services.AddScoped<IConnectionModelLister, ConnectionModelLister>();

// Agent provider abstraction. Per-provider HttpClients have generous timeouts
// because token streaming for a tool-using turn can run minutes.
builder.Services.AddHttpClient("agent.anthropic", c => c.Timeout = TimeSpan.FromMinutes(5));
builder.Services.AddHttpClient("agent.openai", c => c.Timeout = TimeSpan.FromMinutes(5));
builder.Services.AddScoped<IChatProviderResolver, ChatProviderResolver>();

// Read-only diagnostic skills. Skills are scoped because their tools resolve
// further scoped services (record stores, workflow stores) at invocation
// time. The registry is also scoped so it can construct a per-request snapshot
// of the active skill list.
builder.Services.AddScoped<IAgentSkill, ExplainWorkflowSkill>();
builder.Services.AddScoped<IAgentSkill, LookupRecordsSkill>();
builder.Services.AddScoped<IAgentSkill, AnalyzeSystemIssueSkill>();
// First mutating skill. Tools default to confirmed=false (dry-run); the
// agent narrates the proposal and asks the user; only confirmed=true issues
// the actual IRecordStore mutation under the calling user's principal.
builder.Services.AddScoped<IAgentSkill, ManageRecordsSkill>();
// Mutating record-type schema skill. Same confirmed-gate contract as
// ManageRecordsSkill, plus skill-level IAuthorizer checks (the type store
// is unauthorized — endpoints enforce permissions today) and an IsSystem
// refusal that no other layer guards.
builder.Services.AddScoped<IAgentSkill, ManageRecordTypesSkill>();
// WebFetchSkill is always registered; AgentSession filters its tool out of
// the per-turn ChatRequest when chatbot.internetAccessEnabled is off.
builder.Services.AddScoped<IAgentSkill, WebFetchSkill>();
builder.Services.AddSingleton<IDnsResolver, SystemDnsResolver>();
// Dedicated HttpClient — short timeout, no cookies, capped redirects, fixed
// User-Agent. The fetch tool can never accidentally reuse cookies from any
// other named client (notifications etc.) because UseCookies is off.
builder.Services.AddHttpClient("agent.webfetch", c =>
{
    c.Timeout = TimeSpan.FromSeconds(10);
    c.DefaultRequestHeaders.UserAgent.ParseAdd("AutoNate-Agent/1.0");
}).ConfigurePrimaryHttpMessageHandler(() => new System.Net.Http.SocketsHttpHandler
{
    AllowAutoRedirect = true,
    MaxAutomaticRedirections = 5,
    UseCookies = false
});

// WebSearchSkill — same gating as fetch_url. Provider abstraction lets us
// add Brave / Serper / Google later by appending to the resolver's switch.
builder.Services.AddScoped<IAgentSkill, WebSearchSkill>();
builder.Services.AddScoped<IWebSearchProviderResolver, WebSearchProviderResolver>();
builder.Services.AddHttpClient("agent.websearch", c =>
{
    c.Timeout = TimeSpan.FromSeconds(15);
    c.DefaultRequestHeaders.UserAgent.ParseAdd("AutoNate-Agent/1.0");
}).ConfigurePrimaryHttpMessageHandler(() => new System.Net.Http.SocketsHttpHandler
{
    AllowAutoRedirect = false,
    UseCookies = false
});

// Generic page-awareness skill. Always registered; both tools (inspect_page,
// query_page) self-degrade when no snapshot is present on the conversation.
builder.Services.AddScoped<IAgentSkill, InspectPageSkill>();

builder.Services.AddScoped<ISkillRegistry, SkillRegistry>();

// Page-query bridge: a singleton router that holds in-flight TaskCompletionSource
// awaiters keyed by (conversationId, queryId), and a per-request channel that
// agent sessions activate to emit PageQueryRequested events and await replies.
builder.Services.AddSingleton<IPageQueryRouter, PageQueryRouter>();
builder.Services.AddScoped<PageQueryChannel>();
builder.Services.AddScoped<IPageQueryChannel>(sp => sp.GetRequiredService<PageQueryChannel>());

// Page-action bridge: identical pattern to page-query, but for mutations.
// Skills resolve IPageActionChannel and call ApplyAsync; the singleton router
// resolves the awaiting TCS when the SPA POSTs the action result.
builder.Services.AddSingleton<IPageActionRouter, PageActionRouter>();
builder.Services.AddScoped<PageActionChannel>();
builder.Services.AddScoped<IPageActionChannel>(sp => sp.GetRequiredService<PageActionChannel>());

// Conversation persistence + the orchestrator that runs the tool-using loop.
builder.Services.AddOptions<AgentOptions>().BindConfiguration(AgentOptions.SectionName);
builder.Services.AddScoped<IAgentConversationStore, EfCoreAgentConversationStore>();
builder.Services.AddScoped<SystemPromptBuilder>();
builder.Services.AddScoped<IAgentSession, AgentSession>();
builder.Services.AddSingleton<ConversationCompactor>();

// Catalogue of LLM models AutoNate is aware of. Singleton lookup service
// keeps an in-memory snapshot so the chat hot path doesn't pay a DB round-
// trip per turn; the store is scoped because writes go through a request-
// scoped DbContext factory and call Invalidate() on the singleton.
builder.Services.AddSingleton<AutoNate.Web.Services.Agent.Catalog.IAgentModelCatalog,
    AutoNate.Web.Services.Agent.Catalog.AgentModelCatalog>();
builder.Services.AddSingleton<AutoNate.Web.Services.Agent.Catalog.AgentModelDefaultStreamService>();
builder.Services.AddScoped<AutoNate.Web.Services.Agent.Catalog.IAgentModelCatalogStore,
    AutoNate.Web.Services.Agent.Catalog.EfCoreAgentModelCatalogStore>();
builder.Services.AddScoped<AutoNate.Web.Services.Agent.Catalog.IAgentModelCatalogRefresher,
    AutoNate.Web.Services.Agent.Catalog.AgentModelCatalogRefresher>();
builder.Services.AddSingleton<IFieldType, TextFieldType>();
builder.Services.AddSingleton<IFieldType, NumberFieldType>();
builder.Services.AddSingleton<IFieldType, DateFieldType>();
builder.Services.AddSingleton<IFieldType, PhoneFieldType>();
builder.Services.AddSingleton<IFieldType, EmailFieldType>();
builder.Services.AddSingleton<IFieldType, OptionFieldType>();
builder.Services.AddSingleton<IFieldType, BooleanFieldType>();
builder.Services.AddSingleton<IFieldTypeRegistry, FieldTypeRegistry>();

// Workflow behaviors: built-ins register as IWorkflowBehavior singletons;
// the registry merges them with plugin-registered ones (plugins go through
// IPluginContext.Behaviors at enable time, see PluginRuntime).
builder.Services.AddOptions<WorkflowBehaviorOptions>()
    .BindConfiguration(WorkflowBehaviorOptions.SectionName)
    .Validate(
        opts => builder.Environment.IsDevelopment() || !string.IsNullOrWhiteSpace(opts.CallbackSharedSecret),
        $"{WorkflowBehaviorOptions.SectionName}:CallbackSharedSecret must be set outside Development.")
    .ValidateOnStart();
builder.Services.AddSingleton<IWorkflowBehavior, UnlockAccountBehavior>();
builder.Services.AddSingleton<IWorkflowBehaviorRegistry, WorkflowBehaviorRegistry>();
builder.Services.AddSingleton<SharedSecretEndpointFilter>();

builder.Services.AddSingleton<IRecordEventPublisher, DaprRecordEventPublisher>();
builder.Services.AddSingleton<IApplicationEventPublisher, DaprApplicationEventPublisher>();
builder.Services.AddSingleton<INotificationEventPublisher, DaprNotificationEventPublisher>();
builder.Services.AddScoped<INotificationStore, EfCoreNotificationStore>();
builder.Services.AddScoped<ISiteSettingsStore, EfCoreSiteSettingsStore>();
builder.Services.AddHostedService<WorkflowTaskNotificationListener>();
builder.Services.AddHostedService<OrphanedNotificationCleanupService>();

// Self-healing platform: persistent issue store + remediator dispatcher.
// Detectors land in Phase 2; the dispatcher is harmless at zero remediators
// (loop logs once and skips rows with no matching IIssueRemediator).
builder.Services.AddOptions<SystemIssueOptions>()
    .BindConfiguration(SystemIssueOptions.SectionName);
// EfCoreSystemIssueStore is stateless and uses IDbContextFactory (singleton)
// to create a fresh DbContext per call — safe to register as a singleton so
// hosted-service detectors can consume it without their own DI scope.
builder.Services.AddSingleton<ICriticalIssueNotifier, CriticalIssueNotifier>();
builder.Services.AddSingleton<EfCoreSystemIssueStore>();
builder.Services.AddSingleton<ISystemIssueRecorder>(sp => sp.GetRequiredService<EfCoreSystemIssueStore>());
builder.Services.AddSingleton<ISystemIssueStore>(sp => sp.GetRequiredService<EfCoreSystemIssueStore>());
// Singleton + hosted-service-factory so the on-demand remediate endpoint
// can resolve the same dispatcher instance the loop runs on. Plain
// AddHostedService<T>() registers T only as IHostedService, which doesn't
// compose with concrete-type DI from minimal-API endpoints.
builder.Services.AddSingleton<SystemIssueRemediationDispatcher>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<SystemIssueRemediationDispatcher>());

// Phase 2 detectors. Each one is a BackgroundService that respects
// SystemIssues:DetectorsEnabled. Tests opt out via that flag.
builder.Services.AddOptions<SystemHealthSnapshotOptions>()
    .BindConfiguration(SystemHealthSnapshotOptions.SectionName);
builder.Services.AddOptions<AuditOutboxBacklogDetectorOptions>()
    .BindConfiguration(AuditOutboxBacklogDetectorOptions.SectionName);
builder.Services.AddOptions<AuditOutboxDeadLetterDetectorOptions>()
    .BindConfiguration(AuditOutboxDeadLetterDetectorOptions.SectionName);
builder.Services.AddOptions<OrphanReferenceDetectorOptions>()
    .BindConfiguration(OrphanReferenceDetectorOptions.SectionName);
// Phase 5 detectors — workflow + plugin + auth lifecycle.
builder.Services.AddOptions<WorkflowExecutionErrorOpenDetectorOptions>()
    .BindConfiguration(WorkflowExecutionErrorOpenDetectorOptions.SectionName);
builder.Services.AddOptions<LockedAccountDetectorOptions>()
    .BindConfiguration(LockedAccountDetectorOptions.SectionName);
builder.Services.AddOptions<RepeatedAuthFailureDetectorOptions>()
    .BindConfiguration(RepeatedAuthFailureDetectorOptions.SectionName);
builder.Services.AddOptions<StuckWorkflowExecutionDetectorOptions>()
    .BindConfiguration(StuckWorkflowExecutionDetectorOptions.SectionName);
builder.Services.AddOptions<MisconfiguredMenuItemDetectorOptions>()
    .BindConfiguration(MisconfiguredMenuItemDetectorOptions.SectionName);
builder.Services.AddHostedService<SystemHealthSnapshotDetector>();
builder.Services.AddHostedService<AuditOutboxBacklogDetector>();
builder.Services.AddHostedService<AuditOutboxDeadLetterDetector>();
builder.Services.AddHostedService<OrphanReferenceDetector>();
builder.Services.AddHostedService<PluginEnableFailureDetector>();
builder.Services.AddHostedService<WorkflowExecutionErrorOpenDetector>();
builder.Services.AddHostedService<LockedAccountDetector>();
builder.Services.AddHostedService<RepeatedAuthFailureDetector>();
builder.Services.AddHostedService<StuckWorkflowExecutionDetector>();
// Singleton + hosted-service factory so EfCoreMenuStore can resolve the
// same detector instance the periodic loop runs through. Sharing the
// instance lets the auto-resolve fingerprint set survive across triggers
// (operator-save and periodic tick converge on one source of truth).
builder.Services.AddSingleton<MisconfiguredMenuItemDetector>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<MisconfiguredMenuItemDetector>());

// Global exception traps. Pair with app.UseUnhandledExceptionSystemIssues()
// below — together they catch unhandled HTTP exceptions, AppDomain unhandled
// exceptions (incl. terminating ones), and unobserved task exceptions, and
// record each as a fingerprint-deduped SystemIssue under the "unhandled"
// category. Detector-quality issues (audit_outbox backlog, locked accounts,
// etc.) remain authoritative for their domains; this is the catch-all for
// genuinely unexpected failures.
builder.Services.AddHostedService<BackgroundExceptionTrap>();

// Phase 4 remediators. Each registered as IIssueRemediator; the dispatcher
// picks the right one by DetectorId + CanRemediate. Only safe deterministic
// remediators are registered here — anything that mutates business data
// without a full audit trail (records, record_edges) is intentionally
// excluded per the plan's risk analysis.
builder.Services.AddSingleton<IIssueRemediator, AuditOutboxDeadLetterParkRemediator>();
builder.Services.AddSingleton<IIssueRemediator, OrphanReferenceRemediator>();

// Hook system: HookRegistrar is the singleton root that owns both hubs.
// Plugins receive IHookRegistrar (write surface); host services consume
// IActionHub / IFilterHub (read/dispatch surface).
builder.Services.AddOptions<PluginOptions>().BindConfiguration(PluginOptions.SectionName);
builder.Services.AddOptions<DataOptions>().BindConfiguration(DataOptions.SectionName);
builder.Services.AddSingleton<IDataPaths, DataPaths>();
builder.Services.AddSingleton<HookRegistrar>();
builder.Services.AddSingleton<IHookRegistrar>(sp => sp.GetRequiredService<HookRegistrar>());
builder.Services.AddSingleton<IActionHub>(sp => sp.GetRequiredService<HookRegistrar>().Actions);
builder.Services.AddSingleton<IFilterHub>(sp => sp.GetRequiredService<HookRegistrar>().Filters);
builder.Services.AddSingleton<PluginRuntime>();
builder.Services.AddSingleton<PluginSchemaProvisioner>();
builder.Services.AddSingleton<PluginDataAccessRegistry>();
builder.Services.AddSingleton<PluginMigrationRunner>();
builder.Services.AddScoped<IPluginManagementService, PluginManagementService>();
builder.Services.AddHostedService<PluginHostedService>();
builder.Services.AddHttpClient(); // DaprApplicationEventPublisher needs IHttpClientFactory
builder.Services.Configure<FormOptions>(o =>
{
    // Plugin uploads can run up to MaxUploadBytes; keep multipart in sync.
    o.MultipartBodyLengthLimit = 52_428_800;
});
builder.Services.AddScoped<IRecordTypeStore, EfCoreRecordTypeStore>();
builder.Services.AddScoped<IRecordStore, EfCoreRecordStore>();
builder.Services.AddScoped<IRecordHistoryStore, EfCoreRecordHistoryStore>();
builder.Services.AddScoped<IRecordEdgeTypeStore, EfCoreRecordEdgeTypeStore>();
builder.Services.AddScoped<IRecordEdgeStore, EfCoreRecordEdgeStore>();
builder.Services.AddScoped<IRecordCommentStore, EfCoreRecordCommentStore>();
builder.Services.AddHttpClient<IFlowableClient, FlowableClient>()
    .ConfigureHttpClient((serviceProvider, httpClient) =>
    {
        var options = serviceProvider.GetRequiredService<IOptions<FlowableOptions>>().Value;
        FlowableClient.ConfigureHttpClient(httpClient, options);
    });

var app = builder.Build();

if (app.Environment.IsDevelopment()
    && !string.Equals(
        Environment.GetEnvironmentVariable("AUTONATE_ALLOW_RUNNING_WITHOUT_DAPR"),
        bool.TrueString,
        StringComparison.OrdinalIgnoreCase))
{
    await using var startupScope = app.Services.CreateAsyncScope();
    var daprSidecarProbe = startupScope.ServiceProvider.GetRequiredService<DaprSidecarProbe>();
    if (!await daprSidecarProbe.IsAvailableAsync())
    {
        throw new InvalidOperationException(
            "AutoNate.Web requires a local Dapr sidecar in Development because workflow execution events are delivered through Dapr pub/sub. " +
            "Start the app with `make app`, `make app-dapr`, or the Rider flow that runs `dapr: AutoNate.Web Sidecar` before `AutoNate.Web: Rider`. " +
            "Set AUTONATE_ALLOW_RUNNING_WITHOUT_DAPR=true only if you intentionally want to bypass this requirement.");
    }
}

await DatabaseSchemaInitializer.EnsureAsync(app.Services);

// Provision JetStream streams before any subscriber tries to subscribe and
// before the first publish. JetStream requires every published subject to be
// covered by a stream — drift between publisher topics and provisioned
// streams shows up as messages disappearing without errors.
await using (var natsScope = app.Services.CreateAsyncScope())
{
    var streamProvisioner = natsScope.ServiceProvider.GetRequiredService<NatsStreamProvisioner>();
    await streamProvisioner.EnsureStreamsAsync();
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
    app.UseHttpsRedirection();
}

// Sits as the outermost user-registered middleware so it wraps everything
// that follows. Records an unhandled exception as a SystemIssue then
// rethrows, leaving the existing dev-exception-page (development) or
// default 500 handler (production) in charge of the response.
app.UseUnhandledExceptionSystemIssues();

app.UseWebSockets();
app.UseAuthentication();

if (app.Environment.IsDevelopment())
{
    var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("DevelopmentAutoLogin");
    var autoLoginMonitor = app.Services.GetRequiredService<IOptionsMonitor<DevelopmentAutoLoginOptions>>();

    void LogAutoLoginState(DevelopmentAutoLoginOptions current)
    {
        if (!current.Enabled)
        {
            logger.LogInformation("Development auto-login is disabled.");
            return;
        }

        if (string.IsNullOrWhiteSpace(current.Username))
        {
            logger.LogWarning(
                "Development auto-login is enabled, but no username is configured in {Section}.",
                DevelopmentAutoLoginOptions.SectionName);
        }
        else
        {
            logger.LogInformation(
                "Development auto-login is active for username '{Username}'.",
                current.Username);
        }
    }

    LogAutoLoginState(autoLoginMonitor.CurrentValue);
    autoLoginMonitor.OnChange(LogAutoLoginState);

    app.Use(async (context, next) =>
    {
        if (HttpMethods.IsPost(context.Request.Method) ||
            context.Request.Path.Equals("/account/logout", StringComparison.OrdinalIgnoreCase))
        {
            await next();
            return;
        }

        var options = context.RequestServices.GetRequiredService<IOptionsMonitor<DevelopmentAutoLoginOptions>>().CurrentValue;
        var isAuthenticated = context.User.Identity?.IsAuthenticated == true;
        var isDevelopmentAutoLoginIdentity =
            context.User.FindFirstValue(DevelopmentAutoLoginClaimType) == "true";
        var authenticationSource =
            context.User.FindFirstValue(AuthenticationSourceClaimType);
        var configuredUsername = options.Username.Trim();
        var currentDevelopmentAutoLoginUsername =
            context.User.FindFirstValue(DevelopmentAutoLoginUsernameClaimType);

        if (isAuthenticated)
        {
            var isManualIdentity =
                string.Equals(authenticationSource, ManualAuthenticationSource, StringComparison.Ordinal);
            var isTaggedDevelopmentAutoLoginIdentity =
                string.Equals(authenticationSource, DevelopmentAutoLoginAuthenticationSource, StringComparison.Ordinal) ||
                isDevelopmentAutoLoginIdentity;

            if (isManualIdentity)
            {
                await next();
                return;
            }

            var shouldClearDevelopmentAutoLoginCookie =
                !isTaggedDevelopmentAutoLoginIdentity ||
                !options.Enabled ||
                string.IsNullOrWhiteSpace(configuredUsername) ||
                !string.Equals(
                    currentDevelopmentAutoLoginUsername,
                    configuredUsername,
                    StringComparison.OrdinalIgnoreCase);

            if (!shouldClearDevelopmentAutoLoginCookie)
            {
                await next();
                return;
            }

            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            context.User = new ClaimsPrincipal(new ClaimsIdentity());
        }

        if (!options.Enabled)
        {
            await next();
            return;
        }

        if (string.IsNullOrWhiteSpace(configuredUsername))
        {
            await next();
            return;
        }

        var localUserStore = context.RequestServices.GetRequiredService<ILocalUserStore>();
        var autoLoginLogger = context.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("DevelopmentAutoLogin");

        var user = await localUserStore.GetByUsernameAsync(configuredUsername, context.RequestAborted);
        if (user is null)
        {
            autoLoginLogger.LogWarning(
                "Development auto-login is enabled, but configured username '{Username}' was not found.",
                configuredUsername);
            await next();
            return;
        }

        var principal = BuildPrincipal(user, authenticationSource: DevelopmentAutoLoginAuthenticationSource);
        await context.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties
            {
                IsPersistent = false,
                AllowRefresh = true,
                IssuedUtc = DateTimeOffset.UtcNow
            });

        context.User = principal;
        await next();
    });
}

app.UseAuthorization();
app.UseAntiforgery();

// Bus delivery is now via Dapr.Messaging streaming subscriptions owned by
// DaprStreamingSubscriber. The historical /dapr/subscribe manifest and the
// /bus-watcher/messages POST endpoint are gone — there's nothing left to
// declare statically and nothing pushing into the BusWatcher over HTTP. Only
// the WebSocket fan-out for the SPA's live BusWatcher page remains.
app.Map(
    BusWatcherStreamService.WebSocketRoute,
    async (HttpContext context, BusWatcherStreamService busWatcherStreamService, CancellationToken cancellationToken) =>
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        await busWatcherStreamService.AcceptClientAsync(context, cancellationToken);
    });

// Pushes the current default-model snapshot to every chatbot SPA. The
// admin's "Set as default" action updates the catalog, which triggers
// AgentModelDefaultStreamService.BroadcastAsync — connected clients
// receive the new model id and update their in-window footer label
// without a refresh. New clients also receive the current state on
// connect so the label can render before any subsequent broadcasts.
app.Map(
    AutoNate.Web.Services.Agent.Catalog.AgentModelDefaultStreamService.WebSocketRoute,
    async (HttpContext context, AutoNate.Web.Services.Agent.Catalog.AgentModelDefaultStreamService stream, CancellationToken cancellationToken) =>
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }
        await stream.AcceptClientAsync(context, cancellationToken);
    });

app.MapPost(
        "/account/login",
        async Task<IResult> (
            // [FromForm] parameters are what wires this endpoint into the
            // antiforgery middleware: ASP.NET Core's RouteHandlerBuilder
            // detects them and adds RequireAntiforgeryToken metadata, so the
            // app.UseAntiforgery() middleware refuses any POST that lacks a
            // valid antiforgery token + cookie pair. Defends against login
            // CSRF (an attacker-controlled site silently logging the victim
            // into the attacker's account).
            [FromForm] string? username,
            [FromForm] string? password,
            [FromForm] string? returnUrl,
            HttpContext context,
            ILocalUserStore localUserStore,
            IAuditEventPublisher auditPublisher,
            CancellationToken cancellationToken) =>
        {
            username ??= string.Empty;
            password ??= string.Empty;
            returnUrl ??= string.Empty;

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                await auditPublisher.PublishAsync(
                    AuthEventTopic.TopicName,
                    AuthEventTypes.LoginFailed,
                    AuthEventTopic.ResourceKind,
                    resource: new { username },
                    details: new { reason = "missing_credentials" },
                    cancellationToken);
                return Results.Redirect(BuildLoginRedirect(returnUrl, "invalid", username));
            }

            var attempt = await localUserStore.AttemptLoginAsync(username, password, cancellationToken);
            if (attempt.Outcome != LoginAttemptOutcome.Succeeded)
            {
                var reason = attempt.Outcome switch
                {
                    LoginAttemptOutcome.AccountLocked => "account_locked",
                    LoginAttemptOutcome.JustLocked => "account_locked",
                    _ => "invalid_credentials"
                };
                await auditPublisher.PublishAsync(
                    AuthEventTopic.TopicName,
                    AuthEventTypes.LoginFailed,
                    AuthEventTopic.ResourceKind,
                    resource: new { username = attempt.Username ?? username },
                    details: new { reason, failedAttempts = attempt.FailedAttempts },
                    cancellationToken);

                if (attempt.Outcome == LoginAttemptOutcome.JustLocked)
                {
                    await auditPublisher.PublishAsync(
                        AuthEventTopic.TopicName,
                        AuthEventTypes.AccountLocked,
                        AuthEventTopic.ResourceKind,
                        resource: new { userId = attempt.UserId, username = attempt.Username ?? username },
                        details: new
                        {
                            failedAttempts = attempt.FailedAttempts,
                            threshold = EfCoreLocalUserStore.FailedLoginLockoutThreshold
                        },
                        cancellationToken);
                }

                var redirectError = reason == "account_locked" ? "locked" : "invalid";
                return Results.Redirect(BuildLoginRedirect(returnUrl, redirectError, username));
            }

            var user = attempt.User!;
            var principal = BuildPrincipal(user, authenticationSource: ManualAuthenticationSource);

            await context.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                principal,
                new AuthenticationProperties
                {
                    IsPersistent = false,
                    AllowRefresh = true,
                    IssuedUtc = DateTimeOffset.UtcNow
                });

            await auditPublisher.PublishAsync(
                AuthEventTopic.TopicName,
                AuthEventTypes.LoginSucceeded,
                AuthEventTopic.ResourceKind,
                resource: new { userId = user.UserId, username = user.Username },
                details: new { authSource = ManualAuthenticationSource },
                cancellationToken);

            return Results.LocalRedirect(GetSafeReturnUrl(returnUrl));
        });
        // Antiforgery enabled (no .DisableAntiforgery()): defends against
        // login CSRF — without this, an attacker-controlled site could
        // top-level POST credentials here, set the auth cookie, and the
        // victim would unknowingly act inside the attacker's account.
        // The SPA fetches a token from GET /api/auth/antiforgery first
        // and includes it in the form payload (see auth.ts.submitLoginForm).

app.MapPost(
        "/account/logout",
        async Task<IResult> (HttpContext context, IAuditEventPublisher auditPublisher) =>
        {
            var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
            var username = context.User.FindFirstValue(ClaimTypes.Name);
            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            await auditPublisher.PublishAsync(
                AuthEventTopic.TopicName,
                AuthEventTypes.Logout,
                AuthEventTopic.ResourceKind,
                resource: new { userId, username },
                details: null);
            return Results.Redirect("/");
        })
    .DisableAntiforgery();

app.MapAuthEndpoints();
app.MapHealthEndpoints();
app.MapUserEndpoints();
app.MapEventCatalogEndpoints();
app.MapWorkflowEndpoints();
app.MapWorkflowBehaviorEndpoints();
app.MapExecutionEndpoints();
app.MapRecordTypeEndpoints();
app.MapRecordEndpoints();
app.MapRecordWatchEndpoints();
app.MapRecordEdgeEndpoints();
app.MapRecordCommentEndpoints();
app.MapNotificationEndpoints();
app.MapSystemIssueEndpoints();
app.MapRoleEndpoints();
app.MapGroupEndpoints();
app.MapRoleAssignmentEndpoints();
app.MapPermissionGrantEndpoints();
app.MapAuthorizationExplainEndpoints();
app.MapRegistryEndpoints();
app.MapMenuEndpoints();
app.MapPageEndpoints();
app.MapPageTemplateEndpoints();
app.MapStatusAppearanceEndpoints();
app.MapSiteAppearanceEndpoints();
app.MapSiteSettingsEndpoints();
app.MapAdminPluginsEndpoints();
app.MapFormEndpoints();
app.MapExternalConnectionEndpoints();
app.MapAgentModelEndpoints();
app.MapAgentEndpoints();

// Content hierarchy endpoints (Projects → Cabinets → Notebooks → Pages →
// Notes plus per-page versions and binary attachments). All routed through
// IContentAuthorizer; nothing here is gated by SiteConfig.
app.MapProjectEndpoints();
app.MapProjectMemberEndpoints();
app.MapCabinetEndpoints();
app.MapNotebookEndpoints();
app.MapContentPageEndpoints();
app.MapPageVersionEndpoints();
app.MapPageAttachmentEndpoints();
app.MapNoteEndpoints();
app.MapNoteVersionEndpoints();
app.MapContentLocatorEndpoints();

// Runtime-mutable public assets live under /data/wwwroot and are served at the
// configured prefix (default /files). MapStaticAssets only handles compile-time
// known wwwroot files, so we layer UseStaticFiles for the dynamic side. The
// prefix can't be /assets — that path is owned by the Vite-built React bundle
// inside wwwroot/.
{
    var dataPaths = app.Services.GetRequiredService<IDataPaths>();
    var dataOptions = app.Services.GetRequiredService<IOptions<DataOptions>>().Value;
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(dataPaths.PublicRoot),
        RequestPath = dataOptions.PublicUrlPrefix,
        ServeUnknownFileTypes = false,
    });
}

// MapStaticAssets and MapFallbackToFile both depend on WebRootPath (wwwroot/),
// which only exists when the Vite build has run. In Debug the .csproj sets
// BuildSpa=false (devs run Vite separately and serve the SPA from its own dev
// port, with /api proxied here), so the directory is absent — wiring the
// middleware would just log a startup warning and a 404 on every fallback.
// In Release/publish the SPA bundle lands in wwwroot/, so the directory
// exists and both calls do their job. If the directory is missing in Release
// that's a real packaging bug; falling through to a clear 404 surfaces it.
if (Directory.Exists(app.Environment.WebRootPath))
{
    app.MapStaticAssets();

    // React SPA is the only UI now and is mounted at the site root. Any URL that isn't a
    // physical file or an explicitly-mapped endpoint falls back to the SPA index so
    // react-router can pick it up client-side.
    app.MapFallbackToFile("{*path:nonfile}", "index.html");
}

app.Run();

static string BuildLoginRedirect(string? returnUrl, string error, string? username = null)
{
    var query = new Dictionary<string, string?>
    {
        ["error"] = error
    };

    if (!string.IsNullOrWhiteSpace(returnUrl))
    {
        query["returnUrl"] = returnUrl;
    }

    if (!string.IsNullOrWhiteSpace(username))
    {
        query["username"] = username;
    }

    var queryString = QueryString.Create(query).ToUriComponent();
    return string.IsNullOrEmpty(queryString) ? "/" : $"/{queryString}";
}

static string GetSafeReturnUrl(string? returnUrl)
{
    if (string.IsNullOrWhiteSpace(returnUrl))
    {
        return "/home";
    }

    return returnUrl.StartsWith('/') &&
           !returnUrl.StartsWith("//", StringComparison.Ordinal) &&
           !returnUrl.StartsWith("/\\", StringComparison.Ordinal)
        ? returnUrl
        : "/home";
}

static ClaimsPrincipal BuildPrincipal(LocalUser user, string authenticationSource)
{
    var claims = new List<Claim>
    {
        new Claim(ClaimTypes.NameIdentifier, user.UserId.ToString()),
        new Claim(ClaimTypes.Name, user.Username),
        new Claim(ClaimTypes.Email, user.Email),
        new Claim(ClaimTypes.GivenName, user.FirstName),
        new Claim(ClaimTypes.Surname, user.LastName),
        new Claim(AuthenticationSourceClaimType, authenticationSource)
    };

    if (!string.IsNullOrWhiteSpace(user.IdpKey))
    {
        claims.Add(new Claim("idp_key", user.IdpKey));
    }

    if (string.Equals(authenticationSource, DevelopmentAutoLoginAuthenticationSource, StringComparison.Ordinal))
    {
        claims.Add(new Claim(DevelopmentAutoLoginClaimType, "true"));
        claims.Add(new Claim(DevelopmentAutoLoginUsernameClaimType, user.Username));
    }

    return new ClaimsPrincipal(
        new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
}
