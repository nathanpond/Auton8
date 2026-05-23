using AutoNate.Web.Authorization.EndpointFilters;
using AutoNate.Web.Persistence.Scaffolded;
using AutoNate.Web.Services.Events;
using AutoNate.Web.Services.Query;

namespace AutoNate.Web.Endpoints;

public static class SavedQueryEndpoints
{
    // /api/saved-queries — owner-edits + visible-to-shared list/get. Any
    // authenticated user can create their own; updates and deletes are
    // owner-only (store hides non-owner targets behind NotFound). The endpoint
    // group leans on the same query.events audit topic as the executor so all
    // AQL-surface lifecycle events stay together in the Events admin page.
    public static IEndpointRouteBuilder MapSavedQueryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/saved-queries").RequireAuthorization();

        group.MapGet("/", async (
            HttpContext http,
            ISavedQueryStore store,
            CancellationToken ct) =>
        {
            var actorId = http.GetActorId();
            if (actorId == Guid.Empty) return Results.Unauthorized();
            var rows = await store.ListForActorAsync(actorId, ct);
            return Results.Ok(rows.Select(r => MapDto(r, actorId)).ToList());
        }).AuthorizedInHandler(
            "Visibility-filtered: actor's own rows plus every is_shared row. " +
            "Owner-only mutations gated below.");

        group.MapPost("/", async (
            CreateSavedQueryRequest request,
            HttpContext http,
            ISavedQueryStore store,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            var actorId = http.GetActorId();
            if (actorId == Guid.Empty) return Results.Unauthorized();
            try
            {
                var saved = await store.CreateAsync(
                    new CreateSavedQueryInput(
                        request.Name ?? string.Empty,
                        request.Description,
                        request.QueryText ?? string.Empty,
                        request.IsShared),
                    actorId, ct);

                await auditPublisher.PublishAsync(
                    QueryEventTopic.TopicName,
                    QueryEventTypes.SavedQuerySaved,
                    QueryResourceKinds.SavedQuery,
                    resource: new { id = saved.Id, name = saved.Name },
                    details: new { isShared = saved.IsShared, queryText = saved.QueryText },
                    ct);

                return Results.Created($"/api/saved-queries/{saved.Id}", MapDto(saved, actorId));
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (SavedQueryNameConflictException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }
        }).DisableAntiforgery()
          .OpenToAuthenticated(
              "Any signed-in user can save their own queries; ownership is " +
              "captured in saved_queries.owner_user_id at insert time.");

        group.MapPatch("/{id:guid}", async (
            Guid id,
            UpdateSavedQueryRequest request,
            HttpContext http,
            ISavedQueryStore store,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            var actorId = http.GetActorId();
            if (actorId == Guid.Empty) return Results.Unauthorized();
            try
            {
                var saved = await store.UpdateAsync(
                    id,
                    new UpdateSavedQueryInput(
                        request.Name,
                        request.Description,
                        request.QueryText,
                        request.IsShared),
                    actorId, ct);

                await auditPublisher.PublishAsync(
                    QueryEventTopic.TopicName,
                    QueryEventTypes.SavedQueryUpdated,
                    QueryResourceKinds.SavedQuery,
                    resource: new { id = saved.Id, name = saved.Name },
                    details: new { isShared = saved.IsShared, queryText = saved.QueryText },
                    ct);

                return Results.Ok(MapDto(saved, actorId));
            }
            catch (SavedQueryNotFoundException)
            {
                return Results.NotFound();
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (SavedQueryNameConflictException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }
        }).DisableAntiforgery()
          .AuthorizedInHandler(
              "Store enforces owner-only edit; returns NotFound for both " +
              "missing rows and non-owners.");

        group.MapDelete("/{id:guid}", async (
            Guid id,
            HttpContext http,
            ISavedQueryStore store,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            var actorId = http.GetActorId();
            if (actorId == Guid.Empty) return Results.Unauthorized();
            var existing = await store.GetForActorAsync(id, actorId, ct);
            if (existing is null || existing.OwnerUserId != actorId) return Results.NotFound();
            var ok = await store.DeleteAsync(id, actorId, ct);
            if (!ok) return Results.NotFound();

            await auditPublisher.PublishAsync(
                QueryEventTopic.TopicName,
                QueryEventTypes.SavedQueryDeleted,
                QueryResourceKinds.SavedQuery,
                resource: new { id, name = existing.Name },
                details: null,
                ct);

            return Results.NoContent();
        }).DisableAntiforgery()
          .AuthorizedInHandler(
              "Store enforces owner-only delete; returns NotFound for both " +
              "missing rows and non-owners.");

        return app;
    }

    private static SavedQueryDto MapDto(SavedQuery q, Guid actorId) =>
        new(q.Id, q.Name, q.Description, q.QueryText, q.IsShared,
            q.OwnerUserId == actorId,
            q.OwnerUserId,
            q.CreatedAtUtc, q.UpdatedAtUtc);

    public sealed record CreateSavedQueryRequest(
        string? Name,
        string? Description,
        string? QueryText,
        bool IsShared);

    public sealed record UpdateSavedQueryRequest(
        string? Name,
        string? Description,
        string? QueryText,
        bool? IsShared);

    // `IsOwn` lets the SPA show edit/delete affordances only on rows the
    // caller owns without rechecking owner_user_id against the auth/me id.
    public sealed record SavedQueryDto(
        Guid Id,
        string Name,
        string? Description,
        string QueryText,
        bool IsShared,
        bool IsOwn,
        Guid OwnerUserId,
        DateTime CreatedAtUtc,
        DateTime UpdatedAtUtc);
}
