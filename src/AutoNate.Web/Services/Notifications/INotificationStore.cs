using AutoNate.Web.Models.Notifications;

namespace AutoNate.Web.Services.Notifications;

public sealed record class CreateNotificationInput(
    Guid UserId,
    string Kind,
    string Title,
    string Body,
    string? RelatedEntityKind,
    string? RelatedEntityId,
    string? LinkPath);

public interface INotificationStore
{
    Task<Notification> CreateAsync(CreateNotificationInput input, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Notification>> ListForUserAsync(
        Guid userId,
        int? limit,
        CancellationToken cancellationToken = default);

    Task<int> GetUnreadCountAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<Notification?> MarkReadAsync(Guid notificationId, Guid userId, CancellationToken cancellationToken = default);

    Task<int> MarkAllReadAsync(Guid userId, CancellationToken cancellationToken = default);
}
