using AutoNate.Web.Persistence;
using AutoNate.Web.Persistence.Scaffolded;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AutoNate.Web.Services.Transformers.Code;

public sealed class EfCoreCodeTransformerStore(
    IDbContextFactory<AutoNateDbContext> dbContextFactory) : ICodeTransformerStore
{
    private const string PgUniqueViolation = "23505";

    public async Task<IReadOnlyList<CodeTransformer>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
#pragma warning disable CA1304, CA1311
        return await db.CodeTransformers.AsNoTracking()
            .OrderBy(c => c.Name.ToLower())
            .ToListAsync(cancellationToken);
#pragma warning restore CA1304, CA1311
    }

    public async Task<CodeTransformer?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.CodeTransformers.AsNoTracking()
            .SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
    }

    public async Task<CodeTransformer?> GetByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
#pragma warning disable CA1304, CA1311
        var lowered = name.Trim().ToLower();
        return await db.CodeTransformers.AsNoTracking()
            .SingleOrDefaultAsync(c => c.Name.ToLower() == lowered, cancellationToken);
#pragma warning restore CA1304, CA1311
    }

    public async Task<CodeTransformer> CreateAsync(
        CreateCodeTransformerInput input, Guid actorId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var name = (input.Name ?? string.Empty).Trim();
        if (name.Length == 0) throw new ArgumentException("Name is required.", nameof(input));
        ValidateKind(input.Kind);
        ValidateLanguage(input.Language);

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var now = DateTime.UtcNow;
        var entity = new CodeTransformer
        {
            Id = Guid.NewGuid(),
            Name = name,
            Description = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim(),
            Kind = input.Kind,
            Language = input.Language,
            Code = input.Code ?? string.Empty,
            IsUnsafe = input.IsUnsafe,
            OwnerUserId = actorId,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            CreatedBy = actorId,
            UpdatedBy = actorId,
        };
        db.CodeTransformers.Add(entity);
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException pg && pg.SqlState == PgUniqueViolation)
        {
            throw new CodeTransformerNameConflictException(name);
        }
        return entity;
    }

    public async Task<CodeTransformer> UpdateAsync(
        Guid id, UpdateCodeTransformerInput input, Guid actorId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.CodeTransformers.SingleOrDefaultAsync(c => c.Id == id, cancellationToken)
            ?? throw new CodeTransformerNotFoundException(id);
        var changed = false;
        if (input.Name is not null)
        {
            var newName = input.Name.Trim();
            if (newName.Length == 0) throw new ArgumentException("Name cannot be empty.", nameof(input));
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
        if (input.Code is not null && !string.Equals(entity.Code, input.Code, StringComparison.Ordinal))
        {
            entity.Code = input.Code;
            changed = true;
        }
        if (input.IsUnsafe is { } unsafe_ && entity.IsUnsafe != unsafe_)
        {
            entity.IsUnsafe = unsafe_;
            changed = true;
        }
        if (!changed) return entity;
        entity.UpdatedAtUtc = DateTime.UtcNow;
        entity.UpdatedBy = actorId;
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException pg && pg.SqlState == PgUniqueViolation)
        {
            throw new CodeTransformerNameConflictException(entity.Name);
        }
        return entity;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.CodeTransformers.SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (entity is null) return false;
        db.CodeTransformers.Remove(entity);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static void ValidateKind(string kind)
    {
        if (kind != CodeTransformerKinds.Transformer && kind != CodeTransformerKinds.Analyzer)
            throw new ArgumentException(
                $"Code transformer kind must be '{CodeTransformerKinds.Transformer}' or '{CodeTransformerKinds.Analyzer}'.");
    }

    private static void ValidateLanguage(string language)
    {
        if (language != CodeTransformerLanguages.JavaScript && language != CodeTransformerLanguages.Python)
            throw new ArgumentException(
                $"Code transformer language must be '{CodeTransformerLanguages.JavaScript}' or '{CodeTransformerLanguages.Python}'.");
    }
}
