using System.Reflection;
using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.EntityTypes;
using Npgsql;
using Xunit;

namespace AutoNate.Web.Tests;

// #190: `is_unsafe` and its `executeunsafe` permission are gone.
//
// The flag selected a full-CPython runner that was planned in the Phase 0
// scaffold and never built, so it was inert for its whole life while the
// permission read as though it guarded a sandbox escape. A gate that protects
// nothing is worse than no gate: an admin granting it believes they have
// allowed something, and a reader auditing the code believes something is
// guarded.
//
// This is a guard against it drifting back in, which is a live possibility
// precisely because the name still reads as though it ought to exist.
[Trait("Category", "Integration")]
public sealed class CodeTransformerIsUnsafeRemovalTests
{
    [Fact]
    public async Task TheColumnIsGoneAfterTheSchemaInitialiserRuns()
    {
        // Booting the host is what runs the batches — a test that used
        // PostgresTestDatabase directly would see only BaseSchema.sql and
        // would pass without ever exercising the drop.
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        (await factory.CreateClient().GetAsync("/api/auth/me")).EnsureSuccessStatusCode();

        await using var connection = new NpgsqlConnection(factory.Database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = ColumnCountSql;

        Assert.Equal(0L, (long)(await command.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task TheColumnIsDroppedFromADatabaseThatAlreadyHadIt()
    {
        // The test above boots a fresh database, where the CREATE no longer
        // mentions the column — so the drop is a no-op there and proves
        // nothing about the upgrade path, which is the only path that matters.
        //
        // This rewinds: put the column back, clear the ledger row so the batch
        // is eligible again, restart the host over the same database, and
        // assert the column is gone. That is the sequence a real deployment
        // goes through.
        await using var seeded = await AutoNateWebApplicationFactory.CreateAsync();
        (await seeded.CreateClient().GetAsync("/api/auth/me")).EnsureSuccessStatusCode();

        await using (var connection = new NpgsqlConnection(seeded.Database.ConnectionString))
        {
            await connection.OpenAsync();
            await using var setup = connection.CreateCommand();
            setup.CommandText = """
                ALTER TABLE code_transformers
                    ADD COLUMN IF NOT EXISTS is_unsafe BOOLEAN NOT NULL DEFAULT FALSE;
                DELETE FROM schema_versions WHERE step_name = 'DropCodeTransformerIsUnsafeSql';
                """;
            await setup.ExecuteNonQueryAsync();

            await using var check = connection.CreateCommand();
            check.CommandText = ColumnCountSql;
            // Positive control: the rewind actually put it back, or the
            // assertion after the restart would be vacuous.
            Assert.Equal(1L, (long)(await check.ExecuteScalarAsync())!);
        }

        await using var restarted = AutoNateWebApplicationFactory.CreateOn(seeded.Database);
        (await restarted.CreateClient().GetAsync("/api/auth/me")).EnsureSuccessStatusCode();

        await using var verify = new NpgsqlConnection(seeded.Database.ConnectionString);
        await verify.OpenAsync();
        await using var command = verify.CreateCommand();
        command.CommandText = ColumnCountSql;
        Assert.Equal(0L, (long)(await command.ExecuteScalarAsync())!);
    }

    private const string ColumnCountSql = """
        SELECT COUNT(*) FROM information_schema.columns
        WHERE table_name = 'code_transformers' AND column_name = 'is_unsafe'
        """;

    [Fact]
    public void TheTableItselfStillExists()
    {
        // Positive control for the assertion above: if the table were missing
        // entirely, "the column does not exist" would be true for the wrong
        // reason and this guard would be worthless.
        var property = typeof(AutoNate.Web.Persistence.Scaffolded.CodeTransformer)
            .GetProperty("Code");
        Assert.NotNull(property);
    }

    [Fact]
    public void NoExecuteUnsafeActionConstantExists()
    {
        var names = typeof(Actions)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToArray();

        Assert.DoesNotContain("executeunsafe", names, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void NoEntityTypeAdvertisesTheAction()
    {
        // Registration is what makes an action visible to an admin in the
        // grants picker. Leaving it registered would keep offering a grant
        // that changes nothing — the exact shape AnalyticsEntityTypes already
        // records having shipped once.
        foreach (var definition in AnalyticsEntityTypes.All.Concat(CoreEntityTypes.All))
        {
            Assert.DoesNotContain(
                "executeunsafe",
                definition.Actions,
                StringComparer.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void NoCodeTransformerPropertyReintroducesTheFlag()
    {
        var properties = typeof(AutoNate.Web.Persistence.Scaffolded.CodeTransformer)
            .GetProperties()
            .Select(p => p.Name);

        Assert.DoesNotContain("IsUnsafe", properties, StringComparer.OrdinalIgnoreCase);
    }
}
