namespace AutoNate.Web.Models.Records;

public sealed record class RecordComment
{
    public Guid Id { get; init; }

    public Guid RecordId { get; init; }

    public Guid AuthorId { get; init; }

    public string Body { get; init; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; init; }

    public DateTimeOffset BodyUpdatedAtUtc { get; init; }

    public bool IsDeleted { get; init; }

    public DateTimeOffset? DeletedAtUtc { get; init; }

    public Guid? DeletedBy { get; init; }
}

public sealed record class RecordCommentRevision
{
    public long Id { get; init; }

    public Guid CommentId { get; init; }

    public string Body { get; init; } = string.Empty;

    public DateTimeOffset ReplacedAtUtc { get; init; }

    public Guid ReplacedBy { get; init; }
}
