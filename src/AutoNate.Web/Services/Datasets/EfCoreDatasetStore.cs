using AutoNate.Web.Persistence;
using AutoNate.Web.Persistence.Scaffolded;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AutoNate.Web.Services.Datasets;

public sealed class EfCoreDatasetStore(
    IDbContextFactory<AutoNateDbContext> dbContextFactory) : IDatasetStore
{
    private const string PgUniqueViolation = "23505";

    public async Task<IReadOnlyList<Dataset>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
#pragma warning disable CA1304, CA1311
        return await db.Datasets.AsNoTracking()
            .OrderBy(d => d.Name.ToLower())
            .ToListAsync(cancellationToken);
#pragma warning restore CA1304, CA1311
    }

    public async Task<Dataset?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Datasets.AsNoTracking()
            .SingleOrDefaultAsync(d => d.Id == id, cancellationToken);
    }

    public async Task<Dataset?> GetByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        // Npgsql translates ToLower() to LOWER() server-side, lining up with
        // uq_datasets_name(LOWER(name)) so case-insensitive lookup is an
        // index hit rather than a scan.
#pragma warning disable CA1304, CA1311
        var lowered = name.Trim().ToLower();
        return await db.Datasets.AsNoTracking()
            .SingleOrDefaultAsync(d => d.Name.ToLower() == lowered, cancellationToken);
#pragma warning restore CA1304, CA1311
    }

    public async Task<Dataset> CreateAsync(
        CreateDatasetInput input, Guid actorId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var name = (input.Name ?? string.Empty).Trim();
        if (name.Length == 0)
            throw new ArgumentException("Name is required.", nameof(input));
        var sourceKind = (input.SourceKind ?? string.Empty).Trim();
        if (sourceKind.Length == 0)
            throw new ArgumentException("Source kind is required.", nameof(input));

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var now = DateTime.UtcNow;
        var entity = new Dataset
        {
            Id = Guid.NewGuid(),
            Name = name,
            Description = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim(),
            Mode = (short)input.Mode,
            ColumnSchemaJson = DatasetSchemaCodec.Encode(input.Columns ?? Array.Empty<DatasetColumn>()),
            RefreshCron = string.IsNullOrWhiteSpace(input.RefreshCron) ? null : input.RefreshCron.Trim(),
            SourceKind = sourceKind,
            SourceId = input.SourceId,
            SourceTableName = string.IsNullOrWhiteSpace(input.SourceTableName) ? null : input.SourceTableName.Trim(),
            OwnerUserId = actorId,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            CreatedBy = actorId,
            UpdatedBy = actorId
        };
        db.Datasets.Add(entity);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException pg && pg.SqlState == PgUniqueViolation)
        {
            throw new DatasetNameConflictException(name);
        }
        return entity;
    }

    public async Task<Dataset> UpdateAsync(
        Guid id, UpdateDatasetInput input, Guid actorId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.Datasets.SingleOrDefaultAsync(d => d.Id == id, cancellationToken)
            ?? throw new DatasetNotFoundException(id);

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
        if (input.RefreshCron is not null)
        {
            var newCron = string.IsNullOrWhiteSpace(input.RefreshCron) ? null : input.RefreshCron.Trim();
            if (!string.Equals(entity.RefreshCron, newCron, StringComparison.Ordinal))
            {
                entity.RefreshCron = newCron;
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
            throw new DatasetNameConflictException(entity.Name);
        }
        return entity;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.Datasets.SingleOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (entity is null) return false;
        db.Datasets.Remove(entity);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task MarkRefreshedAsync(
        Guid id, DateTime utcNow, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.Datasets.SingleOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (entity is null) return;
        entity.LastRefreshedAtUtc = utcNow;
        entity.UpdatedAtUtc = utcNow;
        await db.SaveChangesAsync(cancellationToken);
    }
}
