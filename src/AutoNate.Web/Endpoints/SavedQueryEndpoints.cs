using AutoNate.Web.Authorization;
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

        // ---- Phase 3 share-token surface -------------------------------------

        group.MapGet("/{id:guid}/shares", async (
            Guid id,
            HttpContext http,
            ISavedQueryStore queryStore,
            ISavedQueryShareTokenStore tokenStore,
            CancellationToken ct) =>
        {
            var actorId = http.GetActorId();
            if (actorId == Guid.Empty) return Results.Unauthorized();
            // Only the owner sees the list of share tokens issued on their
            // own query — non-owners with `query:share` on the kind would
            // still need their own view of the underlying row, and we don't
            // surface foreign-issued tokens here.
            var existing = await queryStore.GetForActorAsync(id, actorId, ct);
            if (existing is null || existing.OwnerUserId != actorId) return Results.NotFound();
            var tokens = await tokenStore.ListForQueryAsync(id, ct);
            return Results.Ok(tokens.Select(MapTokenDto).ToList());
        }).AuthorizedInHandler(
            "Owner-only list of issued share tokens. Token hashes are NOT " +
            "surfaced — only metadata (id, expiry, label, use count).");

        group.MapPost("/{id:guid}/shares", async (
            Guid id,
            IssueShareTokenRequest request,
            HttpContext http,
            ISavedQueryStore queryStore,
            ISavedQueryShareTokenStore tokenStore,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            var actorId = http.GetActorId();
            if (actorId == Guid.Empty) return Results.Unauthorized();
            var existing = await queryStore.GetForActorAsync(id, actorId, ct);
            if (existing is null || existing.OwnerUserId != actorId) return Results.NotFound();
            try
            {
                var issued = await tokenStore.IssueAsync(
                    new IssueShareTokenInput(
                        SavedQueryId: id,
                        ExpiresAtUtc: request?.ExpiresAtUtc,
                        MaxUses: request?.MaxUses,
                        Label: request?.Label),
                    actorId, ct);

                await auditPublisher.PublishAsync(
                    QueryEventTopic.TopicName,
                    QueryEventTypes.SavedQuerySaved,
                    QueryResourceKinds.SavedQuery,
                    resource: new { id, name = existing.Name },
                    details: new
                    {
                        share = "issued",
                        tokenId = issued.Row.Id,
                        expiresAtUtc = issued.Row.ExpiresAtUtc,
                        maxUses = issued.Row.MaxUses,
                    },
                    ct);

                return Results.Created(
                    $"/api/saved-queries/{id}/shares/{issued.Row.Id}",
                    new IssuedShareTokenDto(
                        Token: MapTokenDto(issued.Row),
                        RawToken: issued.RawToken,
                        ShareUrl: BuildShareUrl(http, issued.RawToken)));
            }
            catch (SavedQueryNotFoundException)
            {
                return Results.NotFound();
            }
        }).DisableAntiforgery()
          .RequirePermission(EntityKinds.Query, Actions.Share);

        group.MapDelete("/{id:guid}/shares/{tokenId:guid}", async (
            Guid id,
            Guid tokenId,
            HttpContext http,
            ISavedQueryStore queryStore,
            ISavedQueryShareTokenStore tokenStore,
            CancellationToken ct) =>
        {
            var actorId = http.GetActorId();
            if (actorId == Guid.Empty) return Results.Unauthorized();
            var existing = await queryStore.GetForActorAsync(id, actorId, ct);
            if (existing is null || existing.OwnerUserId != actorId) return Results.NotFound();
            var revoked = await tokenStore.RevokeAsync(tokenId, ct);
            return revoked ? Results.NoContent() : Results.NotFound();
        }).DisableAntiforgery()
          .AuthorizedInHandler(
              "Owner-only revoke of a share token they previously issued.");

        return app;
    }

    private static string BuildShareUrl(HttpContext http, string rawToken)
    {
        var request = http.Request;
        // Audit fix archived-9 — was /api/public/queries/share/{token}, which
        // dropped recipients on raw JSON. The new /q/{token} route is
        // an unauthenticated SPA page that calls the same backend
        // endpoint and renders the result in a DataTable, handles
        // missing parameters with a fill-in form, and reads :params
        // from the query string so the link is the entire shareable
        // artifact.
        return $"{request.Scheme}://{request.Host}/q/{rawToken}";
    }

    private static SavedQueryShareTokenDto MapTokenDto(SavedQueryShareToken t) =>
        new(t.Id, t.IssuedBy, t.IssuedAtUtc, t.ExpiresAtUtc, t.RevokedAtUtc,
            t.MaxUses, t.UseCount, t.LastUsedAtUtc, t.Label);

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

    public sealed record IssueShareTokenRequest(
        DateTime? ExpiresAtUtc,
        int? MaxUses,
        string? Label);

    // Metadata-only token view. RawToken is returned ONLY by POST /shares;
    // every subsequent GET surfaces this redacted shape so a DB or list
    // call can't reconstruct a working URL.
    public sealed record SavedQueryShareTokenDto(
        Guid Id,
        Guid IssuedBy,
        DateTime IssuedAtUtc,
        DateTime? ExpiresAtUtc,
        DateTime? RevokedAtUtc,
        int? MaxUses,
        int UseCount,
        DateTime? LastUsedAtUtc,
        string? Label);

    public sealed record IssuedShareTokenDto(
        SavedQueryShareTokenDto Token,
        string RawToken,
        string ShareUrl);
}
