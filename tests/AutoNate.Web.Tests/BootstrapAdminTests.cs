using Xunit;
using Microsoft.Extensions.DependencyInjection;
using AutoNate.Web.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AutoNate.Web.Tests;

// The first administrator on an empty install.
//
// This replaced a hardcoded INSERT in
// infra/postgres/init/02-create-autonate-app-schema.sql that shipped `admin`
// with its password_hash *and* password_salt committed to the repository,
// ungated by environment. Combined with AssignSuperAdminToAllExistingUsers,
// every install that ran that script came up with a super-admin whose password
// was public.
//
// The property worth guarding is not "bootstrap works" — it is that configuring
// *nothing* creates *nothing*. A regression that reintroduced a default
// password would still pass a happy-path test, so the negative cases are the
// point of this file.
public sealed class BootstrapAdminTests
{
    private static readonly Guid PinnedAdminId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static async Task<int> CountUsersAsync(AutoNateWebApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AutoNateDbContext>();
        var rows = await db.Database
            .SqlQuery<int>($"SELECT COUNT(*)::int AS \"Value\" FROM local_users")
            .ToArrayAsync();
        return rows[0];
    }

    private static async Task<(string Username, string Email, Guid UserId)[]> ReadUsersAsync(
        AutoNateWebApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AutoNateDbContext>();
        var users = await db.LocalUsers
            .Select(u => new { u.Username, u.Email, u.UserId })
            .ToArrayAsync();
        return users.Select(u => (u.Username, u.Email, u.UserId)).ToArray();
    }

    [Fact]
    public async Task Configured_bootstrap_creates_exactly_one_admin_on_an_empty_database()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        // Force the host (and therefore the initializer) to start.
        _ = factory.CreateClient();

        var users = await ReadUsersAsync(factory);

        var admin = Assert.Single(users);
        Assert.Equal("admin", admin.Username);
        Assert.Equal("admin@localhost", admin.Email);
        // Pinned because ~20 other suites assert against this exact id; the
        // removed seed hardcoded it.
        Assert.Equal(PinnedAdminId, admin.UserId);
    }

    [Fact]
    public async Task Configured_bootstrap_stores_a_verifiable_hash_not_the_plaintext()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        _ = factory.CreateClient();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AutoNateDbContext>();
        var row = await db.LocalUsers.SingleAsync(u => u.Username == "admin");

        Assert.NotEqual("admin", row.PasswordHash);
        Assert.False(string.IsNullOrWhiteSpace(row.PasswordHash));
        Assert.False(string.IsNullOrWhiteSpace(row.PasswordSalt));

        // A random salt per install is the other half of removing the committed
        // credential: two installs configured with the same password must not
        // share a hash, or the published one stays useful against them.
        var second = Web.Services.Auth.PasswordHasher.HashPassword("admin");
        Assert.NotEqual(second.Hash, row.PasswordHash);
    }

    [Fact]
    public async Task No_configuration_creates_no_user_at_all()
    {
        // The security property. If this ever passes with a row present, a
        // default credential has been reintroduced.
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(
            new Dictionary<string, string?>
            {
                ["Bootstrap:AdminUsername"] = null,
                ["Bootstrap:AdminPassword"] = null,
                ["Bootstrap:AdminUserId"] = null,
                // Auto-login would otherwise try to sign in as a user that must
                // not exist; this test is about the database, not the request.
                ["DevelopmentAutoLogin:Enabled"] = "false",
            });
        _ = factory.CreateClient();

        Assert.Equal(0, await CountUsersAsync(factory));
    }

    [Theory]
    // A username with no password, and a password with no username, are both
    // incomplete configuration. Creating an account for either would mean
    // inventing the missing half.
    [InlineData("admin", null)]
    [InlineData(null, "correct horse battery staple")]
    [InlineData("", "")]
    [InlineData("   ", "   ")]
    public async Task Partial_or_blank_configuration_creates_no_user(
        string? username,
        string? password)
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(
            new Dictionary<string, string?>
            {
                ["Bootstrap:AdminUsername"] = username,
                ["Bootstrap:AdminPassword"] = password,
                ["Bootstrap:AdminUserId"] = null,
                ["DevelopmentAutoLogin:Enabled"] = "false",
            });
        _ = factory.CreateClient();

        Assert.Equal(0, await CountUsersAsync(factory));
    }

    [Fact]
    public async Task Bootstrap_admin_is_granted_SuperAdmin_without_the_backfill()
    {
        // Authorization:AssignSuperAdminToAllExistingUsers is false here (the
        // factory pins it, and it is now the shipped default). Before this
        // grant existed, an install with that flag off produced an admin who
        // could sign in and then be denied everything — the same lockout the
        // removed seed was working around.
        //
        // GrantSuperAdmin is opted into because the factory turns it off for
        // every other suite; production defaults it on.
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(
            new Dictionary<string, string?> { ["Bootstrap:GrantSuperAdmin"] = "true" });
        _ = factory.CreateClient();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AutoNateDbContext>();
        var grants = await db.Database
            .SqlQuery<int>($"""
                SELECT COUNT(*)::int AS "Value" FROM role_assignments
                WHERE role_id = '00000000-0000-0000-0000-000000000001'::uuid
                  AND principal_kind = 'user'
                  AND principal_id = {PinnedAdminId}::text
                """)
            .ToArrayAsync();

        // Exactly one: the bootstrap grant and the backfill guard against
        // each other, so enabling both must not double-assign.
        Assert.Equal(1, grants[0]);
    }

    [Fact]
    public async Task Bootstrap_admin_gets_no_privilege_when_the_grant_is_switched_off()
    {
        // The twin of the test above, and the reason the switch exists: an
        // account can be created without privilege. If these two ever agree,
        // the flag is being ignored and every enforcement suite is passing
        // vacuously against a super-admin.
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        _ = factory.CreateClient();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AutoNateDbContext>();
        var grants = await db.Database
            .SqlQuery<int>($"""
                SELECT COUNT(*)::int AS "Value" FROM role_assignments
                WHERE role_id = '00000000-0000-0000-0000-000000000001'::uuid
                  AND principal_kind = 'user'
                  AND principal_id = {PinnedAdminId}::text
                """)
            .ToArrayAsync();

        Assert.Equal(0, grants[0]);
    }

    [Theory]
    // Seeds that attribute their rows to "the oldest user" and RETURN silently
    // when there is none. They ran before the bootstrap in the first draft of
    // this change, so a fresh install came up with no Documents nav item and
    // no sample project — and nothing failed. These pin the ordering.
    [InlineData("Documents nav item",
        "SELECT COUNT(*)::int AS \"Value\" FROM menu_items WHERE parent_id IS NULL AND config->>'path' = '/documents'")]
    [InlineData("sample content project",
        "SELECT COUNT(*)::int AS \"Value\" FROM projects")]
    public async Task Seeds_that_need_an_actor_run_after_the_bootstrap(string what, string sql)
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        _ = factory.CreateClient();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AutoNateDbContext>();
        var rows = await db.Database.SqlQueryRaw<int>(sql).ToArrayAsync();

        Assert.True(rows[0] > 0, $"No {what} was seeded — the bootstrap administrator most likely runs after the seed that needs it.");
    }

    [Fact]
    public async Task Bootstrap_does_not_touch_a_database_that_already_has_users()
    {
        // Guards the emptiness check itself. A restart with different bootstrap
        // settings must not add a second privileged account to a live install
        // — that would be a backdoor reachable by anyone who can set an env var.
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        _ = factory.CreateClient();
        Assert.Equal(1, await CountUsersAsync(factory));

        await using var restarted = AutoNateWebApplicationFactory.CreateOn(
            factory.Database,
            new Dictionary<string, string?>
            {
                ["Bootstrap:AdminUsername"] = "second-admin",
                ["Bootstrap:AdminPassword"] = "another-password",
                ["Bootstrap:AdminUserId"] = null,
            });
        _ = restarted.CreateClient();

        var users = await ReadUsersAsync(restarted);
        Assert.Single(users);
        Assert.Equal("admin", users[0].Username);
    }
}
