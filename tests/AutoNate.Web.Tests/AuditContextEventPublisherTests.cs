using System.Security.Claims;
using System.Text.Json;
using AutoNate.Web.Configuration;
using AutoNate.Web.Models.Notifications;
using AutoNate.Web.Services.ApplicationEvents;
using AutoNate.Web.Services.Audit;
using AutoNate.Web.Services.Events;
using AutoNate.Web.Services.Notifications;
using AutoNate.Web.Services.Records;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AutoNate.Web.Tests;

// Phase 1 of the audit-events plan: every event envelope must carry a nested
// auditContext populated from IRequestContext. Phase 5 routed publishing
// through IAuditEventOutbox; these tests now capture the serialized payload
// from the outbox call rather than from a stub Dapr endpoint.
public sealed class AuditContextEventPublisherTests
{
    [Fact]
    public async Task RecordEvent_envelope_includes_populated_auditContext()
    {
        var outbox = new CapturingOutbox();
        var actorId = Guid.NewGuid();
        var requestContext = BuildRequestContext(actorId, "alice", "10.0.0.42", "Mozilla/Test");

        var publisher = new DaprRecordEventPublisher(
            requestContext,
            outbox,
            NullLogger<DaprRecordEventPublisher>.Instance);

        await publisher.PublishAsync(new RecordEventEnvelope(
            EventId: Guid.NewGuid(),
            EventType: RecordEventTypes.Created,
            OccurredAtUtc: DateTimeOffset.UtcNow,
            RecordId: Guid.NewGuid(),
            Key: "TST-1",
            RecordTypeId: Guid.NewGuid(),
            Name: "test record",
            Status: null,
            PreviousStatus: null,
            ChangedFields: Array.Empty<string>(),
            AssigneeIds: Array.Empty<Guid>(),
            IsArchived: false,
            ActorId: actorId,
            SourceAppId: "autonate.web"));

        var captured = Assert.Single(outbox.Captured);
        var json = JsonDocument.Parse(captured.PayloadJson);
        var auditContext = json.RootElement.GetProperty("auditContext");
        Assert.Equal(actorId, auditContext.GetProperty("actorId").GetGuid());
        Assert.Equal("alice", auditContext.GetProperty("actorUserName").GetString());
        Assert.Equal("10.0.0.42", auditContext.GetProperty("ipAddress").GetString());
        Assert.Equal("Mozilla/Test", auditContext.GetProperty("userAgent").GetString());
        Assert.Equal("autonate.web", auditContext.GetProperty("sourceAppId").GetString());
        Assert.Equal("Allowed", auditContext.GetProperty("authOutcome").GetString());
    }

    [Fact]
    public async Task ApplicationEvent_envelope_includes_populated_auditContext()
    {
        var outbox = new CapturingOutbox();
        var actorId = Guid.NewGuid();
        var requestContext = BuildRequestContext(actorId, "bob", "192.168.1.10", "curl/8.0");

        var publisher = new DaprApplicationEventPublisher(
            requestContext,
            outbox,
            NullLogger<DaprApplicationEventPublisher>.Instance);

        await publisher.PublishAsync(new ApplicationEventEnvelope(
            EventId: Guid.NewGuid(),
            EventType: ApplicationEventTypes.PluginUploaded,
            OccurredAtUtc: DateTimeOffset.UtcNow,
            ActorUserId: actorId,
            Payload: new { pluginId = Guid.NewGuid(), name = "x", version = "1.0" },
            SourceAppId: "autonate.web"));

        var captured = Assert.Single(outbox.Captured);
        var json = JsonDocument.Parse(captured.PayloadJson);
        var auditContext = json.RootElement.GetProperty("auditContext");
        Assert.Equal(actorId, auditContext.GetProperty("actorId").GetGuid());
        Assert.Equal("bob", auditContext.GetProperty("actorUserName").GetString());
        Assert.Equal("192.168.1.10", auditContext.GetProperty("ipAddress").GetString());
    }

    [Fact]
    public async Task NotificationEvent_envelope_includes_populated_auditContext()
    {
        var outbox = new CapturingOutbox();
        var actorId = Guid.NewGuid();
        var requestContext = BuildRequestContext(actorId, "carol", "172.16.0.1", "AutoNate/1.0");

        var publisher = new DaprNotificationEventPublisher(
            Options.Create(new DaprOptions { AppId = "autonate.web" }),
            requestContext,
            outbox,
            NullLogger<DaprNotificationEventPublisher>.Instance);

        await publisher.PublishAsync(new Notification
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            Kind = "test",
            Title = "t",
            Body = "b",
            CreatedAtUtc = DateTimeOffset.UtcNow
        });

        var captured = Assert.Single(outbox.Captured);
        var json = JsonDocument.Parse(captured.PayloadJson);
        var auditContext = json.RootElement.GetProperty("auditContext");
        Assert.Equal(actorId, auditContext.GetProperty("actorId").GetGuid());
        Assert.Equal("carol", auditContext.GetProperty("actorUserName").GetString());
        Assert.Equal("172.16.0.1", auditContext.GetProperty("ipAddress").GetString());
    }

    [Fact]
    public void RequestContext_with_no_HttpContext_returns_safe_defaults()
    {
        var accessor = new HttpContextAccessor();
        var requestContext = new RequestContext(accessor);

        Assert.Null(requestContext.ActorId);
        Assert.Null(requestContext.ActorUserName);
        Assert.Equal(string.Empty, requestContext.IpAddress);
        Assert.Equal(string.Empty, requestContext.UserAgent);

        var ctx = requestContext.BuildAuditContext();
        Assert.Null(ctx.ActorId);
        Assert.Equal("autonate.web", ctx.SourceAppId);
        Assert.Equal(AuthOutcome.Allowed, ctx.AuthOutcome);
    }

    [Fact]
    public void RequestContext_ignores_unverified_X_Forwarded_For_header()
    {
        // An attacker with direct access to the listener used to be able
        // to forge the recorded audit IP by setting X-Forwarded-For. The
        // ForwardedHeaders middleware (gated on TrustedProxy.Enabled) is
        // now the only thing that may promote a forwarded value into
        // Connection.RemoteIpAddress. RequestContext itself trusts only
        // the TCP peer, so a spoofed header from a direct client is
        // dropped on the floor.
        var accessor = new HttpContextAccessor();
        var http = new DefaultHttpContext();
        http.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("1.2.3.4");
        http.Request.Headers["X-Forwarded-For"] = "203.0.113.5, 10.0.0.1";
        accessor.HttpContext = http;

        var requestContext = new RequestContext(accessor);
        Assert.Equal("1.2.3.4", requestContext.IpAddress);
    }

    [Fact]
    public void RequestContext_without_remote_ip_returns_empty_even_with_forwarded_header()
    {
        // No TCP peer, only a forwarded header — must not fall back to
        // the header. Empty is the right answer.
        var accessor = new HttpContextAccessor();
        var http = new DefaultHttpContext();
        http.Connection.RemoteIpAddress = null;
        http.Request.Headers["X-Forwarded-For"] = "203.0.113.5";
        accessor.HttpContext = http;

        var requestContext = new RequestContext(accessor);
        Assert.Equal(string.Empty, requestContext.IpAddress);
    }

    [Fact]
    public void RequestContext_uses_remote_ip_when_no_forwarded_header()
    {
        var accessor = new HttpContextAccessor();
        var http = new DefaultHttpContext();
        http.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("198.51.100.7");
        accessor.HttpContext = http;

        var requestContext = new RequestContext(accessor);
        Assert.Equal("198.51.100.7", requestContext.IpAddress);
    }

    private static IRequestContext BuildRequestContext(
        Guid actorId,
        string username,
        string ipAddress,
        string userAgent)
    {
        var accessor = new HttpContextAccessor();
        var http = new DefaultHttpContext();
        http.User = new ClaimsPrincipal(new ClaimsIdentity(
            new[]
            {
                new Claim(ClaimTypes.NameIdentifier, actorId.ToString()),
                new Claim(ClaimTypes.Name, username)
            },
            CookieAuthenticationDefaults.AuthenticationScheme));
        http.Request.Headers["User-Agent"] = userAgent;
        http.Connection.RemoteIpAddress = System.Net.IPAddress.Parse(ipAddress);
        http.SetEndpoint(new RouteEndpoint(
            requestDelegate: _ => Task.CompletedTask,
            routePattern: RoutePatternFactory.Parse("/api/test/{id}"),
            order: 0,
            metadata: null,
            displayName: "test"));
        accessor.HttpContext = http;
        return new RequestContext(accessor);
    }

    private sealed class CapturingOutbox : IAuditEventOutbox
    {
        public List<(string Topic, string EventType, string PayloadJson)> Captured { get; } = new();

        public Task EnqueueAsync(string topic, string eventType, string payloadJson, CancellationToken ct = default)
        {
            Captured.Add((topic, eventType, payloadJson));
            return Task.CompletedTask;
        }
    }
}
