using AutoNate.Web.Models.Records;
using AutoNate.Web.Persistence;
using Microsoft.EntityFrameworkCore;
using RecordCommentEntity = AutoNate.Web.Persistence.Scaffolded.RecordComment;
using RecordCommentRevisionEntity = AutoNate.Web.Persistence.Scaffolded.RecordCommentRevision;

namespace AutoNate.Web.Services.Records;

public sealed class EfCoreRecordCommentStore(IDbContextFactory<AutoNateDbContext> dbContextFactory)
    : IRecordCommentStore
{
    private const int MaxBodyLength = 10_000;

    public async Task<IReadOnlyList<RecordComment>> ListForRecordAsync(
        Guid recordId,
        bool includeDeleted,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var query = dbContext.RecordComments.AsNoTracking()
            .Where(c => c.RecordId == recordId);
        if (!includeDeleted)
        {
            query = query.Where(c => !c.IsDeleted);
        }
        var rows = await query
            .OrderByDescending(c => c.CreatedAtUtc)
            .ToListAsync(cancellationToken);
        return rows.Select(r => r.ToModel()).ToList();
    }

    public async Task<RecordComment?> GetAsync(Guid commentId, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await dbContext.RecordComments.AsNoTracking()
            .SingleOrDefaultAsync(c => c.Id == commentId, cancellationToken);
        return entity?.ToModel();
    }

    public async Task<RecordComment> CreateAsync(
        Guid recordId,
        string body,
        Guid authorId,
        CancellationToken cancellationToken = default)
    {
        var trimmed = (body ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            throw new RecordCommentValidationException("Comment body cannot be empty.");
        }
        if (trimmed.Length > MaxBodyLength)
        {
            throw new RecordCommentValidationException($"Comment body exceeds {MaxBodyLength} characters.");
        }

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var recordExists = await dbContext.Records.AnyAsync(r => r.Id == recordId, cancellationToken);
        if (!recordExists)
        {
            throw new RecordCommentValidationException($"Record '{recordId}' was not found.");
        }

        var now = DateTimeOffset.UtcNow;
        var entity = new RecordCommentEntity
        {
            Id = Guid.NewGuid(),
            RecordId = recordId,
            AuthorId = authorId,
            Body = trimmed,
            CreatedAtUtc = now.UtcDateTime,
            BodyUpdatedAtUtc = now.UtcDateTime,
            IsDeleted = false
        };
        dbContext.RecordComments.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
        return entity.ToModel();
    }

    public async Task<RecordComment> EditAsync(
        Guid commentId,
        string newBody,
        Guid actorId,
        CancellationToken cancellationToken = default)
    {
        var trimmed = (newBody ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            throw new RecordCommentValidationException("Comment body cannot be empty.");
        }
        if (trimmed.Length > MaxBodyLength)
        {
            throw new RecordCommentValidationException($"Comment body exceeds {MaxBodyLength} characters.");
        }

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await dbContext.RecordComments.SingleOrDefaultAsync(c => c.Id == commentId, cancellationToken)
            ?? throw new RecordCommentNotFoundException(commentId);

        if (entity.AuthorId != actorId)
        {
            throw new RecordCommentForbiddenException(commentId);
        }

        if (entity.IsDeleted)
        {
            throw new RecordCommentValidationException("Cannot edit a deleted comment.");
        }

        if (string.Equals(entity.Body, trimmed, StringComparison.Ordinal))
        {
            // No-op. No revision row, no updated_at bump.
            return entity.ToModel();
        }

        var now = DateTimeOffset.UtcNow;

        await using var tx = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        // Capture the previous body BEFORE mutating the comment.
        dbContext.RecordCommentRevisions.Add(new RecordCommentRevisionEntity
        {
            CommentId = entity.Id,
            Body = entity.Body,
            ReplacedAtUtc = now.UtcDateTime,
            ReplacedBy = actorId
        });

        entity.Body = trimmed;
        entity.BodyUpdatedAtUtc = now.UtcDateTime;

        await dbContext.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        return entity.ToModel();
    }

    public async Task<RecordComment> SoftDeleteAsync(
        Guid commentId,
        Guid actorId,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await dbContext.RecordComments.SingleOrDefaultAsync(c => c.Id == commentId, cancellationToken)
            ?? throw new RecordCommentNotFoundException(commentId);

        if (entity.AuthorId != actorId)
        {
            throw new RecordCommentForbiddenException(commentId);
        }

        if (entity.IsDeleted)
        {
            return entity.ToModel();
        }

        var now = DateTimeOffset.UtcNow;
        entity.IsDeleted = true;
        entity.DeletedAtUtc = now.UtcDateTime;
        entity.DeletedBy = actorId;
        await dbContext.SaveChangesAsync(cancellationToken);
        return entity.ToModel();
    }

    public async Task<IReadOnlyList<RecordCommentRevision>> ListRevisionsAsync(
        Guid commentId,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var rows = await dbContext.RecordCommentRevisions.AsNoTracking()
            .Where(r => r.CommentId == commentId)
            .OrderByDescending(r => r.ReplacedAtUtc)
            .ThenByDescending(r => r.Id)
            .ToListAsync(cancellationToken);
        return rows.Select(r => r.ToModel()).ToList();
    }
}
