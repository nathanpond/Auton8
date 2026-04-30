using System.Security.Claims;
using AutoNate.Web.Models.Notifications;
using AutoNate.Web.Services.Events;
using AutoNate.Web.Services.Notifications;

namespace AutoNate.Web.Endpoints;

public sealed record NotificationDto(
    Guid Id,
    Guid UserId,
    string Kind,
    string Title,
    string Body,
    string? RelatedEntityKind,
    string? RelatedEntityId,
    string? LinkPath,
    bool IsRead,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ReadAtUtc);

public sealed record NotificationListResponse(
    NotificationDto[] Items,
    int UnreadCount);

public sealed record UnreadCountResponse(int UnreadCount);

public sealed record MarkAllReadResponse(int Updated);

public static class NotificationEndpoints
{
    public static IEndpointRouteBuilder MapNotificationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/notifications").RequireAuthorization();

        // Recent for dropdown (default 10) and full feed for /notifications page
        // both go through here; the SPA passes ?limit=10 or omits it.
        group.MapGet("/", async (
            int? limit,
            HttpContext http,
            INotificationStore store,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            var userId = GetUserId(http);
            if (userId == Guid.Empty) return Results.Unauthorized();

            var effectiveLimit = limit is > 0 ? limit : null;
            var notifications = await store.ListForUserAsync(userId, effectiveLimit, ct);
            var unreadCount = await store.GetUnreadCountAsync(userId, ct);
            await auditPublisher.PublishAsync(
                DaprNotificationEventPublisher.TopicName,
                NotificationEventTypes.ListViewed,
                NotificationResourceKinds.NotificationCollection,
                resource: new { userId },
                details: new { resultCount = notifications.Count, unreadCount, limit = effectiveLimit },
                ct);
            return Results.Ok(new NotificationListResponse(
                notifications.Select(ToDto).ToArray(),
                unreadCount));
        });

        // Polled every few seconds by the SPA's bell icon — coalesce per user
        // to a 60s window so the audit firehose isn't dominated by polls.
        group.MapGet("/unread-count", async (
            HttpContext http,
            INotificationStore store,
            IAuditEventPublisher auditPublisher,
            ViewEventCoalescer coalescer,
            CancellationToken ct) =>
        {
            var userId = GetUserId(http);
            if (userId == Guid.Empty) return Results.Unauthorized();
            var count = await store.GetUnreadCountAsync(userId, ct);
            if (coalescer.ShouldPublish(userId, NotificationEventTypes.UnreadCountViewed))
            {
                await auditPublisher.PublishAsync(
                    DaprNotificationEventPublisher.TopicName,
                    NotificationEventTypes.UnreadCountViewed,
                    NotificationResourceKinds.NotificationCollection,
                    resource: new { userId },
                    details: new { unreadCount = count, coalesceWindowSeconds = 60 },
                    ct);
            }
            return Results.Ok(new UnreadCountResponse(count));
        });

        group.MapPost("/{id:guid}/read", async (
            Guid id,
            HttpContext http,
            INotificationStore store,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            var userId = GetUserId(http);
            if (userId == Guid.Empty) return Results.Unauthorized();
            var updated = await store.MarkReadAsync(id, userId, ct);
            if (updated is null) return Results.NotFound();
            await auditPublisher.PublishAsync(
                DaprNotificationEventPublisher.TopicName,
                NotificationEventTypes.Read,
                NotificationResourceKinds.Notification,
                resource: new { id, userId },
                details: null,
                ct);
            return Results.Ok(ToDto(updated));
        }).DisableAntiforgery();

        group.MapPost("/mark-all-read", async (
            HttpContext http,
            INotificationStore store,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            var userId = GetUserId(http);
            if (userId == Guid.Empty) return Results.Unauthorized();
            var count = await store.MarkAllReadAsync(userId, ct);
            await auditPublisher.PublishAsync(
                DaprNotificationEventPublisher.TopicName,
                NotificationEventTypes.AllRead,
                NotificationResourceKinds.NotificationCollection,
                resource: new { userId },
                details: new { updatedCount = count },
                ct);
            return Results.Ok(new MarkAllReadResponse(count));
        }).DisableAntiforgery();

        return app;
    }

    private static Guid GetUserId(HttpContext http)
    {
        var claim = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(claim, out var id) ? id : Guid.Empty;
    }

    private static NotificationDto ToDto(Notification model) => new(
        model.Id,
        model.UserId,
        model.Kind,
        model.Title,
        model.Body,
        model.RelatedEntityKind,
        model.RelatedEntityId,
        model.LinkPath,
        model.IsRead,
        model.CreatedAtUtc,
        model.ReadAtUtc);
}
