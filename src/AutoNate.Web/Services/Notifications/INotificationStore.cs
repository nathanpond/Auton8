using AutoNate.Web.Models.Notifications;

namespace AutoNate.Web.Services.Notifications;

public sealed record class CreateNotificationInput(
    Guid UserId,
    string Kind,
    string Title,
    string Body,
    string? RelatedEntityKind,
    string? RelatedEntityId,
    string? LinkPath,
    string? ParentEntityKind = null,
    string? ParentEntityId = null);

public interface INotificationStore
{
    Task<Notification> CreateAsync(CreateNotificationInput input, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Notification>> ListForUserAsync(
        Guid userId,
        int? limit,
        CancellationToken cancellationToken = default);

    Task<NotificationPage> ListPagedForUserAsync(
        Guid userId,
        ListNotificationsRequest request,
        CancellationToken cancellationToken = default);

    Task<int> GetUnreadCountAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<Notification?> MarkReadAsync(Guid notificationId, Guid userId, CancellationToken cancellationToken = default);

    Task<int> MarkAllReadAsync(Guid userId, CancellationToken cancellationToken = default);

    // Deletes notifications matching the (relatedEntityKind, relatedEntityId)
    // tuple. When userId is provided, scoped to that user; when null, deletes
    // across all users (used when we don't know who was notified — e.g. a
    // workflow task completion arriving from the bus). Returns the deleted
    // rows so callers can publish per-row notification.removed events.
    Task<IReadOnlyList<Notification>> DeleteByRelatedEntityAsync(
        Guid? userId,
        string relatedEntityKind,
        string relatedEntityId,
        CancellationToken cancellationToken = default);

    // Deletes notifications attached to a parent (e.g. every workflow_task
    // notification belonging to a workflow_execution). Used when the parent
    // is closed out and any in-flight inbox entries below it become stale.
    Task<IReadOnlyList<Notification>> DeleteByParentEntityAsync(
        string parentEntityKind,
        string parentEntityId,
        CancellationToken cancellationToken = default);
}

public sealed record ListNotificationsRequest(
    int Page = 0,
    int PageSize = 25,
    string? Search = null,
    string? SortBy = null,
    string? SortDir = null,
    bool UnreadOnly = false);

public sealed record NotificationPage(IReadOnlyList<Notification> Items, int TotalCount, int UnreadCount);
