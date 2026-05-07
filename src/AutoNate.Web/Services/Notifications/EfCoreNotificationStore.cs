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
            ParentEntityKind = input.ParentEntityKind,
            ParentEntityId = input.ParentEntityId,
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

    public async Task<NotificationPage> ListPagedForUserAsync(
        Guid userId,
        ListNotificationsRequest request,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var baseQuery = dbContext.Notifications.AsNoTracking().Where(n => n.UserId == userId);
        // Unread count over the user's entire inbox (independent of search/
        // unreadOnly filter), so the bell-badge stays accurate.
        var unreadCount = await baseQuery.CountAsync(n => !n.IsRead, cancellationToken);

        var query = baseQuery;
        if (request.UnreadOnly)
        {
            query = query.Where(n => !n.IsRead);
        }
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var pattern = $"%{request.Search.Trim()}%";
            query = query.Where(n =>
                EF.Functions.ILike(n.Title, pattern) ||
                EF.Functions.ILike(n.Body, pattern) ||
                EF.Functions.ILike(n.Kind, pattern));
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var desc = string.Equals(request.SortDir, "desc", StringComparison.OrdinalIgnoreCase);
        IQueryable<NotificationEntity> ordered = (request.SortBy, desc) switch
        {
            ("title", true) => query.OrderByDescending(n => n.Title).ThenByDescending(n => n.Id),
            ("title", false) => query.OrderBy(n => n.Title).ThenBy(n => n.Id),
            ("kind", true) => query.OrderByDescending(n => n.Kind).ThenByDescending(n => n.Id),
            ("kind", false) => query.OrderBy(n => n.Kind).ThenBy(n => n.Id),
            ("createdAtUtc", false) => query.OrderBy(n => n.CreatedAtUtc).ThenBy(n => n.Id),
            _ => query.OrderByDescending(n => n.CreatedAtUtc).ThenByDescending(n => n.Id)
        };

        IQueryable<NotificationEntity> paged = ordered;
        if (request.PageSize > 0)
        {
            paged = ordered.Skip(Math.Max(0, request.Page) * request.PageSize).Take(request.PageSize);
        }
        else
        {
            paged = ordered.Take(0);
        }

        var rows = await paged.ToListAsync(cancellationToken);
        return new NotificationPage(rows.Select(ToModel).ToList(), totalCount, unreadCount);
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

    public async Task<IReadOnlyList<Notification>> DeleteByRelatedEntityAsync(
        Guid? userId,
        string relatedEntityKind,
        string relatedEntityId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relatedEntityKind);
        ArgumentException.ThrowIfNullOrWhiteSpace(relatedEntityId);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var query = dbContext.Notifications
            .Where(n => n.RelatedEntityKind == relatedEntityKind
                        && n.RelatedEntityId == relatedEntityId);
        if (userId.HasValue)
        {
            var uid = userId.Value;
            query = query.Where(n => n.UserId == uid);
        }

        var rows = await query.ToListAsync(cancellationToken);
        if (rows.Count == 0)
        {
            return Array.Empty<Notification>();
        }

        dbContext.Notifications.RemoveRange(rows);
        await dbContext.SaveChangesAsync(cancellationToken);

        var models = rows.Select(ToModel).ToList();
        foreach (var model in models)
        {
            try
            {
                await eventPublisher.PublishRemovedAsync(model, cancellationToken);
            }
            catch (Exception ex)
            {
                // Persisted delete is the source of truth; fan-out is best-effort.
                logger.LogWarning(ex,
                    "Failed to publish notification.removed event for {NotificationId}.", model.Id);
            }
        }

        return models;
    }

    public async Task<IReadOnlyList<Notification>> DeleteByParentEntityAsync(
        string parentEntityKind,
        string parentEntityId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parentEntityKind);
        ArgumentException.ThrowIfNullOrWhiteSpace(parentEntityId);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var rows = await dbContext.Notifications
            .Where(n => n.ParentEntityKind == parentEntityKind
                        && n.ParentEntityId == parentEntityId)
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
        {
            return Array.Empty<Notification>();
        }

        dbContext.Notifications.RemoveRange(rows);
        await dbContext.SaveChangesAsync(cancellationToken);

        var models = rows.Select(ToModel).ToList();
        foreach (var model in models)
        {
            try
            {
                await eventPublisher.PublishRemovedAsync(model, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Failed to publish notification.removed event for {NotificationId}.", model.Id);
            }
        }

        return models;
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
        ParentEntityKind = entity.ParentEntityKind,
        ParentEntityId = entity.ParentEntityId,
        LinkPath = entity.LinkPath,
        IsRead = entity.IsRead,
        CreatedAtUtc = DateTime.SpecifyKind(entity.CreatedAtUtc, DateTimeKind.Utc),
        ReadAtUtc = entity.ReadAtUtc.HasValue
            ? DateTime.SpecifyKind(entity.ReadAtUtc.Value, DateTimeKind.Utc)
            : null
    };
}
