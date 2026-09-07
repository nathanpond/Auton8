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
// Authoring is gated on Create/Edit/Delete and reading on View, each
// resolved against the *requested* kind via MapKindToEntityKind — a row
// can be an analyzer, and gating everything on Transformer meant
// `analyzer:*` grants were never enforced while a Transformer:Run grant
// conferred authoring rights (archived-23). Row-level access is still owner-only
// at the store boundary in v1; the kind-level check runs first.
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

        group.MapGet("/{id:guid}", async (
            Guid id,
            HttpContext http,
            ICodeTransformerStore store,
            IAuthorizer authorizer,
            CancellationToken ct) =>
        {
            var row = await store.GetAsync(id, ct);
            if (row is null) return Results.NotFound();
            // The response carries the full Python/JS body, so it needs View
            // on the row's own kind. A
            // denial is a NotFound so holding a GUID reveals nothing (archived-22).
            if (!await CanAsync(authorizer, http, row.Kind, Actions.View, ct))
            {
                return Results.NotFound();
            }
            return Results.Ok(MapDto(row));
        }).AuthorizedInHandler(
            "Code transformer detail including the source body: requires View " +
            "on the row's kind (Transformer or Analyzer); denial is NotFound.");

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
            // Authoring right on the kind actually being created — not
            // Transformer:Run, which is an execution grant (archived-23).
            if (!await CanAsync(authorizer, http, request.Kind, Actions.Create, ct))
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }
            try
            {
                var row = await store.CreateAsync(
                    new CreateCodeTransformerInput(
                        request.Name, request.Description, request.Kind,
                        request.Language, request.Code ?? string.Empty),
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
        }).AuthorizedInHandler(
              "Create requires Create on the requested kind (Transformer or " +
              "Analyzer).")
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
            if (!await CanAsync(authorizer, http, existing.Kind, Actions.Edit, ct))
            {
                return Results.NotFound();
            }
            // Owner-only edit for v1 (same convention as saved queries).
            if (existing.OwnerUserId != actorId) return Results.NotFound();
            try
            {
                var row = await store.UpdateAsync(
                    id,
                    new UpdateCodeTransformerInput(
                        request.Name, request.Description, request.Code),
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
              "Requires Edit on the row's kind, then store-side owner-only; " +
              "either failure is NotFound.");

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
            IAuthorizer authorizer,
            JetStreamCodeNodeRunner runner,
            CancellationToken ct) =>
        {
            request ??= new TestCodeTransformerRequest(null, null, null);
            var actorId = http.GetActorId();
            if (actorId == Guid.Empty) return Results.Unauthorized();
            var existing = await store.GetAsync(id, ct);
            if (existing is null) return Results.NotFound();
            // This dispatches code to the executor sidecar, so it is an
            // execution: Run on the row's kind, then owner-only.
            if (!await CanAsync(authorizer, http, existing.Kind, Actions.Run, ct))
            {
                return Results.NotFound();
            }
            if (existing.OwnerUserId != actorId) return Results.NotFound();

            // Layer the request body over the stored row so unsaved edits
            // can be exercised. We do NOT let the request override
            // language / kind — those are sticky to the saved identity.
            var transient = new CodeTransformer
            {
                Id = existing.Id,
                Name = existing.Name,
                Description = existing.Description,
                Kind = existing.Kind,
                Language = existing.Language,
                Code = string.IsNullOrEmpty(request.Code) ? existing.Code : request.Code,
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
              "Requires Run on the row's kind, then store-side owner-only: " +
              "either failure is NotFound. Re-uses the pipeline executor " +
              "sidecar; carries the same timeout / memory limits as a real " +
              "pipeline-node invocation.");

        group.MapDelete("/{id:guid}", async (
            Guid id,
            HttpContext http,
            ICodeTransformerStore store,
            IAuthorizer authorizer,
            CancellationToken ct) =>
        {
            var actorId = http.GetActorId();
            if (actorId == Guid.Empty) return Results.Unauthorized();
            var existing = await store.GetAsync(id, ct);
            if (existing is null) return Results.NotFound();
            if (!await CanAsync(authorizer, http, existing.Kind, Actions.Delete, ct))
            {
                return Results.NotFound();
            }
            if (existing.OwnerUserId != actorId) return Results.NotFound();
            var deleted = await store.DeleteAsync(id, ct);
            return deleted ? Results.NoContent() : Results.NotFound();
        }).DisableAntiforgery()
          .AuthorizedInHandler(
              "Requires Delete on the row's kind, then owner-only; either " +
              "failure is NotFound.");

        return app;
    }

    // Kind-level check against the row's / request's own kind. Every gate in
    // this file goes through here so a transformer grant can never be read as
    // an analyzer grant, or vice versa (archived-23).
    private static async Task<bool> CanAsync(
        IAuthorizer authorizer, HttpContext http, string codeKind, string action, CancellationToken ct)
    {
        var decision = await authorizer.AuthorizeAsync(
            http.User, action, new EntityRef(MapKindToEntityKind(codeKind), string.Empty), ct);
        return decision.IsAllowed;
    }

    private static string MapKindToEntityKind(string codeKind) => codeKind switch
    {
        CodeTransformerKinds.Analyzer => EntityKinds.Analyzer,
        _ => EntityKinds.Transformer,
    };

    private static CodeTransformerDto MapDto(CodeTransformer row) =>
        new(row.Id, row.Name, row.Description, row.Kind, row.Language, row.Code,
            row.OwnerUserId, row.CreatedAtUtc, row.UpdatedAtUtc);

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
    string? Code);

public sealed record class UpdateCodeTransformerRequest(
    string? Name,
    string? Description,
    string? Code);

public sealed record class CodeTransformerDto(
    Guid Id,
    string Name,
    string? Description,
    string Kind,
    string Language,
    string Code,
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
