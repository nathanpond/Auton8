using AutoNate.Web.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace AutoNate.Web.Tests.Persistence;

/// <summary>
/// The base schema is one file, owned and applied by the application.
/// </summary>
/// <remarks>
/// It used to live in <c>infra/postgres/init/</c> and be mounted into the
/// Postgres container's entrypoint, which meant the application could not
/// initialise an empty database on its own: it depended on a file being
/// mounted at exactly the right moment, kept in step by hand across three
/// consumers.
/// </remarks>
public sealed class BaseSchemaSingleSourceTests
{
    private static ServiceProvider BuildProvider(string connectionString) =>
        new ServiceCollection()
            .AddLogging()
            .AddDbContext<AutoNateDbContext>(o => o.UseNpgsql(connectionString))
            .BuildServiceProvider();

    [Fact]
    public async Task An_empty_database_is_initialised_by_the_application_alone()
    {
        // The case that was impossible before this story: a database with the
        // tables created and nothing else — no init script, no mounted file.
        var name = $"autonate_empty_{Guid.NewGuid():N}";
        var admin = PostgresTestDatabase.AdminConnectionStringFor("postgres");
        var target = PostgresTestDatabase.AdminConnectionStringFor(name);

        await using (var connection = new NpgsqlConnection(admin))
        {
            await connection.OpenAsync();
            await using var create = connection.CreateCommand();
            create.CommandText = $"CREATE DATABASE \"{name}\";";
            await create.ExecuteNonQueryAsync();
        }

        try
        {
            // Precondition: genuinely empty.
            await using (var probe = new NpgsqlConnection(target))
            {
                await probe.OpenAsync();
                await using var count = probe.CreateCommand();
                count.CommandText =
                    "SELECT count(*) FROM information_schema.tables WHERE table_schema = 'public';";
                Assert.Equal(0L, (long)(await count.ExecuteScalarAsync())!);
            }

            await using var provider = BuildProvider(target);
            await DatabaseSchemaInitializer.EnsureAsync(provider);

            await using var check = new NpgsqlConnection(target);
            await check.OpenAsync();
            await using var tables = check.CreateCommand();
            tables.CommandText =
                "SELECT count(*) FROM information_schema.tables WHERE table_schema = 'public' "
                + "AND table_name IN ('local_users', 'workflow_models', 'schema_versions');";

            Assert.Equal(3L, (long)(await tables.ExecuteScalarAsync())!);
        }
        finally
        {
            await using var connection = new NpgsqlConnection(admin);
            await connection.OpenAsync();
            await using var drop = connection.CreateCommand();
            drop.CommandText = $"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE);";
            await drop.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public void The_base_schema_exists_exactly_once_in_the_repository()
    {
        // The guard against the hand-syncing coming back. Recognised by a
        // distinctive statement rather than by filename, so a copy under a
        // different name is still caught.
        const string Signature = "CREATE TABLE IF NOT EXISTS local_users";

        var root = Infrastructure.RepoRoot.Path;
        var matches = Directory
            .EnumerateFiles(root, "*.sql", SearchOption.AllDirectories)
            .Where(p =>
            {
                var rel = Path.GetRelativePath(root, p).Replace(Path.DirectorySeparatorChar, '/');
                return !rel.Contains("/bin/", StringComparison.Ordinal)
                    && !rel.Contains("/obj/", StringComparison.Ordinal)
                    && !rel.Contains("/node_modules/", StringComparison.Ordinal);
            })
            .Where(p => File.ReadAllText(p).Contains(Signature, StringComparison.Ordinal))
            .Select(p => Path.GetRelativePath(root, p))
            .ToList();

        Assert.True(matches.Count == 1,
            "The base schema must exist exactly once. Found "
            + $"{matches.Count}:\n  {string.Join("\n  ", matches)}");
    }

    [Fact]
    public void The_embedded_resource_is_readable_and_is_the_base_schema()
    {
        var sql = DatabaseSchemaInitializer.ReadBaseSchemaSql();

        Assert.Contains("CREATE TABLE IF NOT EXISTS local_users", sql, StringComparison.Ordinal);

        // No psql meta-commands: this is executed through Npgsql, which would
        // reject a `\c` as a syntax error.
        Assert.DoesNotContain("\n\\c ", sql, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Initialising_twice_and_over_an_existing_install_both_converge()
    {
        await using var database = await PostgresTestDatabase.CreateAsync(seedLocalAdmin: false);
        await using var provider = BuildProvider(database.ConnectionString);

        // The fixture has already applied the base schema, so this is the
        // "existing install" path; running twice more must be a no-op.
        await DatabaseSchemaInitializer.EnsureAsync(provider);
        await DatabaseSchemaInitializer.EnsureAsync(provider);

        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM local_users;";

        Assert.Equal(0L, (long)(await command.ExecuteScalarAsync())!);
    }
}
