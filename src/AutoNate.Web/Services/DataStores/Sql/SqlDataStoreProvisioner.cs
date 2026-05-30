using Microsoft.Extensions.Logging;
using Npgsql;

namespace AutoNate.Web.Services.DataStores.Sql;

// Per-datastore schema + read-only role provisioning inside `autonate_datastores`.
// Called when a SqlType DataStore row is created on the primary DB. Each
// datastore gets:
//   - schema `ds_<id_no_hyphens>`
//   - role `dsrw_<id_short>` for reads on that schema (could be reused as
//     the connection identity for AQL query executions in Phase 2)
//   - GRANT USAGE on the schema to the read role
//   - GRANT SELECT on all tables present + ALTER DEFAULT PRIVILEGES so
//     new tables created later by the writer role auto-grant SELECT.
//
// Idempotent: re-provisioning a datastore that already has its schema/role
// is a no-op (uses IF NOT EXISTS patterns).
public sealed class SqlDataStoreProvisioner(
    IDatastoresConnectionFactory connectionFactory,
    ILogger<SqlDataStoreProvisioner> log)
{
    public bool IsEnabled => connectionFactory.IsEnabled;

    public async Task ProvisionAsync(Guid dataStoreId, CancellationToken cancellationToken = default)
    {
        if (!connectionFactory.IsEnabled)
        {
            throw new InvalidOperationException(
                "Cannot provision a SqlType DataStore: ConnectionStrings:Datastores is not configured.");
        }

        var schema = SchemaNameFor(dataStoreId);
        var role = ReadRoleNameFor(dataStoreId);
        var quotedSchema = QuoteIdentifier(schema);
        var quotedRole = QuoteIdentifier(role);

        await using var conn = await connectionFactory.OpenAsync(cancellationToken);
        var sql =
            $$"""
            CREATE SCHEMA IF NOT EXISTS {{quotedSchema}};

            DO $$
            BEGIN
                CREATE ROLE {{quotedRole}};
            EXCEPTION WHEN duplicate_object THEN
                NULL;
            END $$;

            GRANT USAGE ON SCHEMA {{quotedSchema}} TO {{quotedRole}};
            GRANT SELECT ON ALL TABLES IN SCHEMA {{quotedSchema}} TO {{quotedRole}};
            ALTER DEFAULT PRIVILEGES IN SCHEMA {{quotedSchema}}
                GRANT SELECT ON TABLES TO {{quotedRole}};
            """;
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
        log.LogInformation("Provisioned SQL datastore schema {Schema} + role {Role}.", schema, role);
    }

    public async Task DeprovisionAsync(Guid dataStoreId, CancellationToken cancellationToken = default)
    {
        if (!connectionFactory.IsEnabled) return;

        var schema = SchemaNameFor(dataStoreId);
        var role = ReadRoleNameFor(dataStoreId);
        var quotedSchema = QuoteIdentifier(schema);
        var quotedRole = QuoteIdentifier(role);

        await using var conn = await connectionFactory.OpenAsync(cancellationToken);
        var sql =
            $$"""
            DROP SCHEMA IF EXISTS {{quotedSchema}} CASCADE;
            DO $$
            BEGIN
                DROP ROLE IF EXISTS {{quotedRole}};
            EXCEPTION WHEN dependent_objects_still_exist THEN
                -- Other DBs in the cluster might depend on this role; leave it.
                NULL;
            END $$;
            """;
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
        log.LogInformation("Deprovisioned SQL datastore schema {Schema}.", schema);
    }

    public static string SchemaNameFor(Guid dataStoreId)
        => "ds_" + dataStoreId.ToString("N");

    public static string ReadRoleNameFor(Guid dataStoreId)
        => "dsrw_" + dataStoreId.ToString("N")[..16];

    private static string QuoteIdentifier(string identifier)
        => "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}
