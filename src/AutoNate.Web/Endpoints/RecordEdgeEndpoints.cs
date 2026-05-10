using System.Text.Json;
using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.EndpointFilters;
using AutoNate.Web.Authorization.Evaluator;
using AutoNate.Web.Models.Records;
using AutoNate.Web.Persistence;
using AutoNate.Web.Services.Events;
using AutoNate.Web.Services.Records;
using Microsoft.EntityFrameworkCore;
using RecordEntity = AutoNate.Web.Persistence.Scaffolded.Record;

namespace AutoNate.Web.Endpoints;

public sealed record CreateEdgeTypeRequest(
    string ShortCode,
    string Name,
    string? InverseName,
    bool IsDirected,
    bool AllowSelfReference,
    string Cardinality,
    Guid[]? FromRecordTypeIds,
    Guid[]? ToRecordTypeIds);

public sealed record UpdateEdgeTypeRequest(
    string Name,
    string? InverseName,
    bool IsDirected,
    bool AllowSelfReference,
    string Cardinality,
    Guid[]? FromRecordTypeIds,
    Guid[]? ToRecordTypeIds);

public sealed record CreateEdgeFieldRequest(
    string FieldKey,
    string DisplayName,
    string DataType,
    JsonElement Config,
    bool IsRequired,
    int SortOrder);

public sealed record UpdateEdgeFieldRequest(
    string DisplayName,
    JsonElement Config,
    bool IsRequired,
    int SortOrder);

public sealed record CreateEdgeRequest(
    Guid EdgeTypeId,
    Guid FromRecordId,
    Guid ToRecordId,
    JsonElement Data);

public sealed record TraverseHttpRequest(
    Guid[] StartRecordIds,
    Guid[]? EdgeTypeIds,
    string? Direction,
    int? MaxHops);

public sealed record EdgeTypeDto(
    Guid Id,
    string ShortCode,
    string Name,
    string? InverseName,
    bool IsDirected,
    bool AllowSelfReference,
    string Cardinality,
    Guid[]? FromRecordTypeIds,
    Guid[]? ToRecordTypeIds,
    bool IsArchived,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record EdgeTypeFieldDto(
    Guid Id,
    Guid EdgeTypeId,
    string FieldKey,
    string DisplayName,
    string DataType,
    JsonElement Config,
    bool IsRequired,
    int SortOrder);

public sealed record EdgeDto(
    Guid Id,
    Guid EdgeTypeId,
    Guid FromRecordId,
    Guid ToRecordId,
    JsonElement Data,
    DateTimeOffset CreatedAtUtc,
    Guid CreatedBy);

public sealed record TraverseResultDto(Guid RecordId, int Hops);

public static class RecordEdgeEndpoints
{
    public static IEndpointRouteBuilder MapRecordEdgeEndpoints(this IEndpointRouteBuilder app)
    {
        var typeGroup = app.MapGroup("/api/record-edge-types").RequireAuthorization();

        // Edge types don't have their own EntityKind. Visibility piggybacks
        // on RecordType:View: an edge type is visible iff the actor can View
        // at least one of the record types it references via FromRecordTypeIds
        // / ToRecordTypeIds. Unrestricted edge types (no record-type filter)
        // are visible iff the actor can View any record type at all.
        typeGroup.MapGet("/", async (
            bool? includeArchived,
            HttpContext http,
            IRecordEdgeTypeStore store,
            IAuthorizer authorizer,
            IDbContextFactory<AutoNateDbContext> dbContextFactory,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            var types = await store.ListAsync(includeArchived ?? false, ct);
            var visibleRecordTypeIds = await ResolveVisibleRecordTypeIdsAsync(
                http.User, authorizer, dbContextFactory, ct);
            var visible = types.Where(t => IsEdgeTypeVisible(t, visibleRecordTypeIds)).ToList();

            await auditPublisher.PublishAsync(
                RecordSchemaEventTopic.TopicName,
                RecordSchemaEventTypes.RecordEdgeTypeListViewed,
                RecordSchemaResourceKinds.RecordEdgeType,
                resource: null,
                details: new { resultCount = visible.Count, includeArchived = includeArchived ?? false },
                ct);
            return Results.Ok(visible.Select(ToDto).ToList());
        }).AuthorizedInHandler("filters via IsEdgeTypeVisible against actor's RecordType:View grants");

        typeGroup.MapGet("/{id:guid}", async (
            Guid id,
            HttpContext http,
            IRecordEdgeTypeStore store,
            IAuthorizer authorizer,
            IDbContextFactory<AutoNateDbContext> dbContextFactory,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            var type = await store.GetAsync(id, ct);
            if (type is null) return Results.NotFound();

            var visibleRecordTypeIds = await ResolveVisibleRecordTypeIdsAsync(
                http.User, authorizer, dbContextFactory, ct);
            if (!IsEdgeTypeVisible(type, visibleRecordTypeIds))
            {
                return Results.Forbid();
            }

            await auditPublisher.PublishAsync(
                RecordSchemaEventTopic.TopicName,
                RecordSchemaEventTypes.RecordEdgeTypeViewed,
                RecordSchemaResourceKinds.RecordEdgeType,
                resource: new { id = type.Id, shortCode = type.ShortCode, name = type.Name },
                details: null,
                ct);
            return Results.Ok(ToDto(type));
        }).AuthorizedInHandler("returns 403 when IsEdgeTypeVisible says the actor can't see any of the edge type's referenced record types");

        typeGroup.MapPost("/", async (
            CreateEdgeTypeRequest request,
            IRecordEdgeTypeStore store,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            try
            {
                var created = await store.CreateAsync(new CreateRecordEdgeTypeInput(
                    request.ShortCode,
                    request.Name,
                    request.InverseName,
                    request.IsDirected,
                    request.AllowSelfReference,
                    request.Cardinality,
                    request.FromRecordTypeIds,
                    request.ToRecordTypeIds), ct);
                await auditPublisher.PublishAsync(
                    RecordSchemaEventTopic.TopicName,
                    RecordSchemaEventTypes.RecordEdgeTypeCreated,
                    RecordSchemaResourceKinds.RecordEdgeType,
                    resource: new { id = created.Id, shortCode = created.ShortCode, name = created.Name },
                    details: null,
                    ct);
                return Results.Created($"/api/record-edge-types/{created.Id}", ToDto(created));
            }
            catch (RecordEdgeValidationException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        }).DisableAntiforgery()
          .RequireKindPermission(EntityKinds.RecordType, Actions.DefineFields);

        typeGroup.MapPatch("/{id:guid}", async (
            Guid id,
            UpdateEdgeTypeRequest request,
            IRecordEdgeTypeStore store,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            try
            {
                var updated = await store.UpdateAsync(id, new UpdateRecordEdgeTypeInput(
                    request.Name,
                    request.InverseName,
                    request.IsDirected,
                    request.AllowSelfReference,
                    request.Cardinality,
                    request.FromRecordTypeIds,
                    request.ToRecordTypeIds), ct);
                await auditPublisher.PublishAsync(
                    RecordSchemaEventTopic.TopicName,
                    RecordSchemaEventTypes.RecordEdgeTypeUpdated,
                    RecordSchemaResourceKinds.RecordEdgeType,
                    resource: new { id = updated.Id, shortCode = updated.ShortCode, name = updated.Name },
                    details: null,
                    ct);
                return Results.Ok(ToDto(updated));
            }
            catch (RecordEdgeTypeNotFoundException)
            {
                return Results.NotFound();
            }
            catch (RecordEdgeValidationException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        }).DisableAntiforgery()
          .RequireKindPermission(EntityKinds.RecordType, Actions.DefineFields);

        typeGroup.MapDelete("/{id:guid}", async (
            Guid id, IRecordEdgeTypeStore store,
            IAuditEventPublisher auditPublisher, CancellationToken ct) =>
        {
            try
            {
                var archived = await store.SetArchivedAsync(id, true, ct);
                await auditPublisher.PublishAsync(
                    RecordSchemaEventTopic.TopicName,
                    RecordSchemaEventTypes.RecordEdgeTypeArchived,
                    RecordSchemaResourceKinds.RecordEdgeType,
                    resource: new { id = archived.Id, shortCode = archived.ShortCode, name = archived.Name },
                    details: null,
                    ct);
                return Results.Ok(ToDto(archived));
            }
            catch (RecordEdgeTypeNotFoundException) { return Results.NotFound(); }
        }).DisableAntiforgery()
          .RequireKindPermission(EntityKinds.RecordType, Actions.DefineFields);

        typeGroup.MapPost("/{id:guid}/restore", async (
            Guid id, IRecordEdgeTypeStore store,
            IAuditEventPublisher auditPublisher, CancellationToken ct) =>
        {
            try
            {
                var restored = await store.SetArchivedAsync(id, false, ct);
                await auditPublisher.PublishAsync(
                    RecordSchemaEventTopic.TopicName,
                    RecordSchemaEventTypes.RecordEdgeTypeRestored,
                    RecordSchemaResourceKinds.RecordEdgeType,
                    resource: new { id = restored.Id, shortCode = restored.ShortCode, name = restored.Name },
                    details: null,
                    ct);
                return Results.Ok(ToDto(restored));
            }
            catch (RecordEdgeTypeNotFoundException) { return Results.NotFound(); }
        }).DisableAntiforgery()
          .RequireKindPermission(EntityKinds.RecordType, Actions.DefineFields);

        typeGroup.MapGet("/{id:guid}/fields", async (
            Guid id,
            HttpContext http,
            IRecordEdgeTypeStore store,
            IAuthorizer authorizer,
            IDbContextFactory<AutoNateDbContext> dbContextFactory,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            // Gate field reads on the parent edge type's visibility — same
            // rule as GET /{id}.
            var parent = await store.GetAsync(id, ct);
            if (parent is null) return Results.NotFound();

            var visibleRecordTypeIds = await ResolveVisibleRecordTypeIdsAsync(
                http.User, authorizer, dbContextFactory, ct);
            if (!IsEdgeTypeVisible(parent, visibleRecordTypeIds))
            {
                return Results.Forbid();
            }

            var fields = await store.ListFieldsAsync(id, ct);
            await auditPublisher.PublishAsync(
                RecordSchemaEventTopic.TopicName,
                RecordSchemaEventTypes.RecordEdgeTypeFieldListViewed,
                RecordSchemaResourceKinds.RecordEdgeTypeField,
                resource: new { edgeTypeId = id },
                details: new { resultCount = fields.Count },
                ct);
            return Results.Ok(fields.Select(ToDto).ToList());
        }).AuthorizedInHandler("returns 403 when the parent edge type isn't visible per IsEdgeTypeVisible");

        typeGroup.MapPost("/{id:guid}/fields", async (
            Guid id,
            CreateEdgeFieldRequest request,
            IRecordEdgeTypeStore store,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            try
            {
                var created = await store.CreateFieldAsync(id, new CreateRecordEdgeTypeFieldInput(
                    request.FieldKey,
                    request.DisplayName,
                    request.DataType,
                    request.Config,
                    request.IsRequired,
                    request.SortOrder), ct);
                await auditPublisher.PublishAsync(
                    RecordSchemaEventTopic.TopicName,
                    RecordSchemaEventTypes.RecordEdgeTypeFieldCreated,
                    RecordSchemaResourceKinds.RecordEdgeTypeField,
                    resource: new { id = created.Id, edgeTypeId = id, fieldKey = created.FieldKey, dataType = created.DataType },
                    details: null,
                    ct);
                return Results.Created($"/api/record-edge-types/{id}/fields/{created.Id}", ToDto(created));
            }
            catch (RecordEdgeTypeNotFoundException) { return Results.NotFound(); }
            catch (RecordEdgeValidationException ex) { return Results.BadRequest(new { message = ex.Message }); }
        }).DisableAntiforgery()
          .RequireKindPermission(EntityKinds.RecordType, Actions.DefineFields);

        typeGroup.MapPatch("/{id:guid}/fields/{fieldId:guid}", async (
            Guid id,
            Guid fieldId,
            UpdateEdgeFieldRequest request,
            IRecordEdgeTypeStore store,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            try
            {
                var updated = await store.UpdateFieldAsync(id, fieldId, new UpdateRecordEdgeTypeFieldInput(
                    request.DisplayName,
                    request.Config,
                    request.IsRequired,
                    request.SortOrder), ct);
                await auditPublisher.PublishAsync(
                    RecordSchemaEventTopic.TopicName,
                    RecordSchemaEventTypes.RecordEdgeTypeFieldUpdated,
                    RecordSchemaResourceKinds.RecordEdgeTypeField,
                    resource: new { id = updated.Id, edgeTypeId = id, fieldKey = updated.FieldKey },
                    details: null,
                    ct);
                return Results.Ok(ToDto(updated));
            }
            catch (RecordEdgeValidationException ex) { return Results.BadRequest(new { message = ex.Message }); }
        }).DisableAntiforgery()
          .RequireKindPermission(EntityKinds.RecordType, Actions.DefineFields);

        typeGroup.MapDelete("/{id:guid}/fields/{fieldId:guid}", async (
            Guid id,
            Guid fieldId,
            IRecordEdgeTypeStore store,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            await store.DeleteFieldAsync(id, fieldId, ct);
            await auditPublisher.PublishAsync(
                RecordSchemaEventTopic.TopicName,
                RecordSchemaEventTypes.RecordEdgeTypeFieldDeleted,
                RecordSchemaResourceKinds.RecordEdgeTypeField,
                resource: new { id = fieldId, edgeTypeId = id },
                details: null,
                ct);
            return Results.NoContent();
        }).DisableAntiforgery()
          .RequireKindPermission(EntityKinds.RecordType, Actions.DefineFields);

        // ---- Edge instances ----
        var edgeGroup = app.MapGroup("/api/record-edges").RequireAuthorization();

        edgeGroup.MapPost("/", async (
            CreateEdgeRequest request,
            HttpContext http,
            IRecordEdgeStore store,
            IAuthorizer authorizer,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            // Linking two records mutates both sides of the graph, so the
            // actor must be allowed to edit BOTH endpoints — authorizing one
            // would let a user attach records they can't otherwise touch.
            if (!await CanEditAsync(authorizer, http.User, request.FromRecordId, ct) ||
                !await CanEditAsync(authorizer, http.User, request.ToRecordId, ct))
            {
                return Results.Forbid();
            }

            try
            {
                var created = await store.CreateAsync(new CreateRecordEdgeInput(
                    request.EdgeTypeId,
                    request.FromRecordId,
                    request.ToRecordId,
                    request.Data), http.GetActorId(), ct);
                await auditPublisher.PublishAsync(
                    RecordSchemaEventTopic.TopicName,
                    RecordSchemaEventTypes.RecordEdgeCreated,
                    RecordSchemaResourceKinds.RecordEdge,
                    resource: new
                    {
                        id = created.Id,
                        edgeTypeId = created.EdgeTypeId,
                        fromRecordId = created.FromRecordId,
                        toRecordId = created.ToRecordId
                    },
                    details: null,
                    ct);
                return Results.Created($"/api/record-edges/{created.Id}", ToDto(created));
            }
            catch (RecordEdgeTypeNotFoundException) { return Results.NotFound(); }
            catch (RecordEdgeValidationException ex) { return Results.BadRequest(new { message = ex.Message }); }
        }).DisableAntiforgery()
          .AuthorizedInHandler("authorizes Record:Edit on both From and To endpoints inline before the store call");

        edgeGroup.MapDelete("/{id:guid}", async (
            Guid id,
            HttpContext http,
            IRecordEdgeStore store,
            IAuthorizer authorizer,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            // Load before deciding so we can authorize against the edge's
            // actual endpoints. 404 on missing edges keeps the same shape as
            // other record sub-resources (e.g. comments).
            var edge = await store.GetAsync(id, ct);
            if (edge is null) return Results.NotFound();

            if (!await CanEditAsync(authorizer, http.User, edge.FromRecordId, ct) ||
                !await CanEditAsync(authorizer, http.User, edge.ToRecordId, ct))
            {
                return Results.Forbid();
            }

            await store.DeleteAsync(id, ct);
            await auditPublisher.PublishAsync(
                RecordSchemaEventTopic.TopicName,
                RecordSchemaEventTypes.RecordEdgeDeleted,
                RecordSchemaResourceKinds.RecordEdge,
                resource: new { id },
                details: null,
                ct);
            return Results.NoContent();
        }).DisableAntiforgery()
          .AuthorizedInHandler("loads the edge then authorizes Record:Edit on both endpoints before deleting");

        // Edges-for-record + traversal live under /api/records to keep the
        // navigation aligned with the record-centric SPA.
        var recordsGroup = app.MapGroup("/api/records").RequireAuthorization();

        recordsGroup.MapGet("/{id:guid}/edges", async (
            Guid id,
            string? direction,
            Guid? edgeTypeId,
            HttpContext http,
            IRecordEdgeStore store,
            IAuthorizer authorizer,
            IDbContextFactory<AutoNateDbContext> dbContextFactory,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            var dir = ParseDirection(direction);
            var edges = await store.ListForRecordAsync(id, dir, edgeTypeId, ct);

            // Suppress edges whose other endpoint the actor isn't allowed to
            // see, so listing edges for record A doesn't leak ids of records
            // they can't view directly.
            var visible = await FilterVisibleEdgesAsync(
                edges, id, http.User, authorizer, dbContextFactory, ct);

            await auditPublisher.PublishAsync(
                RecordSchemaEventTopic.TopicName,
                RecordSchemaEventTypes.RecordEdgeListViewed,
                RecordSchemaResourceKinds.RecordEdge,
                resource: new { recordId = id },
                details: new { direction = dir.ToString(), edgeTypeId, resultCount = visible.Count },
                ct);
            return Results.Ok(visible.Select(ToDto).ToArray());
        }).RequirePermission(EntityKinds.Record, Actions.View, "id");

        recordsGroup.MapPost("/{id:guid}/traverse", async (
            Guid id,
            TraverseHttpRequest request,
            HttpContext http,
            IRecordEdgeStore store,
            IAuthorizer authorizer,
            IDbContextFactory<AutoNateDbContext> dbContextFactory,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            var startIds = request.StartRecordIds is { Length: > 0 }
                ? request.StartRecordIds
                : new[] { id };

            // Caller-supplied StartRecordIds bypass the route-level gate, so
            // each one needs an explicit View check. Without this the actor
            // could pivot off /api/records/{visibleId}/traverse to walk graphs
            // anchored on records they can't see.
            foreach (var startId in startIds)
            {
                if (startId == id) continue;
                var decision = await authorizer.AuthorizeAsync(
                    http.User, Actions.View,
                    new EntityRef(EntityKinds.Record, startId.ToString()), ct);
                if (!decision.IsAllowed) return Results.Forbid();
            }

            var dir = ParseDirection(request.Direction);
            var rows = await store.TraverseAsync(new TraverseRequest(
                startIds,
                request.EdgeTypeIds,
                dir,
                request.MaxHops ?? 1), ct);

            var visible = await FilterVisibleTraverseRowsAsync(
                rows, http.User, authorizer, dbContextFactory, ct);

            await auditPublisher.PublishAsync(
                RecordSchemaEventTopic.TopicName,
                RecordSchemaEventTypes.RecordEdgeTraversed,
                RecordSchemaResourceKinds.RecordEdge,
                resource: new { recordId = id },
                details: new
                {
                    startCount = startIds.Length,
                    edgeTypeIds = request.EdgeTypeIds,
                    direction = dir.ToString(),
                    maxHops = request.MaxHops ?? 1,
                    resultCount = visible.Count
                },
                ct);
            return Results.Ok(visible.Select(r => new TraverseResultDto(r.RecordId, r.Hops)).ToArray());
        }).DisableAntiforgery()
          .RequirePermission(EntityKinds.Record, Actions.View, "id");

        return app;
    }

    private static EdgeDirection ParseDirection(string? raw) => raw?.ToLowerInvariant() switch
    {
        "outgoing" or "out" => EdgeDirection.Outgoing,
        "incoming" or "in" => EdgeDirection.Incoming,
        _ => EdgeDirection.Both
    };

    private static async Task<bool> CanEditAsync(
        IAuthorizer authorizer,
        System.Security.Claims.ClaimsPrincipal actor,
        Guid recordId,
        CancellationToken ct)
    {
        var decision = await authorizer.AuthorizeAsync(
            actor, Actions.Edit,
            new EntityRef(EntityKinds.Record, recordId.ToString()), ct);
        return decision.IsAllowed;
    }

    // Filters edges so callers only see relationships pointing at records
    // they can View. The route-level RequirePermission already gated access
    // to `selfRecordId`; here we strip edges whose other endpoint is hidden.
    private static async Task<IReadOnlyList<RecordEdge>> FilterVisibleEdgesAsync(
        IReadOnlyList<RecordEdge> edges,
        Guid selfRecordId,
        System.Security.Claims.ClaimsPrincipal actor,
        IAuthorizer authorizer,
        IDbContextFactory<AutoNateDbContext> dbContextFactory,
        CancellationToken ct)
    {
        if (edges.Count == 0) return edges;

        var otherIds = new HashSet<Guid>();
        foreach (var edge in edges)
        {
            var other = edge.FromRecordId == selfRecordId ? edge.ToRecordId : edge.FromRecordId;
            if (other != selfRecordId) otherIds.Add(other);
        }
        if (otherIds.Count == 0) return edges;

        var visibleIds = await ResolveVisibleRecordIdsAsync(
            otherIds, actor, authorizer, dbContextFactory, ct);

        return edges
            .Where(e =>
            {
                var other = e.FromRecordId == selfRecordId ? e.ToRecordId : e.FromRecordId;
                return other == selfRecordId || visibleIds.Contains(other);
            })
            .ToList();
    }

    private static async Task<IReadOnlyList<TraverseResultRow>> FilterVisibleTraverseRowsAsync(
        IReadOnlyList<TraverseResultRow> rows,
        System.Security.Claims.ClaimsPrincipal actor,
        IAuthorizer authorizer,
        IDbContextFactory<AutoNateDbContext> dbContextFactory,
        CancellationToken ct)
    {
        if (rows.Count == 0) return rows;
        var visibleIds = await ResolveVisibleRecordIdsAsync(
            rows.Select(r => r.RecordId).ToHashSet(), actor, authorizer, dbContextFactory, ct);
        return rows.Where(r => visibleIds.Contains(r.RecordId)).ToList();
    }

    private static async Task<HashSet<Guid>> ResolveVisibleRecordIdsAsync(
        IReadOnlyCollection<Guid> candidateIds,
        System.Security.Claims.ClaimsPrincipal actor,
        IAuthorizer authorizer,
        IDbContextFactory<AutoNateDbContext> dbContextFactory,
        CancellationToken ct)
    {
        if (candidateIds.Count == 0) return new HashSet<Guid>();

        await using var db = await dbContextFactory.CreateDbContextAsync(ct);
        var query = db.Records.AsNoTracking()
            .Where(r => candidateIds.Contains(r.Id));
        var filtered = await authorizer.FilterQueryAsync(
            db, actor, EntityKinds.Record, Actions.View, query, ct);
        var ids = await filtered.Select(r => r.Id).ToListAsync(ct);
        return ids.ToHashSet();
    }

    // Returns the set of record-type ids the actor can View. Used to
    // decide which edge types they can see.
    private static async Task<HashSet<Guid>> ResolveVisibleRecordTypeIdsAsync(
        System.Security.Claims.ClaimsPrincipal actor,
        IAuthorizer authorizer,
        IDbContextFactory<AutoNateDbContext> dbContextFactory,
        CancellationToken ct)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);
        var query = db.RecordTypes.AsNoTracking();
        var filtered = await authorizer.FilterQueryAsync(
            db, actor, EntityKinds.RecordType, Actions.View, query, ct);
        var ids = await filtered.Select(t => t.Id).ToListAsync(ct);
        return ids.ToHashSet();
    }

    // An edge type is visible iff the actor can View at least one of the
    // record types it references. Unrestricted edge types (no
    // FromRecordTypeIds and no ToRecordTypeIds) are visible iff the actor
    // can View any record type at all — keeps universal edges from leaking
    // to users with zero record-type access.
    private static bool IsEdgeTypeVisible(
        RecordEdgeType edgeType, HashSet<Guid> visibleRecordTypeIds)
    {
        var fromList = edgeType.FromRecordTypeIds;
        var toList = edgeType.ToRecordTypeIds;
        var hasFrom = fromList is { Count: > 0 };
        var hasTo = toList is { Count: > 0 };

        if (!hasFrom && !hasTo)
        {
            return visibleRecordTypeIds.Count > 0;
        }

        if (hasFrom && fromList!.Any(visibleRecordTypeIds.Contains)) return true;
        if (hasTo && toList!.Any(visibleRecordTypeIds.Contains)) return true;
        return false;
    }
    private static EdgeTypeDto ToDto(RecordEdgeType model) => new(
        model.Id,
        model.ShortCode,
        model.Name,
        model.InverseName,
        model.IsDirected,
        model.AllowSelfReference,
        model.Cardinality,
        model.FromRecordTypeIds?.ToArray(),
        model.ToRecordTypeIds?.ToArray(),
        model.IsArchived,
        model.CreatedAtUtc,
        model.UpdatedAtUtc);

    private static EdgeTypeFieldDto ToDto(RecordEdgeTypeField model) => new(
        model.Id,
        model.EdgeTypeId,
        model.FieldKey,
        model.DisplayName,
        model.DataType,
        model.Config,
        model.IsRequired,
        model.SortOrder);

    private static EdgeDto ToDto(RecordEdge model) => new(
        model.Id,
        model.EdgeTypeId,
        model.FromRecordId,
        model.ToRecordId,
        model.Data,
        model.CreatedAtUtc,
        model.CreatedBy);
}
