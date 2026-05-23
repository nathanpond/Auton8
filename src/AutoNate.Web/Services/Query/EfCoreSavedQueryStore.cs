using AutoNate.Web.Persistence;
using AutoNate.Web.Persistence.Scaffolded;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AutoNate.Web.Services.Query;

public sealed class EfCoreSavedQueryStore(
    IDbContextFactory<AutoNateDbContext> dbContextFactory) : ISavedQueryStore
{
    // Postgres unique_violation. We catch on the EF SaveChanges path so the
    // owner-uniqueness collision surfaces as a clean domain exception that
    // the endpoint maps to 409.
    private const string PgUniqueViolation = "23505";

    public async Task<IReadOnlyList<SavedQuery>> ListForActorAsync(
        Guid actorId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.SavedQueries.AsNoTracking()
            .Where(q => q.OwnerUserId == actorId || q.IsShared)
            .OrderBy(q => q.Name.ToLower())
            .ToListAsync(cancellationToken);
    }

    public async Task<SavedQuery?> GetForActorAsync(
        Guid id, Guid actorId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.SavedQueries.AsNoTracking()
            .SingleOrDefaultAsync(
                q => q.Id == id && (q.OwnerUserId == actorId || q.IsShared),
                cancellationToken);
    }

    public async Task<SavedQuery> CreateAsync(
        CreateSavedQueryInput input, Guid actorId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var name = (input.Name ?? string.Empty).Trim();
        if (name.Length == 0)
            throw new ArgumentException("Name is required.", nameof(input));
        var queryText = (input.QueryText ?? string.Empty).Trim();
        if (queryText.Length == 0)
            throw new ArgumentException("Query text is required.", nameof(input));

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var now = DateTime.UtcNow;
        var entity = new SavedQuery
        {
            Id = Guid.NewGuid(),
            Name = name,
            Description = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim(),
            QueryText = queryText,
            IsShared = input.IsShared,
            OwnerUserId = actorId,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            CreatedBy = actorId,
            UpdatedBy = actorId
        };
        db.SavedQueries.Add(entity);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException pg && pg.SqlState == PgUniqueViolation)
        {
            throw new SavedQueryNameConflictException(name);
        }
        return entity;
    }

    public async Task<SavedQuery> UpdateAsync(
        Guid id, UpdateSavedQueryInput input, Guid actorId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.SavedQueries
            .SingleOrDefaultAsync(q => q.Id == id, cancellationToken);
        // Owner-only: hide non-owner attempts behind the same NotFound that
        // missing rows return, so the existence of someone else's owner-only
        // saved query doesn't leak.
        if (entity is null || entity.OwnerUserId != actorId)
        {
            throw new SavedQueryNotFoundException(id);
        }

        var changed = false;
        if (input.Name is not null)
        {
            var newName = input.Name.Trim();
            if (newName.Length == 0)
                throw new ArgumentException("Name cannot be empty.", nameof(input));
            if (!string.Equals(entity.Name, newName, StringComparison.Ordinal))
            {
                entity.Name = newName;
                changed = true;
            }
        }
        if (input.Description is not null)
        {
            var newDesc = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim();
            if (!string.Equals(entity.Description, newDesc, StringComparison.Ordinal))
            {
                entity.Description = newDesc;
                changed = true;
            }
        }
        if (input.QueryText is not null)
        {
            var newText = input.QueryText.Trim();
            if (newText.Length == 0)
                throw new ArgumentException("Query text cannot be empty.", nameof(input));
            if (!string.Equals(entity.QueryText, newText, StringComparison.Ordinal))
            {
                entity.QueryText = newText;
                changed = true;
            }
        }
        if (input.IsShared is { } shared && entity.IsShared != shared)
        {
            entity.IsShared = shared;
            changed = true;
        }

        if (!changed) return entity;

        entity.UpdatedAtUtc = DateTime.UtcNow;
        entity.UpdatedBy = actorId;
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException pg && pg.SqlState == PgUniqueViolation)
        {
            throw new SavedQueryNameConflictException(entity.Name);
        }
        return entity;
    }

    public async Task<bool> DeleteAsync(
        Guid id, Guid actorId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.SavedQueries
            .SingleOrDefaultAsync(q => q.Id == id, cancellationToken);
        if (entity is null || entity.OwnerUserId != actorId)
        {
            // Same shape as Update: non-owners get a "doesn't exist" so the
            // owner-only row stays invisible.
            return false;
        }
        db.SavedQueries.Remove(entity);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
