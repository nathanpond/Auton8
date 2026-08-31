using AutoNate.Plugins.Abstractions;
using Microsoft.AspNetCore.HttpOverrides;
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
using AutoNate.Web.Services.Dashboards;
using AutoNate.Web.Services.Authorization;
using AutoNate.Web.Services.BusWatcher;
using AutoNate.Web.Services.BusWatcher.Subscriptions;
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
using AutoNate.Web.Services.Notes;
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
// ---------------------------------------------------------------------------
// CSRF threat model (read this before adding a new endpoint)
// ---------------------------------------------------------------------------
// Most authenticated state-changing endpoints in this codebase call
// `.DisableAntiforgery()`. That is deliberate, not an oversight. The CSRF
// defense is layered as follows:
//
//   1. Auth cookie SameSite=Strict (see AddCookie above). The browser will
//      not attach the cookie to ANY cross-origin request — including top-level
//      form POSTs, image/script loads, and fetch with credentials: 'include'
//      from another origin. A cross-site attacker therefore cannot forge an
//      authenticated request to a JSON/POST endpoint at all.
//
//   2. Antiforgery tokens are still required on the pre-auth login endpoint
//      (`POST /account/login`, see further down). SameSite cannot defend that
//      one because the auth cookie does not exist yet — the attack is the
//      cookie being SET, not replayed — so the token is the only defense
//      against login CSRF (where an attacker silently logs the victim into
//      an attacker-controlled account).
//
//   3. Server-to-server callback endpoints (`/api/workflow-behaviors/*/execute`,
//      `/internal/yjs-*`) disable antiforgery and substitute an HMAC + shared
//      secret check via `SharedSecretEndpointFilter` /
//      `YjsInternalSecretEndpointFilter`. They are never reached from a
//      browser, so no cookie is involved.
//
// Residual risks accepted under this model:
//   * Same-site attackers (XSS on a sibling subdomain, malicious browser
//     extension acting on the page, hostile JS injected via a vulnerable
//     plugin's admin UI) can issue authenticated requests. We accept this:
//     under any of those conditions the attacker already has full
//     same-origin script execution and antiforgery tokens would not stop
//     them either.
//   * Pre-CSRF-aware browsers (no SameSite support) would not be protected.
//     SameSite=Strict has been supported by every evergreen browser since
//     2017; we do not target legacy browsers.
//
// When adding a NEW state-changing endpoint:
//   * Authenticated mutation from the SPA → `.DisableAntiforgery()` is fine,
//     SameSite=Strict carries it. Do NOT also `AllowAnonymous`.
//   * Anonymous mutation (no auth cookie required) → MUST validate either an
//     antiforgery token (preferred for browser-originated flows) OR a
//     server-to-server shared secret via an endpoint filter. Never both off.
// ---------------------------------------------------------------------------
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
builder.Services.AddOptions<TrustedProxyOptions>()
    .BindConfiguration(TrustedProxyOptions.SectionName);
builder.Services.AddOptions<FlowableOptions>()
    .BindConfiguration(FlowableOptions.SectionName);
builder.Services.AddOptions<DaprOptions>()
    .BindConfiguration(DaprOptions.SectionName);
builder.Services.AddOptions<NatsOptions>()
    .BindConfiguration(NatsOptions.SectionName);
builder.Services.AddSingleton<NatsStreamProvisioner>();
// Phase 6 of the Data Stores plan — shared NATS connection for callers
// (the code-node runner today) that need request/reply rather than the
// short-lived publish-only flow the provisioner uses.
builder.Services.AddSingleton<AutoNate.Web.Services.Nats.INatsConnectionProvider,
    AutoNate.Web.Services.Nats.NatsConnectionProvider>();
builder.Services.AddSingleton<BusWatcherStreamService>();
builder.Services.AddScopedSubscriptions();
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
// Database initializers run at startup before the rest of the host stands up,
// in Order ascending. Primary AutoNate DB is Order 0; Phase 1 of the Data
// Stores plan adds a secondary at Order 10 against `autonate_datastores`.
builder.Services.AddSingleton<AutoNate.Web.Persistence.IDatabaseInitializer,
    AutoNate.Web.Persistence.PrimaryDatabaseInitializer>();
// Authorization posture is fail-closed by default and validated at start-up:
// a deployment cannot end up with grants ignored by simply omitting these keys
// (#59). Mirrors the CallbackSharedSecret / InternalSharedSecret validators
// below. Development stays free to run with enforcement off.
builder.Services.AddSingleton<
    Microsoft.Extensions.Options.IValidateOptions<AutoNate.Web.Authorization.AuthorizationOptions>>(
    new AutoNate.Web.Authorization.AuthorizationOptionsValidator(builder.Environment.IsDevelopment()));
builder.Services.AddOptions<AutoNate.Web.Authorization.AuthorizationOptions>()
    .BindConfiguration(AutoNate.Web.Authorization.AuthorizationOptions.SectionName)
    .ValidateOnStart();
foreach (var entityType in CoreEntityTypes.All)
{
    builder.Services.AddSingleton<IEntityType>(entityType);
}
foreach (var entityType in AutoNate.Web.Authorization.EntityTypes.AnalyticsEntityTypes.All)
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
builder.Services.AddSingleton<ISelectorCompiler, WorkflowExecutionCacheSelectorCompiler>();
builder.Services.AddSingleton<ISelectorCompiler, WorkflowTaskCacheSelectorCompiler>();
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
// Data Stores & Analytics Pipeline (docs/plans/2026-05-30-data-stores-implementation.md).
// Path-only is sufficient for v1; tag predicates can join the compilers
// later if grants need to scope by kind/owner without enumerating ids.
builder.Services.AddSingleton<ISelectorCompiler>(_ =>
    new PathOnlySelectorCompiler<AutoNate.Web.Persistence.Scaffolded.DataStore>(
        EntityKinds.DataStore, d => d.Id));
builder.Services.AddSingleton<ISelectorCompiler>(_ =>
    new PathOnlySelectorCompiler<AutoNate.Web.Persistence.Scaffolded.DataConnector>(
        EntityKinds.DataConnector, d => d.Id));
builder.Services.AddSingleton<ISelectorCompiler>(_ =>
    new PathOnlySelectorCompiler<AutoNate.Web.Persistence.Scaffolded.Dataset>(
        EntityKinds.Dataset, d => d.Id));
builder.Services.AddSingleton<ISelectorCompiler>(_ =>
    new PathOnlySelectorCompiler<AutoNate.Web.Persistence.Scaffolded.SavedQuery>(
        EntityKinds.Query, q => q.Id));
builder.Services.AddSingleton<ISelectorCompiler>(_ =>
    new PathOnlySelectorCompiler<AutoNate.Web.Persistence.Scaffolded.Pipeline>(
        EntityKinds.Pipeline, p => p.Id));
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

// Document binding resolvers (Phase 5). Per-kind implementations are
// scoped because they depend on scoped services (IAuthorizer for the
// record-field resolver, IAqlExecutor for the aql-table resolver). The
// registry is also scoped so DI can inject the matching lifetime of
// resolvers; resolution lookup is a single dictionary read.
builder.Services.AddScoped<AutoNate.Web.Services.Content.Bindings.IDocumentBindingResolver,
    AutoNate.Web.Services.Content.Bindings.RecordFieldBindingResolver>();
builder.Services.AddScoped<AutoNate.Web.Services.Content.Bindings.IDocumentBindingResolver,
    AutoNate.Web.Services.Content.Bindings.AqlTableBindingResolver>();
builder.Services.AddScoped<AutoNate.Web.Services.Content.Bindings.IDocumentBindingResolverRegistry,
    AutoNate.Web.Services.Content.Bindings.DocumentBindingResolverRegistry>();
builder.Services.AddOptions<AutoNate.Web.Services.Content.ContentVersioningOptions>()
    .BindConfiguration(AutoNate.Web.Services.Content.ContentVersioningOptions.SectionName);
builder.Services.AddOptions<AutoNate.Web.Services.Content.ContentAttachmentOptions>()
    .BindConfiguration("ContentAttachments");
builder.Services.AddSingleton<AutoNate.Web.Services.Content.IContentAttachmentStore,
    AutoNate.Web.Services.Content.FilesystemContentAttachmentStore>();
builder.Services.AddOptions<AutoNate.Web.Services.Content.DocumentImportOptions>()
    .BindConfiguration("DocumentImports");
builder.Services.AddSingleton<AutoNate.Web.Services.Content.IDocumentImportStorage,
    AutoNate.Web.Services.Content.FilesystemDocumentImportStorage>();
builder.Services.AddScoped<AuthCacheBumper>();
builder.Services.AddScoped<EntityEdgeReconciler>();
builder.Services.AddScoped<IRoleStore, EfCoreRoleStore>();
builder.Services.AddScoped<IGroupStore, EfCoreGroupStore>();
builder.Services.AddScoped<IRoleAssignmentStore, EfCoreRoleAssignmentStore>();
builder.Services.AddScoped<IPermissionGrantStore, EfCorePermissionGrantStore>();
builder.Services.AddSingleton<AutoNate.Web.Services.Menus.PageRegistrySnapshotCache>();
builder.Services.AddSingleton<AutoNate.Web.Services.SiteSettings.SiteAppearanceSnapshotCache>();
builder.Services.AddScoped<IMenuStore, EfCoreMenuStore>();
builder.Services.AddScoped<IPageTemplateStore, EfCorePageTemplateStore>();
builder.Services.AddScoped<IDashboardStore, EfCoreDashboardStore>();
builder.Services.AddScoped<ILocalUserStore, EfCoreLocalUserStore>();
builder.Services.AddScoped<IWorkflowModelStore, EfCoreWorkflowModelStore>();
builder.Services.AddScoped<IFormStore, EfCoreFormStore>();

// AQL — AutoNate Query Language. The registry collects every IQueryEntity
// registered as a scoped service into a single dispatcher; the executor is
// the public entry point and respects per-entity row authorization inside.
builder.Services.AddScoped<AutoNate.Web.Services.Query.Entities.RecordsQueryEntity>();
builder.Services.AddScoped<AutoNate.Web.Services.Query.Entities.WorkflowModelsQueryEntity>();
builder.Services.AddScoped<AutoNate.Web.Services.Query.Entities.WorkflowExecutionsQueryEntity>();
builder.Services.AddScoped<AutoNate.Web.Services.Query.Entities.FlowsQueryEntity>();
builder.Services.AddScoped<AutoNate.Web.Services.Query.Entities.WorkflowTasksQueryEntity>();
builder.Services.AddScoped<AutoNate.Web.Services.Query.Entities.WorkflowVariablesQueryEntity>();
builder.Services.AddScoped<AutoNate.Web.Services.Query.Entities.WorkflowHistoryQueryEntity>();
builder.Services.AddScoped<AutoNate.Web.Services.Query.Entities.WorkflowAnalyticsQueryEntity>();
builder.Services.AddScoped<AutoNate.Web.Services.Query.Entities.RecordActivityRollupQueryEntity>();
builder.Services.AddScoped<AutoNate.Web.Services.Query.Entities.NotesQueryEntity>();
builder.Services.AddScoped<AutoNate.Web.Services.Query.Entities.IQueryEntity>(sp =>
    sp.GetRequiredService<AutoNate.Web.Services.Query.Entities.RecordsQueryEntity>());
builder.Services.AddScoped<AutoNate.Web.Services.Query.Entities.IQueryEntity>(sp =>
    sp.GetRequiredService<AutoNate.Web.Services.Query.Entities.WorkflowModelsQueryEntity>());
builder.Services.AddScoped<AutoNate.Web.Services.Query.Entities.IQueryEntity>(sp =>
    sp.GetRequiredService<AutoNate.Web.Services.Query.Entities.WorkflowExecutionsQueryEntity>());
builder.Services.AddScoped<AutoNate.Web.Services.Query.Entities.IQueryEntity>(sp =>
    sp.GetRequiredService<AutoNate.Web.Services.Query.Entities.FlowsQueryEntity>());
builder.Services.AddScoped<AutoNate.Web.Services.Query.Entities.IQueryEntity>(sp =>
    sp.GetRequiredService<AutoNate.Web.Services.Query.Entities.WorkflowTasksQueryEntity>());
builder.Services.AddScoped<AutoNate.Web.Services.Query.Entities.IQueryEntity>(sp =>
    sp.GetRequiredService<AutoNate.Web.Services.Query.Entities.WorkflowVariablesQueryEntity>());
builder.Services.AddScoped<AutoNate.Web.Services.Query.Entities.IQueryEntity>(sp =>
    sp.GetRequiredService<AutoNate.Web.Services.Query.Entities.WorkflowHistoryQueryEntity>());
builder.Services.AddScoped<AutoNate.Web.Services.Query.Entities.IQueryEntity>(sp =>
    sp.GetRequiredService<AutoNate.Web.Services.Query.Entities.WorkflowAnalyticsQueryEntity>());
builder.Services.AddScoped<AutoNate.Web.Services.Query.Entities.IQueryEntity>(sp =>
    sp.GetRequiredService<AutoNate.Web.Services.Query.Entities.RecordActivityRollupQueryEntity>());
builder.Services.AddScoped<AutoNate.Web.Services.Query.Entities.IQueryEntity>(sp =>
    sp.GetRequiredService<AutoNate.Web.Services.Query.Entities.NotesQueryEntity>());
// Phase 2 of the Data Stores plan — parameterized FROM Dataset("name").
builder.Services.AddScoped<AutoNate.Web.Services.Query.Entities.DatasetQueryEntity>();
builder.Services.AddScoped<AutoNate.Web.Services.Query.Entities.IQueryEntity>(sp =>
    sp.GetRequiredService<AutoNate.Web.Services.Query.Entities.DatasetQueryEntity>());
builder.Services.AddScoped<AutoNate.Web.Services.Query.Entities.IQueryEntityRegistry,
    AutoNate.Web.Services.Query.Entities.QueryEntityRegistry>();
builder.Services.AddScoped<AutoNate.Web.Services.Query.IAqlExecutor,
    AutoNate.Web.Services.Query.AqlExecutor>();
builder.Services.AddScoped<AutoNate.Web.Services.Query.ISavedQueryStore,
    AutoNate.Web.Services.Query.EfCoreSavedQueryStore>();
// Phase 3 of the Data Stores plan — share-token issuance + anonymous redemption.
builder.Services.AddScoped<AutoNate.Web.Services.Query.ISavedQueryShareTokenStore,
    AutoNate.Web.Services.Query.EfCoreSavedQueryShareTokenStore>();
builder.Services.AddScoped<AutoNate.Web.Services.Query.IShareIssuerPrincipalFactory,
    AutoNate.Web.Services.Query.ShareIssuerPrincipalFactory>();
builder.Services.AddScoped<AutoNate.Web.Services.Query.IAqlSuggestionService,
    AutoNate.Web.Services.Query.AqlSuggestionService>();

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

// Data Stores & Analytics Pipeline — Phase 1 wiring
// (docs/plans/2026-05-30-data-stores-implementation.md). The primary-DB
// metadata stores are scoped (one DbContext per request); the SQL
// provisioner + connection factory are singletons because they hold no
// per-request state. The DatastoresDatabaseInitializer auto-disables when
// ConnectionStrings:Datastores is absent (logs an Info and no-ops).
builder.Services.AddOptions<AutoNate.Web.Services.DataStores.Sql.DatastoresDatabaseOptions>()
    .BindConfiguration(AutoNate.Web.Services.DataStores.Sql.DatastoresDatabaseOptions.SectionName);
builder.Services.AddSingleton<AutoNate.Web.Persistence.IDatabaseInitializer,
    AutoNate.Web.Services.DataStores.Sql.DatastoresDatabaseInitializer>();
builder.Services.AddSingleton<AutoNate.Web.Services.DataStores.Sql.IDatastoresConnectionFactory,
    AutoNate.Web.Services.DataStores.Sql.DatastoresConnectionFactory>();
builder.Services.AddSingleton<AutoNate.Web.Services.DataStores.Sql.SqlDataStoreProvisioner>();
builder.Services.AddScoped<AutoNate.Web.Services.DataStores.IDataStoreStore,
    AutoNate.Web.Services.DataStores.EfCoreDataStoreStore>();
builder.Services.AddScoped<AutoNate.Web.Services.DataConnectors.IDataConnectorStore,
    AutoNate.Web.Services.DataConnectors.EfCoreDataConnectorStore>();
builder.Services.AddScoped<AutoNate.Web.Services.DataStores.File.IFileDataStoreService,
    AutoNate.Web.Services.DataStores.File.FileDataStoreService>();
builder.Services.AddScoped<AutoNate.Web.Services.DataStores.Sql.CsvIngestor>();
// Built-in connector handlers. Plugin-contributed handlers join through
// IPluginConnectorRegistry → PluginDataConnectorAdapter, registered below.
builder.Services.AddHttpClient();
// Dedicated HttpClient for the REST connector — third-party APIs need a
// shorter ceiling than the IHttpClientFactory default (100s) so a hung
// upstream doesn't stall the admin Test endpoint or the scheduled fetch
// worker for a minute and a half.
builder.Services.AddHttpClient("data-connector", c => c.Timeout = TimeSpan.FromSeconds(30));
builder.Services.AddSingleton<AutoNate.Web.Services.DataConnectors.IDataConnectorHandler,
    AutoNate.Web.Services.DataConnectors.Builtin.RestDataConnectorHandler>();
builder.Services.AddSingleton<AutoNate.Web.Services.DataConnectors.IDataConnectorHandler,
    AutoNate.Web.Services.DataConnectors.Builtin.SmbDataConnectorHandler>();
builder.Services.AddSingleton<AutoNate.Web.Plugins.IPluginConnectorRegistry,
    AutoNate.Web.Plugins.PluginConnectorRegistry>();
builder.Services.AddSingleton<AutoNate.Web.Services.DataConnectors.IDataConnectorHandlerRegistry,
    AutoNate.Web.Services.DataConnectors.DataConnectorHandlerRegistry>();
// Phase 4 of the Data Stores plan — Transformer + Analyzer registries
// (built-ins + plugin-contributed). Built-ins are singletons because they
// hold no per-request state; the registry composes the live union of
// DI-registered + plugin-contributed implementations on each call.
builder.Services.AddSingleton<AutoNate.Web.Services.Transformers.ITransformer,
    AutoNate.Web.Services.Transformers.Builtin.FilterRowsTransformer>();
builder.Services.AddSingleton<AutoNate.Web.Services.Transformers.ITransformer,
    AutoNate.Web.Services.Transformers.Builtin.DedupeTransformer>();
builder.Services.AddSingleton<AutoNate.Web.Services.Transformers.ITransformer,
    AutoNate.Web.Services.Transformers.Builtin.ColumnRenameCastTransformer>();
builder.Services.AddSingleton<AutoNate.Web.Services.Transformers.ITransformer,
    AutoNate.Web.Services.Transformers.Builtin.JoinTwoInputsTransformer>();
builder.Services.AddSingleton<AutoNate.Web.Services.Transformers.ITransformer,
    AutoNate.Web.Services.Transformers.Builtin.NullFillTransformer>();
builder.Services.AddSingleton<AutoNate.Web.Services.Transformers.ITransformer,
    AutoNate.Web.Services.Transformers.Builtin.DateNormalizeTransformer>();
builder.Services.AddSingleton<AutoNate.Web.Services.Transformers.ITransformer,
    AutoNate.Web.Services.Transformers.Builtin.RegexExtractTransformer>();
builder.Services.AddSingleton<AutoNate.Web.Services.Transformers.ITransformer,
    AutoNate.Web.Services.Transformers.Builtin.SchemaInferTransformer>();
builder.Services.AddSingleton<AutoNate.Web.Services.Transformers.ITransformer,
    AutoNate.Web.Services.Transformers.Builtin.PivotTransformer>();
builder.Services.AddSingleton<AutoNate.Web.Services.Transformers.ITransformer,
    AutoNate.Web.Services.Transformers.Builtin.UnpivotTransformer>();
builder.Services.AddSingleton<AutoNate.Web.Services.Transformers.ITransformer,
    AutoNate.Web.Services.Transformers.Builtin.CsvToJsonTransformer>();
builder.Services.AddSingleton<AutoNate.Web.Services.Transformers.ITransformer,
    AutoNate.Web.Services.Transformers.Builtin.JsonToCsvTransformer>();
builder.Services.AddSingleton<AutoNate.Web.Services.Transformers.ITransformer,
    AutoNate.Web.Services.Transformers.Builtin.JsonFlattenTransformer>();
builder.Services.AddSingleton<AutoNate.Web.Services.Transformers.ITransformer,
    AutoNate.Web.Services.Transformers.Builtin.XlsxToCsvTransformer>();
builder.Services.AddSingleton<AutoNate.Web.Plugins.IPluginTransformerRegistry,
    AutoNate.Web.Plugins.PluginTransformerRegistry>();
builder.Services.AddSingleton<AutoNate.Web.Services.Transformers.ITransformerRegistry,
    AutoNate.Web.Services.Transformers.TransformerRegistry>();

builder.Services.AddSingleton<AutoNate.Web.Services.Analyzers.IAnalyzer,
    AutoNate.Web.Services.Analyzers.Builtin.SummaryStatisticsAnalyzer>();
builder.Services.AddSingleton<AutoNate.Web.Services.Analyzers.IAnalyzer,
    AutoNate.Web.Services.Analyzers.Builtin.NullRateAnalyzer>();
builder.Services.AddSingleton<AutoNate.Web.Services.Analyzers.IAnalyzer,
    AutoNate.Web.Services.Analyzers.Builtin.DistinctCountAnalyzer>();
builder.Services.AddSingleton<AutoNate.Web.Services.Analyzers.IAnalyzer,
    AutoNate.Web.Services.Analyzers.Builtin.TopKAnalyzer>();
builder.Services.AddSingleton<AutoNate.Web.Services.Analyzers.IAnalyzer,
    AutoNate.Web.Services.Analyzers.Builtin.GroupByAggregateAnalyzer>();
builder.Services.AddSingleton<AutoNate.Web.Services.Analyzers.IAnalyzer,
    AutoNate.Web.Services.Analyzers.Builtin.HistogramBinAnalyzer>();
builder.Services.AddSingleton<AutoNate.Web.Services.Analyzers.IAnalyzer,
    AutoNate.Web.Services.Analyzers.Builtin.CorrelationMatrixAnalyzer>();
builder.Services.AddSingleton<AutoNate.Web.Services.Analyzers.IAnalyzer,
    AutoNate.Web.Services.Analyzers.Builtin.AnomalyZScoreAnalyzer>();
builder.Services.AddSingleton<AutoNate.Web.Services.Analyzers.IAnalyzer,
    AutoNate.Web.Services.Analyzers.Builtin.AnomalyIqrAnalyzer>();
builder.Services.AddSingleton<AutoNate.Web.Services.Analyzers.IAnalyzer,
    AutoNate.Web.Services.Analyzers.Builtin.TrendLinearRegressionAnalyzer>();
builder.Services.AddSingleton<AutoNate.Web.Services.Analyzers.IAnalyzer,
    AutoNate.Web.Services.Analyzers.Builtin.KMeansClusterAnalyzer>();
builder.Services.AddSingleton<AutoNate.Web.Plugins.IPluginAnalyzerRegistry,
    AutoNate.Web.Plugins.PluginAnalyzerRegistry>();
builder.Services.AddSingleton<AutoNate.Web.Services.Analyzers.IAnalyzerRegistry,
    AutoNate.Web.Services.Analyzers.AnalyzerRegistry>();

// Datasets (Phase 2 of the Data Stores plan).
builder.Services.AddScoped<AutoNate.Web.Services.Datasets.IDatasetStore,
    AutoNate.Web.Services.Datasets.EfCoreDatasetStore>();
builder.Services.AddScoped<AutoNate.Web.Services.Datasets.IDatasetExecutor,
    AutoNate.Web.Services.Datasets.DatasetExecutor>();
// File parser registry — looked up by dataset.parser_kind at execute /
// materialize time so a Files-backed dataset can stream rows out of its
// scoped file(s). Add a new IDatasetFileParser registration to support a
// new format (json, xlsx, etc.); no other code in the pipeline cares.
builder.Services.AddSingleton<AutoNate.Web.Services.Datasets.Files.IDatasetFileParser,
    AutoNate.Web.Services.Datasets.Files.CsvFileParser>();
builder.Services.AddSingleton<AutoNate.Web.Services.Datasets.Files.IDatasetFileParser,
    AutoNate.Web.Services.Datasets.Files.RawFileParser>();
builder.Services.AddSingleton<AutoNate.Web.Services.Datasets.Files.DatasetFileParserRegistry>();
builder.Services.AddScoped<AutoNate.Web.Services.Datasets.Files.DatasetFileScopeReader>();
builder.Services.AddScoped<AutoNate.Web.Services.Datasets.Cached.ICachedDatasetMaterializer,
    AutoNate.Web.Services.Datasets.Cached.CachedDatasetMaterializer>();
// One-minute polling scheduler that drives cron-based cached-dataset
// refresh. Manual /api/datasets/{id}/refresh calls go through the
// materializer directly without this loop.
builder.Services.AddHostedService<AutoNate.Web.Services.Datasets.Cached.DatasetRefreshScheduler>();

// Phase 5 of the Data Stores plan — Analytics Pipelines.
builder.Services.AddScoped<AutoNate.Web.Services.Pipelines.IPipelineStore,
    AutoNate.Web.Services.Pipelines.EfCorePipelineStore>();
builder.Services.AddScoped<AutoNate.Web.Services.Pipelines.IPipelineRunStore,
    AutoNate.Web.Services.Pipelines.EfCorePipelineRunStore>();
builder.Services.AddScoped<AutoNate.Web.Services.Pipelines.Execution.INodeRunner,
    AutoNate.Web.Services.Pipelines.Execution.DatasetSourceRunner>();
builder.Services.AddScoped<AutoNate.Web.Services.Pipelines.Execution.INodeRunner,
    AutoNate.Web.Services.Pipelines.Execution.TransformerNodeRunner>();
builder.Services.AddScoped<AutoNate.Web.Services.Pipelines.Execution.INodeRunner,
    AutoNate.Web.Services.Pipelines.Execution.AnalyzerNodeRunner>();
builder.Services.AddScoped<AutoNate.Web.Services.Pipelines.Execution.INodeRunner,
    AutoNate.Web.Services.Pipelines.Execution.DatasetSinkRunner>();
builder.Services.AddScoped<AutoNate.Web.Services.Pipelines.Execution.INodeRunnerRegistry,
    AutoNate.Web.Services.Pipelines.Execution.NodeRunnerRegistry>();
builder.Services.AddScoped<AutoNate.Web.Services.Pipelines.Orchestration.PipelineOrchestrator>();
// Polls every 5s for Queued pipeline_runs rows and dispatches the oldest
// few to the orchestrator. Same one-loop-per-host model as the dataset
// refresh scheduler.
builder.Services.AddHostedService<AutoNate.Web.Services.Pipelines.Orchestration.PipelineRunWorker>();

// Phase 6 of the Data Stores plan — user-authored code transformers /
// analyzers. The JetStreamCodeNodeRunner is registered Scoped because
// TransformerNodeRunner / AnalyzerNodeRunner consume it via constructor
// injection alongside the registries (also Scoped). When Nats:Url isn't
// configured, the runner will fail on first publish — but the Phase 4
// fallthrough path means non-code transformers keep working regardless.
builder.Services.AddScoped<AutoNate.Web.Services.Transformers.Code.ICodeTransformerStore,
    AutoNate.Web.Services.Transformers.Code.EfCoreCodeTransformerStore>();
builder.Services.AddScoped<AutoNate.Web.Services.Pipelines.Execution.JetStreamCodeNodeRunner>();

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
// Outbound-URL guards for user-supplied destinations (#60, #61). The DNS/
// private-address guard is for open-ended destinations (the REST data
// connector); the base-URL policy is the allowlist used wherever a stored
// provider credential is about to be sent somewhere.
builder.Services.AddSingleton<AutoNate.Web.Services.Http.IOutboundUrlGuard,
    AutoNate.Web.Services.Http.OutboundUrlGuard>();
builder.Services.AddOptions<AutoNate.Web.Services.ExternalConnections.ExternalConnectionUrlOptions>()
    .BindConfiguration(AutoNate.Web.Services.ExternalConnections.ExternalConnectionUrlOptions.SectionName);
builder.Services.AddSingleton<AutoNate.Web.Services.ExternalConnections.IProviderBaseUrlPolicy,
    AutoNate.Web.Services.ExternalConnections.ProviderBaseUrlPolicy>();
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

// Phase 1 read-coverage skills. Each tool routes through stores that already
// gate by the caller's principal (IContentAuthorizer for notes, ILocal*Store
// for users, IPermissionGrantStore for grants, IFlowableClient + the
// IInstanceAuthorizer registry for executions, INotificationStore for
// per-user inbox).
builder.Services.AddScoped<IAgentSkill, LookupNotesSkill>();
builder.Services.AddScoped<IAgentSkill, LookupAqlSkill>();
builder.Services.AddScoped<IAgentSkill, LookupWorkflowExecutionsSkill>();
builder.Services.AddScoped<IAgentSkill, LookupPermissionsSkill>();
builder.Services.AddScoped<IAgentSkill, LookupDirectorySkill>();
builder.Services.AddScoped<IAgentSkill, LookupNotificationsSkill>();

// Phase 2 AQL write-help. Pairs with LookupAqlSkill (grammar + schema) and
// InspectPageSkill.apply_page_action (insert into the QueryPage editor).
builder.Services.AddScoped<IAgentSkill, AqlAssistSkill>();

// Phase 3 Markdown → BlockNote converter. Pure (no I/O); used by
// ManageNotesSkill.create_page_from_markdown so the agent can drop a
// markdown summary into a new BlockNote page in one tool call. Existing
// pages are Yjs-managed, so edits on already-mounted pages go through
// the NotesPage page-action handler (SPA-side BlockNote markdown parse).
builder.Services.AddSingleton<IMarkdownToBlockNoteConverter, MarkdownToBlockNoteConverter>();

// Phase 3 manage-skills. Each follows the same confirm-gate envelope
// (Skills/Internal/ConfirmGate.cs) and gates writes through the same
// authorizer paths the corresponding REST endpoints use.
builder.Services.AddScoped<IAgentSkill, ManageNotesSkill>();
builder.Services.AddScoped<IAgentSkill, ManageSavedQueriesSkill>();
builder.Services.AddScoped<IAgentSkill, OperateWorkflowExecutionsSkill>();
builder.Services.AddScoped<IAgentSkill, ManagePermissionsSkill>();
builder.Services.AddScoped<IAgentSkill, SendNotificationsSkill>();

// Phase 5a — operate gaps (projections, plugins admin, external connections,
// site settings + event catalog, record edges). Each mirrors the existing
// REST endpoint's authorization gate.
builder.Services.AddScoped<IAgentSkill, ProjectionsSkill>();
builder.Services.AddScoped<IAgentSkill, PluginsAdminSkill>();
builder.Services.AddScoped<IAgentSkill, ExternalConnectionsSkill>();
builder.Services.AddScoped<IAgentSkill, SiteSettingsSkill>();
builder.Services.AddScoped<IAgentSkill, ManageRecordEdgesSkill>();

// Phase 5b — design-surface read coverage (dashboards / forms / workflow
// models / appearance). Writes for these surfaces go through the SPA editors;
// form-fill auto-discovery handles the rest via InspectPageSkill.
builder.Services.AddScoped<IAgentSkill, DesignSurfacesLookupSkill>();

// Data-stack agent surface: chatbot-driven inspection + CRUD for data stores,
// datasets, and dashboards. Mirrors the same per-endpoint auth gates the
// REST surfaces enforce so the bot can never act past the caller's grants.
builder.Services.AddScoped<IAgentSkill, LookupDataStoresSkill>();
builder.Services.AddScoped<IAgentSkill, ManageDataStoresSkill>();
builder.Services.AddScoped<IAgentSkill, LookupDatasetsSkill>();
builder.Services.AddScoped<IAgentSkill, ManageDatasetsSkill>();
builder.Services.AddScoped<IAgentSkill, LookupDashboardsSkill>();
builder.Services.AddScoped<IAgentSkill, ManageDashboardsSkill>();

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

// Yjs / Hocuspocus sidecar integration. The shared secret is required in
// non-Development environments (mirrors the workflow-behavior pattern):
// without it, no internal callbacks would be accepted, and Hocuspocus would
// fail to authenticate any browser connections.
builder.Services.AddOptions<AutoNate.Web.Services.Yjs.YjsServerOptions>()
    .BindConfiguration(AutoNate.Web.Services.Yjs.YjsServerOptions.SectionName)
    .Validate(
        opts => builder.Environment.IsDevelopment() || !string.IsNullOrWhiteSpace(opts.InternalSharedSecret),
        $"{AutoNate.Web.Services.Yjs.YjsServerOptions.SectionName}:InternalSharedSecret must be set outside Development.")
    .ValidateOnStart();
builder.Services.AddSingleton<YjsInternalSecretEndpointFilter>();

// Refuse to start in non-Development with a permissive AllowedHosts.
// HostFiltering treats both "*" and "" (empty) as "allow all", which leaves
// the app open to Host-header injection / cache poisoning if an operator
// forgets to override per-environment. Development keeps "*" so localhost,
// 127.0.0.1, the Hocuspocus sidecar host, etc. all work without ceremony.
if (!builder.Environment.IsDevelopment())
{
    var allowedHosts = builder.Configuration["AllowedHosts"];
    if (string.IsNullOrWhiteSpace(allowedHosts) || allowedHosts.Trim() == "*")
    {
        throw new InvalidOperationException(
            "AllowedHosts must be set to a semicolon-separated list of expected " +
            "host names outside Development (e.g. \"autonate.example.com\"). " +
            "The base appsettings.json ships empty so deployments fail closed " +
            "until an operator wires it up.");
    }
}

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
builder.Services.AddSingleton<AutoNate.Web.Plugins.PluginScheduledJobRegistry>();
// Singleton store of plugin-contributed chatbot tools. Read per-request via
// the scoped PluginContributedSkill below, so newly-enabled plugin tools
// surface in the next conversation turn without a restart.
builder.Services.AddSingleton<AutoNate.Web.Plugins.PluginAgentSkillRegistry>();
// Single IAgentSkill aggregator that snapshots the plugin registry on each
// per-request construction. SkillRegistry picks it up like any other skill.
builder.Services.AddScoped<IAgentSkill, PluginContributedSkill>();
builder.Services.AddScoped<IPluginManagementService, PluginManagementService>();
builder.Services.AddHostedService<PluginHostedService>();
builder.Services.AddHostedService<AutoNate.Web.Plugins.PluginScheduledJobsHostedService>();
builder.Services.AddHttpClient(); // DaprApplicationEventPublisher needs IHttpClientFactory
// The audit outbox publishes to the Dapr sidecar from inside an open Postgres
// transaction holding FOR UPDATE locks on the batch. On the unnamed client's
// 100 s default, a sidecar that accepts TCP but stalls could hold that
// transaction open for hours across a 100-row batch — idle in transaction,
// autovacuum's xmin horizon pinned database-wide (#71). Five seconds is far
// beyond a healthy local publish and bounds the whole batch to ~8 minutes
// worst case.
builder.Services.AddHttpClient(
    AutoNate.Web.Services.Events.AuditOutboxDispatcher.HttpClientName,
    c => c.Timeout = TimeSpan.FromSeconds(5));
// Global request-body ceiling. This is deliberately modest: JSON routes need
// kilobytes and the plugin upload route caps itself at Plugins:MaxUploadBytes
// (50 MB by default), so a 1 GiB global limit removed the cheapest defence
// against a body that expands in managed memory downstream (#67). Routes that
// genuinely accept large uploads — datastore files — raise it per route with
// RequestSizeLimit / IHttpMaxRequestBodySizeFeature.
const long GlobalMaxRequestBodyBytes = 64L * 1024 * 1024;
builder.Services.Configure<FormOptions>(o =>
{
    // Kestrel's MaxRequestBodySize is the outer gate; keep multipart in sync.
    o.MultipartBodyLengthLimit = GlobalMaxRequestBodyBytes;
});
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = GlobalMaxRequestBodyBytes);
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

// Projection framework — materializes external/expensive data into AQL-queryable
// Postgres tables. AddProjectionFramework() wires the worker, registry, and
// version/watermark stores. Each AddProjection<TSource, TProjection>() +
// AddChangeFeed<TSource, TFeed>() pair registers one cache.
builder.Services.Configure<AutoNate.Web.Services.Projections.ProjectionOptions>(
    builder.Configuration.GetSection(AutoNate.Web.Services.Projections.ProjectionOptions.SectionName));
builder.Services.Configure<AutoNate.Web.Services.Flowable.Cache.FlowableCacheOptions>(
    builder.Configuration.GetSection(AutoNate.Web.Services.Flowable.Cache.FlowableCacheOptions.SectionName));
AutoNate.Web.Services.Projections.ProjectionServiceCollectionExtensions
    .AddProjectionFramework(builder.Services);
AutoNate.Web.Services.Projections.ProjectionServiceCollectionExtensions
    .AddProjection<AutoNate.Web.Models.WorkflowExecutionSummary,
        AutoNate.Web.Services.Flowable.Cache.FlowableExecutionProjection>(builder.Services);
AutoNate.Web.Services.Projections.ProjectionServiceCollectionExtensions
    .AddProjection<AutoNate.Web.Models.FlowableTaskSummary,
        AutoNate.Web.Services.Flowable.Cache.FlowableTaskProjection>(builder.Services);
AutoNate.Web.Services.Projections.ProjectionServiceCollectionExtensions
    .AddProjection<AutoNate.Web.Services.Flowable.Cache.FlowableInstanceVariables,
        AutoNate.Web.Services.Flowable.Cache.FlowableVariableProjection>(builder.Services);
AutoNate.Web.Services.Projections.ProjectionServiceCollectionExtensions
    .AddProjection<AutoNate.Web.Models.FlowableHistoricActivityEvent,
        AutoNate.Web.Services.Flowable.Cache.FlowableHistoryProjection>(builder.Services);
AutoNate.Web.Services.Projections.ProjectionServiceCollectionExtensions
    .AddChangeFeed<AutoNate.Web.Models.WorkflowExecutionSummary,
        AutoNate.Web.Services.Flowable.Cache.FlowableExecutionPollingFeed>(builder.Services);
AutoNate.Web.Services.Projections.ProjectionServiceCollectionExtensions
    .AddChangeFeed<AutoNate.Web.Models.FlowableTaskSummary,
        AutoNate.Web.Services.Flowable.Cache.FlowableTaskPollingFeed>(builder.Services);
AutoNate.Web.Services.Projections.ProjectionServiceCollectionExtensions
    .AddChangeFeed<AutoNate.Web.Services.Flowable.Cache.FlowableInstanceVariables,
        AutoNate.Web.Services.Flowable.Cache.FlowableVariablePollingFeed>(builder.Services);
AutoNate.Web.Services.Projections.ProjectionServiceCollectionExtensions
    .AddChangeFeed<AutoNate.Web.Models.FlowableHistoricActivityEvent,
        AutoNate.Web.Services.Flowable.Cache.FlowableHistoryPollingFeed>(builder.Services);

// Internal-aggregate projection — first non-Flowable consumer of the
// projection framework. Demonstrates the reusability that motivated lifting
// the substrate out of Flowable-specific code in Phase 1.
builder.Services.Configure<AutoNate.Web.Services.Records.Rollups.RecordActivityRollupOptions>(
    builder.Configuration.GetSection(AutoNate.Web.Services.Records.Rollups.RecordActivityRollupOptions.SectionName));
AutoNate.Web.Services.Projections.ProjectionServiceCollectionExtensions
    .AddProjection<AutoNate.Web.Services.Records.Rollups.RecordActivityRollupSnapshot,
        AutoNate.Web.Services.Records.Rollups.RecordActivityRollupProjection>(builder.Services);
AutoNate.Web.Services.Projections.ProjectionServiceCollectionExtensions
    .AddChangeFeed<AutoNate.Web.Services.Records.Rollups.RecordActivityRollupSnapshot,
        AutoNate.Web.Services.Records.Rollups.RecordActivityRollupFeed>(builder.Services);
builder.Services.AddSingleton<AutoNate.Web.Services.Flowable.Cache.IFlowableReadThrough,
    AutoNate.Web.Services.Flowable.Cache.FlowableReadThrough>();
builder.Services.AddSingleton<AutoNate.Web.Services.Flowable.Cache.WorkflowCacheRetentionService>();
builder.Services.AddHostedService(sp =>
    sp.GetRequiredService<AutoNate.Web.Services.Flowable.Cache.WorkflowCacheRetentionService>());

// Cold tier (Phase 3) — Parquet archive of workflow_event_log_cache,
// queried via DuckDB-in-process. ColdTierArchiverService is gated by
// FlowableCache:ColdTier:Enabled (defaults to false) so installs without
// disk persistence skip it cleanly.
builder.Services.Configure<AutoNate.Web.Services.Flowable.Cache.ColdTier.ColdTierOptions>(
    builder.Configuration.GetSection(AutoNate.Web.Services.Flowable.Cache.ColdTier.ColdTierOptions.SectionName));
builder.Services.AddSingleton<AutoNate.Web.Services.Flowable.Cache.ColdTier.ColdTierLayout>();
builder.Services.AddSingleton<AutoNate.Web.Services.Flowable.Cache.ColdTier.ColdTierArchiverService>();
builder.Services.AddHostedService(sp =>
    sp.GetRequiredService<AutoNate.Web.Services.Flowable.Cache.ColdTier.ColdTierArchiverService>());

var app = builder.Build();

// Reading .Value here runs AuthorizationOptionsValidator before any database
// or hosted-service work, so a fail-open posture — or an Enforcement value the
// evaluator would silently read as "not full" — crashes start-up with the
// offending key named rather than serving an open system (#59). ValidateOnStart
// covers the same ground if this line ever moves.
var authPosture = app.Services
    .GetRequiredService<Microsoft.Extensions.Options.IOptions<AutoNate.Web.Authorization.AuthorizationOptions>>()
    .Value;

// Fail-open-ish authorization flags that are legitimate but should never be
// left on silently in a real environment (#59). Neither is a refusal: DryRun is
// the documented staged-rollout tool, and the SuperAdmin backfill is the only
// thing that grants a greenfield install its first admin.
if (!app.Environment.IsDevelopment())
{
    if (authPosture.DryRun)
    {
        app.Logger.LogWarning(
            "{Section}:DryRun is true — write-path denials are logged but still allowed. " +
            "This is a temporary rollout setting; turn it off once the warnings are quiet.",
            AutoNate.Web.Authorization.AuthorizationOptions.SectionName);
    }
    if (authPosture.AssignSuperAdminToAllExistingUsers)
    {
        app.Logger.LogWarning(
            "{Section}:AssignSuperAdminToAllExistingUsers is true. The one-shot backfill grants " +
            "SuperAdmin to every local_user that exists the first time it runs (tracked by the " +
            "'superadmin_backfill_v1' row in auth_seed_state). Set it to false once the first " +
            "admin is seeded, and before pointing this deployment at a database that already " +
            "holds other users.",
            AutoNate.Web.Authorization.AuthorizationOptions.SectionName);
    }
}

// Wire the projection.lag_seconds gauge to the health service. The ObservableGauge
// callback fires per Prometheus scrape, so this just hands it a delegate that
// snapshots the current state — no background timer needed.
{
    var rootProjections = app.Services.GetRequiredService<AutoNate.Web.Services.Projections.IProjectionRegistry>();
    var rootHealth = app.Services.GetRequiredService<AutoNate.Web.Services.Projections.IProjectionHealthService>();
    AutoNate.Web.Services.Projections.ProjectionMetrics.ConfigureLagSampler(() =>
    {
        var now = DateTimeOffset.UtcNow;
        return rootHealth.Snapshot(rootProjections.Projections)
            .Select(s => (s.Name, (now - (s.LastAppliedAtUtc ?? now)).TotalSeconds))
            .ToList();
    });
}

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

await using (var dbInitScope = app.Services.CreateAsyncScope())
{
    var initializers = dbInitScope.ServiceProvider
        .GetServices<AutoNate.Web.Persistence.IDatabaseInitializer>()
        .OrderBy(i => i.Order);
    foreach (var initializer in initializers)
    {
        await initializer.InitializeAsync();
    }
}

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

// Only honor X-Forwarded-For when an operator has explicitly named the
// upstream proxies/networks they trust. Runs before auth so the recorded
// audit IP (read from Connection.RemoteIpAddress downstream) reflects the
// real client when a trusted proxy is in place.
{
    var trustedProxyOptions = app.Services
        .GetRequiredService<IOptions<TrustedProxyOptions>>().Value;
    if (trustedProxyOptions.Enabled)
    {
        var forwardedHeadersOptions = new ForwardedHeadersOptions
        {
            // XForwardedProto is required behind a TLS-terminating proxy:
            // without it Request.IsHttps reflects the proxy→app hop (http)
            // and Cookie.SecurePolicy = Always (set on auth + antiforgery
            // cookies) refuses to emit them — silently breaking sign-in.
            // XForwardedHost keeps Request.Host pointing at the externally
            // visible name so generated absolute URLs (password-reset
            // emails, OAuth callbacks) don't leak the internal proxy hop.
            ForwardedHeaders = ForwardedHeaders.XForwardedFor
                | ForwardedHeaders.XForwardedProto
                | ForwardedHeaders.XForwardedHost,
            ForwardLimit = trustedProxyOptions.ForwardLimit
        };
        // ASP.NET defaults to trusting loopback (127.0.0.1, ::1) which
        // is fine for local sidecars; operators add their LB IPs here.
        foreach (var ip in trustedProxyOptions.KnownProxies)
        {
            if (System.Net.IPAddress.TryParse(ip, out var parsed))
            {
                forwardedHeadersOptions.KnownProxies.Add(parsed);
            }
        }
        foreach (var network in trustedProxyOptions.KnownNetworks)
        {
            if (System.Net.IPNetwork.TryParse(network, out var parsed))
            {
                forwardedHeadersOptions.KnownIPNetworks.Add(parsed);
            }
        }
        app.UseForwardedHeaders(forwardedHeadersOptions);
    }
}

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
    async (
        HttpContext context,
        SubscriptionManager subscriptionManager,
        AutoNate.Web.Authorization.Evaluator.IAuthorizer authorizer,
        Microsoft.Extensions.Options.IOptions<AutoNate.Web.Authorization.AuthorizationOptions> authorizationOptions,
        Microsoft.EntityFrameworkCore.IDbContextFactory<AutoNate.Web.Persistence.AutoNateDbContext> dbFactory,
        CancellationToken cancellationToken) =>
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        await subscriptionManager.AcceptAsync(context, authorizer, authorizationOptions, dbFactory, cancellationToken);
    })
    .RequireAuthorization();

// Bridge the in-process BusWatcher notifier into the SubscriptionManager so
// every Dapr message reaches the scoped fan-out path. AuthChangeListener
// subscribes to the same notifier to react to iam.events mutations.
{
    var bus = app.Services.GetRequiredService<BusWatcherStreamService>();
    var manager = app.Services.GetRequiredService<SubscriptionManager>();
    var authChangeListener = app.Services.GetRequiredService<AuthChangeListener>();
    // Subscriptions returned by Subscribe are intentionally not disposed:
    // both services are singletons that live for the app lifetime.
    _ = bus.Subscribe((message, ct) => manager.PublishAsync(message, ct));
    authChangeListener.Start(bus);
}

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
app.MapDashboardEndpoints();
app.MapQueryEndpoints();
app.MapAqlSchemaEndpoints();
app.MapAqlSuggestEndpoints();
app.MapSavedQueryEndpoints();
app.MapPublicQueryShareEndpoints();
app.MapStatusAppearanceEndpoints();
app.MapSiteAppearanceEndpoints();
app.MapSiteSettingsEndpoints();
app.MapAdminPluginsEndpoints();
app.MapAdminProjectionsEndpoints();
app.MapFormEndpoints();
app.MapExternalConnectionEndpoints();
app.MapDataStoreEndpoints();
app.MapDataConnectorEndpoints();
app.MapDatasetEndpoints();
app.MapTransformerEndpoints();
app.MapAnalyzerEndpoints();
app.MapPipelineEndpoints();
app.MapCodeTransformerEndpoints();
app.MapAgentModelEndpoints();
app.MapAgentEndpoints();

// Content hierarchy endpoints (Projects → Cabinets → Notebooks → Pages →
// Notes plus per-page versions and binary attachments). All routed through
// IContentAuthorizer; nothing here is gated by SiteConfig.
app.MapProjectEndpoints();
app.MapProjectMemberEndpoints();
app.MapCabinetEndpoints();
app.MapNotebookEndpoints();
app.MapContentFolderEndpoints();
app.MapContentDocumentEndpoints();
app.MapDocumentVersionEndpoints();
app.MapContentDocumentCommentEndpoints();
app.MapContentDocumentBindingEndpoints();
app.MapContentPermissionOverrideEndpoints();
app.MapContentPageEndpoints();
app.MapPageVersionEndpoints();
app.MapPageAttachmentEndpoints();
app.MapNoteEndpoints();
app.MapNoteVersionEndpoints();
app.MapContentLocatorEndpoints();
app.MapContentShareEndpoints();
app.MapYjsEndpoints();

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
    // UseStaticFiles is the runtime-enumerating backstop for MapStaticAssets.
    // The SDK's compile-time static-web-assets manifest only catches files
    // that exist at the start of build; files that the BuildSpa target drops
    // into wwwroot/ during BeforeBuild (Vite's hashed bundles, drawio public/
    // tree, etc.) don't make it into AutoNate.Web.staticwebassets.endpoints
    // .json, so MapStaticAssets ends up serving nothing. UseStaticFiles
    // enumerates wwwroot/ on each request and serves anything it finds. The
    // E2E fixture (dotnet run -p:BuildSpa=true) is the canonical repro;
    // dotnet publish also currently breaks on commas in drawio filenames,
    // which would leave the manifest empty in production too. Negligible
    // overhead behind MapStaticAssets's manifest hits in normal operation.
    app.UseStaticFiles();
    app.MapStaticAssets();

    // /api/* must NOT fall through to the SPA index. A missing or
    // unregistered API route should produce a clean 404; serving index.html
    // for /api hides routing bugs and — because the static-files pipeline
    // attaches ETag/Last-Modified — lets the browser heuristically cache
    // the HTML body against that exact (path,query) pair. Subsequent
    // requests then keep returning the cached HTML even after the real
    // endpoint ships.
    //
    // Two pieces, deliberately NOT a MapFallback("/api/{**rest}") route:
    //   1. The SPA catch-all below carries a regex constraint that refuses
    //      any path starting with "api/", so an unknown /api path matches
    //      no endpoint at all.
    //   2. This middleware turns "no endpoint under /api" into an
    //      uncacheable 404.
    // A route endpoint would instead become a *candidate* in endpoint
    // selection, and AcceptsMatcherPolicy then prefers it over a real
    // endpoint whose JSON body contract the request doesn't satisfy — a
    // body-less POST to /api/system-issues/{id}/resolve (a legal call) got
    // 404 instead of reaching its handler. It would also need an
    // auth-decision marker to pass AuthorizationGatePresenceTests.
    app.Use(async (http, next) =>
    {
        if (http.Request.Path.StartsWithSegments("/api") && http.GetEndpoint() is null)
        {
            http.Response.Headers.CacheControl = "no-store";
            http.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }
        await next(http);
    });

    // React SPA is the only UI now and is mounted at the site root. Any URL that isn't a
    // physical file, an explicitly-mapped endpoint, or under /api falls back to the SPA
    // index so react-router can pick it up client-side.
    //
    // Known wrinkle: MapFallbackToFile is GET/HEAD-only, and HTTP-method
    // matching is decided in the routing DFA *before* route constraints
    // run, so a non-GET to an unknown /api path answers 405 rather than
    // 404. Don't "fix" that by making the fallback verb-agnostic — then it
    // becomes a body-less-POST candidate again and re-breaks the resolve
    // case above. GET (the only verb a browser will cache) is the one that
    // matters here.
    app.MapFallbackToFile("{*path:nonfile:regex(^(?!api(/|$)))}", "index.html");
    // The catch-all above never matches the site root: for "/" the `path`
    // parameter is absent, and RegexRouteConstraint (like most constraints)
    // returns false for a missing value. Without this explicit root fallback
    // GET / is a bare 404 while /home and every deep link serve the shell —
    // which is exactly how the E2E suite broke (SignInAsync starts at "/").
    // Regression guard: SpaRootFallbackTests. Refs #132.
    app.MapFallbackToFile("/", "index.html");
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
