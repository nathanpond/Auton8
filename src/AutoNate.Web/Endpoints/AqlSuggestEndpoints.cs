using AutoNate.Web.Authorization.EndpointFilters;
using AutoNate.Web.Services.Query;

namespace AutoNate.Web.Endpoints;

// Phase 8c — natural-language → AQL suggestion. Powers the binding "suggest
// a query" dialog: a user describes what they want in plain English and gets
// back a drafted, server-validated AQL query they can drop into an aql-table
// binding. Same authorization posture as /api/aql/schema — any authenticated
// user, because the endpoint only drafts query TEXT (it never executes the
// query or reads record data; per-entity read enforcement happens later at
// /api/query / binding-resolve time).
public static class AqlSuggestEndpoints
{
    public static IEndpointRouteBuilder MapAqlSuggestEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/aql/suggest", async (
            SuggestAqlRequest request,
            HttpContext http,
            IAqlSuggestionService service,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Description))
            {
                return Results.BadRequest(new { error = "Description is required." });
            }
            try
            {
                var s = await service.SuggestAsync(request.Description, http.User, ct);
                return Results.Ok(new
                {
                    query = s.Query,
                    valid = s.Valid,
                    errors = s.Errors,
                    explanation = s.Explanation
                });
            }
            catch (AqlSuggestionUnavailableException ex)
            {
                // 503: the feature is configured-dependent (needs an LLM
                // connection). The SPA surfaces the message as a soft warning.
                return Results.Json(
                    new { error = ex.Message },
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        })
        .RequireAuthorization()
        .DisableAntiforgery()
        .AuthorizedInHandler(
            "Drafts an AQL query from a natural-language description via the " +
            "configured LLM and validates it. Returns query TEXT only — never " +
            "executes the query or reads record data. Same posture as the " +
            "/api/aql/schema catalog: any authenticated user.");

        return app;
    }

    public sealed record SuggestAqlRequest(string Description);
}
