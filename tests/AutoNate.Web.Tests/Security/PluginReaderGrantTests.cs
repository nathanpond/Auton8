using Npgsql;
using Xunit;

namespace AutoNate.Web.Tests.Security;

// #62: every per-plugin LOGIN role inherits plg_readers, and plg_readers was
// granted SELECT on ALL TABLES IN SCHEMA public (plus default privileges for
// future tables). Reading app tables is a documented plugin capability
// (IPluginDataAccess), but "app tables" was never meant to include password
// hashes, DataProtection-encrypted provider secrets, every other plugin's role
// password, or share-link token hashes — so those are revoked explicitly.
[Trait("Category", "Integration")]
public sealed class PluginReaderGrantTests
{
    // The tables an uploaded plugin must not be able to read.
    public static TheoryData<string> Forbidden()
    {
        var data = new TheoryData<string>();
        data.Add("local_users");
        data.Add("external_connections");
        data.Add("plugins");
        data.Add("saved_query_share_tokens");
        return data;
    }

    [Theory]
    [MemberData(nameof(Forbidden))]
    public async Task Plg_readers_cannot_select_credential_tables(string table)
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        // Starting the host is what runs DatabaseSchemaInitializer; without it the
        // database is only half-built and every assertion below is vacuous.
        (await factory.CreateClient().GetAsync("/api/auth/me")).EnsureSuccessStatusCode();
        await using var connection = new NpgsqlConnection(factory.Database.ConnectionString);
        await connection.OpenAsync();

        // Skip cleanly if this deployment's schema doesn't have the table —
        // the revoke is guarded the same way.
        if (!await TableExistsAsync(connection, table)) return;

        Assert.False(
            await HasSelectAsync(connection, table),
            $"plg_readers can SELECT from {table}; an uploaded plugin inherits that role.");
    }

    // Positive control: the revokes must not have taken the whole capability
    // away, or plugins that legitimately read app tables break.
    [Fact]
    public async Task Plg_readers_can_still_select_ordinary_app_tables()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        // Starting the host is what runs DatabaseSchemaInitializer; without it the
        // database is only half-built and every assertion below is vacuous.
        (await factory.CreateClient().GetAsync("/api/auth/me")).EnsureSuccessStatusCode();
        await using var connection = new NpgsqlConnection(factory.Database.ConnectionString);
        await connection.OpenAsync();

        foreach (var table in new[] { "pages", "projects" })
        {
            if (!await TableExistsAsync(connection, table)) continue;
            Assert.True(
                await HasSelectAsync(connection, table),
                $"plg_readers lost SELECT on {table}, which plugins are documented to read.");
        }
    }

    private static async Task<bool> TableExistsAsync(NpgsqlConnection connection, string table)
    {
        await using var cmd = new NpgsqlCommand("SELECT to_regclass(@t) IS NOT NULL", connection);
        cmd.Parameters.AddWithValue("t", "public." + table);
        return (bool)(await cmd.ExecuteScalarAsync())!;
    }

    private static async Task<bool> HasSelectAsync(NpgsqlConnection connection, string table)
    {
        await using var cmd = new NpgsqlCommand(
            "SELECT has_table_privilege('plg_readers', @t, 'SELECT')", connection);
        cmd.Parameters.AddWithValue("t", "public." + table);
        return (bool)(await cmd.ExecuteScalarAsync())!;
    }
}
