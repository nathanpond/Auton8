using System;

namespace AutoNate.Web.Persistence.Scaffolded;

public partial class Record
{
    public Guid Id { get; set; }

    public Guid RecordTypeId { get; set; }

    public string Key { get; set; } = null!;

    public long KeyNumber { get; set; }

    public string Name { get; set; } = null!;

    public Guid[] AssigneeIds { get; set; } = Array.Empty<Guid>();

    // JSONB serialized as text; parsed/serialized at the domain boundary.
    public string Values { get; set; } = "{}";

    public bool IsArchived { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public Guid CreatedBy { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public Guid UpdatedBy { get; set; }
}
