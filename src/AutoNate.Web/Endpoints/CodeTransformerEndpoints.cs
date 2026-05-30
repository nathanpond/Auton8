using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.EndpointFilters;
using AutoNate.Web.Authorization.Evaluator;
using AutoNate.Web.Persistence.Scaffolded;
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
