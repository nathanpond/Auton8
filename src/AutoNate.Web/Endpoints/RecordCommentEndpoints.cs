using System.Security.Claims;
using AutoNate.Web.Models.Records;
using AutoNate.Web.Services.Records;

namespace AutoNate.Web.Endpoints;

public sealed record CreateCommentRequest(string Body);
public sealed record UpdateCommentRequest(string Body);

public sealed record CommentDto(
    Guid Id,
    Guid RecordId,
    Guid AuthorId,
    string Body,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset BodyUpdatedAtUtc,
    bool IsEdited,
    bool IsDeleted,
    DateTimeOffset? DeletedAtUtc,
    Guid? DeletedBy);

public sealed record CommentRevisionDto(
    long Id,
    Guid CommentId,
    string Body,
    DateTimeOffset ReplacedAtUtc,
    Guid ReplacedBy);

public static class RecordCommentEndpoints
{
    public static IEndpointRouteBuilder MapRecordCommentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/records/{recordId:guid}/comments").RequireAuthorization();

        group.MapGet("/", async (
            Guid recordId,
            bool? includeDeleted,
            IRecordCommentStore store,
            CancellationToken ct) =>
        {
            var comments = await store.ListForRecordAsync(recordId, includeDeleted ?? false, ct);
            return Results.Ok(comments.Select(ToDto).ToArray());
        });

        group.MapPost("/", async (
            Guid recordId,
            CreateCommentRequest request,
            HttpContext http,
            IRecordCommentStore store,
            CancellationToken ct) =>
        {
            try
            {
                var created = await store.CreateAsync(recordId, request.Body, GetActorId(http), ct);
                return Results.Created(
                    $"/api/records/{recordId}/comments/{created.Id}",
                    ToDto(created));
            }
            catch (RecordCommentValidationException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        }).DisableAntiforgery();

        group.MapPatch("/{commentId:guid}", async (
            Guid recordId,
            Guid commentId,
            UpdateCommentRequest request,
            HttpContext http,
            IRecordCommentStore store,
            CancellationToken ct) =>
        {
            try
            {
                var updated = await store.EditAsync(commentId, request.Body, GetActorId(http), ct);
                return updated.RecordId == recordId
                    ? Results.Ok(ToDto(updated))
                    : Results.NotFound();
            }
            catch (RecordCommentNotFoundException)
            {
                return Results.NotFound();
            }
            catch (RecordCommentValidationException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        }).DisableAntiforgery();

        group.MapDelete("/{commentId:guid}", async (
            Guid recordId,
            Guid commentId,
            HttpContext http,
            IRecordCommentStore store,
            CancellationToken ct) =>
        {
            try
            {
                var deleted = await store.SoftDeleteAsync(commentId, GetActorId(http), ct);
                return deleted.RecordId == recordId
                    ? Results.Ok(ToDto(deleted))
                    : Results.NotFound();
            }
            catch (RecordCommentNotFoundException)
            {
                return Results.NotFound();
            }
        }).DisableAntiforgery();

        group.MapGet("/{commentId:guid}/revisions", async (
            Guid recordId,
            Guid commentId,
            IRecordCommentStore store,
            CancellationToken ct) =>
        {
            var comment = await store.GetAsync(commentId, ct);
            if (comment is null || comment.RecordId != recordId)
            {
                return Results.NotFound();
            }
            var revisions = await store.ListRevisionsAsync(commentId, ct);
            return Results.Ok(revisions.Select(ToDto).ToArray());
        });

        return app;
    }

    private static Guid GetActorId(HttpContext http)
    {
        var claim = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(claim, out var id) ? id : Guid.Empty;
    }

    private static CommentDto ToDto(RecordComment model) => new(
        model.Id,
        model.RecordId,
        model.AuthorId,
        model.Body,
        model.CreatedAtUtc,
        model.BodyUpdatedAtUtc,
        IsEdited: model.BodyUpdatedAtUtc > model.CreatedAtUtc,
        model.IsDeleted,
        model.DeletedAtUtc,
        model.DeletedBy);

    private static CommentRevisionDto ToDto(RecordCommentRevision model) => new(
        model.Id,
        model.CommentId,
        model.Body,
        model.ReplacedAtUtc,
        model.ReplacedBy);
}
