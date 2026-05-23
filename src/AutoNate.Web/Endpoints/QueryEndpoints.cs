using AutoNate.Web.Authorization.EndpointFilters;
using AutoNate.Web.Services.Events;
using AutoNate.Web.Services.Query;

namespace AutoNate.Web.Endpoints;

public static class QueryEndpoints
{
    // POST /api/query — Execute an AQL query. Returns columns + rows for the
    // SPA's dynamic-column table. Authentication is required at the route
    // group; per-entity reads are enforced inside IAqlExecutor (Record:
    // visibility SQL filter; WorkflowModel: kind-level WorkflowModel:View).
    public static IEndpointRouteBuilder MapQueryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/query").RequireAuthorization();

        group.MapPost("/", async (
            ExecuteQueryRequest request,
            HttpContext http,
            IAqlExecutor executor,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            var queryText = request.Query ?? string.Empty;
            try
            {
                var result = await executor.ExecuteAsync(queryText, http.User, hardCap: 1000, ct);

                await auditPublisher.PublishAsync(
                    QueryEventTopic.TopicName,
                    QueryEventTypes.Executed,
                    QueryResourceKinds.Query,
                    resource: null,
                    details: new
                    {
                        queryText,
                        columnCount = result.Columns.Count,
                        rowCount = result.Rows.Count,
                        truncated = result.Truncated,
                        durationMs = result.DurationMs
                    },
                    ct);

                return Results.Ok(new ExecuteQueryResponse(
                    result.Columns.Select(c => new ColumnDto(c.Name, c.DataType.ToString().ToLowerInvariant())).ToList(),
                    result.Rows,
                    result.TotalCount,
                    result.Truncated,
                    result.DurationMs));
            }
            catch (AqlValidationException ex)
            {
                await auditPublisher.PublishAsync(
                    QueryEventTopic.TopicName,
                    QueryEventTypes.Failed,
                    QueryResourceKinds.Query,
                    resource: null,
                    details: new { queryText, errors = ex.Errors },
                    ct);
                return Results.BadRequest(new { errors = ex.Errors });
            }
        }).DisableAntiforgery()
          .AuthorizedInHandler(
              "Per-entity reads are enforced inside the executor (Record: " +
              "visibility SQL filter; WorkflowModel: kind-level WorkflowModel:View). " +
              "No new permission is introduced — any authenticated user can run AQL.");

        return app;
    }

    public sealed record ExecuteQueryRequest(string? Query);

    public sealed record ColumnDto(string Name, string DataType);

    public sealed record ExecuteQueryResponse(
        IReadOnlyList<ColumnDto> Columns,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows,
        long TotalCount,
        bool Truncated,
        long DurationMs);
}
