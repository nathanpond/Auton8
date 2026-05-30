using System.Security.Cryptography;
using AutoNate.Web.Persistence;
using AutoNate.Web.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace AutoNate.Web.Services.DataStores.Sql;

// Second-DB initializer per docs/plans/2026-05-30-data-stores-implementation.md.
// Ensures `autonate_datastores` exists on the Postgres cluster, plus a shared
// writer role used by the per-datastore SQL provisioner. If
// `ConnectionStrings:Datastores` is absent, the feature is disabled — the
// initializer logs a single Info line and returns without raising a
// SystemIssue (this is a "not configured" state, not a fault). All
// SqlType DataStore endpoints subsequently surface a clean 503 when called.
public sealed class DatastoresDatabaseInitializer(
    IConfiguration configuration,
    IDataPaths dataPaths,
    IOptions<DatastoresDatabaseOptions> options,
    ILogger<DatastoresDatabaseInitializer> log) : IDatabaseInitializer
{
    public int Order => 10;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var connectionString = configuration.GetConnectionString("Datastores");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            log.LogInformation(
                "ConnectionStrings:Datastores not configured; SqlType DataStores feature disabled. " +
                "Set DataStores__Sql__WriterRolePassword and ConnectionStrings__Datastores to enable.");
            return;
        }

        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        var targetDatabase = builder.Database;
        if (string.IsNullOrWhiteSpace(targetDatabase))
        {
            log.LogWarning("ConnectionStrings:Datastores has no Database= component; cannot bootstrap.");
            return;
        }

        await EnsureDatabaseExistsAsync(builder, targetDatabase, cancellationToken);
        await EnsureWriterRoleAsync(connectionString, options.Value, cancellationToken);
    }

    // CREATE DATABASE cannot run inside a transaction, and there's no
    // CREATE DATABASE IF NOT EXISTS — we have to probe pg_database first.
    // We connect to the cluster's maintenance DB (default "postgres") using
    // the same credentials as the target connection.
    private async Task EnsureDatabaseExistsAsync(
        NpgsqlConnectionStringBuilder builder,
        string targetDatabase,
        CancellationToken cancellationToken)
    {
        var maintenance = new NpgsqlConnectionStringBuilder(builder.ConnectionString)
        {
            Database = "postgres",
            Pooling = false,
        };
        await using var conn = new NpgsqlConnection(maintenance.ConnectionString);
        await conn.OpenAsync(cancellationToken);

        await using var probe = new NpgsqlCommand(
            "SELECT 1 FROM pg_database WHERE datname = @name", conn);
        probe.Parameters.AddWithValue("name", targetDatabase);
        var exists = await probe.ExecuteScalarAsync(cancellationToken);
        if (exists is not null)
        {
            log.LogDebug("Datastores DB '{Name}' already present.", targetDatabase);
            return;
        }

        // No parameter binding for the database identifier — CREATE DATABASE
        // doesn't accept placeholders. Quote-escape the literal instead. The
        // identifier is operator-supplied via config, not request data.
        var quoted = QuoteIdentifier(targetDatabase);
        await using var create = new NpgsqlCommand($"CREATE DATABASE {quoted}", conn);
        await create.ExecuteNonQueryAsync(cancellationToken);
        log.LogInformation("Created datastores DB '{Name}'.", targetDatabase);
    }

    private async Task EnsureWriterRoleAsync(
        string targetConnectionString,
        DatastoresDatabaseOptions opts,
        CancellationToken cancellationToken)
    {
        var role = opts.WriterRole;
        if (string.IsNullOrWhiteSpace(role)) return;
        var password = opts.WriterRolePassword ?? LoadOrGenerateWriterPassword();

        await using var conn = new NpgsqlConnection(targetConnectionString);
        await conn.OpenAsync(cancellationToken);

        // CREATE ROLE … IF NOT EXISTS doesn't exist; use a DO block that
        // catches duplicate_object. Password set with ALTER ROLE so an
        // existing role gets its password kept in sync with what AutoNate
        // expects (otherwise a rotated config would leave the SqlDataStore
        // provisioner unable to GRANT into schemas it owns).
        var quotedRole = QuoteIdentifier(role);
        var literalPassword = QuoteLiteral(password);
        var sql =
            $$"""
            DO $$
            BEGIN
                CREATE ROLE {{quotedRole}} LOGIN PASSWORD {{literalPassword}};
            EXCEPTION WHEN duplicate_object THEN
                ALTER ROLE {{quotedRole}} WITH LOGIN PASSWORD {{literalPassword}};
            END $$;
            """;
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
        log.LogDebug("Ensured datastores writer role '{Role}'.", role);
    }

    private string LoadOrGenerateWriterPassword()
    {
        var secretPath = Path.Combine(dataPaths.Root, "datastores-writer.secret");
        if (System.IO.File.Exists(secretPath))
        {
            return System.IO.File.ReadAllText(secretPath).Trim();
        }
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        var generated = Convert.ToBase64String(bytes);
        System.IO.File.WriteAllText(secretPath, generated);
        if (!OperatingSystem.IsWindows())
        {
            try
            {
                System.IO.File.SetUnixFileMode(secretPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
            catch (PlatformNotSupportedException)
            {
                // Defensive: SetUnixFileMode is documented as unsupported on
                // Windows but the OS check above already routes around it.
            }
        }
        log.LogWarning(
            "DataStores__Sql__WriterRolePassword was not configured; generated one at {Path}. " +
            "Move it to your secret store and clear the file in production.",
            secretPath);
        return generated;
    }

    private static string QuoteIdentifier(string identifier)
    {
        // ANSI/Postgres double-quote identifier with internal " escaping.
        return "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    private static string QuoteLiteral(string literal)
    {
        return "'" + literal.Replace("'", "''", StringComparison.Ordinal) + "'";
    }
}
