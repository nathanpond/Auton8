using AutoNate.Web.Models;
using AutoNate.Web.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AutoNate.Web.Services.Auth;

public sealed class EfCoreLocalUserStore(
    IDbContextFactory<AutoNateDbContext> dbContextFactory,
    UserDirectorySnapshotCache directoryCache) : ILocalUserStore
{
    public const int FailedLoginLockoutThreshold = 3;

    public async Task<IReadOnlyList<LocalUser>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var users = await dbContext.LocalUsers
            .AsNoTracking()
            .OrderBy(user => user.Username)
            .ToListAsync(cancellationToken);

        return users.Select(user => user.ToModel()).ToList();
    }

    public async Task<LocalUserPage> ListPagedAsync(
        ListLocalUsersRequest request,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var query = dbContext.LocalUsers.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var pattern = $"%{request.Search.Trim()}%";
            query = query.Where(user =>
                EF.Functions.ILike(user.Username, pattern) ||
                EF.Functions.ILike(user.FirstName, pattern) ||
                EF.Functions.ILike(user.LastName, pattern) ||
                EF.Functions.ILike(user.Email, pattern));
        }

        // Status mapping mirrors the SPA's getUserStatus(): "Disabled" has no
        // backing column today and is intentionally a no-match filter.
        query = request.Status switch
        {
            "Locked" => query.Where(user => user.IsLocked),
            "Invited" => query.Where(user => !user.IsLocked && user.LastLoginDate == null),
            "Active" => query.Where(user => !user.IsLocked && user.LastLoginDate != null),
            "Disabled" => query.Where(_ => false),
            _ => query
        };

        var totalCount = await query.CountAsync(cancellationToken);

        var desc = string.Equals(request.SortDir, "desc", StringComparison.OrdinalIgnoreCase);
        query = (request.SortBy, desc) switch
        {
            ("username", true) => query.OrderByDescending(u => u.Username),
            ("username", false) => query.OrderBy(u => u.Username),
            ("lastName", true) => query.OrderByDescending(u => u.LastName).ThenBy(u => u.Username),
            ("lastName", false) => query.OrderBy(u => u.LastName).ThenBy(u => u.Username),
            ("fullName", true) => query.OrderByDescending(u => u.FirstName + " " + u.LastName).ThenBy(u => u.Username),
            ("fullName", false) => query.OrderBy(u => u.FirstName + " " + u.LastName).ThenBy(u => u.Username),
            ("lastLogin", true) => query.OrderByDescending(u => u.LastLoginDate).ThenBy(u => u.Username),
            ("lastLogin", false) => query.OrderBy(u => u.LastLoginDate).ThenBy(u => u.Username),
            ("status", true) => query.OrderByDescending(u => u.IsLocked).ThenByDescending(u => u.LastLoginDate).ThenBy(u => u.Username),
            ("status", false) => query.OrderBy(u => u.IsLocked).ThenBy(u => u.LastLoginDate).ThenBy(u => u.Username),
            _ => query.OrderBy(u => u.Username)
        };

        if (request.PageSize > 0)
        {
            query = query
                .Skip(Math.Max(0, request.Page) * request.PageSize)
                .Take(request.PageSize);
        }
        else
        {
            // pageSize == 0 means "count probe": return total only, no items.
            query = query.Take(0);
        }

        var rows = await query.ToListAsync(cancellationToken);
        return new LocalUserPage(rows.Select(u => u.ToModel()).ToList(), totalCount);
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

    public async Task<LocalUser?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await dbContext.LocalUsers
            .AsNoTracking()
            .SingleOrDefaultAsync(localUser => localUser.Id == id, cancellationToken);
        return entity?.ToModel();
    }

    public async Task<LocalUser?> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await dbContext.LocalUsers
            .AsNoTracking()
            .SingleOrDefaultAsync(localUser => localUser.UserId == userId, cancellationToken);
        return entity?.ToModel();
    }

    public async Task<LocalUser?> ValidateCredentialsAsync(
        string username,
        string password,
        CancellationToken cancellationToken = default)
    {
        var result = await AttemptLoginAsync(username, password, cancellationToken);
        return result.Outcome == LoginAttemptOutcome.Succeeded ? result.User : null;
    }

    // Attempts a login against the local user store and applies the lockout
    // policy: three consecutive incorrect attempts lock the account, and a
    // locked account rejects every login (even with the right password) until
    // an admin clears the lock. Successful authentication resets the counter.
    public async Task<LoginAttemptResult> AttemptLoginAsync(
        string username,
        string password,
        CancellationToken cancellationToken = default)
    {
        var normalizedUsername = NormalizeRequired(username, nameof(username));

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await dbContext.LocalUsers
            .SingleOrDefaultAsync(localUser => localUser.Username == normalizedUsername, cancellationToken);
        if (entity is null)
        {
            return new LoginAttemptResult(LoginAttemptOutcome.InvalidCredentials, null, normalizedUsername, 0);
        }

        if (entity.IsLocked)
        {
            return new LoginAttemptResult(
                LoginAttemptOutcome.AccountLocked,
                null,
                entity.Username,
                entity.FailedLoginAttempts,
                entity.UserId);
        }

        if (!PasswordHasher.VerifyPassword(password, entity.PasswordHash, entity.PasswordSalt))
        {
            entity.FailedLoginAttempts += 1;
            var justLocked = entity.FailedLoginAttempts >= FailedLoginLockoutThreshold;
            if (justLocked)
            {
                entity.IsLocked = true;
                entity.LockedAtUtc = DateTime.UtcNow;
            }

            await dbContext.SaveChangesAsync(cancellationToken);

            return new LoginAttemptResult(
                justLocked ? LoginAttemptOutcome.JustLocked : LoginAttemptOutcome.InvalidCredentials,
                null,
                entity.Username,
                entity.FailedLoginAttempts,
                entity.UserId);
        }

        entity.LastLoginDate = DateTime.UtcNow;
        entity.FailedLoginAttempts = 0;
        await dbContext.SaveChangesAsync(cancellationToken);

        return new LoginAttemptResult(LoginAttemptOutcome.Succeeded, entity.ToModel(), entity.Username, 0, entity.UserId);
    }

    public async Task<LocalUser?> SetLockedAsync(long id, bool isLocked, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await dbContext.LocalUsers.SingleOrDefaultAsync(user => user.Id == id, cancellationToken);
        if (entity is null)
        {
            return null;
        }

        entity.IsLocked = isLocked;
        entity.LockedAtUtc = isLocked ? DateTime.UtcNow : null;
        if (!isLocked)
        {
            entity.FailedLoginAttempts = 0;
        }

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

        // Every write drops the directory snapshot (#9): a user created in
        // the admin screen must appear in an assignee picker at once, and
        // waiting out the TTL would read as the create having failed.
        directoryCache.Invalidate();
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

        // Every write drops the directory snapshot (#9): a user created in
        // the admin screen must appear in an assignee picker at once, and
        // waiting out the TTL would read as the create having failed.
        directoryCache.Invalidate();
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

        // Every write drops the directory snapshot (#9): a user created in
        // the admin screen must appear in an assignee picker at once, and
        // waiting out the TTL would read as the create having failed.
        directoryCache.Invalidate();
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
        // Every write drops the directory snapshot (#9): a user created in
        // the admin screen must appear in an assignee picker at once, and
        // waiting out the TTL would read as the create having failed.
        directoryCache.Invalidate();
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
