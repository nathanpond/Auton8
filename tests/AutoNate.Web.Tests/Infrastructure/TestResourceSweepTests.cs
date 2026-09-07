using Npgsql;
using Xunit;

namespace AutoNate.Web.Tests.Infrastructure;

/// <summary>
/// The cluster-wide sweep removes scratch and nothing else (#214).
/// </summary>
[Trait("Category", "Integration")]
public sealed class TestResourceSweepTests
{
    [Fact]
    public async Task An_orphaned_plugin_role_is_removed()
    {
        var role = $"plg_orph{Guid.NewGuid():N}"[..20];
        await ExecuteAsync("postgres", $"create role \"{role}\";");

        try
        {
            var counts = await TestResourceSweep.SweepAsync(TimeSpan.FromHours(2));

            Assert.True(counts.Roles > 0, "The sweep reported dropping no roles.");
            Assert.False(await RoleExistsAsync(role), "An orphaned plugin role survived the sweep.");
        }
        finally
        {
            await ExecuteAsync("postgres", $"drop role if exists \"{role}\";");
        }
    }

    [Fact]
    public async Task A_role_backing_an_installed_plugin_survives()
    {
        // The criterion this story exists for. PluginSchemaProvisioner.RoleNameFor is
        // PRODUCTION code — "plg_" + code — so a test's plugin role and a real
        // installed plugin's role are identical by name. A prefix sweep would drop a
        // developer's working plugin off their own cluster.
        //
        // The role survives because a schema of the same name exists, which is what
        // an installed plugin looks like. Nothing here depends on the code looking
        // random or not.
        var code = $"plg_live{Guid.NewGuid():N}"[..20];
        await ExecuteAsync("postgres", $"create role \"{code}\";");
        await ExecuteAsync("AutoNate", $"create schema if not exists \"{code}\";");

        try
        {
            await TestResourceSweep.SweepAsync(TimeSpan.FromHours(2));

            Assert.True(await RoleExistsAsync(code),
                "The sweep dropped a role whose schema is live — this is the failure that breaks a developer's installed plugin.");
            Assert.True(await SchemaExistsAsync("AutoNate", code),
                "The sweep dropped a schema in a database it does not own.");
        }
        finally
        {
            await ExecuteAsync("AutoNate", $"drop schema if exists \"{code}\" cascade;");
            await ExecuteAsync("postgres", $"drop role if exists \"{code}\";");
        }
    }

    [Fact]
    public async Task A_role_that_still_owns_objects_is_never_dropped()
    {
        // A second backstop, and one Postgres enforces for us: DROP ROLE fails while
        // the role owns anything. Found while verifying this story — `plg_readers`
        // on the dev cluster owns 99 objects in AutoNate and 92 in AutoNate_E2E, and
        // the sweep correctly skipped it even though no schema carries its name.
        //
        // Asserted rather than relied on silently: if the sweep ever grew a
        // `DROP ROLE ... CASCADE` or a reassign-owned step, this is what would
        // notice before a developer's data did.
        var role = $"plg_owner{Guid.NewGuid():N}"[..20];
        await ExecuteAsync("postgres", $"create role \"{role}\";");
        await ExecuteAsync("AutoNate", $"create table if not exists owned_by_{role} (id int);");
        await ExecuteAsync("AutoNate", $"alter table owned_by_{role} owner to \"{role}\";");

        try
        {
            await TestResourceSweep.SweepAsync(TimeSpan.FromHours(2));
            Assert.True(await RoleExistsAsync(role), "The sweep dropped a role that still owns objects.");
        }
        finally
        {
            await ExecuteAsync("AutoNate", $"drop table if exists owned_by_{role};");
            await ExecuteAsync("postgres", $"drop role if exists \"{role}\";");
        }
    }

    [Fact]
    public async Task Each_class_reports_its_own_count()
    {
        // "Cleaned up 400 things" can hide one category doing nothing, which is how
        // a sweep silently stops working. The counts are separate and the string
        // names each one.
        var counts = await TestResourceSweep.SweepAsync(TimeSpan.FromHours(2));

        Assert.Contains("roles=", counts.ToString(), StringComparison.Ordinal);
        Assert.Contains("schemas=", counts.ToString(), StringComparison.Ordinal);
        Assert.Contains("directories=", counts.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_recent_temp_directory_is_left_alone()
    {
        // Complement to the age rule: a run in progress owns its content root, and
        // deleting it mid-run breaks a test that was going to pass.
        var directory = Path.Combine(Path.GetTempPath(), $"autonate-pgtests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);

        try
        {
            await TestResourceSweep.SweepAsync(TimeSpan.FromHours(2));
            Assert.True(Directory.Exists(directory), "The sweep deleted a directory created moments ago.");
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static async Task<bool> RoleExistsAsync(string role) =>
        await ScalarAsync("postgres", "select 1 from pg_roles where rolname = @n;", role) is not null;

    private static async Task<bool> SchemaExistsAsync(string database, string schema) =>
        await ScalarAsync(database, "select 1 from information_schema.schemata where schema_name = @n;", schema) is not null;

    private static async Task<object?> ScalarAsync(string database, string sql, string name)
    {
        await using var connection = new NpgsqlConnection(PostgresTestDatabase.AdminConnectionStringFor(database));
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("n", name);
        return await command.ExecuteScalarAsync();
    }

    private static async Task ExecuteAsync(string database, string sql)
    {
        await using var connection = new NpgsqlConnection(PostgresTestDatabase.AdminConnectionStringFor(database));
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}
