using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.EndpointFilters;
using AutoNate.Web.Services.DataConnectors;

namespace AutoNate.Web.Endpoints;

public static class DataConnectorEndpoints
{
    public static IEndpointRouteBuilder MapDataConnectorEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/dataconnectors").RequireAuthorization();

        group.MapGet("/", async (IDataConnectorStore store, CancellationToken ct) =>
        {
            var rows = await store.ListAsync(ct);
            return Results.Ok(rows);
        }).RequireKindPermission(EntityKinds.DataConnector, Actions.List);

        // Live list of registered connector kinds (built-in + plugin-contributed).
        // Surfaced so the SPA create form's kind dropdown stays in sync with what
        // plugins enable/disable at runtime.
        group.MapGet("/kinds", (IDataConnectorHandlerRegistry registry) =>
        {
            return Results.Ok(registry.Kinds);
        }).RequireKindPermission(EntityKinds.DataConnector, Actions.List);

        group.MapGet("/{id:guid}", async (Guid id, IDataConnectorStore store, CancellationToken ct) =>
        {
            var row = await store.GetAsync(id, ct);
            return row is null ? Results.NotFound() : Results.Ok(row);
        }).RequirePermission(EntityKinds.DataConnector, Actions.View);

        group.MapPost("/", async (
            CreateDataConnectorRequest request,
            HttpContext http,
            IDataConnectorStore store,
            IDataConnectorHandlerRegistry registry,
            CancellationToken ct) =>
        {
            if (request is null) return Results.BadRequest();
            var actorId = http.GetActorId();
            if (actorId == Guid.Empty) return Results.Unauthorized();
            if (!registry.TryGet(request.Kind, out _))
            {
                return Results.BadRequest(new { reason = $"Unknown connector kind '{request.Kind}'." });
            }
            try
            {
                var row = await store.CreateAsync(
                    new CreateDataConnectorInput(request.Name, request.Description, request.Kind, request.ConfigJson ?? "{}"),
                    actorId, ct);
                return Results.Created($"/api/dataconnectors/{row.Id}", row);
            }
            catch (DataConnectorNameConflictException ex)
            {
                return Results.Conflict(new { reason = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { reason = ex.Message });
            }
        }).RequireKindPermission(EntityKinds.DataConnector, Actions.Create)
          .DisableAntiforgery();

        group.MapPut("/{id:guid}", async (
            Guid id,
            UpdateDataConnectorRequest request,
            HttpContext http,
            IDataConnectorStore store,
            CancellationToken ct) =>
        {
            if (request is null) return Results.BadRequest();
            var actorId = http.GetActorId();
            if (actorId == Guid.Empty) return Results.Unauthorized();
            try
            {
                var row = await store.UpdateAsync(
                    id,
                    new UpdateDataConnectorInput(request.Name, request.Description, request.ConfigJson),
                    actorId, ct);
                return Results.Ok(row);
            }
            catch (DataConnectorNotFoundException)
            {
                return Results.NotFound();
            }
            catch (DataConnectorNameConflictException ex)
            {
                return Results.Conflict(new { reason = ex.Message });
            }
        }).RequirePermission(EntityKinds.DataConnector, Actions.Edit)
          .DisableAntiforgery();

        group.MapDelete("/{id:guid}", async (
            Guid id, IDataConnectorStore store, CancellationToken ct) =>
        {
            var deleted = await store.DeleteAsync(id, ct);
            return deleted ? Results.NoContent() : Results.NotFound();
        }).RequirePermission(EntityKinds.DataConnector, Actions.Delete);

        group.MapPost("/{id:guid}/test", async (
            Guid id,
            IDataConnectorStore store,
            IDataConnectorHandlerRegistry registry,
            CancellationToken ct) =>
        {
            var row = await store.GetAsync(id, ct);
            if (row is null) return Results.NotFound();
            if (!registry.TryGet(row.Kind, out var handler))
            {
                return Results.BadRequest(new { reason = $"No handler registered for kind '{row.Kind}'." });
            }
            var result = await handler.TestAsync(row, ct);
            return Results.Ok(result);
        }).RequirePermission(EntityKinds.DataConnector, Actions.Connect);

        // Fetch a small sample of rows from the connector without
        // persisting any state. The audit's "no fetch data preview surface"
        // gap (#6) — previously authors had to point a Dataset at the
        // connector and wait for a refresh tick to know if the config was
        // pulling anything. Uses a buffered sink that throws a sentinel
        // exception once the cap is reached so handlers that streamingly
        // emit don't have to know about preview semantics. Cursor is left
        // null so the preview always shows a "from-scratch" pull
        // regardless of incremental state.
        group.MapPost("/{id:guid}/preview", async (
            Guid id,
            PreviewDataConnectorRequest? request,
            IDataConnectorStore store,
            IDataConnectorHandlerRegistry registry,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var row = await store.GetAsync(id, ct);
            if (row is null) return Results.NotFound();
            if (!registry.TryGet(row.Kind, out var handler))
            {
                return Results.BadRequest(new { reason = $"No handler registered for kind '{row.Kind}'." });
            }
            var maxRows = Math.Clamp(request?.MaxRows ?? 5, 1, 50);
            var sink = new BufferedPreviewSink(maxRows);
            var state = new ConnectorRefreshState(LastFetchedAtUtc: null, Cursor: null);
            try
            {
                await handler.FetchAsync(row, state, sink, ct);
            }
            catch (PreviewRowLimitReachedException)
            {
                // Expected once the cap is hit — sink throws to short-
                // circuit handlers that stream entire result sets.
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Fold any handler-side error into the structured response so
                // the editor renders it inline rather than bubbling a 5xx —
                // but the raw text carries internal hostnames, connection-
                // string fragments and driver detail, which is reconnaissance
                // for anyone holding DataConnector:Connect. Log the detail
                // against a correlation id and hand back only the id (#68).
                var errorId = Guid.NewGuid().ToString("N")[..12];
                loggerFactory
                    .CreateLogger("AutoNate.Web.Endpoints.DataConnectorEndpoints")
                    .LogWarning(ex,
                        "Data connector preview failed for {ConnectorId} (kind {Kind}). ErrorId {ErrorId}.",
                        row.Id, row.Kind, errorId);
                return Results.Ok(new DataConnectorPreviewResult(
                    Success: false,
                    ErrorMessage: $"Preview failed. Reference {errorId} in the server log for details.",
                    Columns: Array.Empty<string>(),
                    Rows: Array.Empty<IReadOnlyDictionary<string, object?>>()));
            }
            return Results.Ok(new DataConnectorPreviewResult(
                Success: true,
                ErrorMessage: null,
                Columns: sink.ColumnNames,
                Rows: sink.Rows));
        }).RequirePermission(EntityKinds.DataConnector, Actions.Connect)
          .DisableAntiforgery();

        return app;
    }
}

// Sentinel — thrown by BufferedPreviewSink once the cap is reached. The
// endpoint catches and treats as a clean success. Public-but-internal-
// namespace so the analyzer's "exceptions must be public" rule is happy
// without growing the API surface meaningfully.
public sealed class PreviewRowLimitReachedException : Exception;

internal sealed class BufferedPreviewSink(int maxRows) : IConnectorFetchSink
{
    private readonly List<IReadOnlyDictionary<string, object?>> _rows = new();
    private readonly List<string> _columnNames = new();
    private readonly HashSet<string> _seenColumns = new(StringComparer.Ordinal);

    public IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows => _rows;
    public IReadOnlyList<string> ColumnNames => _columnNames;

    public Task WriteRowAsync(
        IReadOnlyDictionary<string, object?> row, CancellationToken cancellationToken = default)
    {
        foreach (var key in row.Keys)
        {
            if (_seenColumns.Add(key)) _columnNames.Add(key);
        }
        _rows.Add(row);
        if (_rows.Count >= maxRows) throw new PreviewRowLimitReachedException();
        return Task.CompletedTask;
    }

    // The preview endpoint speaks rows; binary blobs from file-shaped
    // connectors don't have a tabular preview, so just count them and
    // stop once the limit hits.
    public Task WriteBlobAsync(
        string filename, Stream content, CancellationToken cancellationToken = default)
    {
        if (_seenColumns.Add("filename")) _columnNames.Add("filename");
        _rows.Add(new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["filename"] = filename
        });
        if (_rows.Count >= maxRows) throw new PreviewRowLimitReachedException();
        return Task.CompletedTask;
    }
}

public sealed record class CreateDataConnectorRequest(
    string Name, string? Description, string Kind, string? ConfigJson);

public sealed record class UpdateDataConnectorRequest(
    string? Name, string? Description, string? ConfigJson);

public sealed record class PreviewDataConnectorRequest(int? MaxRows);

public sealed record class DataConnectorPreviewResult(
    bool Success,
    string? ErrorMessage,
    IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows);
