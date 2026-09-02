using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.EndpointFilters;
using AutoNate.Web.Services.Analyzers;
using AutoNate.Web.Services.Transformers;

namespace AutoNate.Web.Endpoints;

// Catalog endpoints for the Phase 5 React Flow node palette to populate.
// Read-only: Phase 5 introduces actual run endpoints (orchestrated through
// pipelines). The flat list is (key, displayName, inputArity) for
// transformers and (key, displayName) for analyzers; the per-key schema
// endpoint (audit fix archived-7) drives the pipeline editor's node-config form
// so authors don't hand-edit JSON for the 14 built-ins.
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

        // Per-key config schema for built-ins. Plugin-contributed
        // transformers don't have a schema today; 404 lets the SPA fall
        // back to its freeform JSON Textarea without a kind probe.
        group.MapGet("/{key}/schema", (string key) =>
        {
            return BuiltinSchemas.Transformers.TryGetValue(key, out var schema)
                ? Results.Ok(schema)
                : Results.NotFound();
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

        group.MapGet("/{key}/schema", (string key) =>
        {
            return BuiltinSchemas.Analyzers.TryGetValue(key, out var schema)
                ? Results.Ok(schema)
                : Results.NotFound();
        }).RequireKindPermission(EntityKinds.Analyzer, Actions.List);

        return app;
    }

    public sealed record AnalyzerCatalogEntry(string Key, string DisplayName);
}
