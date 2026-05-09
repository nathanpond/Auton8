using AutoNate.Web.Services.Agent.Catalog;
using Xunit;

namespace AutoNate.Web.Tests;

// Lookup-logic tests for AgentModelCatalog. The DB-backed singleton is
// covered by integration tests; here we just exercise the longest-prefix
// match against a fixed set of catalog rows so this stays a fast,
// in-process suite.
public sealed class ModelCatalogTests
{
    private static readonly IReadOnlyList<AgentModelRow> Catalog = new[]
    {
        Row("claude-opus-4-7", 200_000),
        Row("claude-opus-4-7[1m]", 1_000_000),
        Row("claude-sonnet-4-6", 200_000),
        Row("claude-sonnet-4-6[1m]", 1_000_000),
        Row("claude-3-5-sonnet-latest", 200_000),
        Row("claude-3-haiku-20240307", 200_000),
        Row("gpt-4.1", 1_047_576),
        Row("gpt-4o", 128_000),
        Row("gpt-4o-mini", 128_000),
        Row("gpt-3.5-turbo", 16_385),
        Row("o1", 200_000),
        Row("o1-mini", 128_000)
    };

    [Theory]
    [InlineData("claude-sonnet-4-6", 200_000)]
    [InlineData("claude-sonnet-4-6-20250514", 200_000)] // longest-prefix to family
    [InlineData("claude-opus-4-7", 200_000)]
    [InlineData("gpt-4o", 128_000)]
    [InlineData("gpt-4o-mini", 128_000)]
    [InlineData("gpt-4.1", 1_047_576)]
    [InlineData("gpt-3.5-turbo", 16_385)]
    [InlineData("o1", 200_000)]
    [InlineData("o1-mini", 128_000)]
    public void Resolves_known_models_via_longest_prefix(string modelId, int expected)
    {
        Assert.Equal(expected, AgentModelCatalog.ResolveContextWindow(Catalog, modelId, fallback: 100_000));
    }

    [Theory]
    [InlineData("claude-opus-4-7[1m]", 1_000_000)]
    [InlineData("claude-sonnet-4-6[1m]", 1_000_000)]
    public void Extended_context_variants_outrank_family_default(string modelId, int expected)
    {
        Assert.Equal(expected, AgentModelCatalog.ResolveContextWindow(Catalog, modelId, fallback: 100_000));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("totally-made-up-model")]
    public void Falls_back_for_unknown_model_ids(string? modelId)
    {
        Assert.Equal(100_000, AgentModelCatalog.ResolveContextWindow(Catalog, modelId, fallback: 100_000));
    }

    [Fact]
    public void IsKnown_distinguishes_catalog_hits_from_fallbacks()
    {
        Assert.True(AgentModelCatalog.ResolveIsKnown(Catalog, "claude-sonnet-4-6"));
        Assert.True(AgentModelCatalog.ResolveIsKnown(Catalog, "gpt-4o"));
        Assert.False(AgentModelCatalog.ResolveIsKnown(Catalog, "totally-made-up-model"));
        Assert.False(AgentModelCatalog.ResolveIsKnown(Catalog, null));
        Assert.False(AgentModelCatalog.ResolveIsKnown(Catalog, ""));
    }

    private static AgentModelRow Row(string modelId, int contextWindow) => new(
        Id: Guid.NewGuid(),
        ModelId: modelId,
        DisplayName: modelId,
        Provider: modelId.StartsWith("claude") ? "Anthropic" : "OpenAI",
        ContextWindowTokens: contextWindow,
        InputCostPerMillionTokens: null,
        OutputCostPerMillionTokens: null,
        CostCurrency: "USD",
        CostPublishedAtUtc: null,
        Description: null,
        IsArchived: false,
        IsDefault: false,
        IsAvailable: true,
        SortOrder: 0,
        CreatedAtUtc: DateTime.UtcNow,
        UpdatedAtUtc: DateTime.UtcNow);
}
