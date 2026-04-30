using System.Security.Claims;
using System.Text.Json;
using AutoNate.Web.Models.Records;
using AutoNate.Web.Services.Events;
using AutoNate.Web.Services.Records;
using AutoNate.Web.Services.Records.Fields;

namespace AutoNate.Web.Endpoints;

public sealed record CreateRecordTypeRequest(
    string ShortCode,
    string Name,
    string? Description,
    string? Icon,
    string? Color);

public sealed record UpdateRecordTypeRequest(
    string Name,
    string? Description,
    string? Icon,
    string? Color);

public sealed record CreateFieldRequest(
    string FieldKey,
    string DisplayName,
    string DataType,
    JsonElement Config,
    bool IsRequired,
    int SortOrder);

public sealed record UpdateFieldRequest(
    string DisplayName,
    JsonElement Config,
    bool IsRequired,
    int SortOrder);

public sealed record RecordTypeDto(
    Guid Id,
    string ShortCode,
    string Name,
    string? Description,
    string? Icon,
    string? Color,
    bool IsSystem,
    bool IsArchived,
    long NextKeyNumber,
    DateTimeOffset CreatedAtUtc,
    Guid CreatedBy,
    DateTimeOffset UpdatedAtUtc,
    Guid UpdatedBy);

public sealed record RecordTypeFieldDto(
    Guid Id,
    Guid RecordTypeId,
    string FieldKey,
    string DisplayName,
    string DataType,
    JsonElement Config,
    bool IsRequired,
    bool IsArchived,
    int SortOrder,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record RecordTypeAuditDto(
    long Id,
    Guid RecordTypeId,
    string ChangeKind,
    JsonElement? Before,
    JsonElement? After,
    Guid ChangedBy,
    DateTimeOffset ChangedAtUtc);

public sealed record FieldTypeMetadataDto(string DataType);

public static class RecordTypeEndpoints
{
    public static IEndpointRouteBuilder MapRecordTypeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/record-types").RequireAuthorization();

        group.MapGet("/field-types", (IFieldTypeRegistry registry) =>
        {
            var items = registry.All
                .Select(ft => new FieldTypeMetadataDto(ft.DataType))
                .OrderBy(m => m.DataType, StringComparer.Ordinal)
                .ToList();
            return Results.Ok(items);
        });

        group.MapGet("/", async (
            bool? includeArchived, IRecordTypeStore store,
            IAuditEventPublisher auditPublisher, CancellationToken cancellationToken) =>
        {
            var types = await store.ListAsync(includeArchived ?? false, cancellationToken);
            await auditPublisher.PublishAsync(
                RecordSchemaEventTopic.TopicName,
                RecordSchemaEventTypes.RecordTypeListViewed,
                RecordSchemaResourceKinds.RecordType,
                resource: null,
                details: new { resultCount = types.Count, includeArchived = includeArchived ?? false },
                cancellationToken);
            return Results.Ok(types.Select(ToDto).ToList());
        });

        group.MapGet("/{id:guid}", async (
            Guid id, IRecordTypeStore store,
            IAuditEventPublisher auditPublisher, CancellationToken cancellationToken) =>
        {
            var model = await store.GetAsync(id, cancellationToken);
            if (model is null) return Results.NotFound();
            await auditPublisher.PublishAsync(
                RecordSchemaEventTopic.TopicName,
                RecordSchemaEventTypes.RecordTypeViewed,
                RecordSchemaResourceKinds.RecordType,
                resource: new { id = model.Id, shortCode = model.ShortCode, name = model.Name },
                details: null,
                cancellationToken);
            return Results.Ok(ToDto(model));
        });

        group.MapPost("/", async (
            CreateRecordTypeRequest request,
            HttpContext http,
            IRecordTypeStore store,
            IAuditEventPublisher auditPublisher,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var created = await store.CreateAsync(
                    new CreateRecordTypeInput(request.ShortCode, request.Name, request.Description, request.Icon, request.Color),
                    GetActorId(http),
                    cancellationToken);
                await auditPublisher.PublishAsync(
                    RecordSchemaEventTopic.TopicName,
                    RecordSchemaEventTypes.RecordTypeCreated,
                    RecordSchemaResourceKinds.RecordType,
                    resource: new { id = created.Id, shortCode = created.ShortCode, name = created.Name },
                    details: null,
                    cancellationToken);
                return Results.Created($"/api/record-types/{created.Id}", ToDto(created));
            }
            catch (RecordTypeValidationException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        }).DisableAntiforgery();

        group.MapPatch("/{id:guid}", async (
            Guid id,
            UpdateRecordTypeRequest request,
            HttpContext http,
            IRecordTypeStore store,
            IAuditEventPublisher auditPublisher,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var updated = await store.UpdateAsync(
                    id,
                    new UpdateRecordTypeInput(request.Name, request.Description, request.Icon, request.Color),
                    GetActorId(http),
                    cancellationToken);
                await auditPublisher.PublishAsync(
                    RecordSchemaEventTopic.TopicName,
                    RecordSchemaEventTypes.RecordTypeUpdated,
                    RecordSchemaResourceKinds.RecordType,
                    resource: new { id = updated.Id, shortCode = updated.ShortCode, name = updated.Name },
                    details: null,
                    cancellationToken);
                return Results.Ok(ToDto(updated));
            }
            catch (RecordTypeNotFoundException)
            {
                return Results.NotFound();
            }
            catch (RecordTypeValidationException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        }).DisableAntiforgery();

        group.MapDelete("/{id:guid}", async (
            Guid id,
            HttpContext http,
            IRecordTypeStore store,
            IAuditEventPublisher auditPublisher,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var archived = await store.SetArchivedAsync(id, archived: true, GetActorId(http), cancellationToken);
                await auditPublisher.PublishAsync(
                    RecordSchemaEventTopic.TopicName,
                    RecordSchemaEventTypes.RecordTypeArchived,
                    RecordSchemaResourceKinds.RecordType,
                    resource: new { id = archived.Id, shortCode = archived.ShortCode, name = archived.Name },
                    details: null,
                    cancellationToken);
                return Results.Ok(ToDto(archived));
            }
            catch (RecordTypeNotFoundException)
            {
                return Results.NotFound();
            }
        }).DisableAntiforgery();

        group.MapPost("/{id:guid}/restore", async (
            Guid id,
            HttpContext http,
            IRecordTypeStore store,
            IAuditEventPublisher auditPublisher,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var restored = await store.SetArchivedAsync(id, archived: false, GetActorId(http), cancellationToken);
                await auditPublisher.PublishAsync(
                    RecordSchemaEventTopic.TopicName,
                    RecordSchemaEventTypes.RecordTypeRestored,
                    RecordSchemaResourceKinds.RecordType,
                    resource: new { id = restored.Id, shortCode = restored.ShortCode, name = restored.Name },
                    details: null,
                    cancellationToken);
                return Results.Ok(ToDto(restored));
            }
            catch (RecordTypeNotFoundException)
            {
                return Results.NotFound();
            }
        }).DisableAntiforgery();

        group.MapGet("/{id:guid}/fields", async (
            Guid id,
            bool? includeArchived,
            IRecordTypeStore store,
            IAuditEventPublisher auditPublisher,
            CancellationToken cancellationToken) =>
        {
            var fields = await store.ListFieldsAsync(id, includeArchived ?? false, cancellationToken);
            await auditPublisher.PublishAsync(
                RecordSchemaEventTopic.TopicName,
                RecordSchemaEventTypes.RecordTypeFieldListViewed,
                RecordSchemaResourceKinds.RecordTypeField,
                resource: new { recordTypeId = id },
                details: new { resultCount = fields.Count, includeArchived = includeArchived ?? false },
                cancellationToken);
            return Results.Ok(fields.Select(ToDto).ToList());
        });

        group.MapGet("/{id:guid}/fields/{fieldId:guid}", async (
            Guid id,
            Guid fieldId,
            IRecordTypeStore store,
            IAuditEventPublisher auditPublisher,
            CancellationToken cancellationToken) =>
        {
            var field = await store.GetFieldAsync(id, fieldId, cancellationToken);
            if (field is null) return Results.NotFound();
            await auditPublisher.PublishAsync(
                RecordSchemaEventTopic.TopicName,
                RecordSchemaEventTypes.RecordTypeFieldViewed,
                RecordSchemaResourceKinds.RecordTypeField,
                resource: new { id = field.Id, recordTypeId = id, fieldKey = field.FieldKey },
                details: null,
                cancellationToken);
            return Results.Ok(ToDto(field));
        });

        group.MapPost("/{id:guid}/fields", async (
            Guid id,
            CreateFieldRequest request,
            HttpContext http,
            IRecordTypeStore store,
            IAuditEventPublisher auditPublisher,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var created = await store.CreateFieldAsync(
                    id,
                    new CreateRecordTypeFieldInput(
                        request.FieldKey,
                        request.DisplayName,
                        request.DataType,
                        request.Config,
                        request.IsRequired,
                        request.SortOrder),
                    GetActorId(http),
                    cancellationToken);
                await auditPublisher.PublishAsync(
                    RecordSchemaEventTopic.TopicName,
                    RecordSchemaEventTypes.RecordTypeFieldCreated,
                    RecordSchemaResourceKinds.RecordTypeField,
                    resource: new { id = created.Id, recordTypeId = id, fieldKey = created.FieldKey, dataType = created.DataType },
                    details: null,
                    cancellationToken);
                return Results.Created($"/api/record-types/{id}/fields/{created.Id}", ToDto(created));
            }
            catch (RecordTypeNotFoundException)
            {
                return Results.NotFound();
            }
            catch (RecordTypeValidationException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        }).DisableAntiforgery();

        group.MapPatch("/{id:guid}/fields/{fieldId:guid}", async (
            Guid id,
            Guid fieldId,
            UpdateFieldRequest request,
            HttpContext http,
            IRecordTypeStore store,
            IAuditEventPublisher auditPublisher,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var updated = await store.UpdateFieldAsync(
                    id,
                    fieldId,
                    new UpdateRecordTypeFieldInput(
                        request.DisplayName,
                        request.Config,
                        request.IsRequired,
                        request.SortOrder),
                    GetActorId(http),
                    cancellationToken);
                await auditPublisher.PublishAsync(
                    RecordSchemaEventTopic.TopicName,
                    RecordSchemaEventTypes.RecordTypeFieldUpdated,
                    RecordSchemaResourceKinds.RecordTypeField,
                    resource: new { id = updated.Id, recordTypeId = id, fieldKey = updated.FieldKey },
                    details: null,
                    cancellationToken);
                return Results.Ok(ToDto(updated));
            }
            catch (RecordTypeNotFoundException)
            {
                return Results.NotFound();
            }
            catch (RecordTypeFieldNotFoundException)
            {
                return Results.NotFound();
            }
            catch (RecordTypeValidationException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        }).DisableAntiforgery();

        group.MapDelete("/{id:guid}/fields/{fieldId:guid}", async (
            Guid id,
            Guid fieldId,
            HttpContext http,
            IRecordTypeStore store,
            IAuditEventPublisher auditPublisher,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var archived = await store.SetFieldArchivedAsync(id, fieldId, archived: true, GetActorId(http), cancellationToken);
                await auditPublisher.PublishAsync(
                    RecordSchemaEventTopic.TopicName,
                    RecordSchemaEventTypes.RecordTypeFieldArchived,
                    RecordSchemaResourceKinds.RecordTypeField,
                    resource: new { id = archived.Id, recordTypeId = id, fieldKey = archived.FieldKey },
                    details: null,
                    cancellationToken);
                return Results.Ok(ToDto(archived));
            }
            catch (RecordTypeNotFoundException)
            {
                return Results.NotFound();
            }
            catch (RecordTypeFieldNotFoundException)
            {
                return Results.NotFound();
            }
        }).DisableAntiforgery();

        group.MapPost("/{id:guid}/fields/{fieldId:guid}/restore", async (
            Guid id,
            Guid fieldId,
            HttpContext http,
            IRecordTypeStore store,
            IAuditEventPublisher auditPublisher,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var restored = await store.SetFieldArchivedAsync(id, fieldId, archived: false, GetActorId(http), cancellationToken);
                await auditPublisher.PublishAsync(
                    RecordSchemaEventTopic.TopicName,
                    RecordSchemaEventTypes.RecordTypeFieldRestored,
                    RecordSchemaResourceKinds.RecordTypeField,
                    resource: new { id = restored.Id, recordTypeId = id, fieldKey = restored.FieldKey },
                    details: null,
                    cancellationToken);
                return Results.Ok(ToDto(restored));
            }
            catch (RecordTypeNotFoundException)
            {
                return Results.NotFound();
            }
            catch (RecordTypeFieldNotFoundException)
            {
                return Results.NotFound();
            }
        }).DisableAntiforgery();

        group.MapGet("/{id:guid}/audit", async (
            Guid id,
            int? take,
            IRecordTypeStore store,
            IAuditEventPublisher auditPublisher,
            CancellationToken cancellationToken) =>
        {
            var audit = await store.ListAuditAsync(id, take ?? 100, cancellationToken);
            await auditPublisher.PublishAsync(
                RecordSchemaEventTopic.TopicName,
                RecordSchemaEventTypes.RecordTypeAuditViewed,
                RecordSchemaResourceKinds.RecordType,
                resource: new { recordTypeId = id },
                details: new { take = take ?? 100, resultCount = audit.Count },
                cancellationToken);
            return Results.Ok(audit.Select(ToDto).ToList());
        });

        return app;
    }

    private static Guid GetActorId(HttpContext http)
    {
        var claim = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(claim, out var id) ? id : Guid.Empty;
    }

    private static RecordTypeDto ToDto(RecordType model) => new(
        model.Id,
        model.ShortCode,
        model.Name,
        model.Description,
        model.Icon,
        model.Color,
        model.IsSystem,
        model.IsArchived,
        model.NextKeyNumber,
        model.CreatedAtUtc,
        model.CreatedBy,
        model.UpdatedAtUtc,
        model.UpdatedBy);

    private static RecordTypeFieldDto ToDto(RecordTypeField model) => new(
        model.Id,
        model.RecordTypeId,
        model.FieldKey,
        model.DisplayName,
        model.DataType,
        model.Config,
        model.IsRequired,
        model.IsArchived,
        model.SortOrder,
        model.CreatedAtUtc,
        model.UpdatedAtUtc);

    private static RecordTypeAuditDto ToDto(RecordTypeAuditEntry model) => new(
        model.Id,
        model.RecordTypeId,
        model.ChangeKind,
        model.Before,
        model.After,
        model.ChangedBy,
        model.ChangedAtUtc);
}
