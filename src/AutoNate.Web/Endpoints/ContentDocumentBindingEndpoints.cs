using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.EndpointFilters;
using AutoNate.Web.Persistence;
using AutoNate.Web.Persistence.Scaffolded;
using AutoNate.Web.Services.Content;
using AutoNate.Web.Services.Content.Bindings;
using AutoNate.Web.Services.Events;
using Microsoft.EntityFrameworkCore;

namespace AutoNate.Web.Endpoints;

// Document bindings (Phase 5).
//
// Bindings live in REST (not Yjs) so they're queryable for audit + RAG
// and their permissions can be enforced at the row level. The document
// body carries only a placeholder `{{binding:<id>}}` text marker; the
// resolved value lives in this table and the editor's decoration plugin
// paints it over the placeholder at render time.
//
// Snapshot-on-open semantics — refresh is explicit. v1 wires Document.Edit
// users to create + delete bindings; Document.RefreshBindings (a separate
// action so Phase 4's Commenter role could later be promoted to also
// refresh without granting Edit) for per-binding + refresh-all.
public static class ContentDocumentBindingEndpoints
{
    public static IEndpointRouteBuilder MapContentDocumentBindingEndpoints(
        this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/content/documents/{documentId:guid}/bindings")
            .RequireAuthorization();

        group.MapGet("/", async (
            Guid documentId,
            IDbContextFactory<AutoNateDbContext> dbFactory,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var rows = await db.DocumentBindings.AsNoTracking()
                .Where(b => b.DocumentId == documentId)
                .OrderBy(b => b.CreatedAtUtc)
                .ToListAsync(ct);

            await auditPublisher.PublishAsync(
                ContentEventTopic.TopicName,
                ContentEventTypes.DocumentBindingListViewed,
                ContentResourceKinds.DocumentBinding,
                resource: new { documentId },
                details: new { resultCount = rows.Count },
                ct);

            return Results.Ok(new DocumentBindingListResponse(
                rows.Select(MapDto).ToList()));
        }).RequirePermission(EntityKinds.Document, Actions.View, "documentId");

        group.MapPost("/", async (
            Guid documentId,
            CreateDocumentBindingRequest request,
            HttpContext http,
            IDbContextFactory<AutoNateDbContext> dbFactory,
            IDocumentBindingResolverRegistry resolvers,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            if (!DocumentBindingKinds.IsValid(request.Kind))
            {
                return Results.BadRequest(new { error = $"Unknown binding kind '{request.Kind}'." });
            }
            if (string.IsNullOrWhiteSpace(request.ConfigJsonb))
            {
                return Results.BadRequest(new { error = "Binding config is required." });
            }

            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var documentExists = await db.Documents.AsNoTracking()
                .AnyAsync(d => d.Id == documentId, ct);
            if (!documentExists) return Results.NotFound();

            // Resolve at create time so the binding shows a value
            // immediately — the user gets feedback that the config is
            // valid + sees what they're inserting before they commit.
            var resolver = resolvers.Get(request.Kind);
            var actorId = http.GetActorId();
            DocumentBindingResolveResult resolved;
            try
            {
                resolved = await resolver.ResolveAsync(request.ConfigJsonb, http.User, ct);
            }
            catch (DocumentBindingResolveException ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: ex.StatusCode);
            }

            var now = DateTime.UtcNow;
            var binding = new DocumentBinding
            {
                Id = Guid.NewGuid(),
                DocumentId = documentId,
                Kind = request.Kind,
                ConfigJsonb = request.ConfigJsonb,
                LastResolvedValueJsonb = resolved.ResolvedValueJsonb,
                LastResolvedAtUtc = now,
                LastResolvedByUserId = actorId,
                Label = string.IsNullOrWhiteSpace(request.Label)
                    ? resolved.SuggestedLabel
                    : request.Label!.Trim(),
                CreatedAtUtc = now, UpdatedAtUtc = now,
                CreatedBy = actorId, UpdatedBy = actorId
            };
            db.DocumentBindings.Add(binding);
            await db.SaveChangesAsync(ct);

            await auditPublisher.PublishAsync(
                ContentEventTopic.TopicName,
                ContentEventTypes.DocumentBindingCreated,
                ContentResourceKinds.DocumentBinding,
                resource: new
                {
                    documentId,
                    bindingId = binding.Id,
                    kind = binding.Kind,
                    label = binding.Label
                },
                details: null,
                ct);

            return Results.Created(
                $"/api/content/documents/{documentId}/bindings/{binding.Id}",
                MapDto(binding));
        }).DisableAntiforgery()
          .RequirePermission(EntityKinds.Document, Actions.Edit, "documentId");

        group.MapPost("/{bindingId:guid}/refresh", async (
            Guid documentId,
            Guid bindingId,
            HttpContext http,
            IDbContextFactory<AutoNateDbContext> dbFactory,
            IDocumentBindingResolverRegistry resolvers,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var binding = await db.DocumentBindings
                .FirstOrDefaultAsync(b =>
                    b.DocumentId == documentId && b.Id == bindingId, ct);
            if (binding is null) return Results.NotFound();

            var resolver = resolvers.Get(binding.Kind);
            DocumentBindingResolveResult resolved;
            try
            {
                resolved = await resolver.ResolveAsync(binding.ConfigJsonb, http.User, ct);
            }
            catch (DocumentBindingResolveException ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: ex.StatusCode);
            }

            var actorId = http.GetActorId();
            var now = DateTime.UtcNow;
            binding.LastResolvedValueJsonb = resolved.ResolvedValueJsonb;
            binding.LastResolvedAtUtc = now;
            binding.LastResolvedByUserId = actorId;
            binding.UpdatedAtUtc = now;
            binding.UpdatedBy = actorId;
            // Keep an existing user-set label; only overwrite from
            // resolver suggestion if the row's label is null.
            if (string.IsNullOrWhiteSpace(binding.Label) && resolved.SuggestedLabel is not null)
            {
                binding.Label = resolved.SuggestedLabel;
            }
            await db.SaveChangesAsync(ct);

            await auditPublisher.PublishAsync(
                ContentEventTopic.TopicName,
                ContentEventTypes.DocumentBindingRefreshed,
                ContentResourceKinds.DocumentBinding,
                resource: new
                {
                    documentId,
                    bindingId = binding.Id,
                    kind = binding.Kind
                },
                details: null,
                ct);

            return Results.Ok(MapDto(binding));
        }).DisableAntiforgery()
          .RequirePermission(EntityKinds.Document, Actions.RefreshBindings, "documentId");

        group.MapPost("/refresh-all", async (
            Guid documentId,
            HttpContext http,
            IDbContextFactory<AutoNateDbContext> dbFactory,
            IDocumentBindingResolverRegistry resolvers,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var rows = await db.DocumentBindings
                .Where(b => b.DocumentId == documentId)
                .ToListAsync(ct);
            if (rows.Count == 0)
            {
                return Results.Ok(new DocumentBindingListResponse([]));
            }

            var actorId = http.GetActorId();
            var now = DateTime.UtcNow;
            var failures = new List<object>();
            foreach (var binding in rows)
            {
                if (!resolvers.Has(binding.Kind))
                {
                    // Resolver registration is a server-side thing —
                    // a missing one is a bug, not user error. Skip + log.
                    failures.Add(new { bindingId = binding.Id, error = $"no resolver for kind '{binding.Kind}'" });
                    continue;
                }
                try
                {
                    var resolver = resolvers.Get(binding.Kind);
                    var resolved = await resolver.ResolveAsync(binding.ConfigJsonb, http.User, ct);
                    binding.LastResolvedValueJsonb = resolved.ResolvedValueJsonb;
                    binding.LastResolvedAtUtc = now;
                    binding.LastResolvedByUserId = actorId;
                    binding.UpdatedAtUtc = now;
                    binding.UpdatedBy = actorId;
                    if (string.IsNullOrWhiteSpace(binding.Label) && resolved.SuggestedLabel is not null)
                    {
                        binding.Label = resolved.SuggestedLabel;
                    }
                }
                catch (DocumentBindingResolveException ex)
                {
                    // Don't abort the whole batch on one bad binding —
                    // record the failure and continue. The response
                    // reports per-binding failures so the SPA can flag
                    // them in the side panel.
                    failures.Add(new { bindingId = binding.Id, error = ex.Message });
                }
            }
            await db.SaveChangesAsync(ct);

            await auditPublisher.PublishAsync(
                ContentEventTopic.TopicName,
                ContentEventTypes.DocumentBindingsRefreshedAll,
                ContentResourceKinds.DocumentBinding,
                resource: new { documentId },
                details: new
                {
                    total = rows.Count,
                    succeeded = rows.Count - failures.Count,
                    failed = failures.Count
                },
                ct);

            return Results.Ok(new RefreshAllResponse(
                rows.Select(MapDto).ToList(),
                failures));
        }).DisableAntiforgery()
          .RequirePermission(EntityKinds.Document, Actions.RefreshBindings, "documentId");

        group.MapDelete("/{bindingId:guid}", async (
            Guid documentId,
            Guid bindingId,
            IDbContextFactory<AutoNateDbContext> dbFactory,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var binding = await db.DocumentBindings
                .FirstOrDefaultAsync(b =>
                    b.DocumentId == documentId && b.Id == bindingId, ct);
            if (binding is null) return Results.NotFound();
            db.DocumentBindings.Remove(binding);
            await db.SaveChangesAsync(ct);

            await auditPublisher.PublishAsync(
                ContentEventTopic.TopicName,
                ContentEventTypes.DocumentBindingDeleted,
                ContentResourceKinds.DocumentBinding,
                resource: new
                {
                    documentId,
                    bindingId = binding.Id,
                    kind = binding.Kind
                },
                details: null,
                ct);

            return Results.NoContent();
        }).DisableAntiforgery()
          .RequirePermission(EntityKinds.Document, Actions.Edit, "documentId");

        return app;
    }

    private static DocumentBindingDto MapDto(DocumentBinding b) => new(
        b.Id,
        b.DocumentId,
        b.Kind,
        b.ConfigJsonb,
        b.LastResolvedValueJsonb,
        b.LastResolvedAtUtc,
        b.LastResolvedByUserId,
        b.Label,
        b.CreatedAtUtc,
        b.UpdatedAtUtc,
        b.CreatedBy,
        b.UpdatedBy);

    public sealed record CreateDocumentBindingRequest(
        string Kind,
        string ConfigJsonb,
        string? Label);

    public sealed record DocumentBindingDto(
        Guid Id,
        Guid DocumentId,
        string Kind,
        string ConfigJsonb,
        string? LastResolvedValueJsonb,
        DateTime? LastResolvedAtUtc,
        Guid? LastResolvedByUserId,
        string? Label,
        DateTime CreatedAtUtc,
        DateTime UpdatedAtUtc,
        Guid CreatedBy,
        Guid UpdatedBy);

    public sealed record DocumentBindingListResponse(List<DocumentBindingDto> Items);

    public sealed record RefreshAllResponse(
        List<DocumentBindingDto> Items,
        List<object> Failures);
}
