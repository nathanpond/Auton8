using Npgsql;

namespace AutoNate.Web.Plugins;

public sealed record PluginMigrationOutcome(bool Success, int Applied, string? FailedFile, string? ErrorMessage);

// Applies any new SQL files in `<pluginFolder>/migrations/` to the plugin's
// own schema, in lexical order. Tracking lives in `__plugin_migrations`
// inside that schema, so a plugin's migration history travels with its data:
// disable doesn't lose it, delete drops it with the schema.
//
// Each file runs in its own transaction (BEGIN, exec, INSERT tracking row,
// COMMIT). A failure halts the run with the offending filename in the
// outcome, and the caller (PluginRuntime) treats this exactly like a failed
// Configure(): the plugin is disabled and its row's last_error is set.
public sealed class PluginMigrationRunner
{
    private readonly PluginDataAccessRegistry _registry;
    private readonly ILogger<PluginMigrationRunner> _log;

    public PluginMigrationRunner(
        PluginDataAccessRegistry registry,
        ILogger<PluginMigrationRunner> log)
    {
        _registry = registry;
        _log = log;
    }

    public async Task<PluginMigrationOutcome> RunAsync(
        string code,
        byte[] encryptedPassword,
        string pluginFolder,
        CancellationToken ct = default)
    {
        var migrationsDir = Path.Combine(pluginFolder, "migrations");
        if (!Directory.Exists(migrationsDir))
        {
            return new(true, 0, null, null);
        }

        var files = Directory.EnumerateFiles(migrationsDir, "*.sql")
            .OrderBy(p => Path.GetFileName(p), StringComparer.Ordinal)
            .ToList();
        if (files.Count == 0)
        {
            return new(true, 0, null, null);
        }

        var dataSource = _registry.GetDataSource(code, encryptedPassword);
        await using var connection = dataSource.CreateConnection();
        await connection.OpenAsync(ct);

        await EnsureTrackingTableAsync(connection, ct);
        var applied = await GetAppliedMigrationsAsync(connection, ct);

        var newCount = 0;
        foreach (var file in files)
        {
            var name = Path.GetFileName(file);
            if (applied.Contains(name)) continue;

            var sql = await File.ReadAllTextAsync(file, ct);
            try
            {
                await ApplyMigrationAsync(connection, name, sql, ct);
                newCount++;
                _log.LogInformation("Applied plugin migration {File} for code {Code}.", name, code);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Plugin migration {File} failed for code {Code}.", name, code);
                return new(false, newCount, name, ex.Message);
            }
        }

        return new(true, newCount, null, null);
    }

    private static async Task EnsureTrackingTableAsync(NpgsqlConnection connection, CancellationToken ct)
    {
        // The plugin role is the owner of its own schema, so it can create
        // this table directly. Search path is already plg_<code>,public so
        // no qualification is needed.
        await using var cmd = connection.CreateCommand();
        cmd.CommandText =
            """
            CREATE TABLE IF NOT EXISTS __plugin_migrations (
                name TEXT PRIMARY KEY,
                applied_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );
            """;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task<HashSet<string>> GetAppliedMigrationsAsync(NpgsqlConnection connection, CancellationToken ct)
    {
        var applied = new HashSet<string>(StringComparer.Ordinal);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT name FROM __plugin_migrations;";
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            applied.Add(reader.GetString(0));
        }
        return applied;
    }

    private static async Task ApplyMigrationAsync(
        NpgsqlConnection connection,
        string name,
        string sql,
        CancellationToken ct)
    {
        await using var tx = await connection.BeginTransactionAsync(ct);
        try
        {
            await using (var migrationCmd = connection.CreateCommand())
            {
                migrationCmd.Transaction = tx;
                migrationCmd.CommandText = sql;
                await migrationCmd.ExecuteNonQueryAsync(ct);
            }

            await using (var insertCmd = connection.CreateCommand())
            {
                insertCmd.Transaction = tx;
                insertCmd.CommandText = "INSERT INTO __plugin_migrations (name) VALUES (@name);";
                insertCmd.Parameters.AddWithValue("@name", name);
                await insertCmd.ExecuteNonQueryAsync(ct);
            }

            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }
}
