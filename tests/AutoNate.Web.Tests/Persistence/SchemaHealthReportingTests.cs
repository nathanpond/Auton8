using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AutoNate.Web.Authorization;
using AutoNate.Web.Services.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace AutoNate.Web.Tests.Persistence;

/// <summary>
/// `GET /api/health/system` surfaces the schema ledger.
/// </summary>
/// <remarks>
/// The acceptance criterion this covers exists so an operator can answer "which
/// schema version is this database at" from the admin UI rather than from a
/// psql session. `SchemaVersionLedgerTests` proves the ledger itself; this
/// proves it reaches the surface people actually read.
///
/// That distinction matters more than it looks. `ReadSchemaHealthAsync`
/// deliberately swallows exceptions and returns null, because an install
/// predating the ledger is a valid state and system health should report what
/// it can rather than fail wholesale. A bug that made it return null *always*
/// would therefore be completely silent. Both directions are asserted here.
/// </remarks>
[Trait("Category", "Integration")]
public sealed class SchemaHealthReportingTests
{
    private static readonly Guid AdminUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static Dictionary<string, string?> EnforceConfigNoBackfill() => new()
    {
        ["Authorization:Enabled"] = "true",
        ["Authorization:Enforcement"] = AuthorizationEnforcement.Full,
        ["Authorization:AssignSuperAdminToAllExistingUsers"] = "false"
    };

    private static async Task GrantSiteConfigViewAsync(AutoNateWebApplicationFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var grants = scope.ServiceProvider.GetRequiredService<IPermissionGrantStore>();
        await grants.CreateAsync(new CreatePermissionGrantInput(
            EntityKinds.User, AdminUserId.ToString(),
            Actions.View, "/siteconfig/*", "allow", 0), AdminUserId);
    }

    [Fact]
    public async Task System_health_reports_the_schema_version_and_applied_step_count()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(
            EnforceConfigNoBackfill());
        await GrantSiteConfigViewAsync(factory);

        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        var response = await client.GetAsync("/api/health/system");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var report = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.True(report.TryGetProperty("schema", out var schema),
            "GET /api/health/system returned no `schema` block. An operator cannot "
            + "answer 'which schema version is this database at' from the admin UI.");
        Assert.NotEqual(JsonValueKind.Null, schema.ValueKind);

        var version = schema.GetProperty("appVersion").GetString();
        Assert.False(string.IsNullOrWhiteSpace(version));

        // Every batch the initialiser ran is recorded, so this is comfortably
        // above zero. Asserting > 0 rather than an exact count keeps the test
        // from breaking every time a schema batch is added.
        var applied = schema.GetProperty("appliedStepCount").GetInt32();
        Assert.True(applied > 0, $"Expected a non-zero applied-step count; got {applied}.");

        Assert.NotEqual(JsonValueKind.Null,
            schema.GetProperty("lastAppliedAtUtc").ValueKind);
    }

    [Fact]
    public async Task System_health_degrades_rather_than_failing_when_the_ledger_is_absent()
    {
        // An install that predates the ledger is a valid state. The rest of the
        // report must still be served — the alternative is one missing answer
        // taking down the whole health page.
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(
            EnforceConfigNoBackfill());
        await GrantSiteConfigViewAsync(factory);

        await using (var connection = new NpgsqlConnection(factory.Database.ConnectionString))
        {
            await connection.OpenAsync();
            await using var drop = connection.CreateCommand();
            drop.CommandText = "DROP TABLE IF EXISTS schema_versions;";
            await drop.ExecuteNonQueryAsync();
        }

        var client = factory.CreateClient();
        await client.GetAsync("/api/auth/me");

        var response = await client.GetAsync("/api/health/system");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var report = await response.Content.ReadFromJsonAsync<JsonElement>();

        // The rest of the report is still there.
        Assert.True(report.TryGetProperty("components", out var components));
        Assert.NotEqual(0, components.GetArrayLength());

        // And the schema block is null rather than absent or invented.
        if (report.TryGetProperty("schema", out var schema))
        {
            Assert.Equal(JsonValueKind.Null, schema.ValueKind);
        }
    }
}
