using Npgsql;

namespace AutoNate.Web.Tests.Infrastructure;

/// <summary>
/// Removes cluster-wide scratch left by earlier runs (#214).
/// </summary>
/// <remarks>
/// <para>
/// #191 drops abandoned test <em>databases</em>, and anything inside one goes with
/// it. What survives a dropped database is what this handles: **roles**, which are
/// cluster-wide, and **temp directories**, which are not in Postgres at all.
/// </para>
/// <para>
/// **The danger is `plg_*`.** `PluginSchemaProvisioner.RoleNameFor(code) => "plg_" +
/// code` is production code, so a test's plugin role and a real installed plugin's
/// role are identical by name — a prefix sweep would drop a developer's working
/// plugin off their own cluster. Guessing from the shape of the code ("looks random")
/// is exactly the heuristic that eventually deletes something real.
/// </para>
/// <para>
/// The signal used instead is structural: a plugin role exists to own a schema, so a
/// `plg_*` role with **no schema of that name in any live database** is serving
/// nothing. When the database it belonged to was dropped, the role was orphaned; when
/// a plugin is genuinely installed, its schema is right there and the role is left
/// alone.
/// </para>
/// </remarks>
internal static class TestResourceSweep
{
    /// <summary>Databases the suite owns, and may therefore sweep schemas inside.</summary>
    private static bool IsSuiteOwnedDatabase(string name) =>
        name.StartsWith("autonate_test_", StringComparison.Ordinal)
        || string.Equals(name, "AutoNate_E2E", StringComparison.Ordinal);

    internal sealed record Counts(int Roles, int Schemas, int Directories)
    {
        // Per class, so "cleaned up 400 things" cannot hide one category doing
        // nothing — which is how a sweep silently stops working.
        public override string ToString() =>
            $"roles={Roles}, schemas={Schemas}, directories={Directories}";
    }

    internal static async Task<Counts> SweepAsync(TimeSpan olderThan)
    {
        var roles = await SweepOrphanedPluginRolesAsync();
        var schemas = await SweepPluginSchemasInSuiteDatabasesAsync();
        var directories = SweepTempDirectories(olderThan);
        return new Counts(roles, schemas, directories);
    }

    /// <summary>
    /// Drops `plg_*` roles that no live database has a schema for.
    /// </summary>
    private static async Task<int> SweepOrphanedPluginRolesAsync() =>
        await SweepOrphanedPluginRolesAsync(databases: null);

    // #215. The database list is injectable so a test can include a name that is
    // not there — the state the sweep is really exposed to, because the suite
    // drops a database per test class in parallel with it. Reproducing that by
    // timing is not possible from outside; supplying the list is exact.
    internal static async Task<int> SweepOrphanedPluginRolesAsync(
        IReadOnlyList<string>? databases)
    {
        var roles = await QueryStringsAsync("postgres",
            "select rolname from pg_roles where rolname like 'plg\\_%';");
        if (roles.Count == 0) return 0;

        // #258. Only databases the suite does NOT own.
        //
        // A `plg_*` schema inside an ephemeral `autonate_test_*` database is not
        // evidence that the role is serving anything — those databases are
        // created and dropped constantly, and the role is test scratch by
        // definition. An installed plugin's schema lives in an app database.
        //
        // This is also what makes the sweep reliable: the cluster routinely
        // carries hundreds of suite databases, and connecting to each one is what
        // made this fail under load. Scanning the handful that matter turns ~210
        // connections into ~6.
        databases ??= (await QueryStringsAsync("postgres",
                "select datname from pg_database where datistemplate = false and datallowconn;"))
            .Where(name => !IsSuiteOwnedDatabase(name))
            .ToList();

        // Every schema name in use anywhere. A role matching one of these is
        // serving an installed plugin and must survive.
        var schemasInUse = new HashSet<string>(StringComparer.Ordinal);
        var unreadableDatabases = 0;
        foreach (var database in databases)
        {
            try
            {
                foreach (var schema in await QueryStringsAsync(database,
                    "select schema_name from information_schema.schemata where schema_name like 'plg\\_%';"))
                {
                    schemasInUse.Add(schema);
                }
            }
            catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.InvalidCatalogName)
            {
                // #215. The database was dropped between listing pg_database and
                // connecting to it — which happens constantly, because the suite
                // creates and drops a database per test class in parallel with this
                // sweep. A database that no longer exists holds no schemas, so it
                // constrains nothing and skipping it is exact, not a guess.
                //
                // This used to fall into the bail-out below, so a sweep running
                // alongside a busy suite returned 0 having examined nothing. The
                // visible symptom was TestResourceSweepTests reporting "the sweep
                // dropped no roles" about eleven minutes into a full run.
            }
            catch (Exception ex) when (ex is PostgresException or NpgsqlException or TimeoutException)
            {
                // #258. This used to `return 0` for ANY Postgres failure, and that
                // is what kept the flake alive after #215: under full-suite load
                // the pool saturates and a connection fails for reasons that have
                // nothing to do with the database's contents, so the sweep gave up
                // having examined nothing and the test reported "dropped no roles"
                // — nine and a half minutes into a run, exactly the symptom #215
                // claimed to have fixed.
                //
                // One retry, then treat this database as unreadable and carry on.
                // Skipping ONE database only risks dropping a role whose schema
                // lives in it; bailing out entirely guaranteed the sweep did
                // nothing at all, which is strictly worse and much harder to see.
                try
                {
                    await Task.Delay(250);
                    foreach (var schema in await QueryStringsAsync(database,
                        @"select schema_name from information_schema.schemata where schema_name like 'plg\_%';"))
                    {
                        schemasInUse.Add(schema);
                    }
                }
                catch (Exception retry) when (retry is PostgresException or NpgsqlException or TimeoutException)
                {
                    // Genuinely unreadable. Every role it might have backed stays,
                    // because a role we cannot rule out is a role we do not drop.
                    unreadableDatabases++;
                }
            }
        }

        // A database we could not read might hold the schema backing any of these
        // roles, so dropping on incomplete knowledge is the one thing this sweep
        // must never do (#214: `plg_*` names a real installed plugin's role).
        if (unreadableDatabases > 0) return 0;

        var dropped = 0;
        foreach (var role in roles.Where(role => !schemasInUse.Contains(role)))
        {
            try
            {
                await ExecuteAsync("postgres", $"drop role if exists \"{role}\";");
                dropped++;
            }
            catch (PostgresException)
            {
                // Still owns something somewhere. Leaving it is correct.
            }
        }

        return dropped;
    }

    /// <summary>
    /// Drops `plg_*` schemas inside databases the suite owns.
    /// </summary>
    /// <remarks>
    /// Never touches `AutoNate` or `autonate_datastores` — a developer's own installed
    /// plugins live there, and this tool is not allowed to be the thing that breaks
    /// them.
    /// </remarks>
    private static async Task<int> SweepPluginSchemasInSuiteDatabasesAsync()
    {
        var databases = (await QueryStringsAsync("postgres",
                "select datname from pg_database where datistemplate = false and datallowconn;"))
            .Where(IsSuiteOwnedDatabase)
            .ToList();

        var dropped = 0;
        foreach (var database in databases)
        {
            try
            {
                foreach (var schema in await QueryStringsAsync(database,
                    "select schema_name from information_schema.schemata where schema_name like 'plg\\_%';"))
                {
                    await ExecuteAsync(database, $"drop schema if exists \"{schema}\" cascade;");
                    dropped++;
                }
            }
            catch (PostgresException)
            {
                // The database went away underneath us — #191's sweep is the other
                // half of this and may have dropped it. Nothing to do.
            }
        }

        return dropped;
    }

    /// <summary>Removes the temp content roots test hosts create.</summary>
    private static int SweepTempDirectories(TimeSpan olderThan)
    {
        var cutoff = DateTime.UtcNow - olderThan;
        var dropped = 0;

        IEnumerable<string> candidates;
        try
        {
            candidates = Directory.EnumerateDirectories(Path.GetTempPath(), "autonate-pgtests-*");
        }
        catch (IOException)
        {
            return 0;
        }

        foreach (var directory in candidates)
        {
            try
            {
                // Age from the directory itself, so a run in progress keeps its own.
                if (Directory.GetCreationTimeUtc(directory) >= cutoff) continue;
                Directory.Delete(directory, recursive: true);
                dropped++;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // In use, or not ours to delete.
            }
        }

        return dropped;
    }

    // ── Postgres helpers ────────────────────────────────────────────────────

    private static async Task<List<string>> QueryStringsAsync(string database, string sql)
    {
        await using var connection = new NpgsqlConnection(PostgresTestDatabase.AdminConnectionStringFor(database));
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync();

        var values = new List<string>();
        while (await reader.ReadAsync()) values.Add(reader.GetString(0));
        return values;
    }

    private static async Task ExecuteAsync(string database, string sql)
    {
        await using var connection = new NpgsqlConnection(PostgresTestDatabase.AdminConnectionStringFor(database));
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}
