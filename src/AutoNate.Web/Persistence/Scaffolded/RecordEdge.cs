using System;

namespace AutoNate.Web.Persistence.Scaffolded;

public partial class RecordEdge
{
    public Guid Id { get; set; }

    public Guid EdgeTypeId { get; set; }

    public Guid FromRecordId { get; set; }

    public Guid ToRecordId { get; set; }

    public string Data { get; set; } = "{}";

    public DateTime CreatedAtUtc { get; set; }

    public Guid CreatedBy { get; set; }
}
