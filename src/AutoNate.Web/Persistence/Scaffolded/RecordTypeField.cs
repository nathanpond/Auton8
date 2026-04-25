using System;

namespace AutoNate.Web.Persistence.Scaffolded;

public partial class RecordTypeField
{
    public Guid Id { get; set; }

    public Guid RecordTypeId { get; set; }

    public string FieldKey { get; set; } = null!;

    public string DisplayName { get; set; } = null!;

    public string DataType { get; set; } = null!;

    // JSONB stored as a JSON string; parsed/serialized at the domain boundary.
    public string Config { get; set; } = "{}";

    public bool IsRequired { get; set; }

    public bool IsArchived { get; set; }

    public int SortOrder { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}
