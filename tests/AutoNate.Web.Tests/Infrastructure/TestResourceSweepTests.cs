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
        // #215: drain the once-per-process startup sweep before planting, or it can
        // run concurrently, drop the role first, and leave the sweep below with
        // nothing to report. Counting drops is the point of this test, so the count
        // has to be attributable to the call under test.
        await PostgresTestDatabase.EnsureStartupSweepCompleteAsync();

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

    // #215. The sweep listed pg_database and then connected to each name in turn,
    // and treated ANY failure as "cannot see this database's schemas, so assume
    // every role is in use" — returning 0 without examining anything. The suite
    // creates and drops a database per test class in parallel with the sweep, so a
    // name that was listed and then vanished is the normal case, not an edge one.
    // The visible symptom was this class failing about eleven minutes into a full
    // run with "the sweep reported dropping no roles".
    //
    // The timing cannot be reproduced from outside, so the database list is
    // supplied instead: a name that is not there is exactly the state the race
    // produces. Against the old code this test drops nothing and fails.
    [Fact]
    public async Task A_database_that_vanished_mid_sweep_does_not_abort_the_role_pass()
    {
        await PostgresTestDatabase.EnsureStartupSweepCompleteAsync();

        var role = $"plg_gone{Guid.NewGuid():N}"[..20];
        await ExecuteAsync("postgres", $"create role \"{role}\";");

        try
        {
            // "postgres" is real; the second name was dropped by another test class
            // between the listing and the connect, as far as this sweep can tell.
            var dropped = await TestResourceSweep.SweepOrphanedPluginRolesAsync(
                ["postgres", $"autonate_test_{Guid.NewGuid():N}"]);

            Assert.True(
                dropped > 0,
                "A database disappearing mid-sweep aborted the whole role pass; " +
                "the sweep reported dropping no roles.");
            Assert.False(
                await RoleExistsAsync(role),
                "The orphaned role survived a sweep that should have skipped only the missing database.");
        }
        finally
        {
            await ExecuteAsync("postgres", $"drop role if exists \"{role}\";");
        }
    }

    // The conservative half of the same decision: a database that EXISTS but
    // cannot be read still stops the pass, because its schemas might be the thing
    // keeping a role alive. Asserting the skip without this would licence dropping
    // roles on any connection error.
    [Fact]
    public async Task A_database_that_cannot_be_read_still_stops_the_role_pass()
    {
        await PostgresTestDatabase.EnsureStartupSweepCompleteAsync();

        var role = $"plg_keep{Guid.NewGuid():N}"[..20];
        await ExecuteAsync("postgres", $"create role \"{role}\";");

        try
        {
            // A real database this connection is refused on: connecting as a role
            // with no CONNECT privilege fails with something other than
            // "database does not exist".
            var dropped = await TestResourceSweep.SweepOrphanedPluginRolesAsync(
                ["postgres", "template0"]);

            Assert.Equal(0, dropped);
            Assert.True(
                await RoleExistsAsync(role),
                "A role was dropped even though a live database's schemas could not be read.");
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
        var database = await CreateUnownedDatabaseAsync();
        await ExecuteAsync("postgres", $"create role \"{code}\";");
        await ExecuteAsync(database, $"create schema if not exists \"{code}\";");

        try
        {
            await TestResourceSweep.SweepAsync(TimeSpan.FromHours(2));

            Assert.True(await RoleExistsAsync(code),
                "The sweep dropped a role whose schema is live — this is the failure that breaks a developer's installed plugin.");
            Assert.True(await SchemaExistsAsync(database, code),
                "The sweep dropped a schema in a database it does not own.");
        }
        finally
        {
            await DropDatabaseAsync(database);
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
        var database = await CreateUnownedDatabaseAsync();
        await ExecuteAsync("postgres", $"create role \"{role}\";");
        await ExecuteAsync(database, $"create table if not exists owned_by_{role} (id int);");
        await ExecuteAsync(database, $"alter table owned_by_{role} owner to \"{role}\";");

        try
        {
            await TestResourceSweep.SweepAsync(TimeSpan.FromHours(2));
            Assert.True(await RoleExistsAsync(role), "The sweep dropped a role that still owns objects.");
        }
        finally
        {
            await DropDatabaseAsync(database);
            await ExecuteAsync("postgres", $"drop role if exists \"{role}\";");
        }
    }

    /// <summary>
    /// Every class reports a real count, proved by planting one of each (#299).
    /// </summary>
    /// <remarks>
    /// <para>
    /// This used to assert only that the summary string contained the substrings
    /// <c>roles=</c>, <c>schemas=</c> and <c>directories=</c>. With the schema pass
    /// <b>and</b> the directory pass both disabled it still passed — which is
    /// precisely the failure its own AC names: "so one category can't quietly do
    /// nothing".
    /// </para>
    /// <para>
    /// Labels are not counts. It plants one sweepable item per class and asserts
    /// each count moved, so a pass that stops working fails here rather than
    /// reporting <c>schemas=0</c> in a string nobody reads.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Each_class_reports_its_own_count()
    {
        await PostgresTestDatabase.EnsureStartupSweepCompleteAsync();

        var role = $"plg_cnt{Guid.NewGuid():N}"[..20];
        var directory = Path.Combine(Path.GetTempPath(), $"autonate-pgtests-{Guid.NewGuid():N}");
        // Old enough to be swept, unlike the run's own content roots.
        var (database, schema) = await PlantOldSuiteDatabaseWithSchemaAsync();

        await ExecuteAsync("postgres", $"create role \"{role}\";");
        Directory.CreateDirectory(directory);
        Directory.SetCreationTimeUtc(directory, DateTime.UtcNow.AddHours(-3));

        try
        {
            var counts = await TestResourceSweep.SweepAsync(TimeSpan.FromHours(2));

            // The numbers, not the labels.
            Assert.True(counts.Roles > 0, $"The role pass reported nothing: {counts}");
            Assert.True(counts.Schemas > 0, $"The schema pass reported nothing: {counts}");
            Assert.True(counts.Directories > 0, $"The directory pass reported nothing: {counts}");

            // And the planted items are actually gone, so a count that increments
            // without doing anything fails too.
            Assert.False(await RoleExistsAsync(role), "An orphaned plugin role survived.");
            Assert.False(Directory.Exists(directory), "An old temp directory survived.");
            Assert.False(await SchemaExistsAsync(database, schema),
                "A plugin schema in an abandoned suite database survived.");

            // The string still names each class, because that is what a human reads
            // in the log when one of the numbers above is zero.
            Assert.Contains("roles=", counts.ToString(), StringComparison.Ordinal);
            Assert.Contains("schemas=", counts.ToString(), StringComparison.Ordinal);
            Assert.Contains("directories=", counts.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            await ExecuteAsync("postgres", $"drop role if exists \"{role}\";");
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
            await ExecuteOnPostgresAsync($"drop database if exists \"{database}\" with (force);");
        }
    }

    /// <summary>
    /// A live suite database keeps its plugin schema (#300).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The schema pass had no age check and no liveness check: it dropped
    /// <c>plg_*</c> schemas from every suite-owned database. Reproduced before the
    /// fix — a fresh, stamped, LIVE database's schema went in a single
    /// <c>SweepAsync</c> call, and <c>SweepAsync</c> runs four times per suite run,
    /// so a plugin test in parallel could lose its schema mid-test.
    /// </para>
    /// <para>
    /// This is the rule the DATABASE sweep learned in #191, one axis over. The
    /// complement — an old, abandoned database really does lose its schema — is
    /// asserted by <c>Each_class_reports_its_own_count</c> above, so the pass
    /// cannot satisfy this one by simply never sweeping.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_live_suite_database_keeps_its_plugin_schema()
    {
        await PostgresTestDatabase.EnsureStartupSweepCompleteAsync();

        // Stamped NOW, so it is a run in progress by every rule the sweep has.
        var (database, schema) = await PlantSuiteDatabaseWithSchemaAsync(DateTimeOffset.UtcNow);

        try
        {
            Assert.True(await SchemaExistsAsync(database, schema),
                "the fixture did not create the schema, so the assertion below is vacuous");

            await TestResourceSweep.SweepAsync(TimeSpan.FromHours(2));

            Assert.True(await SchemaExistsAsync(database, schema),
                "The sweep dropped a plugin schema from a database a live run owns. " +
                "SweepAsync runs four times per suite run, so this destroys a " +
                "concurrent plugin test's state mid-test.");
        }
        finally
        {
            await ExecuteOnPostgresAsync($"drop database if exists \"{database}\" with (force);");
        }
    }

    // ── Helpers for the two tests above ─────────────────────────────────────

    private static Task<(string Database, string Schema)> PlantOldSuiteDatabaseWithSchemaAsync() =>
        PlantSuiteDatabaseWithSchemaAsync(DateTimeOffset.UtcNow.AddHours(-3));

    /// <summary>
    /// A suite-shaped database carrying a `plg_*` schema, stamped as at <paramref name="createdAt"/>.
    /// </summary>
    /// <remarks>
    /// The stamp is the COMMENT ON DATABASE the real create writes; the sweep reads
    /// it to decide whether a run still owns the database, so planting it is what
    /// makes age testable without waiting three hours.
    /// </remarks>
    private static async Task<(string Database, string Schema)> PlantSuiteDatabaseWithSchemaAsync(
        DateTimeOffset createdAt)
    {
        var database = $"autonate_test_{Guid.NewGuid():N}";
        var schema = $"plg_probe{Guid.NewGuid():N}"[..18];

        await ExecuteOnPostgresAsync($"create database \"{database}\";");
        // The stamp the real create writes. The sweep reads it to decide whether a
        // run still owns the database, so planting it makes age testable without
        // waiting three hours.
        await ExecuteOnPostgresAsync(
            $"comment on database \"{database}\" is '{createdAt:O}';");
        await ExecuteAsync(database, $"create schema \"{schema}\";");

        return (database, schema);
    }

    private static async Task ExecuteOnPostgresAsync(string sql) =>
        await ExecuteAsync("postgres", sql);

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

    /// <summary>
    /// A live database the sweep does NOT own, created for one test.
    /// </summary>
    /// <remarks>
    /// These two tests used the developer's own `AutoNate` database, which is what
    /// an installed plugin's schema really lives in — but it does not exist in CI,
    /// so they passed on a laptop and failed on the first run that had no dev data
    /// (`3D000: database "AutoNate" does not exist`). Nothing about what they
    /// assert needs a *particular* database, only one the sweep will leave alone:
    /// `IsSuiteOwnedDatabase` matches `autonate_test_*` and `AutoNate_E2E`, and
    /// this name is neither.
    /// </remarks>
    private static async Task<string> CreateUnownedDatabaseAsync()
    {
        var database = $"autonate_sweepfix_{Guid.NewGuid():N}"[..28];
        await ExecuteAsync("postgres", $"create database \"{database}\";");
        return database;
    }

    private static async Task DropDatabaseAsync(string database)
    {
        // Npgsql pools per connection string, and Postgres refuses to drop a
        // database with an open connection — including one this test left in the
        // pool.
        NpgsqlConnection.ClearAllPools();
        await ExecuteAsync("postgres", $"drop database if exists \"{database}\" with (force);");
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
