using AutoNate.Web.Persistence;
using AutoNate.Web.Persistence.Scaffolded;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AutoNate.Web.Services.Pipelines;

public sealed class EfCorePipelineStore(
    IDbContextFactory<AutoNateDbContext> dbContextFactory) : IPipelineStore
{
    private const string PgUniqueViolation = "23505";

    public async Task<IReadOnlyList<Pipeline>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
#pragma warning disable CA1304, CA1311
        return await db.Pipelines.AsNoTracking()
            .OrderBy(p => p.Name.ToLower())
            .ToListAsync(cancellationToken);
#pragma warning restore CA1304, CA1311
    }

    public async Task<Pipeline?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Pipelines.AsNoTracking()
            .SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
    }

    public async Task<Pipeline> CreateAsync(
        CreatePipelineInput input, Guid actorId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var name = (input.Name ?? string.Empty).Trim();
        if (name.Length == 0)
            throw new ArgumentException("Name is required.", nameof(input));

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var now = DateTime.UtcNow;
        var entity = new Pipeline
        {
            Id = Guid.NewGuid(),
            Name = name,
            Description = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim(),
            GraphJson = (input.Graph ?? PipelineGraph.Empty).ToJson(),
            ScheduleCron = string.IsNullOrWhiteSpace(input.ScheduleCron) ? null : input.ScheduleCron.Trim(),
            OwnerUserId = actorId,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            CreatedBy = actorId,
            UpdatedBy = actorId,
        };
        db.Pipelines.Add(entity);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException pg && pg.SqlState == PgUniqueViolation)
        {
            throw new PipelineNameConflictException(name);
        }
        return entity;
    }

    public async Task<Pipeline> UpdateAsync(
        Guid id, UpdatePipelineInput input, Guid actorId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.Pipelines.SingleOrDefaultAsync(p => p.Id == id, cancellationToken)
            ?? throw new PipelineNotFoundException(id);

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
        if (input.Graph is not null)
        {
            entity.GraphJson = input.Graph.ToJson();
            changed = true;
        }
        if (input.ScheduleCron is not null)
        {
            var newCron = string.IsNullOrWhiteSpace(input.ScheduleCron) ? null : input.ScheduleCron.Trim();
            if (!string.Equals(entity.ScheduleCron, newCron, StringComparison.Ordinal))
            {
                entity.ScheduleCron = newCron;
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
            throw new PipelineNameConflictException(entity.Name);
        }
        return entity;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.Pipelines.SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (entity is null) return false;
        db.Pipelines.Remove(entity);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task MarkRunCompletedAsync(Guid id, DateTime utcNow, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.Pipelines.SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (entity is null) return;
        entity.LastRunAtUtc = utcNow;
        await db.SaveChangesAsync(cancellationToken);
    }
}
