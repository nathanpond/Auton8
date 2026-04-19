using AutoNate.Web.Models;
using AutoNate.Web.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AutoNate.Web.Services.Auth;

public sealed class EfCoreLocalUserStore(IDbContextFactory<AutoNateDbContext> dbContextFactory) : ILocalUserStore
{
    public async Task<IReadOnlyList<LocalUser>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var users = await dbContext.LocalUsers
            .AsNoTracking()
            .OrderBy(user => user.Username)
            .ToListAsync(cancellationToken);

        return users.Select(user => user.ToModel()).ToList();
    }

    public async Task<LocalUser?> GetByUsernameAsync(string username, CancellationToken cancellationToken = default)
    {
        var normalizedUsername = NormalizeRequired(username, nameof(username));

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await dbContext.LocalUsers
            .AsNoTracking()
            .SingleOrDefaultAsync(
                localUser => localUser.Username == normalizedUsername,
                cancellationToken);

        return entity?.ToModel();
    }

    public async Task<LocalUser?> ValidateCredentialsAsync(
        string username,
        string password,
        CancellationToken cancellationToken = default)
    {
        var normalizedUsername = NormalizeRequired(username, nameof(username));

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await dbContext.LocalUsers
            .SingleOrDefaultAsync(localUser => localUser.Username == normalizedUsername, cancellationToken);
        if (entity is null || !PasswordHasher.VerifyPassword(password, entity.PasswordHash, entity.PasswordSalt))
        {
            return null;
        }

        entity.LastLoginDate = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        return entity.ToModel();
    }

    public async Task<LocalUser> CreateAsync(
        string username,
        string firstName,
        string lastName,
        string password,
        string? email = null,
        CancellationToken cancellationToken = default)
    {
        var normalizedUsername = NormalizeRequired(username, nameof(username));
        var normalizedFirstName = NormalizeRequired(firstName, nameof(firstName));
        var normalizedLastName = NormalizeRequired(lastName, nameof(lastName));
        var normalizedEmail = string.IsNullOrWhiteSpace(email)
            ? $"{normalizedUsername}@localhost"
            : NormalizeRequired(email, nameof(email));
        var (hash, salt) = PasswordHasher.HashPassword(NormalizeRequired(password, nameof(password)));
        var userId = Guid.NewGuid();

        var entity = new Persistence.Scaffolded.LocalUser
        {
            Username = normalizedUsername,
            PasswordHash = hash,
            PasswordSalt = salt,
            Email = normalizedEmail,
            FirstName = normalizedFirstName,
            LastName = normalizedLastName,
            UserId = userId,
            CreatedDate = DateTime.UtcNow,
            LastLoginDate = null,
            IdpKey = $"local-{userId:N}"
        };

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        dbContext.LocalUsers.Add(entity);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            throw new InvalidOperationException("A user with that username already exists.", exception);
        }

        return entity.ToModel();
    }

    public async Task<LocalUser?> UpdateAsync(
        long id,
        string username,
        string firstName,
        string lastName,
        string email,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await dbContext.LocalUsers.SingleOrDefaultAsync(user => user.Id == id, cancellationToken);
        if (entity is null)
        {
            return null;
        }

        entity.Username = NormalizeRequired(username, nameof(username));
        entity.FirstName = NormalizeRequired(firstName, nameof(firstName));
        entity.LastName = NormalizeRequired(lastName, nameof(lastName));
        entity.Email = NormalizeRequired(email, nameof(email));

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            throw new InvalidOperationException("A user with that username already exists.", exception);
        }

        return entity.ToModel();
    }

    public async Task<bool> ResetPasswordAsync(long id, string password, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await dbContext.LocalUsers.SingleOrDefaultAsync(user => user.Id == id, cancellationToken);
        if (entity is null)
        {
            return false;
        }

        var (hash, salt) = PasswordHasher.HashPassword(NormalizeRequired(password, nameof(password)));
        entity.PasswordHash = hash;
        entity.PasswordSalt = salt;
        await dbContext.SaveChangesAsync(cancellationToken);

        return true;
    }

    public async Task<bool> DeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await dbContext.LocalUsers.SingleOrDefaultAsync(user => user.Id == id, cancellationToken);
        if (entity is null)
        {
            return false;
        }

        dbContext.LocalUsers.Remove(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    private static string NormalizeRequired(string value, string paramName)
    {
        var normalized = value.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new InvalidOperationException($"'{paramName}' is required.");
        }

        return normalized;
    }
}
