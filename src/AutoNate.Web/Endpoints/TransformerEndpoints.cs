using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.EndpointFilters;
using AutoNate.Web.Services.Analyzers;
using AutoNate.Web.Services.Transformers;

namespace AutoNate.Web.Endpoints;

// Catalog endpoints for the Phase 5 React Flow node palette to populate.
// Read-only: Phase 5 introduces actual run endpoints (orchestrated through
// pipelines). v1 surface is a flat list of (key, displayName, inputArity)
// for transformers and (key, displayName) for analyzers — config schemas
// are intentionally not surfaced here; they're authored in the node form
// from documentation today and will be machine-readable in a follow-up.
public static class TransformerEndpoints
{
    public static IEndpointRouteBuilder MapTransformerEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/transformers").RequireAuthorization();

        group.MapGet("/", (ITransformerRegistry registry) =>
        {
            return Results.Ok(registry.All
                .OrderBy(t => t.Key, StringComparer.Ordinal)
                .Select(t => new TransformerCatalogEntry(t.Key, t.DisplayName, t.InputArity))
                .ToList());
        }).RequireKindPermission(EntityKinds.Transformer, Actions.List);

        return app;
    }

    public sealed record TransformerCatalogEntry(string Key, string DisplayName, int InputArity);
}

public static class AnalyzerEndpoints
{
    public static IEndpointRouteBuilder MapAnalyzerEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/analyzers").RequireAuthorization();

        group.MapGet("/", (IAnalyzerRegistry registry) =>
        {
            return Results.Ok(registry.All
                .OrderBy(a => a.Key, StringComparer.Ordinal)
                .Select(a => new AnalyzerCatalogEntry(a.Key, a.DisplayName))
                .ToList());
        }).RequireKindPermission(EntityKinds.Analyzer, Actions.List);

        return app;
    }

    public sealed record AnalyzerCatalogEntry(string Key, string DisplayName);
}
