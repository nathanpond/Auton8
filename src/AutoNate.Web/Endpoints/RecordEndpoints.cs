using System.Security.Claims;
using System.Text.Json;
using AutoNate.Web.Models.Records;
using AutoNate.Web.Services.Records;
using AutoNate.Web.Services.Records.Fields;

namespace AutoNate.Web.Endpoints;

public sealed record CreateRecordRequest(
    Guid RecordTypeId,
    string Name,
    JsonElement Values,
    Guid[]? AssigneeIds);

public sealed record UpdateRecordRequest(
    string? Name,
    JsonElement? Values,
    Guid[]? AssigneeIds);

public sealed record SearchRecordsRequest(
    Guid RecordTypeId,
    SearchFilterClause[]? Filters,
    Guid? AssigneeId,
    bool IncludeArchived,
    int Page,
    int PageSize,
    string? Sort);

public sealed record SearchFilterClause(string FieldKey, string Op, JsonElement Value);

public sealed record RecordDto(
    Guid Id,
    Guid RecordTypeId,
    string Key,
    long KeyNumber,
    string Name,
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
            IRecordStore store,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await store.SearchAsync(new RecordSearchInput(
                    recordTypeId,
                    Filters: null,
                    AssigneeId: assigneeId,
                    IncludeArchived: includeArchived ?? false,
                    Page: page ?? 0,
                    PageSize: pageSize ?? 25,
                    Sort: sort),
                    cancellationToken);
                return Results.Ok(ToPageDto(result));
            }
            catch (RecordValidationException ex)
            {
                return Results.BadRequest(new { message = ex.Message, errors = ex.Errors });
            }
        });

        group.MapGet("/{id:guid}", async (Guid id, IRecordStore store, CancellationToken cancellationToken) =>
        {
            var record = await store.GetAsync(id, cancellationToken);
            return record is null ? Results.NotFound() : Results.Ok(ToDto(record));
        });

        group.MapGet("/by-key/{key}", async (string key, IRecordStore store, CancellationToken cancellationToken) =>
        {
            var record = await store.GetByKeyAsync(key, cancellationToken);
            return record is null ? Results.NotFound() : Results.Ok(ToDto(record));
        });

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
                        request.Values,
                        request.AssigneeIds),
                    GetActorId(http),
                    cancellationToken);
                return Results.Created($"/api/records/{created.Id}", ToDto(created));
            }
            catch (RecordValidationException ex)
            {
                return Results.BadRequest(new { message = ex.Message, errors = ex.Errors });
            }
        }).DisableAntiforgery();

        group.MapPatch("/{id:guid}", async (
            Guid id,
            UpdateRecordRequest request,
            HttpContext http,
            IRecordStore store,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var updated = await store.UpdateAsync(
                    id,
                    new UpdateRecordInput(request.Name, request.Values, request.AssigneeIds),
                    GetActorId(http),
                    cancellationToken);
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
        }).DisableAntiforgery();

        group.MapDelete("/{id:guid}", async (
            Guid id,
            HttpContext http,
            IRecordStore store,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var archived = await store.SetArchivedAsync(id, archived: true, GetActorId(http), cancellationToken);
                return Results.Ok(ToDto(archived));
            }
            catch (RecordNotFoundException)
            {
                return Results.NotFound();
            }
        }).DisableAntiforgery();

        group.MapPost("/{id:guid}/restore", async (
            Guid id,
            HttpContext http,
            IRecordStore store,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var restored = await store.SetArchivedAsync(id, archived: false, GetActorId(http), cancellationToken);
                return Results.Ok(ToDto(restored));
            }
            catch (RecordNotFoundException)
            {
                return Results.NotFound();
            }
        }).DisableAntiforgery();

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
                    new UpdateRecordInput(Name: null, Values: null, AssigneeIds: assigneeIds),
                    GetActorId(http),
                    cancellationToken);
                return Results.Ok(ToDto(updated));
            }
            catch (RecordNotFoundException)
            {
                return Results.NotFound();
            }
        }).DisableAntiforgery();

        group.MapGet("/{id:guid}/history", async (
            Guid id,
            string? fieldKey,
            int? take,
            IRecordHistoryStore history,
            CancellationToken cancellationToken) =>
        {
            var rows = await history.ListAsync(id, fieldKey, take ?? 100, cancellationToken);
            return Results.Ok(rows.Select(ToDto).ToArray());
        });

        group.MapPost("/search", async (
            SearchRecordsRequest request,
            IRecordStore store,
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

                var result = await store.SearchAsync(new RecordSearchInput(
                    request.RecordTypeId,
                    Filters: clauses,
                    AssigneeId: request.AssigneeId,
                    IncludeArchived: request.IncludeArchived,
                    Page: request.Page,
                    PageSize: request.PageSize == 0 ? 25 : request.PageSize,
                    Sort: request.Sort),
                    cancellationToken);
                return Results.Ok(ToPageDto(result));
            }
            catch (RecordValidationException ex)
            {
                return Results.BadRequest(new { message = ex.Message, errors = ex.Errors });
            }
        }).DisableAntiforgery();

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

    private static Guid GetActorId(HttpContext http)
    {
        var claim = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(claim, out var id) ? id : Guid.Empty;
    }

    private static RecordDto ToDto(Models.Records.Record model) => new(
        model.Id,
        model.RecordTypeId,
        model.Key,
        model.KeyNumber,
        model.Name,
        model.AssigneeIds.ToArray(),
        model.Values,
        model.IsArchived,
        model.CreatedAtUtc,
        model.CreatedBy,
        model.UpdatedAtUtc,
        model.UpdatedBy);

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
