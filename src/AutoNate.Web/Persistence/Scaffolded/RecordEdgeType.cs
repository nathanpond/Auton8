using System;

namespace AutoNate.Web.Persistence.Scaffolded;

public partial class RecordEdgeType
{
    public Guid Id { get; set; }

    public string ShortCode { get; set; } = null!;

    public string Name { get; set; } = null!;

    public string? InverseName { get; set; }

    public bool IsDirected { get; set; }

    public bool AllowSelfReference { get; set; }

    public string Cardinality { get; set; } = "many_to_many";

    public Guid[]? FromRecordTypeIds { get; set; }

    public Guid[]? ToRecordTypeIds { get; set; }

    public bool IsArchived { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}
