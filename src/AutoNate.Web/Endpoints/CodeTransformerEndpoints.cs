using AutoNate.Plugins.Abstractions;
using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.EndpointFilters;
using AutoNate.Web.Authorization.Evaluator;
using AutoNate.Web.Persistence.Scaffolded;
using AutoNate.Web.Services.Pipelines;
using AutoNate.Web.Services.Pipelines.Execution;
using AutoNate.Web.Services.Transformers.Code;

namespace AutoNate.Web.Endpoints;

// User-authored transformer / analyzer CRUD (Phase 6 of the Data Stores
// plan). Reuses Transformer/Analyzer EntityKinds — the catalog endpoints
// in TransformerEndpoints.cs / AnalyzerEndpoints.cs would surface these
// alongside built-ins in a Phase 6.1 follow-up; today the list endpoint
// here is the single read surface.
//
// Setting `IsUnsafe=true` requires the `executeunsafe` action on the
// matching kind so a non-trusted author can't silently flip a sandbox
// off. Plain create/edit reuses the existing Transformer/Analyzer
// Run/View actions for kind-level gating; row-level access falls back to
// owner-only at the store boundary in v1.
public static class CodeTransformerEndpoints
{
    public static IEndpointRouteBuilder MapCodeTransformerEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/code-transformers").RequireAuthorization();

        group.MapGet("/", async (ICodeTransformerStore store, CancellationToken ct) =>
        {
            var rows = await store.ListAsync(ct);
            return Results.Ok(rows.Select(MapDto).ToList());
        }).RequireKindPermission(EntityKinds.Transformer, Actions.List);

        group.MapGet("/{id:guid}", async (Guid id, ICodeTransformerStore store, CancellationToken ct) =>
        {
            var row = await store.GetAsync(id, ct);
            return row is null ? Results.NotFound() : Results.Ok(MapDto(row));
        }).AuthorizedInHandler(
            "Code transformer detail (including source). Catalog visibility " +
            "is gated by Transformer:List at the list endpoint above; the " +
            "detail call returns the same shape plus the code body.");

        group.MapPost("/", async (
            CreateCodeTransformerRequest request,
            HttpContext http,
            ICodeTransformerStore store,
            IAuthorizer authorizer,
            CancellationToken ct) =>
        {
            if (request is null) return Results.BadRequest();
            var actorId = http.GetActorId();
            if (actorId == Guid.Empty) return Results.Unauthorized();
            if (request.IsUnsafe)
            {
                var decision = await authorizer.AuthorizeAsync(
                    http.User, Actions.ExecuteUnsafe,
                    new EntityRef(MapKindToEntityKind(request.Kind), string.Empty), ct);
                if (!decision.IsAllowed)
                {
                    return Results.StatusCode(StatusCodes.Status403Forbidden);
                }
            }
            try
            {
                var row = await store.CreateAsync(
                    new CreateCodeTransformerInput(
                        request.Name, request.Description, request.Kind,
                        request.Language, request.Code ?? string.Empty, request.IsUnsafe),
                    actorId, ct);
                return Results.Created($"/api/code-transformers/{row.Id}", MapDto(row));
            }
            catch (CodeTransformerNameConflictException ex)
            {
                return Results.Conflict(new { reason = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { reason = ex.Message });
            }
        }).RequireKindPermission(EntityKinds.Transformer, Actions.Run)
          .DisableAntiforgery();

        group.MapPut("/{id:guid}", async (
            Guid id,
            UpdateCodeTransformerRequest request,
            HttpContext http,
            ICodeTransformerStore store,
            IAuthorizer authorizer,
            CancellationToken ct) =>
        {
            if (request is null) return Results.BadRequest();
            var actorId = http.GetActorId();
            if (actorId == Guid.Empty) return Results.Unauthorized();
            var existing = await store.GetAsync(id, ct);
            if (existing is null) return Results.NotFound();
            // Owner-only edit for v1 (same convention as saved queries).
            if (existing.OwnerUserId != actorId) return Results.NotFound();
            // Toggling on IsUnsafe requires the per-kind executeunsafe gate;
            // toggling off does not (you're closing the door).
            if (request.IsUnsafe is true && !existing.IsUnsafe)
            {
                var decision = await authorizer.AuthorizeAsync(
                    http.User, Actions.ExecuteUnsafe,
                    new EntityRef(MapKindToEntityKind(existing.Kind), string.Empty), ct);
                if (!decision.IsAllowed)
                {
                    return Results.StatusCode(StatusCodes.Status403Forbidden);
                }
            }
            try
            {
                var row = await store.UpdateAsync(
                    id,
                    new UpdateCodeTransformerInput(
                        request.Name, request.Description, request.Code, request.IsUnsafe),
                    actorId, ct);
                return Results.Ok(MapDto(row));
            }
            catch (CodeTransformerNotFoundException) { return Results.NotFound(); }
            catch (CodeTransformerNameConflictException ex)
            {
                return Results.Conflict(new { reason = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { reason = ex.Message });
            }
        }).DisableAntiforgery()
          .AuthorizedInHandler(
              "Store-side owner-only edit; non-owners see NotFound. IsUnsafe " +
              "toggle-on additionally requires executeunsafe on the matching kind.");

        // Synchronous "test run" — author dispatches their current editor
        // buffer (the request body's `code` overrides the stored row so
        // they can iterate without saving) against a small JSON sample and
        // gets the executor sidecar's output or error back inline. Same
        // owner-only gate the edit endpoint uses; uses the existing
        // JetStreamCodeNodeRunner so v1 inherits the sidecar's 30s timeout,
        // 128MB memory cap, and Pyodide cold-start. The synthesized run /
        // node ids are namespaced under `test-` so a sidecar log line is
        // obviously a test invocation rather than a real pipeline step.
        group.MapPost("/{id:guid}/test", async (
            Guid id,
            TestCodeTransformerRequest? request,
            HttpContext http,
            ICodeTransformerStore store,
            JetStreamCodeNodeRunner runner,
            CancellationToken ct) =>
        {
            request ??= new TestCodeTransformerRequest(null, null, null);
            var actorId = http.GetActorId();
            if (actorId == Guid.Empty) return Results.Unauthorized();
            var existing = await store.GetAsync(id, ct);
            if (existing is null) return Results.NotFound();
            if (existing.OwnerUserId != actorId) return Results.NotFound();

            // Layer the request body over the stored row so unsaved edits
            // can be exercised. We do NOT let the request override
            // language / kind / isUnsafe — those are sticky to the saved
            // identity. If the author wants to flip isUnsafe they save
            // first (which goes through the executeunsafe gate).
            var transient = new CodeTransformer
            {
                Id = existing.Id,
                Name = existing.Name,
                Description = existing.Description,
                Kind = existing.Kind,
                Language = existing.Language,
                Code = string.IsNullOrEmpty(request.Code) ? existing.Code : request.Code,
                IsUnsafe = existing.IsUnsafe,
                OwnerUserId = existing.OwnerUserId,
                CreatedAtUtc = existing.CreatedAtUtc,
                UpdatedAtUtc = existing.UpdatedAtUtc,
                CreatedBy = existing.CreatedBy,
                UpdatedBy = existing.UpdatedBy
            };

            var node = new PipelineNode(
                Id: $"test-{Guid.NewGuid():N}",
                Kind: existing.Kind == CodeTransformerKinds.Analyzer
                    ? PipelineNodeKinds.Analyzer
                    : PipelineNodeKinds.Transformer,
                Key: existing.Name,
                Config: request.Config ?? new Dictionary<string, string>(StringComparer.Ordinal),
                Position: null);

            var inputs = BuildInputFrames(request.InputRows);
            var runId = Guid.NewGuid();

            try
            {
                var output = await runner.RunCodeAsync(runId, node, transient, inputs, ct);
                return Results.Ok(new TestCodeTransformerResult(
                    Success: true,
                    ErrorMessage: null,
                    OutputRows: output?.Rows ?? Array.Empty<IReadOnlyDictionary<string, object?>>()));
            }
            catch (InvalidOperationException ex)
            {
                // Sidecar timeouts, executor errors, and authoring bugs
                // all surface as InvalidOperationException from the
                // runner — fold into the structured response so the
                // editor can render the message inline rather than
                // bubbling a 5xx.
                return Results.Ok(new TestCodeTransformerResult(
                    Success: false,
                    ErrorMessage: ex.Message,
                    OutputRows: Array.Empty<IReadOnlyDictionary<string, object?>>()));
            }
        }).DisableAntiforgery()
          .AuthorizedInHandler(
              "Store-side owner-only: non-owners see NotFound. Re-uses " +
              "the pipeline executor sidecar; carries the same timeout / " +
              "memory limits as a real pipeline-node invocation.");

        group.MapDelete("/{id:guid}", async (
            Guid id, HttpContext http, ICodeTransformerStore store, CancellationToken ct) =>
        {
            var actorId = http.GetActorId();
            if (actorId == Guid.Empty) return Results.Unauthorized();
            var existing = await store.GetAsync(id, ct);
            if (existing is null) return Results.NotFound();
            if (existing.OwnerUserId != actorId) return Results.NotFound();
            var deleted = await store.DeleteAsync(id, ct);
            return deleted ? Results.NoContent() : Results.NotFound();
        }).DisableAntiforgery()
          .AuthorizedInHandler("Owner-only delete; non-owners see NotFound.");

        return app;
    }

    private static string MapKindToEntityKind(string codeKind) => codeKind switch
    {
        CodeTransformerKinds.Analyzer => EntityKinds.Analyzer,
        _ => EntityKinds.Transformer,
    };

    private static CodeTransformerDto MapDto(CodeTransformer row) =>
        new(row.Id, row.Name, row.Description, row.Kind, row.Language, row.Code,
            row.IsUnsafe, row.OwnerUserId, row.CreatedAtUtc, row.UpdatedAtUtc);

    // Build a single-frame input list out of the test request's row
    // array. Columns are inferred from the union of keys across all
    // rows and typed as Text — the sidecar interprets the raw JSON
    // values regardless of declared type, so getting types "wrong"
    // here doesn't change behavior for the v1 test surface. An empty
    // input array produces an empty frame (which most transformers
    // pass through as an empty output).
    private static IReadOnlyList<DataFrame> BuildInputFrames(
        IReadOnlyList<IReadOnlyDictionary<string, object?>>? rows)
    {
        if (rows is null || rows.Count == 0)
        {
            return new[] { DataFrame.Empty };
        }
        var columnNames = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            foreach (var key in row.Keys)
            {
                if (seen.Add(key)) columnNames.Add(key);
            }
        }
        var columns = columnNames
            .Select(name => new DataColumn(name, DataColumnType.Text))
            .ToList();
        return new[] { new DataFrame(columns, rows) };
    }
}

public sealed record class CreateCodeTransformerRequest(
    string Name,
    string? Description,
    string Kind,
    string Language,
    string? Code,
    bool IsUnsafe);

public sealed record class UpdateCodeTransformerRequest(
    string? Name,
    string? Description,
    string? Code,
    bool? IsUnsafe);

public sealed record class CodeTransformerDto(
    Guid Id,
    string Name,
    string? Description,
    string Kind,
    string Language,
    string Code,
    bool IsUnsafe,
    Guid OwnerUserId,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record class TestCodeTransformerRequest(
    // Optional: override the stored row's code with the editor's current
    // unsaved buffer. Null/empty falls back to the stored row.
    string? Code,
    // Optional flat string→string config map, same shape as a pipeline
    // node's config. Null is treated as empty.
    IReadOnlyDictionary<string, string>? Config,
    // Sample input rows. Treated as a single input frame; columns are
    // inferred from the union of keys.
    IReadOnlyList<IReadOnlyDictionary<string, object?>>? InputRows);

public sealed record class TestCodeTransformerResult(
    bool Success,
    string? ErrorMessage,
    IReadOnlyList<IReadOnlyDictionary<string, object?>> OutputRows);
