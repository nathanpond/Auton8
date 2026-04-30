using AutoNate.Web.Models.Notifications;
using AutoNate.Web.Persistence;
using Microsoft.EntityFrameworkCore;
using NotificationEntity = AutoNate.Web.Persistence.Scaffolded.Notification;

namespace AutoNate.Web.Services.Notifications;

public sealed class EfCoreNotificationStore(
    IDbContextFactory<AutoNateDbContext> dbContextFactory,
    INotificationEventPublisher eventPublisher,
    ILogger<EfCoreNotificationStore> logger) : INotificationStore
{
    public async Task<Notification> CreateAsync(
        CreateNotificationInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var entity = new NotificationEntity
        {
            Id = Guid.NewGuid(),
            UserId = input.UserId,
            Kind = input.Kind,
            Title = input.Title,
            Body = input.Body,
            RelatedEntityKind = input.RelatedEntityKind,
            RelatedEntityId = input.RelatedEntityId,
            LinkPath = input.LinkPath,
            IsRead = false,
            CreatedAtUtc = now.UtcDateTime
        };
        dbContext.Notifications.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);

        var model = ToModel(entity);

        try
        {
            await eventPublisher.PublishAsync(model, cancellationToken);
        }
        catch (Exception ex)
        {
            // Notification persistence is the source of truth; fan-out push is
            // best-effort. Bell will pick the row up on next refresh.
            logger.LogWarning(ex,
                "Failed to publish notification.created event for {NotificationId}.", entity.Id);
        }

        return model;
    }

    public async Task<IReadOnlyList<Notification>> ListForUserAsync(
        Guid userId,
        int? limit,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var query = dbContext.Notifications.AsNoTracking()
            .Where(n => n.UserId == userId)
            .OrderByDescending(n => n.CreatedAtUtc);
        var rows = limit.HasValue
            ? await query.Take(limit.Value).ToListAsync(cancellationToken)
            : await query.ToListAsync(cancellationToken);
        return rows.Select(ToModel).ToList();
    }

    public async Task<int> GetUnreadCountAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await dbContext.Notifications.AsNoTracking()
            .CountAsync(n => n.UserId == userId && !n.IsRead, cancellationToken);
    }

    public async Task<Notification?> MarkReadAsync(
        Guid notificationId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await dbContext.Notifications
            .SingleOrDefaultAsync(n => n.Id == notificationId && n.UserId == userId, cancellationToken);
        if (entity is null)
        {
            return null;
        }
        if (!entity.IsRead)
        {
            entity.IsRead = true;
            entity.ReadAtUtc = DateTimeOffset.UtcNow.UtcDateTime;
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        return ToModel(entity);
    }

    public async Task<int> MarkAllReadAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow.UtcDateTime;
        var rows = await dbContext.Notifications
            .Where(n => n.UserId == userId && !n.IsRead)
            .ToListAsync(cancellationToken);
        foreach (var row in rows)
        {
            row.IsRead = true;
            row.ReadAtUtc = now;
        }
        if (rows.Count > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        return rows.Count;
    }

    private static Notification ToModel(NotificationEntity entity) => new()
    {
        Id = entity.Id,
        UserId = entity.UserId,
        Kind = entity.Kind,
        Title = entity.Title,
        Body = entity.Body,
        RelatedEntityKind = entity.RelatedEntityKind,
        RelatedEntityId = entity.RelatedEntityId,
        LinkPath = entity.LinkPath,
        IsRead = entity.IsRead,
        CreatedAtUtc = DateTime.SpecifyKind(entity.CreatedAtUtc, DateTimeKind.Utc),
        ReadAtUtc = entity.ReadAtUtc.HasValue
            ? DateTime.SpecifyKind(entity.ReadAtUtc.Value, DateTimeKind.Utc)
            : null
    };
}
