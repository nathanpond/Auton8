using System.Security.Claims;
using AutoNate.Web.Models.Notifications;
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
            CancellationToken ct) =>
        {
            var userId = GetUserId(http);
            if (userId == Guid.Empty) return Results.Unauthorized();

            var effectiveLimit = limit is > 0 ? limit : null;
            var notifications = await store.ListForUserAsync(userId, effectiveLimit, ct);
            var unreadCount = await store.GetUnreadCountAsync(userId, ct);
            return Results.Ok(new NotificationListResponse(
                notifications.Select(ToDto).ToArray(),
                unreadCount));
        });

        group.MapGet("/unread-count", async (
            HttpContext http,
            INotificationStore store,
            CancellationToken ct) =>
        {
            var userId = GetUserId(http);
            if (userId == Guid.Empty) return Results.Unauthorized();
            var count = await store.GetUnreadCountAsync(userId, ct);
            return Results.Ok(new UnreadCountResponse(count));
        });

        group.MapPost("/{id:guid}/read", async (
            Guid id,
            HttpContext http,
            INotificationStore store,
            CancellationToken ct) =>
        {
            var userId = GetUserId(http);
            if (userId == Guid.Empty) return Results.Unauthorized();
            var updated = await store.MarkReadAsync(id, userId, ct);
            return updated is null ? Results.NotFound() : Results.Ok(ToDto(updated));
        }).DisableAntiforgery();

        group.MapPost("/mark-all-read", async (
            HttpContext http,
            INotificationStore store,
            CancellationToken ct) =>
        {
            var userId = GetUserId(http);
            if (userId == Guid.Empty) return Results.Unauthorized();
            var count = await store.MarkAllReadAsync(userId, ct);
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
