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
}
