using AutoNate.Web.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace AutoNate.Web.Tests.Persistence;

/// <summary>
/// The schema ledger: what has been applied, by which build, and when.
/// </summary>
public sealed class SchemaVersionLedgerTests
{
    private static ServiceProvider BuildProvider(string connectionString) =>
        new ServiceCollection()
            .AddLogging()
            .AddDbContext<AutoNateDbContext>(o => o.UseNpgsql(connectionString))
            .BuildServiceProvider();

    private static async Task<(int Count, string? Version)> ReadLedgerAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT count(*), (SELECT app_version FROM schema_versions LIMIT 1) FROM schema_versions;";
        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();
        return (Convert.ToInt32(reader.GetValue(0)), reader.IsDBNull(1) ? null : reader.GetString(1));
    }

    [Fact]
    public async Task First_run_records_every_step_with_the_application_version()
    {
        await using var database = await PostgresTestDatabase.CreateAsync(seedLocalAdmin: false);
        await using var provider = BuildProvider(database.ConnectionString);

        await DatabaseSchemaInitializer.EnsureAsync(provider);

        var (count, version) = await ReadLedgerAsync(database.ConnectionString);

        Assert.True(count > 50, $"Expected the ledger to record every schema batch; found {count}.");
        Assert.Equal(DatabaseSchemaInitializer.AppVersion, version);
    }

    [Fact]
    public async Task A_second_run_performs_no_schema_work()
    {
        // The point of the ledger. Asserted by the ledger being unchanged and
        // by the applied-at timestamps not moving — a re-run that re-executed
        // and re-recorded would look identical by row count alone.
        await using var database = await PostgresTestDatabase.CreateAsync(seedLocalAdmin: false);
        await using var provider = BuildProvider(database.ConnectionString);

        await DatabaseSchemaInitializer.EnsureAsync(provider);

        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();

        async Task<DateTime> MaxAppliedAsync()
        {
            // Npgsql maps timestamptz to DateTime through ExecuteScalar.
            await using var c = connection.CreateCommand();
            c.CommandText = "SELECT max(applied_at_utc) FROM schema_versions;";
            return Convert.ToDateTime(await c.ExecuteScalarAsync());
        }

        var before = await MaxAppliedAsync();
        var countBefore = (await ReadLedgerAsync(database.ConnectionString)).Count;

        await DatabaseSchemaInitializer.EnsureAsync(provider);

        var after = await MaxAppliedAsync();
        var countAfter = (await ReadLedgerAsync(database.ConnectionString)).Count;

        Assert.Equal(countBefore, countAfter);
        Assert.Equal(before, after);
    }

    [Fact]
    public async Task A_database_that_predates_the_ledger_is_back_filled_without_failing()
    {
        // PostgresTestDatabase replays the base schema, so this database has
        // tables and no schema_versions table — exactly the shape of an
        // existing 0.1 install meeting this build for the first time.
        await using var database = await PostgresTestDatabase.CreateAsync(seedLocalAdmin: false);

        await using (var connection = new NpgsqlConnection(database.ConnectionString))
        {
            await connection.OpenAsync();
            await using var drop = connection.CreateCommand();
            drop.CommandText = "DROP TABLE IF EXISTS schema_versions;";
            await drop.ExecuteNonQueryAsync();
        }

        await using var provider = BuildProvider(database.ConnectionString);
        await DatabaseSchemaInitializer.EnsureAsync(provider);

        var (count, _) = await ReadLedgerAsync(database.ConnectionString);
        Assert.True(count > 50, $"Expected the ledger to be back-filled; found {count}.");
    }

    [Fact]
    public async Task Startup_refuses_when_the_database_was_initialised_by_a_newer_build()
    {
        await using var database = await PostgresTestDatabase.CreateAsync(seedLocalAdmin: false);
        await using var provider = BuildProvider(database.ConnectionString);

        await DatabaseSchemaInitializer.EnsureAsync(provider);

        // Synthesize a database written by a future build.
        await using (var connection = new NpgsqlConnection(database.ConnectionString))
        {
            await connection.OpenAsync();
            await using var insert = connection.CreateCommand();
            insert.CommandText =
                "INSERT INTO schema_versions (step_name, app_version, applied_at_utc) "
                + "VALUES ('FromTheFuture', '99.0.0', NOW());";
            await insert.ExecuteNonQueryAsync();
        }

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => DatabaseSchemaInitializer.EnsureAsync(provider));

        // Both versions and the step, so a rollback is diagnosable in one read.
        Assert.Contains("99.0.0", failure.Message, StringComparison.Ordinal);
        Assert.Contains(DatabaseSchemaInitializer.AppVersion, failure.Message, StringComparison.Ordinal);
        Assert.Contains("FromTheFuture", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Startup_proceeds_when_the_recorded_version_matches_or_is_older()
    {
        // The ordinary case, asserted so the guard cannot be trivially
        // over-strict and block every normal start.
        await using var database = await PostgresTestDatabase.CreateAsync(seedLocalAdmin: false);
        await using var provider = BuildProvider(database.ConnectionString);

        await DatabaseSchemaInitializer.EnsureAsync(provider);

        await using (var connection = new NpgsqlConnection(database.ConnectionString))
        {
            await connection.OpenAsync();
            await using var insert = connection.CreateCommand();
            insert.CommandText =
                "INSERT INTO schema_versions (step_name, app_version, applied_at_utc) "
                + "VALUES ('FromThePast', '0.0.1', NOW());";
            await insert.ExecuteNonQueryAsync();
        }

        await DatabaseSchemaInitializer.EnsureAsync(provider);
    }

    [Fact]
    public async Task A_batch_that_carries_its_own_auth_seed_state_gate_still_runs_after_being_recorded()
    {
        // The ledger must not become a second gate over data migrations that
        // already have one. Clearing an auth_seed_state marker is how an
        // operator (and RebrandMigrationTests) re-enables such a migration; if
        // the ledger skipped the step, that would silently do nothing.
        //
        // Demonstrated with the rebrand migration: rewind the branding and
        // clear its marker, run EnsureAsync again, and the rename must happen
        // even though the step is already in the ledger.
        await using var database = await PostgresTestDatabase.CreateAsync(seedLocalAdmin: false);
        await using var provider = BuildProvider(database.ConnectionString);

        await DatabaseSchemaInitializer.EnsureAsync(provider);

        await using (var connection = new NpgsqlConnection(database.ConnectionString))
        {
            await connection.OpenAsync();

            await using (var rewind = connection.CreateCommand())
            {
                rewind.CommandText =
                    "UPDATE site_appearance_settings SET site_name = 'Auto Nate', logo_text = 'Auto Nate'; "
                    + "DELETE FROM auth_seed_state WHERE key = 'rebrand_auton8_v1';";
                await rewind.ExecuteNonQueryAsync();
            }

            // Precondition: the step is recorded, so a naive ledger would skip it.
            await using var recorded = connection.CreateCommand();
            recorded.CommandText =
                // The rebrand lives inside SiteConfigFormsSql rather than in a
                // const of its own — that batch also carries the Form Mappings
                // stub retirement and the login-cover fix.
                "SELECT count(*) FROM schema_versions WHERE step_name = 'SiteConfigFormsSql';";
            var isRecorded = Convert.ToInt64(await recorded.ExecuteScalarAsync()) > 0;
            Assert.True(isRecorded, "Expected the rebrand step to be in the ledger before this assertion means anything.");
        }

        await DatabaseSchemaInitializer.EnsureAsync(provider);

        await using var check = new NpgsqlConnection(database.ConnectionString);
        await check.OpenAsync();
        await using var command = check.CreateCommand();
        command.CommandText = "SELECT site_name FROM site_appearance_settings LIMIT 1;";

        Assert.Equal("Auton8", (string)(await command.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task auth_seed_state_is_left_untouched()
    {
        // Its keys gate one-shot DATA migrations with their own semantics.
        // Conflating them with the schema ledger is a separate decision that
        // this story explicitly does not take.
        await using var database = await PostgresTestDatabase.CreateAsync(seedLocalAdmin: false);
        await using var provider = BuildProvider(database.ConnectionString);

        await DatabaseSchemaInitializer.EnsureAsync(provider);

        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT count(*) FROM information_schema.tables "
            + "WHERE table_schema = 'public' AND table_name = 'auth_seed_state';";

        Assert.Equal(1L, (long)(await command.ExecuteScalarAsync())!);
    }
}
