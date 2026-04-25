using System;

namespace AutoNate.Web.Persistence.Scaffolded;

public partial class RecordCommentRevision
{
    public long Id { get; set; }

    public Guid CommentId { get; set; }

    public string Body { get; set; } = null!;

    public DateTime ReplacedAtUtc { get; set; }

    public Guid ReplacedBy { get; set; }
}
