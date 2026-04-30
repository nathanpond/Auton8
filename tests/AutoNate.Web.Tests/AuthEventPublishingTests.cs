using System.Net.Http.Json;
using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.EndpointFilters;
using AutoNate.Web.Authorization.Evaluator;
using AutoNate.Web.Authorization.Selectors;
using AutoNate.Web.Persistence;
using AutoNate.Web.Services.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AutoNate.Web.Tests;

[Trait("Category", "Integration")]
public sealed class AuthEventPublishingTests
{
    [Fact]
    public async Task GetMe_publishes_auth_me_viewed()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();

        await client.GetAsync("/api/auth/me");

        var meEvents = factory.RecordedAuditEvents.Events
            .Where(e => e.EventType == AuthEventTypes.MeViewed)
            .ToArray();
        Assert.NotEmpty(meEvents);
        Assert.All(meEvents, e =>
        {
            Assert.Equal(AuthEventTopic.TopicName, e.Topic);
            Assert.Equal(AuthEventTopic.ResourceKind, e.ResourceKind);
        });
    }

    [Fact]
    public async Task PostLogout_publishes_auth_logout()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();

        // Prime auth so the logout endpoint sees an authenticated principal.
        (await client.GetAsync("/api/auth/me")).EnsureSuccessStatusCode();
        factory.RecordedAuditEvents.Clear();

        await client.PostAsync("/api/auth/logout", content: null);

        Assert.Contains(
            factory.RecordedAuditEvents.Events,
            e => e.Topic == AuthEventTopic.TopicName
                 && e.EventType == AuthEventTypes.Logout);
    }

    [Fact]
    public async Task PostCheck_publishes_auth_permission_checked_with_counts()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();

        // Prime auth.
        (await client.GetAsync("/api/auth/me")).EnsureSuccessStatusCode();
        factory.RecordedAuditEvents.Clear();

        var response = await client.PostAsJsonAsync("/api/auth/check", new
        {
            checks = new[]
            {
                new { kind = "record", action = "view", id = Guid.NewGuid().ToString() },
                new { kind = "record", action = "edit", id = Guid.NewGuid().ToString() }
            }
        });
        response.EnsureSuccessStatusCode();

        var checkedEvent = Assert.Single(factory.RecordedAuditEvents.Events,
            e => e.EventType == AuthEventTypes.PermissionChecked);
        Assert.Equal(AuthEventTopic.TopicName, checkedEvent.Topic);
        // details.checkCount should be 2 — verify by reflection over the
        // anonymous type produced by the endpoint.
        var details = checkedEvent.Details!;
        var checkCountProp = details.GetType().GetProperty("checkCount");
        Assert.NotNull(checkCountProp);
        Assert.Equal(2, (int)checkCountProp!.GetValue(details)!);
    }

    [Fact]
    public async Task RequirePermissionFilter_publishes_access_denied_on_deny()
    {
        var recording = new RecordingAuditEventPublisher();
        var http = BuildHttpContext(
            services => services
                .AddSingleton<IAuthorizer>(new StubAuthorizer(allow: false, "rule rejected"))
                .AddSingleton<Services.Events.IAuditEventPublisher>(recording));
        var invocation = new TestEndpointFilterInvocationContext(http);

        var filter = new RequirePermissionFilter(
            kind: "record",
            action: "view",
            idFrom: _ => Guid.NewGuid().ToString());

        var result = await filter.InvokeAsync(invocation, _ =>
            ValueTask.FromResult<object?>(Results.Ok()));

        Assert.IsType<StatusCodeHttpResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, ((StatusCodeHttpResult)result!).StatusCode);
        var denied = Assert.Single(recording.Events);
        Assert.Equal(AuthEventTypes.AccessDenied, denied.EventType);
        Assert.Equal(AuthEventTopic.TopicName, denied.Topic);
    }

    [Fact]
    public async Task RequirePermissionFilter_publishes_access_denied_on_missing_id()
    {
        var recording = new RecordingAuditEventPublisher();
        var http = BuildHttpContext(
            services => services
                .AddSingleton<IAuthorizer>(new StubAuthorizer(allow: true, "always allow"))
                .AddSingleton<Services.Events.IAuditEventPublisher>(recording));
        var invocation = new TestEndpointFilterInvocationContext(http);

        var filter = new RequirePermissionFilter(
            kind: "record",
            action: "view",
            idFrom: _ => null);

        var result = await filter.InvokeAsync(invocation, _ =>
            ValueTask.FromResult<object?>(Results.Ok()));

        Assert.IsType<StatusCodeHttpResult>(result);
        var denied = Assert.Single(recording.Events);
        Assert.Equal(AuthEventTypes.AccessDenied, denied.EventType);
    }

    [Fact]
    public async Task RequireKindPermissionFilter_publishes_access_denied_on_deny()
    {
        var recording = new RecordingAuditEventPublisher();
        var http = BuildHttpContext(
            services => services
                .AddSingleton<IAuthorizer>(new StubAuthorizer(allow: false, "no kind grant"))
                .AddSingleton<Services.Events.IAuditEventPublisher>(recording));
        var invocation = new TestEndpointFilterInvocationContext(http);

        var filter = new RequireKindPermissionFilter(kind: "user", action: "create");

        var result = await filter.InvokeAsync(invocation, _ =>
            ValueTask.FromResult<object?>(Results.Ok()));

        Assert.IsType<StatusCodeHttpResult>(result);
        var denied = Assert.Single(recording.Events);
        Assert.Equal(AuthEventTypes.AccessDenied, denied.EventType);
    }

    [Fact]
    public async Task RequireKindPermissionFilter_does_not_publish_on_allow()
    {
        var recording = new RecordingAuditEventPublisher();
        var http = BuildHttpContext(
            services => services
                .AddSingleton<IAuthorizer>(new StubAuthorizer(allow: true, "ok"))
                .AddSingleton<Services.Events.IAuditEventPublisher>(recording));
        var invocation = new TestEndpointFilterInvocationContext(http);

        var filter = new RequireKindPermissionFilter(kind: "user", action: "create");

        await filter.InvokeAsync(invocation, _ =>
            ValueTask.FromResult<object?>(Results.Ok()));

        Assert.Empty(recording.Events);
    }

    private static HttpContext BuildHttpContext(Action<IServiceCollection> configureServices)
    {
        var services = new ServiceCollection();
        configureServices(services);
        var ctx = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        ctx.SetEndpoint(new RouteEndpoint(
            requestDelegate: _ => Task.CompletedTask,
            routePattern: RoutePatternFactory.Parse("/api/test"),
            order: 0,
            metadata: null,
            displayName: "test"));
        return ctx;
    }

    private sealed class StubAuthorizer(bool allow, string reason) : IAuthorizer
    {
        public Task<AuthDecision> AuthorizeAsync(
            System.Security.Claims.ClaimsPrincipal actor,
            string action,
            EntityRef target,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(allow ? AuthDecision.Allow(reason) : AuthDecision.Deny(reason));

        public Task<IQueryable<T>> FilterQueryAsync<T>(
            AutoNateDbContext db,
            System.Security.Claims.ClaimsPrincipal actor,
            string kind,
            string action,
            IQueryable<T> source,
            CancellationToken cancellationToken = default) where T : class =>
            throw new NotSupportedException();

        public Task<CapabilitySummary> GetCapabilitiesAsync(
            System.Security.Claims.ClaimsPrincipal actor,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> IsAuthorizedAsync(
            System.Security.Claims.ClaimsPrincipal actor,
            string kind,
            string action,
            Func<SelectorAst, bool> selectorMatcher,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<RecordSqlFilter> BuildRecordSqlFilterAsync(
            System.Security.Claims.ClaimsPrincipal actor,
            string action,
            int parameterOffset,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AuthExplanation> ExplainAsync(
            Guid asUserId,
            string action,
            EntityRef target,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class TestEndpointFilterInvocationContext(HttpContext http) : EndpointFilterInvocationContext
    {
        public override HttpContext HttpContext { get; } = http;
        public override IList<object?> Arguments { get; } = new List<object?>();
        public override T GetArgument<T>(int index) => throw new NotSupportedException();
    }
}
