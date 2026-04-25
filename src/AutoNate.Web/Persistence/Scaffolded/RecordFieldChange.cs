using System;

namespace AutoNate.Web.Persistence.Scaffolded;

public partial class RecordFieldChange
{
    public long Id { get; set; }

    public Guid RecordId { get; set; }

    public Guid? ChangeSetId { get; set; }

    public string ChangeKind { get; set; } = null!;

    public string? FieldKey { get; set; }

    public string? OldValue { get; set; }

    public string? NewValue { get; set; }

    public Guid ChangedBy { get; set; }

    public DateTime ChangedAtUtc { get; set; }
}
