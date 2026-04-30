using System.Security.Claims;
using System.Text.Json;
using AutoNate.Web.Models.Records;
using AutoNate.Web.Services.Events;
using AutoNate.Web.Services.Records;

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

        typeGroup.MapGet("/", async (
            bool? includeArchived,
            IRecordEdgeTypeStore store,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            var types = await store.ListAsync(includeArchived ?? false, ct);
            await auditPublisher.PublishAsync(
                RecordSchemaEventTopic.TopicName,
                RecordSchemaEventTypes.RecordEdgeTypeListViewed,
                RecordSchemaResourceKinds.RecordEdgeType,
                resource: null,
                details: new { resultCount = types.Count, includeArchived = includeArchived ?? false },
                ct);
            return Results.Ok(types.Select(ToDto).ToList());
        });

        typeGroup.MapGet("/{id:guid}", async (
            Guid id, IRecordEdgeTypeStore store,
            IAuditEventPublisher auditPublisher, CancellationToken ct) =>
        {
            var type = await store.GetAsync(id, ct);
            if (type is null) return Results.NotFound();
            await auditPublisher.PublishAsync(
                RecordSchemaEventTopic.TopicName,
                RecordSchemaEventTypes.RecordEdgeTypeViewed,
                RecordSchemaResourceKinds.RecordEdgeType,
                resource: new { id = type.Id, shortCode = type.ShortCode, name = type.Name },
                details: null,
                ct);
            return Results.Ok(ToDto(type));
        });

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
        }).DisableAntiforgery();

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
        }).DisableAntiforgery();

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
        }).DisableAntiforgery();

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
        }).DisableAntiforgery();

        typeGroup.MapGet("/{id:guid}/fields", async (
            Guid id, IRecordEdgeTypeStore store,
            IAuditEventPublisher auditPublisher, CancellationToken ct) =>
        {
            var fields = await store.ListFieldsAsync(id, ct);
            await auditPublisher.PublishAsync(
                RecordSchemaEventTopic.TopicName,
                RecordSchemaEventTypes.RecordEdgeTypeFieldListViewed,
                RecordSchemaResourceKinds.RecordEdgeTypeField,
                resource: new { edgeTypeId = id },
                details: new { resultCount = fields.Count },
                ct);
            return Results.Ok(fields.Select(ToDto).ToList());
        });

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
        }).DisableAntiforgery();

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
        }).DisableAntiforgery();

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
        }).DisableAntiforgery();

        // ---- Edge instances ----
        var edgeGroup = app.MapGroup("/api/record-edges").RequireAuthorization();

        edgeGroup.MapPost("/", async (
            CreateEdgeRequest request,
            HttpContext http,
            IRecordEdgeStore store,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            try
            {
                var created = await store.CreateAsync(new CreateRecordEdgeInput(
                    request.EdgeTypeId,
                    request.FromRecordId,
                    request.ToRecordId,
                    request.Data), GetActorId(http), ct);
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
        }).DisableAntiforgery();

        edgeGroup.MapDelete("/{id:guid}", async (
            Guid id, IRecordEdgeStore store,
            IAuditEventPublisher auditPublisher, CancellationToken ct) =>
        {
            await store.DeleteAsync(id, ct);
            await auditPublisher.PublishAsync(
                RecordSchemaEventTopic.TopicName,
                RecordSchemaEventTypes.RecordEdgeDeleted,
                RecordSchemaResourceKinds.RecordEdge,
                resource: new { id },
                details: null,
                ct);
            return Results.NoContent();
        }).DisableAntiforgery();

        // Edges-for-record + traversal live under /api/records to keep the
        // navigation aligned with the record-centric SPA.
        var recordsGroup = app.MapGroup("/api/records").RequireAuthorization();

        recordsGroup.MapGet("/{id:guid}/edges", async (
            Guid id,
            string? direction,
            Guid? edgeTypeId,
            IRecordEdgeStore store,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            var dir = ParseDirection(direction);
            var edges = await store.ListForRecordAsync(id, dir, edgeTypeId, ct);
            await auditPublisher.PublishAsync(
                RecordSchemaEventTopic.TopicName,
                RecordSchemaEventTypes.RecordEdgeListViewed,
                RecordSchemaResourceKinds.RecordEdge,
                resource: new { recordId = id },
                details: new { direction = dir.ToString(), edgeTypeId, resultCount = edges.Count },
                ct);
            return Results.Ok(edges.Select(ToDto).ToArray());
        });

        recordsGroup.MapPost("/{id:guid}/traverse", async (
            Guid id,
            TraverseHttpRequest request,
            IRecordEdgeStore store,
            IAuditEventPublisher auditPublisher,
            CancellationToken ct) =>
        {
            var startIds = request.StartRecordIds is { Length: > 0 }
                ? request.StartRecordIds
                : new[] { id };
            var dir = ParseDirection(request.Direction);
            var rows = await store.TraverseAsync(new TraverseRequest(
                startIds,
                request.EdgeTypeIds,
                dir,
                request.MaxHops ?? 1), ct);
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
                    resultCount = rows.Count
                },
                ct);
            return Results.Ok(rows.Select(r => new TraverseResultDto(r.RecordId, r.Hops)).ToArray());
        }).DisableAntiforgery();

        return app;
    }

    private static EdgeDirection ParseDirection(string? raw) => raw?.ToLowerInvariant() switch
    {
        "outgoing" or "out" => EdgeDirection.Outgoing,
        "incoming" or "in" => EdgeDirection.Incoming,
        _ => EdgeDirection.Both
    };

    private static Guid GetActorId(HttpContext http)
    {
        var claim = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(claim, out var id) ? id : Guid.Empty;
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
