using System.Text.Json;
using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.EndpointFilters;
using AutoNate.Web.Authorization.Evaluator;
using AutoNate.Web.Models.Records;
using AutoNate.Web.Persistence;
using AutoNate.Web.Services.Events;
using AutoNate.Web.Services.Records;
using AutoNate.Web.Services.Records.Fields;
using Microsoft.EntityFrameworkCore;
using RecordTypeEntity = AutoNate.Web.Persistence.Scaffolded.RecordType;

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
        }).OpenToAuthenticated("system data-type catalog (string/number/date/etc.); not record-type or tenant data");

        // List filters via FilterQueryAsync(RecordType, View) — users with no
        // grants get an empty list, users with scoped grants (e.g.
        // `[shortcode=lead]`) get only the types they can see. Intentionally
        // does NOT 403 on no-grant: SPA flows hit this endpoint broadly.
        group.MapGet("/", async (
            bool? includeArchived,
            HttpContext http,
            IAuthorizer authorizer,
            IDbContextFactory<AutoNateDbContext> dbContextFactory,
            IAuditEventPublisher auditPublisher,
            CancellationToken cancellationToken) =>
        {
            await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            IQueryable<RecordTypeEntity> query = db.RecordTypes.AsNoTracking();
            if (!(includeArchived ?? false))
            {
                query = query.Where(t => !t.IsArchived);
            }

            var visible = await authorizer.FilterQueryAsync(
                db, http.User, EntityKinds.RecordType, Actions.View, query, cancellationToken);

            var entities = await visible
                .OrderByDescending(t => t.UpdatedAtUtc)
                .ThenBy(t => t.Name)
                .ToListAsync(cancellationToken);

            var types = entities.Select(t => t.ToModel()).ToList();

            await auditPublisher.PublishAsync(
                RecordSchemaEventTopic.TopicName,
                RecordSchemaEventTypes.RecordTypeListViewed,
                RecordSchemaResourceKinds.RecordType,
                resource: null,
                details: new { resultCount = types.Count, includeArchived = includeArchived ?? false },
                cancellationToken);
            return Results.Ok(types.Select(ToDto).ToList());
        }).AuthorizedInHandler("filters via FilterQueryAsync(RecordType, View); empty grants -> empty list");

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
        }).RequirePermission(EntityKinds.RecordType, Actions.View);

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
                    http.GetActorId(),
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
        }).DisableAntiforgery()
          .RequireKindPermission(EntityKinds.RecordType, Actions.Create);

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
                    http.GetActorId(),
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
        }).DisableAntiforgery()
          .RequirePermission(EntityKinds.RecordType, Actions.Edit);

        group.MapDelete("/{id:guid}", async (
            Guid id,
            HttpContext http,
            IRecordTypeStore store,
            IAuditEventPublisher auditPublisher,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var archived = await store.SetArchivedAsync(id, archived: true, http.GetActorId(), cancellationToken);
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
        }).DisableAntiforgery()
          .RequirePermission(EntityKinds.RecordType, Actions.Delete);

        group.MapPost("/{id:guid}/restore", async (
            Guid id,
            HttpContext http,
            IRecordTypeStore store,
            IAuditEventPublisher auditPublisher,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var restored = await store.SetArchivedAsync(id, archived: false, http.GetActorId(), cancellationToken);
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
        }).DisableAntiforgery()
          .RequirePermission(EntityKinds.RecordType, Actions.Edit);

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
        }).RequirePermission(EntityKinds.RecordType, Actions.View);

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
        }).RequirePermission(EntityKinds.RecordType, Actions.View);

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
                    http.GetActorId(),
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
        }).DisableAntiforgery()
          .RequirePermission(EntityKinds.RecordType, Actions.DefineFields);

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
                    http.GetActorId(),
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
        }).DisableAntiforgery()
          .RequirePermission(EntityKinds.RecordType, Actions.DefineFields);

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
                var archived = await store.SetFieldArchivedAsync(id, fieldId, archived: true, http.GetActorId(), cancellationToken);
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
        }).DisableAntiforgery()
          .RequirePermission(EntityKinds.RecordType, Actions.DefineFields);

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
                var restored = await store.SetFieldArchivedAsync(id, fieldId, archived: false, http.GetActorId(), cancellationToken);
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
        }).DisableAntiforgery()
          .RequirePermission(EntityKinds.RecordType, Actions.DefineFields);

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
        }).RequirePermission(EntityKinds.RecordType, Actions.View);

        return app;
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
