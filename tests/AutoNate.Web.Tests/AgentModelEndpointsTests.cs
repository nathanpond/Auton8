using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AutoNate.Web.Services.Agent.Catalog;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AutoNate.Web.Tests;

[Trait("Category", "Integration")]
public sealed class AgentModelEndpointsTests
{
    [Fact]
    public async Task List_returns_seeded_catalogue_entries_grouped_by_provider()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await PrimeAuthAsync(client);

        var resp = await client.GetAsync("/api/agent-models");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var list = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Array, list.ValueKind);
        var rows = list.EnumerateArray().ToArray();

        // The seed inserts our flagship Claude + GPT entries; assert a
        // representative pair so the SPA dropdown has something to populate
        // out of the box.
        Assert.Contains(rows, r => r.GetProperty("modelId").GetString() == "claude-sonnet-4-6");
        Assert.Contains(rows, r => r.GetProperty("modelId").GetString() == "gpt-4o");

        var sonnet = rows.First(r => r.GetProperty("modelId").GetString() == "claude-sonnet-4-6");
        Assert.Equal("Anthropic", sonnet.GetProperty("provider").GetString());
        Assert.Equal(200_000, sonnet.GetProperty("contextWindowTokens").GetInt32());
    }

    [Fact]
    public async Task Filter_by_provider_returns_only_that_provider()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await PrimeAuthAsync(client);

        var resp = await client.GetAsync("/api/agent-models?provider=Anthropic");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var rows = (await resp.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray().ToArray();
        Assert.NotEmpty(rows);
        Assert.All(rows, r => Assert.Equal("Anthropic", r.GetProperty("provider").GetString()));
    }

    [Fact]
    public async Task Seed_then_lookup_through_catalog_service_resolves_context_window()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await PrimeAuthAsync(client);

        // The catalogue is provider-curated — there's no public POST
        // anymore. Create rows the way the refresher does: through the
        // store directly. The singleton lookup must still reflect the
        // write immediately because the store calls Invalidate().
        var row = await CreateModelViaStoreAsync(factory, "test-custom-llm", "Anthropic", 32_000);

        var catalog = factory.Services.GetRequiredService<IAgentModelCatalog>();
        Assert.Equal(32_000, catalog.GetContextWindow("test-custom-llm"));
        Assert.True(catalog.IsKnown("test-custom-llm"));

        // Longest-prefix match: an unversioned-suffix variant resolves
        // through the registered id.
        Assert.Equal(32_000, catalog.GetContextWindow("test-custom-llm-20260101"));
        Assert.NotEqual(Guid.Empty, row.Id);
    }

    [Fact]
    public async Task Update_changes_take_effect_on_next_lookup()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await PrimeAuthAsync(client);

        var seeded = await CreateModelViaStoreAsync(factory, "test-resize-llm", "Anthropic", 50_000);

        var catalog = factory.Services.GetRequiredService<IAgentModelCatalog>();
        Assert.Equal(50_000, catalog.GetContextWindow("test-resize-llm"));

        // Bump the window via the public API — the supported edit path.
        var updateResp = await client.PutAsJsonAsync($"/api/agent-models/{seeded.Id}", new
        {
            contextWindowTokens = 75_000
        });
        Assert.Equal(HttpStatusCode.OK, updateResp.StatusCode);

        Assert.Equal(75_000, catalog.GetContextWindow("test-resize-llm"));
    }

    [Fact]
    public async Task Set_default_clears_default_globally_not_just_per_provider()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await PrimeAuthAsync(client);

        // Two connections (one per provider) so set-default's
        // connection-gating check passes for both providers we'll touch.
        await CreateAnthropicConnectionAsync(client);
        await CreateOpenAIConnectionAsync(client);

        // Sanity: the seed makes claude-sonnet-4-6 the global default and
        // every other row is non-default.
        var beforeRows = (await client.GetFromJsonAsync<JsonElement>("/api/agent-models")).EnumerateArray().ToArray();
        var defaultsBefore = beforeRows.Where(r => r.GetProperty("isDefault").GetBoolean()).ToArray();
        Assert.Single(defaultsBefore);
        Assert.Equal("claude-sonnet-4-6", defaultsBefore[0].GetProperty("modelId").GetString());

        // Promote a different model — and importantly, a different
        // provider — so we prove the clear-other-defaults sweep is
        // global, not per-provider.
        var gpt4o = beforeRows.First(r => r.GetProperty("modelId").GetString() == "gpt-4o");
        var setResp = await client.PostAsync($"/api/agent-models/{gpt4o.GetProperty("id").GetGuid()}/set-default", content: null);
        Assert.Equal(HttpStatusCode.OK, setResp.StatusCode);

        var afterRows = (await client.GetFromJsonAsync<JsonElement>("/api/agent-models")).EnumerateArray().ToArray();
        var defaultsAfter = afterRows.Where(r => r.GetProperty("isDefault").GetBoolean()).ToArray();
        Assert.Single(defaultsAfter);
        Assert.Equal("gpt-4o", defaultsAfter[0].GetProperty("modelId").GetString());

        // Catalog service reflects the change.
        var catalog = factory.Services.GetRequiredService<IAgentModelCatalog>();
        Assert.Equal("gpt-4o", catalog.GetDefault()?.ModelId);
    }

    [Fact]
    public async Task Set_default_returns_400_when_no_connection_for_provider()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await PrimeAuthAsync(client);

        // No connections at all — every model is gated.
        var rows = (await client.GetFromJsonAsync<JsonElement>("/api/agent-models")).EnumerateArray().ToArray();
        var opus = rows.First(r => r.GetProperty("modelId").GetString() == "claude-opus-4-7");

        var resp = await client.PostAsync($"/api/agent-models/{opus.GetProperty("id").GetGuid()}/set-default", content: null);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("External Connection", body.GetProperty("reason").GetString() ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Set_available_returns_400_when_no_connection_for_provider()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await PrimeAuthAsync(client);

        var rows = (await client.GetFromJsonAsync<JsonElement>("/api/agent-models")).EnumerateArray().ToArray();
        var gpt = rows.First(r => r.GetProperty("modelId").GetString() == "gpt-4o-mini");
        // Flip it to unavailable first (no connection guard on that path).
        Assert.Equal(HttpStatusCode.NoContent,
            (await client.PostAsync($"/api/agent-models/{gpt.GetProperty("id").GetGuid()}/set-unavailable", null)).StatusCode);

        // Now try to bring it back to available — should 400 because
        // there's no OpenAI connection.
        var resp = await client.PostAsync($"/api/agent-models/{gpt.GetProperty("id").GetGuid()}/set-available", content: null);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task List_response_marks_provider_has_connection_per_row()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await PrimeAuthAsync(client);

        // Configure Anthropic only — every Anthropic row should carry
        // providerHasConnection=true and every OpenAI row =false.
        await CreateAnthropicConnectionAsync(client);

        var rows = (await client.GetFromJsonAsync<JsonElement>("/api/agent-models")).EnumerateArray().ToArray();
        Assert.NotEmpty(rows);
        foreach (var row in rows)
        {
            var provider = row.GetProperty("provider").GetString();
            var has = row.GetProperty("providerHasConnection").GetBoolean();
            if (provider == "Anthropic") Assert.True(has, $"Expected Anthropic row {row.GetProperty("modelId").GetString()} to have providerHasConnection=true.");
            else Assert.False(has, $"Expected non-Anthropic row {row.GetProperty("modelId").GetString()} to have providerHasConnection=false.");
        }
    }

    [Fact]
    public async Task Set_unavailable_then_available_round_trips()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await PrimeAuthAsync(client);
        await CreateOpenAIConnectionAsync(client);

        var rows = (await client.GetFromJsonAsync<JsonElement>("/api/agent-models")).EnumerateArray().ToArray();
        var target = rows.First(r => r.GetProperty("modelId").GetString() == "gpt-4o-mini");
        var id = target.GetProperty("id").GetGuid();
        Assert.True(target.GetProperty("isAvailable").GetBoolean());

        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync($"/api/agent-models/{id}/set-unavailable", null)).StatusCode);
        var afterOff = (await client.GetFromJsonAsync<JsonElement>("/api/agent-models")).EnumerateArray().First(r => r.GetProperty("id").GetGuid() == id);
        Assert.False(afterOff.GetProperty("isAvailable").GetBoolean());

        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync($"/api/agent-models/{id}/set-available", null)).StatusCode);
        var afterOn = (await client.GetFromJsonAsync<JsonElement>("/api/agent-models")).EnumerateArray().First(r => r.GetProperty("id").GetGuid() == id);
        Assert.True(afterOn.GetProperty("isAvailable").GetBoolean());
    }

    [Fact]
    public async Task Refresh_with_no_connections_returns_a_skipped_reason()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await PrimeAuthAsync(client);

        var resp = await client.PostAsync("/api/agent-models/refresh", content: null);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var result = await resp.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Empty(result.GetProperty("providers").EnumerateArray());
        var reasons = result.GetProperty("skippedReasons").EnumerateArray().Select(r => r.GetString()).ToArray();
        Assert.Contains(reasons, r => r != null && r.Contains("No enabled LLM connections", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task PrimeAuthAsync(HttpClient client)
    {
        (await client.GetAsync("/api/agent-models")).EnsureSuccessStatusCode();
    }

    private static async Task CreateAnthropicConnectionAsync(HttpClient client)
    {
        var resp = await client.PostAsJsonAsync("/api/external-connections", new
        {
            kind = "LlmProvider:Anthropic",
            name = "Test Anthropic",
            isEnabled = true,
            metadata = new { },
            secret = "sk-ant-test-key"
        });
        resp.EnsureSuccessStatusCode();
    }

    private static async Task CreateOpenAIConnectionAsync(HttpClient client)
    {
        var resp = await client.PostAsJsonAsync("/api/external-connections", new
        {
            kind = "LlmProvider:OpenAI",
            name = "Test OpenAI",
            isEnabled = true,
            metadata = new { },
            secret = "sk-openai-test-key"
        });
        resp.EnsureSuccessStatusCode();
    }

    // Inserts a fresh row through the same store path the refresher uses.
    // Tests need this because the public POST endpoint was removed —
    // catalogue rows now arrive only via /refresh in production. The
    // singleton catalog cache reflects the write immediately because
    // EfCoreAgentModelCatalogStore.CreateAsync calls Invalidate().
    private static async Task<AgentModelRow> CreateModelViaStoreAsync(
        AutoNateWebApplicationFactory factory,
        string modelId,
        string provider,
        int contextWindowTokens)
    {
        using var scope = factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IAgentModelCatalogStore>();
        return await store.CreateAsync(new CreateAgentModelInput(
            ModelId: modelId,
            DisplayName: modelId,
            Provider: provider,
            ContextWindowTokens: contextWindowTokens,
            InputCostPerMillionTokens: null,
            OutputCostPerMillionTokens: null,
            CostCurrency: "USD",
            CostPublishedAtUtc: null,
            Description: null,
            SortOrder: 1000));
    }
}
