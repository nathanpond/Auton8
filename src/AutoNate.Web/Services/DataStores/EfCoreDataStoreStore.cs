using AutoNate.Web.Persistence;
using AutoNate.Web.Persistence.Scaffolded;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AutoNate.Web.Services.DataStores;

public sealed class EfCoreDataStoreStore(
    IDbContextFactory<AutoNateDbContext> dbContextFactory) : IDataStoreStore
{
    private const string PgUniqueViolation = "23505";

    public async Task<IReadOnlyList<DataStore>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
#pragma warning disable CA1304, CA1311
        return await db.DataStores.AsNoTracking()
            .OrderBy(d => d.Name.ToLower())
            .ToListAsync(cancellationToken);
#pragma warning restore CA1304, CA1311
    }

    public async Task<DataStore?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.DataStores.AsNoTracking()
            .SingleOrDefaultAsync(d => d.Id == id, cancellationToken);
    }

    public async Task<DataStore> CreateAsync(
        CreateDataStoreInput input, Guid actorId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var name = (input.Name ?? string.Empty).Trim();
        if (name.Length == 0)
            throw new ArgumentException("Name is required.", nameof(input));

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var now = DateTime.UtcNow;
        var entity = new DataStore
        {
            Id = Guid.NewGuid(),
            Name = name,
            Description = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim(),
            Kind = (short)input.Kind,
            OwnerUserId = actorId,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            CreatedBy = actorId,
            UpdatedBy = actorId
        };
        db.DataStores.Add(entity);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException pg && pg.SqlState == PgUniqueViolation)
        {
            throw new DataStoreNameConflictException(name);
        }
        return entity;
    }

    public async Task<DataStore> UpdateAsync(
        Guid id, UpdateDataStoreInput input, Guid actorId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.DataStores
            .SingleOrDefaultAsync(d => d.Id == id, cancellationToken)
            ?? throw new DataStoreNotFoundException(id);

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
        if (!changed) return entity;

        entity.UpdatedAtUtc = DateTime.UtcNow;
        entity.UpdatedBy = actorId;
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException pg && pg.SqlState == PgUniqueViolation)
        {
            throw new DataStoreNameConflictException(entity.Name);
        }
        return entity;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.DataStores
            .SingleOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (entity is null) return false;
        db.DataStores.Remove(entity);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
