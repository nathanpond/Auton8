using AutoNate.Web.Persistence;
using AutoNate.Web.Persistence.Scaffolded;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AutoNate.Web.Services.DataConnectors;

public sealed class EfCoreDataConnectorStore(
    IDbContextFactory<AutoNateDbContext> dbContextFactory) : IDataConnectorStore
{
    private const string PgUniqueViolation = "23505";

    public async Task<IReadOnlyList<DataConnector>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
#pragma warning disable CA1304, CA1311
        return await db.DataConnectors.AsNoTracking()
            .OrderBy(d => d.Name.ToLower())
            .ToListAsync(cancellationToken);
#pragma warning restore CA1304, CA1311
    }

    public async Task<DataConnector?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.DataConnectors.AsNoTracking()
            .SingleOrDefaultAsync(d => d.Id == id, cancellationToken);
    }

    public async Task<DataConnector> CreateAsync(
        CreateDataConnectorInput input, Guid actorId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var name = (input.Name ?? string.Empty).Trim();
        if (name.Length == 0)
            throw new ArgumentException("Name is required.", nameof(input));
        var kind = (input.Kind ?? string.Empty).Trim();
        if (kind.Length == 0)
            throw new ArgumentException("Kind is required.", nameof(input));

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var now = DateTime.UtcNow;
        var entity = new DataConnector
        {
            Id = Guid.NewGuid(),
            Name = name,
            Description = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim(),
            Kind = kind,
            ConfigJson = string.IsNullOrWhiteSpace(input.ConfigJson) ? "{}" : input.ConfigJson,
            OwnerUserId = actorId,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            CreatedBy = actorId,
            UpdatedBy = actorId
        };
        db.DataConnectors.Add(entity);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException pg && pg.SqlState == PgUniqueViolation)
        {
            throw new DataConnectorNameConflictException(name);
        }
        return entity;
    }

    public async Task<DataConnector> UpdateAsync(
        Guid id, UpdateDataConnectorInput input, Guid actorId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.DataConnectors
            .SingleOrDefaultAsync(d => d.Id == id, cancellationToken)
            ?? throw new DataConnectorNotFoundException(id);

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
        if (input.ConfigJson is not null && !string.Equals(entity.ConfigJson, input.ConfigJson, StringComparison.Ordinal))
        {
            entity.ConfigJson = input.ConfigJson;
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
            throw new DataConnectorNameConflictException(entity.Name);
        }
        return entity;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.DataConnectors
            .SingleOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (entity is null) return false;
        db.DataConnectors.Remove(entity);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
