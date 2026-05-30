using System.Security.Cryptography;
using AutoNate.Web.Persistence;
using AutoNate.Web.Persistence.Scaffolded;
using Microsoft.EntityFrameworkCore;

namespace AutoNate.Web.Services.Query;

public sealed class EfCoreSavedQueryShareTokenStore(
    IDbContextFactory<AutoNateDbContext> dbContextFactory) : ISavedQueryShareTokenStore
{
    public async Task<IReadOnlyList<SavedQueryShareToken>> ListForQueryAsync(
        Guid savedQueryId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.SavedQueryShareTokens.AsNoTracking()
            .Where(t => t.SavedQueryId == savedQueryId)
            .OrderByDescending(t => t.IssuedAtUtc)
            .ToListAsync(cancellationToken);
    }

    public async Task<IssuedShareToken> IssueAsync(
        IssueShareTokenInput input,
        Guid actorId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var rawToken = GenerateRawToken();
        var hash = HashToken(rawToken);
        var now = DateTime.UtcNow;

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var savedQuery = await db.SavedQueries.AsNoTracking()
            .AnyAsync(q => q.Id == input.SavedQueryId, cancellationToken);
        if (!savedQuery)
        {
            throw new SavedQueryNotFoundException(input.SavedQueryId);
        }
        var entity = new SavedQueryShareToken
        {
            Id = Guid.NewGuid(),
            SavedQueryId = input.SavedQueryId,
            TokenHash = hash,
            IssuedBy = actorId,
            IssuedAtUtc = now,
            ExpiresAtUtc = input.ExpiresAtUtc,
            MaxUses = input.MaxUses,
            UseCount = 0,
            Label = string.IsNullOrWhiteSpace(input.Label) ? null : input.Label.Trim(),
        };
        db.SavedQueryShareTokens.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
        return new IssuedShareToken(entity, rawToken);
    }

    public async Task<bool> RevokeAsync(Guid tokenId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.SavedQueryShareTokens
            .SingleOrDefaultAsync(t => t.Id == tokenId, cancellationToken);
        if (entity is null) return false;
        if (entity.RevokedAtUtc is not null) return true; // idempotent
        entity.RevokedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<SavedQueryShareToken?> RedeemAsync(
        string rawToken, DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rawToken)) return null;
        var hash = HashToken(rawToken);
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.SavedQueryShareTokens
            .SingleOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);
        if (entity is null) return null;
        if (entity.RevokedAtUtc is not null) return null;
        if (entity.ExpiresAtUtc is { } expires && expires <= nowUtc) return null;
        if (entity.MaxUses is { } cap && entity.UseCount >= cap) return null;

        entity.UseCount += 1;
        entity.LastUsedAtUtc = nowUtc;
        await db.SaveChangesAsync(cancellationToken);
        return entity;
    }

    // 32 bytes of cryptographic randomness rendered as url-safe base64. The
    // returned token is what the browser puts in the link; only the SHA-256
    // hash is persisted.
    public static string GenerateRawToken()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }

    public static string HashToken(string rawToken)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(rawToken), hash);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
