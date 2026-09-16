using Npgsql;
using Xunit;

namespace AutoNate.Web.Tests.Security;

// #150: the Flowable engine connects to Postgres as the bootstrap superuser,
// which also owns `AutoNate` and `autonate_datastores`. Anything that reaches
// that datasource — a deployed process definition, a misconfigured REST call —
// reaches the application's own data as its owner.
//
// `infra/postgres/init/02-flowable-role.sql` provisions a restricted
// `flowable_app` role instead, and compose lets a deployment opt in through
// AUTONATE_FLOWABLE_DB_USER. Init scripts run only on an empty data directory,
// so this is a fresh-install measure; see docs/DEPLOYMENT.md for why an
// existing cluster cannot simply switch.
[Trait("Category", "Integration")]
public sealed class FlowableRoleIsolationTests
{
    private const string Host = "Host=localhost;Port=5432;Username=autonate";

    private static string Password =>
        Environment.GetEnvironmentVariable("AUTONATE_POSTGRES_PASSWORD") ?? "Your_password123!";

    private static string AdminTo(string database) =>
        $"{Host};Password={Password};Database={database}";

    // The load-bearing line in the init script is the REVOKE of CONNECT from
    // PUBLIC, because PostgreSQL grants CONNECT to PUBLIC on every database by
    // default — owning `flowable` is not by itself what keeps `flowable_app`
    // out of `AutoNate`; the revoke is.
    //
    // This proves that mechanism against the server the suite actually runs on,
    // using a scratch database and a scratch role. It deliberately does NOT
    // execute the shipped script, which revokes on the real `AutoNate`.
    [Fact]
    public async Task Revoking_connect_from_public_is_what_keeps_a_role_out()
    {
        var suffix = Guid.NewGuid().ToString("N")[..12];
        var db = $"probe_{suffix}";
        var role = $"probe_role_{suffix}";
        const string rolePassword = "probe_only_never_persisted";

        await using var admin = new NpgsqlConnection(AdminTo("postgres"));
        await admin.OpenAsync();

        await Exec(admin, $"CREATE DATABASE \"{db}\"");
        try
        {
            await Exec(admin, $"CREATE ROLE \"{role}\" LOGIN PASSWORD '{rolePassword}'");
            try
            {
                // Pooling=false is load-bearing, not tidiness. With pooling on,
                // the positive control's physical connection is returned to the
                // pool and handed straight back after the revoke without
                // re-authorising — so the revoke looks ineffective and the test
                // fails for a reason that has nothing to do with Postgres.
                var asRole = $"{Host.Replace("Username=autonate", $"Username={role}")};" +
                             $"Password={rolePassword};Database={db};Pooling=false";

                // Positive control. Without it, the assertion below would pass
                // just as happily against a role that could never connect to
                // anything — which would prove nothing about the revoke.
                Assert.True(
                    await CanConnect(asRole),
                    "the scratch role could not connect even before the revoke; " +
                    "the negative assertion below would be vacuous.");

                await Exec(admin, $"REVOKE CONNECT ON DATABASE \"{db}\" FROM PUBLIC");

                // The refusal is the assertion.
                Assert.False(
                    await CanConnect(asRole),
                    $"a role with no explicit grant still connected to {db} after " +
                    "CONNECT was revoked from PUBLIC; the isolation the init " +
                    "script relies on does not hold on this server.");
            }
            finally
            {
                await Exec(admin, $"DROP ROLE IF EXISTS \"{role}\"");
            }
        }
        finally
        {
            await Exec(admin, $"DROP DATABASE IF EXISTS \"{db}\" WITH (FORCE)");
        }
    }

    // The deployment-state check. On a cluster built from the init scripts —
    // CI, and any fresh volume — `flowable_app` exists and this is a real
    // assertion. On a cluster that predates them the role is absent and the
    // test skips, which is the honest reading of a fresh-install-only measure
    // rather than a failure to report.
    [Theory]
    [InlineData("AutoNate")]
    [InlineData("autonate_datastores")]
    public async Task Flowable_role_cannot_reach_application_databases(string database)
    {
        await using var admin = new NpgsqlConnection(AdminTo("postgres"));
        await admin.OpenAsync();

        if (!await Exists(admin, "SELECT 1 FROM pg_roles WHERE rolname = 'flowable_app'")) return;
        if (!await Exists(admin, $"SELECT 1 FROM pg_database WHERE datname = '{database}'")) return;

        await using var command = admin.CreateCommand();
        command.CommandText =
            $"SELECT has_database_privilege('flowable_app', '{database}', 'CONNECT')";
        Assert.False(
            (bool)(await command.ExecuteScalarAsync())!,
            $"flowable_app can CONNECT to {database}; the Flowable engine's " +
            "datasource is not isolated from application data.");
    }

    // The theory above is a fresh-install measure and returns early on any
    // cluster without `flowable_app` -- which is every developer machine whose
    // data directory predates the init scripts. That is honest, and it is also
    // why nobody noticed that `autonate_datastores` was never revoked at all:
    // the check that would have said so does not run where the database exists,
    // and does not exist where the check runs (#506).
    //
    // So this asserts the property directly, on the database the application
    // creates for itself, with no dependence on `flowable_app` being present.
    // Booting the factory is what runs DatastoresDatabaseInitializer; the
    // assertion then reads the catalog the initializer was supposed to change.
    [Fact]
    public async Task The_datastores_database_does_not_leave_connect_with_public()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        // A request, so startup (and therefore the initializers) has certainly
        // completed before the catalog is read.
        (await factory.CreateClient().GetAsync("/api/health/live")).EnsureSuccessStatusCode();

        await using var admin = new NpgsqlConnection(AdminTo("postgres"));
        await admin.OpenAsync();

        await using var command = admin.CreateCommand();
        command.CommandText =
            "SELECT COALESCE(array_to_string(datacl, ','), '') FROM pg_database "
            + "WHERE datname = 'autonate_datastores'";
        var acl = (string?)await command.ExecuteScalarAsync();

        Assert.False(
            acl is null,
            "autonate_datastores does not exist, so this assertion proved nothing. "
            + "It is created by DatastoresDatabaseInitializer at startup.");

        // An EMPTY datacl is the PostgreSQL default, and the default includes
        // CONNECT for PUBLIC -- so "no ACL" is the failing state, not a neutral
        // one. This is exactly the shape the bug shipped in.
        Assert.False(
            acl!.Length == 0,
            "autonate_datastores has an empty datacl, which is the PostgreSQL default: "
            + "PUBLIC -- and so the Flowable engine's role -- retains CONNECT (#506).");

        // PUBLIC's entry is the one with an empty grantee before `=`. `c` in it
        // is CONNECT. Checking the parsed entry rather than the whole string
        // matters: the owner's own entry legitimately contains `c`.
        var publicEntry = acl.Split(',')
            .FirstOrDefault(entry => entry.StartsWith('='));
        Assert.False(
            publicEntry is not null && publicEntry.Split('/')[0].Contains('c', StringComparison.Ordinal),
            $"PUBLIC still holds CONNECT on autonate_datastores (datacl: {acl}). "
            + "Every role on the cluster, including Flowable's, can reach it (#506).");
    }

    // Positive control for the above: a role revoked out of everything would
    // pass that theory while being entirely broken.
    [Fact]
    public async Task Flowable_role_can_still_reach_its_own_database()
    {
        await using var admin = new NpgsqlConnection(AdminTo("postgres"));
        await admin.OpenAsync();

        if (!await Exists(admin, "SELECT 1 FROM pg_roles WHERE rolname = 'flowable_app'")) return;

        await using var command = admin.CreateCommand();
        command.CommandText = "SELECT has_database_privilege('flowable_app', 'flowable', 'CONNECT')";
        Assert.True(
            (bool)(await command.ExecuteScalarAsync())!,
            "flowable_app cannot CONNECT to flowable; the engine would not start.");
    }

    private static async Task Exec(NpgsqlConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<bool> Exists(NpgsqlConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync() is not null;
    }

    private static async Task<bool> CanConnect(string connectionString)
    {
        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            return true;
        }
        catch (PostgresException e) when (e.SqlState == "42501" || e.SqlState == "3D000")
        {
            return false;
        }
    }
}
