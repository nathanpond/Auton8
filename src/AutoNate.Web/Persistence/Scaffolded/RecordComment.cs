using System;

namespace AutoNate.Web.Persistence.Scaffolded;

public partial class RecordComment
{
    public Guid Id { get; set; }

    public Guid RecordId { get; set; }

    public Guid AuthorId { get; set; }

    public string Body { get; set; } = null!;

    public DateTime CreatedAtUtc { get; set; }

    public DateTime BodyUpdatedAtUtc { get; set; }

    public bool IsDeleted { get; set; }

    public DateTime? DeletedAtUtc { get; set; }

    public Guid? DeletedBy { get; set; }
}
