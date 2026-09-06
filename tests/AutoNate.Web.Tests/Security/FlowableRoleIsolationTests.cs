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
