using AutoNate.Web.Models;
using AutoNate.Web.Services.Auth;
using Xunit;

namespace AutoNate.Web.Tests;

[Trait("Category", "Integration")]
public sealed class EfCoreLocalUserStoreTests
{
    [Fact]
    public async Task ListAsync_ReturnsSeededAdminUser()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = database.CreateLocalUserStore();

        var users = await store.ListAsync();

        var admin = Assert.Single(users);
        Assert.Equal("admin", admin.Username);
        Assert.Equal("admin@localhost", admin.Email);
    }

    [Fact]
    public async Task GetByUsernameAsync_ReturnsMatchingUser()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = database.CreateLocalUserStore();

        var admin = await store.GetByUsernameAsync("admin");

        Assert.NotNull(admin);
        Assert.Equal("local-admin", admin.IdpKey);
    }

    [Fact]
    public async Task ValidateCredentialsAsync_UpdatesLastLoginDate()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = database.CreateLocalUserStore();

        var user = await store.ValidateCredentialsAsync("admin", "admin");

        Assert.NotNull(user);
        Assert.NotNull(user.LastLoginDate);
    }

    [Fact]
    public async Task CreateAsync_CreatesUserWithDefaultEmail()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = database.CreateLocalUserStore();

        var created = await store.CreateAsync("newuser", "New", "User", "password123");

        Assert.Equal("newuser", created.Username);
        Assert.Equal("newuser@localhost", created.Email);

        var loaded = await store.GetByUsernameAsync("newuser");
        Assert.NotNull(loaded);
        Assert.Equal(created.Id, loaded.Id);
    }

    [Fact]
    public async Task UpdateAsync_UpdatesMutableFields()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = database.CreateLocalUserStore();
        var created = await store.CreateAsync("editme", "Edit", "Me", "password123");

        var updated = await store.UpdateAsync(created.Id, "edited", "Edited", "User", "edited@example.com");

        Assert.NotNull(updated);
        Assert.Equal("edited", updated.Username);
        Assert.Equal("edited@example.com", updated.Email);
    }

    [Fact]
    public async Task ResetPasswordAsync_ReplacesStoredPassword()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = database.CreateLocalUserStore();
        var created = await store.CreateAsync("resetme", "Reset", "Me", "old-password");

        var reset = await store.ResetPasswordAsync(created.Id, "new-password");

        Assert.True(reset);
        Assert.Null(await store.ValidateCredentialsAsync("resetme", "old-password"));
        Assert.NotNull(await store.ValidateCredentialsAsync("resetme", "new-password"));
    }

    [Fact]
    public async Task DeleteAsync_RemovesUser()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = database.CreateLocalUserStore();
        var created = await store.CreateAsync("deleteme", "Delete", "Me", "password123");

        var deleted = await store.DeleteAsync(created.Id);

        Assert.True(deleted);
        Assert.Null(await store.GetByUsernameAsync("deleteme"));
    }

    [Fact]
    public async Task AttemptLoginAsync_LocksAccountAfterThreeFailedAttempts()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = database.CreateLocalUserStore();
        await store.CreateAsync("lockme", "Lock", "Me", "right-password");

        var first = await store.AttemptLoginAsync("lockme", "wrong");
        var second = await store.AttemptLoginAsync("lockme", "wrong");
        var third = await store.AttemptLoginAsync("lockme", "wrong");

        Assert.Equal(LoginAttemptOutcome.InvalidCredentials, first.Outcome);
        Assert.Equal(1, first.FailedAttempts);
        Assert.Equal(LoginAttemptOutcome.InvalidCredentials, second.Outcome);
        Assert.Equal(2, second.FailedAttempts);
        Assert.Equal(LoginAttemptOutcome.JustLocked, third.Outcome);
        Assert.Equal(3, third.FailedAttempts);

        var loaded = await store.GetByUsernameAsync("lockme");
        Assert.NotNull(loaded);
        Assert.True(loaded!.IsLocked);
        Assert.NotNull(loaded.LockedAtUtc);
    }

    [Fact]
    public async Task AttemptLoginAsync_LockedAccountRejectsCorrectPassword()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = database.CreateLocalUserStore();
        await store.CreateAsync("lockedout", "Locked", "Out", "right-password");
        for (var i = 0; i < 3; i++)
        {
            await store.AttemptLoginAsync("lockedout", "wrong");
        }

        var attempt = await store.AttemptLoginAsync("lockedout", "right-password");

        Assert.Equal(LoginAttemptOutcome.AccountLocked, attempt.Outcome);
        Assert.Null(attempt.User);
    }

    [Fact]
    public async Task AttemptLoginAsync_SuccessResetsFailedAttempts()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = database.CreateLocalUserStore();
        await store.CreateAsync("resetcounter", "Reset", "Counter", "right-password");
        await store.AttemptLoginAsync("resetcounter", "wrong");
        await store.AttemptLoginAsync("resetcounter", "wrong");

        var success = await store.AttemptLoginAsync("resetcounter", "right-password");

        Assert.Equal(LoginAttemptOutcome.Succeeded, success.Outcome);
        var loaded = await store.GetByUsernameAsync("resetcounter");
        Assert.NotNull(loaded);
        Assert.Equal(0, loaded!.FailedLoginAttempts);
        Assert.False(loaded.IsLocked);
    }

    [Fact]
    public async Task SetLockedAsync_UnlockClearsCounterAndLock()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var store = database.CreateLocalUserStore();
        var created = await store.CreateAsync("unlockme", "Un", "Lock", "right-password");
        for (var i = 0; i < 3; i++)
        {
            await store.AttemptLoginAsync("unlockme", "wrong");
        }

        var unlocked = await store.SetLockedAsync(created.Id, isLocked: false);

        Assert.NotNull(unlocked);
        Assert.False(unlocked!.IsLocked);
        Assert.Equal(0, unlocked.FailedLoginAttempts);
        Assert.Null(unlocked.LockedAtUtc);

        var afterUnlock = await store.AttemptLoginAsync("unlockme", "right-password");
        Assert.Equal(LoginAttemptOutcome.Succeeded, afterUnlock.Outcome);
    }
}
