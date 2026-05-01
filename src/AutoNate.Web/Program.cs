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
using AutoNate.Web.Services.Events;
using AutoNate.Web.Services.Flowable;
using AutoNate.Web.Services.Menus;
using AutoNate.Web.Services.Nats;
using AutoNate.Web.Services.Notifications;
using AutoNate.Web.Services.Records;
using AutoNate.Web.Services.Records.Fields;
using AutoNate.Web.Services.Signals;
using AutoNate.Web.Services.SiteSettings;
using AutoNate.Web.Services.Workflow;
using Microsoft.AspNetCore.Http.Features;
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
builder.Services.AddAntiforgery();
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
builder.Services.AddSingleton<IWorkflowSignalRegistry, EfCoreWorkflowSignalRegistry>();
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
builder.Services.AddDbContextFactory<AutoNateDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("Default")
        ?? throw new InvalidOperationException("Connection string 'Default' is required.")));
builder.Services.AddOptions<AutoNate.Web.Authorization.AuthorizationOptions>()
    .BindConfiguration(AutoNate.Web.Authorization.AuthorizationOptions.SectionName);
foreach (var entityType in CoreEntityTypes.All)
{
    builder.Services.AddSingleton<IEntityType>(entityType);
}
builder.Services.AddSingleton<IEntityRegistry, EntityRegistry>();
builder.Services.AddSingleton<IEntityEdgeWriter, EntityEdgeWriter>();

builder.Services.AddSingleton<ISelectorCompiler, RecordSelectorCompiler>();
builder.Services.AddSingleton<ISelectorCompiler>(_ =>
    new PathOnlySelectorCompiler<AutoNate.Web.Persistence.Scaffolded.Role>(
        EntityKinds.Role, x => x.Id));
builder.Services.AddSingleton<ISelectorCompiler>(_ =>
    new PathOnlySelectorCompiler<AutoNate.Web.Persistence.Scaffolded.Group>(
        EntityKinds.Group, x => x.Id));
builder.Services.AddSingleton<ISelectorCompiler>(_ =>
    new PathOnlySelectorCompiler<AutoNate.Web.Persistence.Scaffolded.RecordType>(
        EntityKinds.RecordType, x => x.Id));
builder.Services.AddSingleton<ISelectorCompiler>(_ =>
    new PathOnlySelectorCompiler<AutoNate.Web.Persistence.Scaffolded.WorkflowModel>(
        EntityKinds.WorkflowModel, x => x.Id));
builder.Services.AddSingleton<ISelectorCompilerRegistry, SelectorCompilerRegistry>();

builder.Services.AddScoped<IInstanceAuthorizer, RecordInstanceAuthorizer>();
builder.Services.AddScoped<IInstanceAuthorizer, RoleInstanceAuthorizer>();
builder.Services.AddScoped<IInstanceAuthorizer, GroupInstanceAuthorizer>();
builder.Services.AddScoped<IInstanceAuthorizer, RecordTypeInstanceAuthorizer>();
builder.Services.AddScoped<IInstanceAuthorizer, WorkflowModelInstanceAuthorizer>();
builder.Services.AddScoped<IInstanceAuthorizer, WorkflowTaskInstanceAuthorizer>();
builder.Services.AddScoped<IInstanceAuthorizer, WorkflowExecutionInstanceAuthorizer>();

builder.Services.AddScoped<IAuthorizer, Authorizer>();
builder.Services.AddScoped<AuthCacheBumper>();
builder.Services.AddScoped<EntityEdgeReconciler>();
builder.Services.AddScoped<IRoleStore, EfCoreRoleStore>();
builder.Services.AddScoped<IGroupStore, EfCoreGroupStore>();
builder.Services.AddScoped<IRoleAssignmentStore, EfCoreRoleAssignmentStore>();
builder.Services.AddScoped<IPermissionGrantStore, EfCorePermissionGrantStore>();
builder.Services.AddScoped<IMenuStore, EfCoreMenuStore>();
builder.Services.AddScoped<IPageTemplateStore, EfCorePageTemplateStore>();
builder.Services.AddScoped<ILocalUserStore, EfCoreLocalUserStore>();
builder.Services.AddScoped<IWorkflowModelStore, EfCoreWorkflowModelStore>();
builder.Services.AddSingleton<IFieldType, TextFieldType>();
builder.Services.AddSingleton<IFieldType, NumberFieldType>();
builder.Services.AddSingleton<IFieldType, DateFieldType>();
builder.Services.AddSingleton<IFieldType, PhoneFieldType>();
builder.Services.AddSingleton<IFieldType, EmailFieldType>();
builder.Services.AddSingleton<IFieldType, OptionFieldType>();
builder.Services.AddSingleton<IFieldType, BooleanFieldType>();
builder.Services.AddSingleton<IFieldTypeRegistry, FieldTypeRegistry>();
builder.Services.AddSingleton<IRecordEventPublisher, DaprRecordEventPublisher>();
builder.Services.AddSingleton<IApplicationEventPublisher, DaprApplicationEventPublisher>();
builder.Services.AddSingleton<INotificationEventPublisher, DaprNotificationEventPublisher>();
builder.Services.AddScoped<INotificationStore, EfCoreNotificationStore>();
builder.Services.AddScoped<ISiteSettingsStore, EfCoreSiteSettingsStore>();
builder.Services.AddHostedService<WorkflowTaskNotificationListener>();

// Hook system: HookRegistrar is the singleton root that owns both hubs.
// Plugins receive IHookRegistrar (write surface); host services consume
// IActionHub / IFilterHub (read/dispatch surface).
builder.Services.AddOptions<PluginOptions>().BindConfiguration(PluginOptions.SectionName);
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

app.MapPost(
        "/account/login",
        async Task<IResult> (
            HttpContext context,
            ILocalUserStore localUserStore,
            IAuditEventPublisher auditPublisher,
            CancellationToken cancellationToken) =>
        {
            var form = await context.Request.ReadFormAsync(cancellationToken);
            var username = form["username"].ToString();
            var password = form["password"].ToString();
            var returnUrl = form["returnUrl"].ToString();

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

            var user = await localUserStore.ValidateCredentialsAsync(username, password, cancellationToken);
            if (user is null)
            {
                await auditPublisher.PublishAsync(
                    AuthEventTopic.TopicName,
                    AuthEventTypes.LoginFailed,
                    AuthEventTopic.ResourceKind,
                    resource: new { username },
                    details: new { reason = "invalid_credentials" },
                    cancellationToken);
                return Results.Redirect(BuildLoginRedirect(returnUrl, "invalid", username));
            }

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
        })
    .DisableAntiforgery();

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
app.MapExecutionEndpoints();
app.MapRecordTypeEndpoints();
app.MapRecordEndpoints();
app.MapRecordEdgeEndpoints();
app.MapRecordCommentEndpoints();
app.MapNotificationEndpoints();
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

app.MapStaticAssets();

// React SPA is the only UI now and is mounted at the site root. Any URL that isn't a
// physical file or an explicitly-mapped endpoint falls back to the SPA index so
// react-router can pick it up client-side.
app.MapFallbackToFile("{*path:nonfile}", "index.html");

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

    return returnUrl.StartsWith("/", StringComparison.Ordinal) &&
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
