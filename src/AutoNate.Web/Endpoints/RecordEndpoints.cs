using System.Text.Json;
using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.EndpointFilters;
using AutoNate.Web.Authorization.Evaluator;
using AutoNate.Web.Models.Records;
using AutoNate.Web.Services.Events;
using AutoNate.Web.Services.Records;
using AutoNate.Web.Services.Records.Fields;

namespace AutoNate.Web.Endpoints;

public sealed record CreateRecordRequest(
    Guid RecordTypeId,
    string Name,
    string? Status,
    DateOnly? DueDate,
    JsonElement Values,
    Guid[]? AssigneeIds);

// Note: PATCH /api/records/{id} binds the raw JSON body as a JsonElement so
// `null` (clear) and absence (don't touch) can be told apart for nullable
// fields like status and dueDate. There's no typed UpdateRecordRequest record.

public sealed record SearchRecordsRequest(
    Guid RecordTypeId,
    SearchFilterClause[]? Filters,
    Guid? AssigneeId,
    bool IncludeArchived,
    int Page,
    int PageSize,
    string? Sort,
    string? Search = null);

public sealed record SearchFilterClause(string FieldKey, string Op, JsonElement Value);

public sealed record RecordDto(
    Guid Id,
    Guid RecordTypeId,
    string Key,
    long KeyNumber,
    string Name,
    string? Status,
    DateOnly? DueDate,
    Guid[] AssigneeIds,
    JsonElement Values,
    bool IsArchived,
    DateTimeOffset CreatedAtUtc,
    Guid CreatedBy,
    DateTimeOffset UpdatedAtUtc,
    Guid UpdatedBy);

public sealed record RecordPageDto(
    RecordDto[] Items,
    int TotalCount,
    int Page,
    int PageSize);

public sealed record RecordHistoryEntryDto(
    long Id,
    Guid RecordId,
    Guid? ChangeSetId,
    string ChangeKind,
    string? FieldKey,
    JsonElement? OldValue,
    JsonElement? NewValue,
    Guid ChangedBy,
    DateTimeOffset ChangedAtUtc);

public static class RecordEndpoints
{
    public static IEndpointRouteBuilder MapRecordEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/records").RequireAuthorization();

        group.MapGet("/", async (
            Guid recordTypeId,
            int? page,
            int? pageSize,
            bool? includeArchived,
            Guid? assigneeId,
            string? sort,
            HttpContext http,
            IRecordStore store,
            IAuditEventPublisher auditPublisher,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var resolvedPage = page ?? 0;
                var resolvedPageSize = pageSize ?? 25;
                var result = await store.SearchAsync(new RecordSearchInput(
                    recordTypeId,
                    Filters: null,
                    AssigneeId: assigneeId,
                    IncludeArchived: includeArchived ?? false,
                    Page: resolvedPage,
                    PageSize: resolvedPageSize,
                    Sort: sort),
                    http.User,
                    cancellationToken);
                await auditPublisher.PublishAsync(
                    DaprRecordEventPublisher.TopicName,
                    RecordEventTypes.ListViewed,
                    RecordResourceKinds.Record,
                    resource: null,
                    details: new
                    {
                        recordTypeId,
                        page = resolvedPage,
                        pageSize = resolvedPageSize,
                        resultCount = result.Records.Count,
                        totalCount = result.TotalCount,
                        assigneeId,
                        includeArchived = includeArchived ?? false,
                        sort
                    },
                    cancellationToken);
                return Results.Ok(ToPageDto(result));
            }
            catch (RecordValidationException ex)
            {
                return Results.BadRequest(new { message = ex.Message, errors = ex.Errors });
            }
        }).AuthorizedInHandler("filters via store.SearchAsync(actor) which applies Record:View grants");

        group.MapGet("/assigned-to-me", async (
            int? page,
            int? pageSize,
            bool? includeArchived,
            string? sort,
            HttpContext http,
            IRecordStore store,
            IAuditEventPublisher auditPublisher,
            CancellationToken cancellationToken) =>
        {
            var actorId = http.GetActorId();
            if (actorId == Guid.Empty)
            {
                return Results.Unauthorized();
            }

            var resolvedPage = page ?? 0;
            var resolvedPageSize = pageSize ?? 25;
            var result = await store.SearchAssignedAsync(
                actorId,
                resolvedPage,
                resolvedPageSize,
                includeArchived ?? false,
                sort,
                http.User,
                cancellationToken);
            await auditPublisher.PublishAsync(
                DaprRecordEventPublisher.TopicName,
                RecordEventTypes.ListViewed,
                RecordResourceKinds.Record,
                resource: null,
                details: new
                {
                    scope = "assigned-to-me",
                    page = resolvedPage,
                    pageSize = resolvedPageSize,
                    resultCount = result.Records.Count,
                    totalCount = result.TotalCount,
                    includeArchived = includeArchived ?? false,
                    sort
                },
                cancellationToken);
            return Results.Ok(ToPageDto(result));
        }).AuthorizedInHandler("returns records assigned to the current actor only; store filter is actor-scoped");

        group.MapGet("/{id:guid}", async (
            Guid id, IRecordStore store,
            IAuditEventPublisher auditPublisher, CancellationToken cancellationToken) =>
        {
            var record = await store.GetAsync(id, cancellationToken);
            if (record is null) return Results.NotFound();
            await auditPublisher.PublishAsync(
                DaprRecordEventPublisher.TopicName,
                RecordEventTypes.Viewed,
                RecordResourceKinds.Record,
                resource: new { recordId = record.Id, key = record.Key, recordTypeId = record.RecordTypeId },
                details: null,
                cancellationToken);
            return Results.Ok(ToDto(record));
        }).RequirePermission(EntityKinds.Record, Actions.View);

        group.MapGet("/by-key/{key}", async (
            string key, HttpContext http, IRecordStore store, IAuthorizer authorizer,
            IAuditEventPublisher auditPublisher, CancellationToken cancellationToken) =>
        {
            var record = await store.GetByKeyAsync(key, cancellationToken);
            if (record is null) return Results.NotFound();
            // Same gate the /{id:guid} variant gets via .RequirePermission. We
            // can't use the filter here because the route id is a key, not the
            // record's guid. 404 on deny so existence isn't probed by key.
            var decision = await authorizer.AuthorizeAsync(
                http.User,
                Actions.View,
                new EntityRef(EntityKinds.Record, record.Id.ToString()),
                cancellationToken);
            if (!decision.IsAllowed) return Results.NotFound();
            await auditPublisher.PublishAsync(
                DaprRecordEventPublisher.TopicName,
                RecordEventTypes.Viewed,
                RecordResourceKinds.Record,
                resource: new { recordId = record.Id, key = record.Key, recordTypeId = record.RecordTypeId },
                details: new { lookupBy = "key" },
                cancellationToken);
            return Results.Ok(ToDto(record));
        }).AuthorizedInHandler("inline AuthorizeAsync(Record, View) on the looked-up record's id; 404 on deny so existence isn't probed by key");

        group.MapPost("/", async (
            CreateRecordRequest request,
            HttpContext http,
            IRecordStore store,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var created = await store.CreateAsync(
                    new CreateRecordInput(
                        request.RecordTypeId,
                        request.Name,
                        request.Status,
                        request.DueDate,
                        request.Values,
                        request.AssigneeIds),
                    http.GetActorId(),
                    cancellationToken);
                return Results.Created($"/api/records/{created.Id}", ToDto(created));
            }
            catch (RecordValidationException ex)
            {
                return Results.BadRequest(new { message = ex.Message, errors = ex.Errors });
            }
        }).DisableAntiforgery()
          .RequireKindPermission(EntityKinds.Record, Actions.Create);

        group.MapPatch("/{id:guid}", async (
            Guid id,
            JsonElement body,
            HttpContext http,
            IRecordStore store,
            IAuthorizer authorizer,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var input = ParseUpdateInput(body);

                // This route accepts assigneeIds, and it is the one the SPA
                // actually uses to change them — PUT /{id}/assignees, the route
                // that carries RequirePermission(Record, Assign), has no caller
                // outside its own test. So Record:Assign was grantable and
                // deniable with no observable effect: assignees changed through
                // Edit like any other field (#45). Charge Assign whenever the
                // body actually carries assignees, so the permission means the
                // same thing however the change arrives.
                if (TryGetCaseInsensitive(body, "assigneeIds", out var assigneeProbe)
                    && assigneeProbe.ValueKind == JsonValueKind.Array)
                {
                    var decision = await authorizer.AuthorizeAsync(
                        http.User, Actions.Assign,
                        new EntityRef(EntityKinds.Record, id.ToString()), cancellationToken);
                    if (!decision.IsAllowed)
                    {
                        return Results.StatusCode(StatusCodes.Status403Forbidden);
                    }
                }

                var updated = await store.UpdateAsync(id, input, http.GetActorId(), cancellationToken);
                return Results.Ok(ToDto(updated));
            }
            catch (RecordNotFoundException)
            {
                return Results.NotFound();
            }
            catch (RecordValidationException ex)
            {
                return Results.BadRequest(new { message = ex.Message, errors = ex.Errors });
            }
        }).DisableAntiforgery()
          .RequirePermission(EntityKinds.Record, Actions.Edit)
          .AuthorizedInHandler(
              "Record:Edit via the filter above; a body that carries assigneeIds " +
              "additionally requires Record:Assign on the same record (#45).");

        group.MapDelete("/{id:guid}", async (
            Guid id,
            HttpContext http,
            IRecordStore store,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var archived = await store.SetArchivedAsync(id, archived: true, http.GetActorId(), cancellationToken);
                return Results.Ok(ToDto(archived));
            }
            catch (RecordNotFoundException)
            {
                return Results.NotFound();
            }
        }).DisableAntiforgery()
          .RequirePermission(EntityKinds.Record, Actions.Archive);

        group.MapPost("/{id:guid}/restore", async (
            Guid id,
            HttpContext http,
            IRecordStore store,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var restored = await store.SetArchivedAsync(id, archived: false, http.GetActorId(), cancellationToken);
                return Results.Ok(ToDto(restored));
            }
            catch (RecordNotFoundException)
            {
                return Results.NotFound();
            }
        }).DisableAntiforgery()
          .RequirePermission(EntityKinds.Record, Actions.Edit);

        // Hard-delete: distinct from DELETE /{id} which is the archive path.
        // Cascades clean up edges, comments, history, watches. Gated by the
        // separate `Delete` action so admins can hand out routine archive
        // without unlocking permanent removal.
        group.MapDelete("/{id:guid}/permanent", async (
            Guid id,
            HttpContext http,
            IRecordStore store,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var deleted = await store.DeleteAsync(id, http.GetActorId(), cancellationToken);
                return Results.Ok(ToDto(deleted));
            }
            catch (RecordNotFoundException)
            {
                return Results.NotFound();
            }
        }).DisableAntiforgery()
          .RequirePermission(EntityKinds.Record, Actions.Delete);

        group.MapPut("/{id:guid}/assignees", async (
            Guid id,
            Guid[] assigneeIds,
            HttpContext http,
            IRecordStore store,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var updated = await store.UpdateAsync(
                    id,
                    new UpdateRecordInput(
                        Name: null,
                        Status: Optional<string?>.None,
                        DueDate: Optional<DateOnly?>.None,
                        Values: null,
                        AssigneeIds: assigneeIds),
                    http.GetActorId(),
                    cancellationToken);
                return Results.Ok(ToDto(updated));
            }
            catch (RecordNotFoundException)
            {
                return Results.NotFound();
            }
        }).DisableAntiforgery()
          .RequirePermission(EntityKinds.Record, Actions.Assign);

        group.MapGet("/{id:guid}/history", async (
            Guid id,
            string? fieldKey,
            int? take,
            IRecordHistoryStore history,
            IAuditEventPublisher auditPublisher,
            CancellationToken cancellationToken) =>
        {
            var rows = await history.ListAsync(id, fieldKey, take ?? 100, cancellationToken);
            await auditPublisher.PublishAsync(
                DaprRecordEventPublisher.TopicName,
                RecordEventTypes.HistoryViewed,
                RecordResourceKinds.Record,
                resource: new { recordId = id },
                details: new { fieldKey, take = take ?? 100, resultCount = rows.Count },
                cancellationToken);
            return Results.Ok(rows.Select(ToDto).ToArray());
        }).RequirePermission(EntityKinds.Record, Actions.View);

        group.MapPost("/search", async (
            SearchRecordsRequest request,
            HttpContext http,
            IRecordStore store,
            IAuditEventPublisher auditPublisher,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var clauses = (request.Filters ?? Array.Empty<SearchFilterClause>())
                    .Select(c => new RecordFilterClause(
                        c.FieldKey,
                        ParseFilterOperator(c.Op),
                        c.Value))
                    .ToList();

                var resolvedPageSize = request.PageSize == 0 ? 25 : request.PageSize;
                var result = await store.SearchAsync(new RecordSearchInput(
                    request.RecordTypeId,
                    Filters: clauses,
                    AssigneeId: request.AssigneeId,
                    IncludeArchived: request.IncludeArchived,
                    Page: request.Page,
                    PageSize: resolvedPageSize,
                    Sort: request.Sort,
                    Search: request.Search),
                    http.User,
                    cancellationToken);

                var (filterHash, filterPreview) = ViewEventFilterHash.Compute(new
                {
                    request.Filters,
                    request.AssigneeId,
                    request.IncludeArchived,
                    request.Sort,
                    request.Search
                });
                await auditPublisher.PublishAsync(
                    DaprRecordEventPublisher.TopicName,
                    RecordEventTypes.Searched,
                    RecordResourceKinds.Record,
                    resource: null,
                    details: new
                    {
                        recordTypeId = request.RecordTypeId,
                        page = request.Page,
                        pageSize = resolvedPageSize,
                        resultCount = result.Records.Count,
                        totalCount = result.TotalCount,
                        filterHash,
                        filterPreview
                    },
                    cancellationToken);
                return Results.Ok(ToPageDto(result));
            }
            catch (RecordValidationException ex)
            {
                return Results.BadRequest(new { message = ex.Message, errors = ex.Errors });
            }
        }).DisableAntiforgery()
          .AuthorizedInHandler("filters via store.SearchAsync(actor) which applies Record:View grants");

        return app;
    }

    private static FilterOperator ParseFilterOperator(string op) => op switch
    {
        "eq" or "Equals" => FilterOperator.Equals,
        "neq" or "NotEquals" => FilterOperator.NotEquals,
        "gt" or "GreaterThan" => FilterOperator.GreaterThan,
        "gte" or "GreaterThanOrEqual" => FilterOperator.GreaterThanOrEqual,
        "lt" or "LessThan" => FilterOperator.LessThan,
        "lte" or "LessThanOrEqual" => FilterOperator.LessThanOrEqual,
        "contains" or "Contains" => FilterOperator.Contains,
        "in" or "In" => FilterOperator.In,
        _ => throw new RecordValidationException($"Unsupported filter operator '{op}'.")
    };
    private static RecordDto ToDto(Models.Records.Record model) => new(
        model.Id,
        model.RecordTypeId,
        model.Key,
        model.KeyNumber,
        model.Name,
        model.Status,
        model.DueDate,
        model.AssigneeIds.ToArray(),
        model.Values,
        model.IsArchived,
        model.CreatedAtUtc,
        model.CreatedBy,
        model.UpdatedAtUtc,
        model.UpdatedBy);

    // Manual binder for PATCH so we can tell "field absent" (don't touch) from
    // "field is null" (clear) — System.Text.Json record binding collapses both
    // to the default value of the property.
    private static UpdateRecordInput ParseUpdateInput(JsonElement body)
    {
        if (body.ValueKind != JsonValueKind.Object)
        {
            throw new RecordValidationException("Request body must be a JSON object.");
        }

        string? name = null;
        if (TryGetCaseInsensitive(body, "name", out var nameProp) &&
            nameProp.ValueKind == JsonValueKind.String)
        {
            name = nameProp.GetString();
        }

        JsonElement? values = null;
        if (TryGetCaseInsensitive(body, "values", out var valuesProp) &&
            valuesProp.ValueKind != JsonValueKind.Null &&
            valuesProp.ValueKind != JsonValueKind.Undefined)
        {
            values = valuesProp;
        }

        Guid[]? assigneeIds = null;
        if (TryGetCaseInsensitive(body, "assigneeIds", out var assigneeProp) &&
            assigneeProp.ValueKind == JsonValueKind.Array)
        {
            assigneeIds = assigneeProp.EnumerateArray().Select(e => e.GetGuid()).ToArray();
        }

        var status = Optional<string?>.None;
        if (TryGetCaseInsensitive(body, "status", out var statusProp))
        {
            status = statusProp.ValueKind switch
            {
                JsonValueKind.Null => Optional<string?>.Some(null),
                JsonValueKind.String => Optional<string?>.Some(statusProp.GetString()),
                _ => throw new RecordValidationException("status must be a string or null.")
            };
        }

        var dueDate = Optional<DateOnly?>.None;
        if (TryGetCaseInsensitive(body, "dueDate", out var dueDateProp))
        {
            dueDate = dueDateProp.ValueKind switch
            {
                JsonValueKind.Null => Optional<DateOnly?>.Some(null),
                JsonValueKind.String => Optional<DateOnly?>.Some(ParseDateOnly(dueDateProp.GetString()!)),
                _ => throw new RecordValidationException("dueDate must be a date string (YYYY-MM-DD) or null.")
            };
        }

        return new UpdateRecordInput(name, status, dueDate, values, assigneeIds);
    }

    private static bool TryGetCaseInsensitive(JsonElement obj, string name, out JsonElement value)
    {
        if (obj.TryGetProperty(name, out value))
        {
            return true;
        }

        foreach (var prop in obj.EnumerateObject())
        {
            if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = prop.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static DateOnly ParseDateOnly(string raw)
    {
        if (DateOnly.TryParseExact(raw, "yyyy-MM-dd",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var d) ||
            DateOnly.TryParse(raw,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out d))
        {
            return d;
        }
        throw new RecordValidationException($"Invalid date '{raw}'. Use YYYY-MM-DD.");
    }

    private static RecordPageDto ToPageDto(RecordListPage page) => new(
        page.Records.Select(ToDto).ToArray(),
        page.TotalCount,
        page.Page,
        page.PageSize);

    private static RecordHistoryEntryDto ToDto(RecordFieldChange model) => new(
        model.Id,
        model.RecordId,
        model.ChangeSetId,
        model.ChangeKind,
        model.FieldKey,
        model.OldValue,
        model.NewValue,
        model.ChangedBy,
        model.ChangedAtUtc);
}
