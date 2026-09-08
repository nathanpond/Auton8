using AutoNate.Web.Tests;
using Npgsql;
using Xunit;
using Xunit.Abstractions;

namespace AutoNate.Web.Tests.Infrastructure;

/// <summary>
/// The suite cleans up after itself, and does not clean up after anyone else (#191).
/// </summary>
/// <remarks>
/// One database per test class, dropped on disposal — except when a run is killed, a
/// process crashes, or disposal throws. Those strand a database in a Postgres every
/// developer and every run shares. There were **1,680** when this was written.
/// </remarks>
[Trait("Category", "Integration")]
public sealed class TestDatabaseSweepTests
{
    private readonly ITestOutputHelper _output;

    public TestDatabaseSweepTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task The_sweep_leaves_a_database_a_live_run_is_using()
    {
        // The criterion that matters most: a sweep that cannot tell a live database
        // from a stranded one is worse than the leak it fixes, because it drops a
        // database out from under a running test class and the failure looks like a
        // random flake somewhere else entirely.
        await using var live = await PostgresTestDatabase.CreateAsync(seedLocalAdmin: false);

        // Sweep everything older than an hour. The database above was created
        // moments ago, so it must survive.
        //
        // #112: this used to say "older than a minute", and that made the
        // assertion depend on how long the test itself took. Under full-suite
        // load CreateAsync — migrations, seeding, and its own turn through the
        // connection pool — ran for over six minutes, so by the time the sweep
        // executed the database really was older than the threshold and really
        // was abandoned by the rule being tested. It failed honestly; the premise
        // was wrong.
        //
        // An hour cannot be reached by this test on any machine. Nothing is lost
        // by widening it: what stops this from passing against a sweep that has
        // given up entirely is A_stamped_database_is_still_swept_when_it_is_old_enough,
        // not the size of this number.
        await PostgresTestDatabase.SweepAbandonedDatabasesAsync(TimeSpan.FromHours(1));

        Assert.True(await ExistsAsync(live), "The sweep dropped a database created moments earlier.");

        // And it is still usable, not merely present.
        await using var connection = new NpgsqlConnection(live.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "select 1;";
        Assert.Equal(1, Convert.ToInt32(await command.ExecuteScalarAsync()));
    }

    [Fact]
    public async Task The_sweep_removes_one_that_is_old_enough()
    {
        // The complement. Without it the test above passes for a sweep that does
        // nothing at all — which is the failure mode a cautious implementation
        // reaches for.
        var abandoned = $"autonate_test_{Guid.NewGuid():N}";
        await ExecuteOnPostgresAsync($"create database \"{abandoned}\";");

        try
        {
            // Stamp it as created an hour ago, which is what a run killed an hour
            // ago would have left.
            await ExecuteOnPostgresAsync(
                $"comment on database \"{abandoned}\" is '{DateTimeOffset.UtcNow.AddHours(-1):O}';");

            var dropped = await PostgresTestDatabase.SweepAbandonedDatabasesAsync(TimeSpan.FromMinutes(30));

            Assert.True(dropped > 0, "The sweep reported dropping nothing.");
            Assert.False(await ExistsByNameAsync(abandoned), "The stale database survived the sweep.");
        }
        finally
        {
            await ExecuteOnPostgresAsync($"drop database if exists \"{abandoned}\" with (force);");
        }
    }

    // #215. This test used to be A_database_with_no_stamp_is_treated_as_abandoned,
    // and it pinned the behaviour rather than the intent: "a database with no stamp
    // cannot have been created by a live run of this code". That was false, and the
    // failure it caused was remote from here.
    //
    // InitializeAsync creates a database and stamps it in two separate statements.
    // In between, the database exists, has no connections yet, and has no comment —
    // so it satisfies the liveness filter AND the missing-stamp rule at the same
    // time, and `drop database … with (force)` takes it out from under the test
    // about to use it. That surfaced as EntityEdgeWriterTests failing a full run
    // with "57P01: terminating connection due to administrator command", which
    // reads as a Postgres problem rather than as a sweep dropping a live database.
    //
    // The backlog the old rule existed to clear is gone (the cluster holds none),
    // so it was buying nothing and costing that.
    [Fact]
    public async Task A_database_with_no_stamp_is_left_alone()
    {
        var unstamped = $"autonate_test_{Guid.NewGuid():N}";
        await ExecuteOnPostgresAsync($"create database \"{unstamped}\";");

        try
        {
            // Deliberately no comment, and a threshold generous enough that age
            // cannot be what saves it — the missing stamp has to be.
            await PostgresTestDatabase.SweepAbandonedDatabasesAsync(TimeSpan.FromDays(365));

            Assert.True(
                await ExistsByNameAsync(unstamped),
                "An unstamped database was swept. A database is unstamped for the moment between " +
                "CREATE DATABASE and COMMENT ON DATABASE, so sweeping one drops a database a run " +
                "is in the middle of creating.");
        }
        finally
        {
            await ExecuteOnPostgresAsync($"drop database if exists \"{unstamped}\" with (force);");
        }
    }

    // The complement, so the rule above cannot decay into "the sweep spares
    // everything": a stamped, genuinely old database is still swept. Without this,
    // A_database_with_no_stamp_is_left_alone passes against a sweep that has
    // stopped working entirely.
    [Fact]
    public async Task A_stamped_database_is_still_swept_when_it_is_old_enough()
    {
        var abandoned = $"autonate_test_{Guid.NewGuid():N}";
        await ExecuteOnPostgresAsync($"create database \"{abandoned}\";");

        try
        {
            await ExecuteOnPostgresAsync(
                $"comment on database \"{abandoned}\" is '{DateTimeOffset.UtcNow.AddDays(-2):O}';");

            await PostgresTestDatabase.SweepAbandonedDatabasesAsync(TimeSpan.FromHours(1));

            Assert.False(await ExistsByNameAsync(abandoned), "A stamped, stale database survived the sweep.");
        }
        finally
        {
            await ExecuteOnPostgresAsync($"drop database if exists \"{abandoned}\" with (force);");
        }
    }

    [Fact]
    public async Task A_factory_whose_disposal_throws_still_drops_its_database()
    {
        // The leak's own path. `DisposeAsync` called `base.DisposeAsync()` first with
        // nothing between them, so anything the host threw on teardown stranded the
        // database — invisibly, until someone counted.
        var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var database = factory.Database;
        _ = factory.CreateClient();

        // Force the failure: disposing the service provider out from under the base
        // implementation makes its own teardown throw.
        var thrown = false;
        try
        {
            await factory.DisposeAsync();
            await factory.DisposeAsync();
        }
        catch
        {
            thrown = true;
        }

        // Whether or not the second disposal threw, the database must be gone.
        Assert.False(await ExistsAsync(database),
            $"The database survived disposal (second dispose threw: {thrown}).");
        _output.WriteLine($"second dispose threw: {thrown}");
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static Task<bool> ExistsAsync(PostgresTestDatabase database) =>
        ExistsByNameAsync(NameOf(database));

    private static string NameOf(PostgresTestDatabase database)
    {
        // The connection string is the only public surface carrying the name.
        var builder = new NpgsqlConnectionStringBuilder(database.ConnectionString);
        return builder.Database!;
    }

    private static async Task<bool> ExistsByNameAsync(string name)
    {
        await using var connection = new NpgsqlConnection(
            PostgresTestDatabase.AdminConnectionStringFor("postgres"));
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "select 1 from pg_database where datname = @name;";
        command.Parameters.AddWithValue("name", name);
        return await command.ExecuteScalarAsync() is not null;
    }

    private static async Task ExecuteOnPostgresAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(
            PostgresTestDatabase.AdminConnectionStringFor("postgres"));
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}
