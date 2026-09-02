using System.Reflection;
using System.Text.RegularExpressions;
using AutoNate.Web.Persistence;
using Npgsql;
using Xunit;

namespace AutoNate.Web.Tests;

// Roles are cluster-wide, so creating one is not safe to check-then-act.
//
// pg_roles is a shared catalog and CREATE ROLE takes no lock that serialises
// concurrent creates: two hosts starting at the same moment both see "not
// exists", both issue CREATE, and the loser fails with 23505 on
// pg_authid_rolname_index. An advisory lock does not help, because
// pg_advisory_xact_lock's tag includes the database oid and each host owns a
// different database. Catching the error is the only thing that works.
//
// This has now been the same bug twice — #192 for the datastores writer role,
// and plg_readers, which failed one test of a 1666-test CI run that had just
// passed locally. The test reads the *production* SQL rather than a copy of
// it, so it fails if someone reintroduces the check-then-act shape there.
[Trait("Category", "Integration")]
public sealed class RoleCreationRaceTests
{
    private const string ScratchRole = "autonate_race_probe";

    // The real statement, with only the role name swapped. Pulling it out of
    // the initializer rather than restating it is the point: a copy would go
    // on passing after the production SQL regressed.
    private static string ProductionRoleCreationBlock()
    {
        var sql = (string)typeof(DatabaseSchemaInitializer)
            .GetField("PluginDataIsolationSql", BindingFlags.NonPublic | BindingFlags.Static)!
            .GetRawConstantValue()!;

        var block = Regex.Match(sql, @"DO \$\$.*?END \$\$;", RegexOptions.Singleline);
        Assert.True(block.Success, "No DO block found in PluginDataIsolationSql.");
        Assert.Contains("CREATE ROLE", block.Value);

        return block.Value.Replace("plg_readers", ScratchRole);
    }

    private static async Task ExecAsync(string sql)
    {
        await using var conn = new NpgsqlConnection(PostgresTestDatabase.AdminConnectionStringFor("flowable"));
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task Concurrent_startups_do_not_collide_on_the_shared_role()
    {
        var create = ProductionRoleCreationBlock();
        await ExecAsync($"DROP ROLE IF EXISTS {ScratchRole};");

        try
        {
            // Twelve at once reproduces it reliably: measured against the old
            // check-then-act shape, 11 of 12 failed. The fixed shape is 0.
            var attempts = Enumerable.Range(0, 12).Select(_ => Task.Run(() => ExecAsync(create)));
            var results = await Task.WhenAll(
                attempts.Select(async t =>
                {
                    try { await t; return null; }
                    catch (PostgresException ex) { return ex; }
                }));

            var failures = results.Where(e => e is not null).ToArray();
            Assert.True(
                failures.Length == 0,
                $"{failures.Length} of 12 concurrent creates failed; first was "
                + $"{failures.FirstOrDefault()?.SqlState}: {failures.FirstOrDefault()?.MessageText}. "
                + "The role-creation SQL is check-then-act again — it must catch "
                + "duplicate_object / unique_violation instead.");
        }
        finally
        {
            await ExecAsync($"DROP ROLE IF EXISTS {ScratchRole};");
        }
    }
}
