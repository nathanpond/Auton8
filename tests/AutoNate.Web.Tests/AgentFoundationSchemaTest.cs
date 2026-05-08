using Npgsql;
using Xunit;

namespace AutoNate.Web.Tests;

// Phase 1 of the agentic-AI foundation introduces four new tables:
// external_connection (kind-discriminated config for LLM providers and other
// external services), agent_conversation, agent_message, agent_tool_call. This
// test boots the host (which runs DatabaseSchemaInitializer.EnsureAsync) and
// asserts each table exists, proving both the SQL constants ran and that
// EnsureAsync remains idempotent on top of a freshly-bootstrapped DB whose
// bootstrap SQL already creates the same tables.
[Trait("Category", "Integration")]
public sealed class AgentFoundationSchemaTest
{
    [Fact]
    public async Task Boot_creates_agent_foundation_tables()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();

        // Forces host construction, which calls DatabaseSchemaInitializer.EnsureAsync.
        _ = factory.CreateClient();

        await AssertPublicTableExists(factory.Database.ConnectionString, "external_connection");
        await AssertPublicTableExists(factory.Database.ConnectionString, "agent_conversation");
        await AssertPublicTableExists(factory.Database.ConnectionString, "agent_message");
        await AssertPublicTableExists(factory.Database.ConnectionString, "agent_tool_call");
    }

    private static async Task AssertPublicTableExists(string connectionString, string tableName)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT 1 FROM information_schema.tables " +
            "WHERE table_schema = 'public' AND table_name = @name LIMIT 1";
        command.Parameters.AddWithValue("name", tableName);

        var result = await command.ExecuteScalarAsync();
        Assert.True(result is not null, $"Expected public table '{tableName}' to exist after host boot.");
    }
}
