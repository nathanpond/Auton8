using System.Diagnostics;
using AutoNate.Web.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace AutoNate.Web.Tests.Persistence;

/// <summary>
/// Two hosts starting against one database must be a defined outcome.
/// </summary>
/// <remarks>
/// <c>EnsureAsync</c> runs roughly ninety DDL batches. Each is individually
/// idempotent, which is not the same as concurrency-safe: two sessions issuing
/// <c>CREATE INDEX IF NOT EXISTS</c> or <c>ALTER TABLE ... ADD COLUMN IF NOT
/// EXISTS</c> against the same relation deadlock or fail on a duplicate object
/// rather than one waiting for the other.
/// </remarks>
public sealed class SchemaInitializerConcurrencyTests
{
    private const long SchemaInitLockKey = 0x4175746F6E387631L;

    private static ServiceProvider BuildProvider(string connectionString) =>
        new ServiceCollection()
            .AddLogging()
            .AddDbContext<AutoNateDbContext>(o => o.UseNpgsql(connectionString))
            .BuildServiceProvider();

    [Fact]
    public async Task Two_concurrent_initialisations_of_one_database_both_succeed()
    {
        // The story's core assertion, and the one that fails when the lock is
        // removed — observed before this landed rather than assumed.
        await using var database = await PostgresTestDatabase.CreateAsync(seedLocalAdmin: false);

        await using var first = BuildProvider(database.ConnectionString);
        await using var second = BuildProvider(database.ConnectionString);

        var a = DatabaseSchemaInitializer.EnsureAsync(first);
        var b = DatabaseSchemaInitializer.EnsureAsync(second);

        await Task.WhenAll(a, b);

        await using var context = database.CreateDbContextFactory().CreateDbContext();
        var tables = await context.Database
            .SqlQuery<string>($"select table_name from information_schema.tables where table_schema = 'public'")
            .ToListAsync();

        Assert.Contains("local_users", tables);
        Assert.Contains("workflow_models", tables);
    }

    [Fact]
    public async Task The_second_caller_waits_rather_than_running_concurrently()
    {
        await using var database = await PostgresTestDatabase.CreateAsync(seedLocalAdmin: false);

        // Hold the lock from outside, exactly as another host would.
        await using var holder = new NpgsqlConnection(database.ConnectionString);
        await holder.OpenAsync();
        await using (var take = holder.CreateCommand())
        {
            take.CommandText = "SELECT pg_advisory_lock(@key);";
            take.Parameters.AddWithValue("key", SchemaInitLockKey);
            await take.ExecuteScalarAsync();
        }

        await using var provider = BuildProvider(database.ConnectionString);

        var started = Stopwatch.StartNew();
        var initialisation = DatabaseSchemaInitializer.EnsureAsync(provider);

        // While the lock is held the initialiser must not have finished. If it
        // had, it would have run its DDL alongside the holder — the race this
        // exists to prevent.
        await Task.Delay(TimeSpan.FromSeconds(2));
        Assert.False(initialisation.IsCompleted);

        await using (var release = holder.CreateCommand())
        {
            release.CommandText = "SELECT pg_advisory_unlock(@key);";
            release.Parameters.AddWithValue("key", SchemaInitLockKey);
            await release.ExecuteScalarAsync();
        }

        await initialisation;
        started.Stop();

        Assert.True(started.Elapsed >= TimeSpan.FromSeconds(2),
            $"Expected the initialiser to wait for the lock; it finished in {started.Elapsed}.");
    }

    [Fact]
    public async Task The_lock_is_released_so_a_later_run_can_take_it()
    {
        await using var database = await PostgresTestDatabase.CreateAsync(seedLocalAdmin: false);

        await using (var provider = BuildProvider(database.ConnectionString))
        {
            await DatabaseSchemaInitializer.EnsureAsync(provider);
        }

        // A leaked lock would make the next host wait out the full timeout for
        // a lock nobody owns.
        await using var probe = new NpgsqlConnection(database.ConnectionString);
        await probe.OpenAsync();
        await using var command = probe.CreateCommand();
        command.CommandText = "SELECT pg_try_advisory_lock(@key);";
        command.Parameters.AddWithValue("key", SchemaInitLockKey);

        var acquired = (bool?)await command.ExecuteScalarAsync() ?? false;
        Assert.True(acquired, "The schema-initialisation lock was still held after EnsureAsync returned.");
    }

    [Fact]
    public async Task A_second_run_against_an_initialised_database_still_succeeds()
    {
        // Idempotence asserted alongside the locking, so a lock that somehow
        // changed the batches' behaviour would surface here.
        await using var database = await PostgresTestDatabase.CreateAsync(seedLocalAdmin: false);

        await using var provider = BuildProvider(database.ConnectionString);
        await DatabaseSchemaInitializer.EnsureAsync(provider);
        await DatabaseSchemaInitializer.EnsureAsync(provider);
    }
}
