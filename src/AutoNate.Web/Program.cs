using AutoNate.Web.Configuration;
using AutoNate.Web.Endpoints;
using AutoNate.Web.Models;
using AutoNate.Web.Persistence;
using AutoNate.Web.Services.Auth;
using AutoNate.Web.Services.BusWatcher;
using AutoNate.Web.Services.Dapr;
using AutoNate.Web.Services.Flowable;
using AutoNate.Web.Services.Workflow;
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
        options.LoginPath = "/spa/";
        options.AccessDeniedPath = "/spa/";
        options.SlidingExpiration = true;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
    });
builder.Services.AddAuthorization();
builder.Services.AddAntiforgery();
builder.Services.AddOptions<DevelopmentAutoLoginOptions>()
    .BindConfiguration(DevelopmentAutoLoginOptions.SectionName);
builder.Services.AddOptions<FlowableOptions>()
    .BindConfiguration(FlowableOptions.SectionName);
builder.Services.AddOptions<DaprOptions>()
    .BindConfiguration(DaprOptions.SectionName);
builder.Services.AddSingleton<BusWatcherStreamService>();
builder.Services.AddSingleton<DaprSidecarProbe>();
builder.Services.AddDbContextFactory<AutoNateDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("Default")
        ?? throw new InvalidOperationException("Connection string 'Default' is required.")));
builder.Services.AddScoped<ILocalUserStore, EfCoreLocalUserStore>();
builder.Services.AddScoped<IWorkflowModelStore, EfCoreWorkflowModelStore>();
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
    var autoLoginOptions = app.Services.GetRequiredService<IOptions<DevelopmentAutoLoginOptions>>().Value;

    if (autoLoginOptions.Enabled)
    {
        if (string.IsNullOrWhiteSpace(autoLoginOptions.Username))
        {
            logger.LogWarning(
                "Development auto-login is enabled, but no username is configured in {Section}.",
                DevelopmentAutoLoginOptions.SectionName);
        }
        else
        {
            logger.LogInformation(
                "Development auto-login is active for username '{Username}'.",
                autoLoginOptions.Username);
        }
    }

    app.Use(async (context, next) =>
    {
        if (HttpMethods.IsPost(context.Request.Method) ||
            context.Request.Path.Equals("/account/logout", StringComparison.OrdinalIgnoreCase))
        {
            await next();
            return;
        }

        var options = context.RequestServices.GetRequiredService<IOptions<DevelopmentAutoLoginOptions>>().Value;
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

app.MapGet(
    "/dapr/subscribe",
    (IOptions<DaprOptions> options, BusWatcherStreamService busWatcherStreamService) =>
        Results.Json(busWatcherStreamService.GetSubscriptions(options.Value)));
app.MapPost(
        BusWatcherStreamService.SubscriptionRoute,
        async (HttpContext context, BusWatcherStreamService busWatcherStreamService, CancellationToken cancellationToken) =>
        {
            await busWatcherStreamService.PublishAsync(context, cancellationToken);
            return Results.Ok();
        })
    .DisableAntiforgery();
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
            CancellationToken cancellationToken) =>
        {
            var form = await context.Request.ReadFormAsync(cancellationToken);
            var username = form["username"].ToString();
            var password = form["password"].ToString();
            var returnUrl = form["returnUrl"].ToString();

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                return Results.Redirect(BuildLoginRedirect(returnUrl, "invalid", username));
            }

            var user = await localUserStore.ValidateCredentialsAsync(username, password, cancellationToken);
            if (user is null)
            {
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

            return Results.LocalRedirect(GetSafeReturnUrl(returnUrl));
        })
    .DisableAntiforgery();

app.MapPost(
        "/account/logout",
        async Task<IResult> (HttpContext context) =>
        {
            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Redirect("/");
        })
    .DisableAntiforgery();

app.MapAuthEndpoints();
app.MapHealthEndpoints();
app.MapUserEndpoints();
app.MapWorkflowEndpoints();
app.MapExecutionEndpoints();

app.MapStaticAssets();

// React SPA is the only UI now. Root URL redirects into /spa/ so users hitting the bare
// domain land on the SPA. Any /spa/* route that isn't a physical file falls back to the
// SPA index so react-router can pick it up client-side.
app.MapGet("/", () => Results.Redirect("/spa/"));
app.MapFallbackToFile(
    "/spa/{*path:nonfile}",
    "spa/index.html");

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
    return string.IsNullOrEmpty(queryString) ? "/spa/" : $"/spa/{queryString}";
}

static string GetSafeReturnUrl(string? returnUrl)
{
    if (string.IsNullOrWhiteSpace(returnUrl))
    {
        return "/spa/home";
    }

    return returnUrl.StartsWith("/", StringComparison.Ordinal) &&
           !returnUrl.StartsWith("//", StringComparison.Ordinal) &&
           !returnUrl.StartsWith("/\\", StringComparison.Ordinal)
        ? returnUrl
        : "/spa/home";
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
