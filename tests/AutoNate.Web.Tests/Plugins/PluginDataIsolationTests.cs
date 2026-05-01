using AutoNate.Web.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace AutoNate.Web.Tests.Plugins;

// Integration tests for the per-plugin Postgres isolation. Stands up the full
// host (so DatabaseSchemaInitializer wires the plg_readers group role and the
// plugins table extensions), then provisions a fake plugin schema and asserts
// the resulting role can only do what the design allows:
//
//   * write to its own schema
//   * read from public (app tables)
//   * read from another plugin's schema
//   * NOT write to public, NOT write to another plugin, NOT CREATE elsewhere.
//
// Each provisioned role is torn down at the end of the test — Postgres roles
// are cluster-wide and would otherwise outlive the per-test database.
[Trait("Category", "Integration")]
public sealed class PluginDataIsolationTests
{
    [Fact]
    public async Task Plugin_CanWriteOwnSchema_AndReadPublic_ButNotWriteOthers()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        // Force the host to start so DatabaseSchemaInitializer runs.
        _ = factory.CreateClient();

        var provisioner = factory.Services.GetRequiredService<PluginSchemaProvisioner>();
        var registry = factory.Services.GetRequiredService<PluginDataAccessRegistry>();

        var pluginA = await provisioner.ProvisionAsync();
        var pluginB = await provisioner.ProvisionAsync();
        try
        {
            // Use the registry to build a real per-plugin NpgsqlDataSource so
            // we exercise the same code path the runtime uses.
            var schemaA = PluginSchemaProvisioner.SchemaNameFor(pluginA.Code);
            var schemaB = PluginSchemaProvisioner.SchemaNameFor(pluginB.Code);

            // Plugin A: create a table in its own schema as its role.
            var dataA = registry.GetDataSource(pluginA.Code, pluginA.EncryptedPassword);
            await using (var conn = dataA.CreateConnection())
            {
                await conn.OpenAsync();
                await ExecAsync(conn, "CREATE TABLE widgets (id SERIAL PRIMARY KEY, label TEXT NOT NULL);");
                await ExecAsync(conn, "INSERT INTO widgets (label) VALUES ('hello');");
                var count = await ScalarAsync<long>(conn, "SELECT COUNT(*) FROM widgets;");
                Assert.Equal(1, count);
            }

            // Plugin A reads public successfully (plg_readers grants SELECT on
            // every public table; the plugins table itself is a useful probe).
            await using (var conn = dataA.CreateConnection())
            {
                await conn.OpenAsync();
                _ = await ScalarAsync<long>(conn, "SELECT COUNT(*) FROM public.plugins;");
            }

            // Plugin A cannot write to public — no INSERT grant.
            await using (var conn = dataA.CreateConnection())
            {
                await conn.OpenAsync();
                var ex = await Assert.ThrowsAsync<PostgresException>(async () =>
                    await ExecAsync(conn,
                        "INSERT INTO public.plugins (id, name, version, entry_assembly, status, uploaded_at, uploaded_by) " +
                        "VALUES (gen_random_uuid(), 'x', '1', 'x.dll', 0, NOW(), gen_random_uuid());"));
                Assert.Equal("42501", ex.SqlState); // insufficient_privilege
            }

            // Plugin A cannot CREATE in public — no CREATE grant on public schema for plg_readers.
            await using (var conn = dataA.CreateConnection())
            {
                await conn.OpenAsync();
                var ex = await Assert.ThrowsAsync<PostgresException>(async () =>
                    await ExecAsync(conn, "CREATE TABLE public.shouldnt_exist (id INT);"));
                Assert.Equal("42501", ex.SqlState);
            }

            // Plugin B can SELECT from plugin A's schema (cross-plugin read via plg_readers).
            var dataB = registry.GetDataSource(pluginB.Code, pluginB.EncryptedPassword);
            await using (var conn = dataB.CreateConnection())
            {
                await conn.OpenAsync();
                var label = await ScalarAsync<string>(conn, $"SELECT label FROM \"{schemaA}\".widgets LIMIT 1;");
                Assert.Equal("hello", label);
            }

            // Plugin B cannot INSERT into plugin A's schema.
            await using (var conn = dataB.CreateConnection())
            {
                await conn.OpenAsync();
                var ex = await Assert.ThrowsAsync<PostgresException>(async () =>
                    await ExecAsync(conn, $"INSERT INTO \"{schemaA}\".widgets (label) VALUES ('intruder');"));
                Assert.Equal("42501", ex.SqlState);
            }
        }
        finally
        {
            await registry.RemoveAsync(pluginA.Code);
            await registry.RemoveAsync(pluginB.Code);
            await provisioner.TeardownAsync(pluginA.Code);
            await provisioner.TeardownAsync(pluginB.Code);
        }
    }

    [Fact]
    public async Task MigrationRunner_AppliesEachFileOnce_AndIsIdempotentAcrossEnables()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        _ = factory.CreateClient();

        var provisioner = factory.Services.GetRequiredService<PluginSchemaProvisioner>();
        var runner = factory.Services.GetRequiredService<PluginMigrationRunner>();

        var plugin = await provisioner.ProvisionAsync();
        var folder = Path.Combine(Path.GetTempPath(), "autonate-migration-test-" + Guid.NewGuid().ToString("N"));
        var migrationsDir = Path.Combine(folder, "migrations");
        Directory.CreateDirectory(migrationsDir);

        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(migrationsDir, "001_init.sql"),
                "CREATE TABLE first_table (id INT);");
            await File.WriteAllTextAsync(
                Path.Combine(migrationsDir, "002_second.sql"),
                "CREATE TABLE second_table (id INT);");

            var first = await runner.RunAsync(plugin.Code, plugin.EncryptedPassword, folder);
            Assert.True(first.Success);
            Assert.Equal(2, first.Applied);

            // Re-running with the same files should apply 0 (already tracked).
            var second = await runner.RunAsync(plugin.Code, plugin.EncryptedPassword, folder);
            Assert.True(second.Success);
            Assert.Equal(0, second.Applied);

            // Adding a new file applies just it.
            await File.WriteAllTextAsync(
                Path.Combine(migrationsDir, "003_third.sql"),
                "CREATE TABLE third_table (id INT);");
            var third = await runner.RunAsync(plugin.Code, plugin.EncryptedPassword, folder);
            Assert.True(third.Success);
            Assert.Equal(1, third.Applied);
        }
        finally
        {
            try { Directory.Delete(folder, recursive: true); } catch { /* best-effort */ }
            var registry = factory.Services.GetRequiredService<PluginDataAccessRegistry>();
            await registry.RemoveAsync(plugin.Code);
            await provisioner.TeardownAsync(plugin.Code);
        }
    }

    [Fact]
    public async Task MigrationRunner_FailedFile_ReportsName_AndDoesNotMarkApplied()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        _ = factory.CreateClient();

        var provisioner = factory.Services.GetRequiredService<PluginSchemaProvisioner>();
        var runner = factory.Services.GetRequiredService<PluginMigrationRunner>();

        var plugin = await provisioner.ProvisionAsync();
        var folder = Path.Combine(Path.GetTempPath(), "autonate-migration-fail-" + Guid.NewGuid().ToString("N"));
        var migrationsDir = Path.Combine(folder, "migrations");
        Directory.CreateDirectory(migrationsDir);

        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(migrationsDir, "001_ok.sql"),
                "CREATE TABLE ok_table (id INT);");
            await File.WriteAllTextAsync(
                Path.Combine(migrationsDir, "002_broken.sql"),
                "this is not valid sql;");

            var outcome = await runner.RunAsync(plugin.Code, plugin.EncryptedPassword, folder);
            Assert.False(outcome.Success);
            Assert.Equal(1, outcome.Applied); // 001 succeeded
            Assert.Equal("002_broken.sql", outcome.FailedFile);

            // Fix the broken file; the runner should resume from 002.
            await File.WriteAllTextAsync(
                Path.Combine(migrationsDir, "002_broken.sql"),
                "CREATE TABLE recovered (id INT);");
            var resumed = await runner.RunAsync(plugin.Code, plugin.EncryptedPassword, folder);
            Assert.True(resumed.Success);
            Assert.Equal(1, resumed.Applied);
        }
        finally
        {
            try { Directory.Delete(folder, recursive: true); } catch { /* best-effort */ }
            var registry = factory.Services.GetRequiredService<PluginDataAccessRegistry>();
            await registry.RemoveAsync(plugin.Code);
            await provisioner.TeardownAsync(plugin.Code);
        }
    }

    [Fact]
    public async Task Teardown_AfterDataWritten_RemovesSchemaAndRole()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        _ = factory.CreateClient();

        var provisioner = factory.Services.GetRequiredService<PluginSchemaProvisioner>();
        var registry = factory.Services.GetRequiredService<PluginDataAccessRegistry>();

        var plugin = await provisioner.ProvisionAsync();
        var schema = PluginSchemaProvisioner.SchemaNameFor(plugin.Code);
        var role = PluginSchemaProvisioner.RoleNameFor(plugin.Code);

        var dataSource = registry.GetDataSource(plugin.Code, plugin.EncryptedPassword);
        await using (var conn = dataSource.CreateConnection())
        {
            await conn.OpenAsync();
            await ExecAsync(conn, "CREATE TABLE evidence (id INT);");
            await ExecAsync(conn, "INSERT INTO evidence VALUES (1);");
        }

        await registry.RemoveAsync(plugin.Code);
        await provisioner.TeardownAsync(plugin.Code);

        // Use the host connection (autonate role) to confirm the schema and
        // role are gone.
        await using var hostConn = new NpgsqlConnection(factory.Database.ConnectionString);
        await hostConn.OpenAsync();
        var schemaCount = await ScalarAsync<long>(hostConn,
            "SELECT COUNT(*) FROM pg_namespace WHERE nspname = @s;", ("@s", schema));
        var roleCount = await ScalarAsync<long>(hostConn,
            "SELECT COUNT(*) FROM pg_roles WHERE rolname = @r;", ("@r", role));
        Assert.Equal(0, schemaCount);
        Assert.Equal(0, roleCount);
    }

    private static async Task ExecAsync(NpgsqlConnection connection, string sql)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<T> ScalarAsync<T>(
        NpgsqlConnection connection,
        string sql,
        params (string Name, object Value)[] parameters)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            cmd.Parameters.AddWithValue(name, value);
        }
        var result = await cmd.ExecuteScalarAsync();
        return (T)Convert.ChangeType(result!, typeof(T));
    }
}
