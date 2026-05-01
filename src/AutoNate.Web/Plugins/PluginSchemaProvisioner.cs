using System.Security.Cryptography;
using System.Text;
using AutoNate.Web.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AutoNate.Web.Plugins;

public sealed record PluginProvisioningResult(string Code, byte[] EncryptedPassword);

// Stands up Postgres-level isolation for a freshly uploaded plugin: a unique
// 8-char code, a per-plugin LOGIN role with a random password, and an owned
// schema. Grants are wired so the role:
//   * is owner of plg_<code> (full DDL/DML on its own schema),
//   * inherits read access to public via the plg_readers group role,
//   * lets every other plugin (also via plg_readers) SELECT from this schema.
//
// The password is encrypted with IDataProtector before persistence; the host
// is the only thing that can decrypt it to build a connection on the plugin's
// behalf.
public sealed class PluginSchemaProvisioner
{
    private const int CodeLength = 8;
    private const int MaxCollisionRetries = 8;
    private const string ProtectorPurpose = "AutoNate.Plugins.RolePassword.v1";

    private static readonly char[] CodeFirstChars = "abcdefghijklmnopqrstuvwxyz".ToCharArray();
    private static readonly char[] CodeRestChars = "abcdefghijklmnopqrstuvwxyz0123456789".ToCharArray();

    private readonly IDbContextFactory<AutoNateDbContext> _dbFactory;
    private readonly IDataProtector _protector;
    private readonly ILogger<PluginSchemaProvisioner> _log;

    public PluginSchemaProvisioner(
        IDbContextFactory<AutoNateDbContext> dbFactory,
        IDataProtectionProvider dataProtectionProvider,
        ILogger<PluginSchemaProvisioner> log)
    {
        _dbFactory = dbFactory;
        _protector = dataProtectionProvider.CreateProtector(ProtectorPurpose);
        _log = log;
    }

    public static string SchemaNameFor(string code) => "plg_" + code;
    public static string RoleNameFor(string code) => "plg_" + code;

    public string DecryptPassword(byte[] encrypted) =>
        Encoding.UTF8.GetString(_protector.Unprotect(encrypted));

    public async Task<PluginProvisioningResult> ProvisionAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(ct);
        }

        for (var attempt = 0; attempt < MaxCollisionRetries; attempt++)
        {
            var code = GenerateCode();
            if (await CodeExistsAsync(connection, code, ct))
            {
                continue;
            }

            var password = GeneratePassword();
            var schema = SchemaNameFor(code);
            var role = RoleNameFor(code);

            try
            {
                await using var tx = await connection.BeginTransactionAsync(ct);

                // CREATE ROLE / CREATE SCHEMA can't be parameterized; identifiers
                // are constrained to [a-z0-9_] by GenerateCode, and the password
                // is escaped via E'' literal handling below.
                await ExecuteNonQueryAsync(connection, tx,
                    $"CREATE ROLE \"{role}\" LOGIN PASSWORD {QuoteLiteral(password)};", ct);
                await ExecuteNonQueryAsync(connection, tx,
                    $"GRANT plg_readers TO \"{role}\";", ct);
                await ExecuteNonQueryAsync(connection, tx,
                    $"CREATE SCHEMA \"{schema}\" AUTHORIZATION \"{role}\";", ct);
                await ExecuteNonQueryAsync(connection, tx,
                    $"GRANT USAGE ON SCHEMA \"{schema}\" TO plg_readers;", ct);
                await ExecuteNonQueryAsync(connection, tx,
                    $"ALTER DEFAULT PRIVILEGES FOR ROLE \"{role}\" IN SCHEMA \"{schema}\" " +
                    "GRANT SELECT ON TABLES TO plg_readers;", ct);
                await ExecuteNonQueryAsync(connection, tx,
                    $"ALTER DEFAULT PRIVILEGES FOR ROLE \"{role}\" IN SCHEMA \"{schema}\" " +
                    "GRANT SELECT, USAGE ON SEQUENCES TO plg_readers;", ct);

                await tx.CommitAsync(ct);

                var encrypted = _protector.Protect(Encoding.UTF8.GetBytes(password));
                _log.LogInformation("Provisioned plugin schema {Schema} (role {Role}).", schema, role);
                return new PluginProvisioningResult(code, encrypted);
            }
            catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.DuplicateObject ||
                                                ex.SqlState == PostgresErrorCodes.DuplicateSchema)
            {
                // Extremely unlikely but possible if a parallel host race created
                // the same code or a leftover from a botched tear-down survives.
                // Retry with a fresh code.
                _log.LogWarning(ex, "Collision provisioning plugin code {Code}; retrying.", code);
                continue;
            }
        }

        throw new InvalidOperationException(
            $"Unable to provision plugin schema after {MaxCollisionRetries} attempts.");
    }

    public async Task TeardownAsync(string code, CancellationToken ct = default)
    {
        var schema = SchemaNameFor(code);
        var role = RoleNameFor(code);

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(ct);
        }

        // DROP SCHEMA CASCADE removes all tables and dependents. Only after
        // the schema (and its role-owned objects) are gone can the role itself
        // be dropped — Postgres rejects DROP ROLE while the role still owns
        // anything.
        await ExecuteNonQueryAsync(connection, transaction: null,
            $"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE;", ct);

        // REASSIGN any straggler ownership defensively before drop, then drop
        // any remaining grants the role was party to.
        await ExecuteNonQueryAsync(connection, transaction: null,
            $"DO $$ BEGIN " +
            $"IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{role}') THEN " +
            $"  EXECUTE 'REASSIGN OWNED BY \"{role}\" TO CURRENT_USER'; " +
            $"  EXECUTE 'DROP OWNED BY \"{role}\"'; " +
            $"  EXECUTE 'DROP ROLE \"{role}\"'; " +
            $"END IF; END $$;", ct);

        _log.LogInformation("Tore down plugin schema {Schema} (role {Role}).", schema, role);
    }

    private static async Task<bool> CodeExistsAsync(NpgsqlConnection connection, string code, CancellationToken ct)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM plugins WHERE code = @code LIMIT 1;";
        cmd.Parameters.AddWithValue("@code", code);
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is not null;
    }

    private static async Task ExecuteNonQueryAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string sql,
        CancellationToken ct)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        if (transaction is not null)
        {
            cmd.Transaction = transaction;
        }
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static string GenerateCode()
    {
        Span<char> buffer = stackalloc char[CodeLength];
        buffer[0] = CodeFirstChars[RandomNumberGenerator.GetInt32(CodeFirstChars.Length)];
        for (var i = 1; i < CodeLength; i++)
        {
            buffer[i] = CodeRestChars[RandomNumberGenerator.GetInt32(CodeRestChars.Length)];
        }
        return new string(buffer);
    }

    private static string GeneratePassword()
    {
        // 24 bytes -> 32 chars base64. Avoid '/', '+', '=' since they'd need
        // escaping in the connection string; URL-safe base64 keeps the literal
        // safe for both SQL and Npgsql connection strings.
        Span<byte> bytes = stackalloc byte[24];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }

    private static string QuoteLiteral(string value) =>
        "'" + value.Replace("'", "''") + "'";
}
