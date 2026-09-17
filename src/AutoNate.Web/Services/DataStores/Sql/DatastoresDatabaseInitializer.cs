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
        // After the writer role exists, because the revoke below would
        // otherwise lock it out of the database it is created to write to.
        await EnsureDatabaseIsolationAsync(
            builder, targetDatabase, options.Value.WriterRole, cancellationToken);
    }

    // PUBLIC gets CONNECT on every new database, so isolating this one means
    // revoking it (#506).
    //
    // `infra/postgres/init/02-flowable-role.sql` intends to do exactly that,
    // and cannot: it guards the REVOKE on the database already existing, and
    // init scripts run only on an empty data directory, at which point this
    // database does not exist yet — it is created here, later, by the
    // application. So the guard was always false for `autonate_datastores` and
    // the revoke never ran. Measured on a fresh cluster: `AutoNate` came up
    // with `datacl = {=T/autonate,...}` (PUBLIC has TEMP only) while
    // `autonate_datastores` had an empty datacl, which is the PostgreSQL
    // default — PUBLIC, and therefore `flowable_app`, keeping CONNECT.
    //
    // Run on EVERY startup rather than only on creation: the databases this
    // needs to fix most are the ones that already exist.
    private async Task EnsureDatabaseIsolationAsync(
        NpgsqlConnectionStringBuilder builder,
        string targetDatabase,
        string? writerRole,
        CancellationToken cancellationToken)
    {
        var maintenance = new NpgsqlConnectionStringBuilder(builder.ConnectionString)
        {
            Database = "postgres",
            Pooling = false,
        };

        var quoted = QuoteIdentifier(targetDatabase);
        var role = string.IsNullOrWhiteSpace(writerRole) ? string.Empty : writerRole;

        try
        {
            await using var conn = new NpgsqlConnection(maintenance.ConnectionString);
            await conn.OpenAsync(cancellationToken);

            // READ FIRST, and write only if something actually needs changing.
            //
            // REVOKE/GRANT on a database rewrites the `pg_database` tuple, and
            // every application boot runs this. The suite boots hundreds of
            // hosts in parallel against one cluster, so an unconditional write
            // made them all contend on a single catalog row: measured, 24 tests
            // across unrelated areas failing with `XX000: tuple concurrently
            // updated`. The sibling EnsureWriterRoleAsync documents the same
            // hazard on `pg_authid`. Checking first makes this a no-op in the
            // steady state -- every boot after the first -- so there is nothing
            // to contend on.
            //
            // The decision is made by the SERVER, not by parsing the ACL text
            // here (#512). An aclitem[] is keyed by (grantee, grantor), so
            // PUBLIC can legitimately hold more than one entry, and a
            // hand-rolled `FirstOrDefault(e => e.StartsWith('='))` read only the
            // first: the ACL
            //
            //   =T/autonate,autonate=CTc/autonate,=c/autonate_datastore_writer
            //
            // parsed as "PUBLIC has no CONNECT" while PUBLIC plainly had it, so
            // the startup check that exists to repair exactly that drift did
            // nothing, silently. `aclexplode` enumerates every entry and grantee
            // 0 is PUBLIC, so EXISTS over it cannot be fooled by ordering, by a
            // second grantor, or by a role name containing a comma.
            bool publicHasConnect, writerExists, writerHasExplicitConnect;
            await using (var probe = new NpgsqlCommand(
                """
                SELECT
                    d.datacl IS NULL
                        OR EXISTS (
                            SELECT 1 FROM aclexplode(d.datacl) a
                            WHERE a.grantee = 0 AND a.privilege_type = 'CONNECT'
                        )                                               AS public_has_connect,
                    @role <> '' AND EXISTS (
                        SELECT 1 FROM pg_roles WHERE rolname = @role
                    )                                                   AS writer_exists,
                    EXISTS (
                        SELECT 1 FROM aclexplode(d.datacl) a
                        JOIN pg_roles r ON r.oid = a.grantee
                        WHERE r.rolname = @role AND a.privilege_type = 'CONNECT'
                    )                                                   AS writer_has_connect
                FROM pg_database d
                WHERE d.datname = @name
                """, conn))
            {
                probe.Parameters.AddWithValue("name", targetDatabase);
                probe.Parameters.AddWithValue("role", role);

                // Scoped so the reader is CLOSED before the write runs on this
                // connection: Npgsql allows one command at a time, and leaving
                // it open threw NpgsqlOperationInProgressException.
                await using var reader = await probe.ExecuteReaderAsync(cancellationToken);
                if (!await reader.ReadAsync(cancellationToken)) return;
                publicHasConnect = reader.GetBoolean(0);
                writerExists = reader.GetBoolean(1);
                writerHasExplicitConnect = reader.GetBoolean(2);
            }

            // EXPLICIT is the load-bearing word. The previous version asked
            // `has_database_privilege`, which counts privileges held THROUGH
            // PUBLIC -- so on the one boot that matters, the boot that is about
            // to revoke, it answered "the writer can already connect" and the
            // GRANT was skipped. The revoke then locked the writer out of the
            // database it exists to write to, until the next boot happened to
            // repair it. Measured: a database with `datacl IS NULL` reports
            // has_database_privilege(writer, db, 'CONNECT') = true with no
            // explicit grant anywhere (#512).
            var needsRevoke = publicHasConnect;
            var needsGrant = writerExists && !writerHasExplicitConnect;

            if (!needsRevoke && !needsGrant) return;

            var statements = new List<string>();
            if (needsRevoke)
            {
                // The owner keeps its own privileges -- REVOKE ... FROM PUBLIC
                // does not touch the `owner=CTc/owner` entry -- so this does not
                // lock out the connection string that just ran it.
                statements.Add($"REVOKE CONNECT ON DATABASE {quoted} FROM PUBLIC;");
            }
            if (needsGrant)
            {
                statements.Add(
                    $"GRANT CONNECT ON DATABASE {quoted} TO {QuoteIdentifier(role)};");
            }

            await using var write = new NpgsqlCommand(string.Join("\n", statements), conn);
            await write.ExecuteNonQueryAsync(cancellationToken);

            // Say what actually ran. The previous version logged "CONNECT
            // revoked from PUBLIC" unconditionally, including on the boot where
            // only the GRANT was issued.
            log.LogInformation(
                "Datastores DB '{Name}': {Actions}.",
                targetDatabase,
                string.Join(" and ", new[]
                {
                    needsRevoke ? "CONNECT revoked from PUBLIC" : null,
                    needsGrant ? $"CONNECT granted to '{role}'" : null,
                }.Where(action => action is not null)));
        }
        catch (PostgresException ex) when (ex.SqlState is "XX000" or "40001" or "40P01" or "42704")
        {
            // Another instance won the race and is applying the identical
            // change, or the writer role was dropped between the probe and the
            // grant (42704). Losing that race is not a fault: the end state is
            // the same and the next boot's probe confirms it.
            //
            // Information, not Debug (#512). Debug is off in production, and
            // these codes cannot distinguish a lost race from a persistently
            // broken catalog -- at Debug, the second case leaves PUBLIC holding
            // CONNECT forever with no operator-visible signal at all.
            log.LogInformation(
                "Datastores DB '{Name}': isolation not applied this time ({SqlState}: {Message}). "
                + "Expected under concurrent startup; if it repeats on every boot, PUBLIC still "
                + "holds CONNECT and the cause is not contention.",
                targetDatabase, ex.SqlState, ex.MessageText);
        }
        catch (PostgresException ex) when (ex.SqlState == "42501")
        {
            // Not the owner. A deployment can legitimately run the app as a
            // role that may use the database but not re-grant on it, and
            // refusing to start would be a worse failure than the one this
            // closes -- so warn, by name, and carry on.
            //
            // Deliberately does not claim WHICH statement failed: it may have
            // been the revoke or the writer grant, and those have opposite
            // consequences.
            log.LogWarning(
                "Could not apply datastores isolation on '{Name}': {Message}. Either the Flowable "
                + "engine's role can still reach this database, or the datastores writer role "
                + "cannot (#506). Run as the database owner, or apply by hand: "
                + "REVOKE CONNECT ON DATABASE {Name} FROM PUBLIC; "
                + "GRANT CONNECT ON DATABASE {Name} TO <writer role>;",
                targetDatabase, ex.MessageText, targetDatabase, targetDatabase);
        }
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
        try
        {
            await create.ExecuteNonQueryAsync(cancellationToken);
            log.LogInformation("Created datastores DB '{Name}'.", targetDatabase);
        }
        catch (PostgresException ex) when (ex.SqlState is "42P04" or "23505")
        {
            // Someone else created it between the probe above and this
            // statement. The probe-then-create is a TOCTOU by construction —
            // CREATE DATABASE cannot run in a transaction and there is no
            // IF NOT EXISTS — so the only question is whether losing the race
            // is fatal. It is not: the database exists, which is all this
            // method promises.
            //
            // 42P04 is "database already exists"; 23505 on
            // pg_database_datname_index is the same collision surfacing as a
            // catalog unique-violation when two creates interleave inside the
            // catalog insert. Both mean the same thing here.
            //
            // Real for any deployment that starts two instances at once — a
            // rolling deploy, or a scaled-out replica set — where today one of
            // them would fail startup. It first showed up as a flaky test,
            // because the suite boots many apps in parallel.
            log.LogInformation(
                "Datastores DB '{Name}' was created concurrently by another instance.",
                targetDatabase);
        }
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
        //
        // Roles are cluster-wide, so two hosts starting at once (the test
        // suite boots many in parallel against one Postgres) both hit the
        // same pg_authid tuple and the loser dies with "XX000: tuple
        // concurrently updated". The transaction-scoped advisory lock below
        // is keyed by the role name and releases with the commit.
        //
        // It does NOT serialize hosts connected to different databases,
        // despite what this comment used to claim: an advisory lock's tag
        // includes the database oid, so two connections to different
        // databases take different locks. Every host here connects to its own
        // datastores database, which is exactly the case the lock was meant
        // to cover — so the EXCEPTION clause, not the lock, is what actually
        // makes this safe.
        //
        // Hence catching unique_violation as well as duplicate_object: when
        // two CREATE ROLEs interleave inside the catalog insert the loser
        // sees 23505 on pg_authid_rolname_index rather than 42710. Master CI
        // failed on precisely that, in HealthEndpointEnforcementTests, which
        // has nothing to do with roles — it was simply the test booting a
        // host at the wrong moment.
        var quotedRole = QuoteIdentifier(role);
        var literalPassword = QuoteLiteral(password);
        var sql =
            $$"""
            SELECT pg_advisory_xact_lock(hashtext('autonate:writer-role'), hashtext({{QuoteLiteral(role)}}));
            DO $$
            BEGIN
                CREATE ROLE {{quotedRole}} LOGIN PASSWORD {{literalPassword}};
            EXCEPTION WHEN duplicate_object OR unique_violation THEN
                ALTER ROLE {{quotedRole}} WITH LOGIN PASSWORD {{literalPassword}};
            END $$;
            """;
        await using var tx = await conn.BeginTransactionAsync(cancellationToken);
        await using (var cmd = new NpgsqlCommand(sql, conn, tx))
        {
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        await tx.CommitAsync(cancellationToken);
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
