using System;

namespace AutoNate.Web.Persistence.Scaffolded;

public partial class RecordTypeAuditEntry
{
    public long Id { get; set; }

    public Guid RecordTypeId { get; set; }

    public string ChangeKind { get; set; } = null!;

    public string? Before { get; set; }

    public string? After { get; set; }

    public Guid ChangedBy { get; set; }

    public DateTime ChangedAtUtc { get; set; }
}
