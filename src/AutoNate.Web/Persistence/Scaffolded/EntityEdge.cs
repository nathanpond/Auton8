using System;

namespace AutoNate.Web.Persistence.Scaffolded;

public partial class EntityEdge
{
    public Guid Id { get; set; }

    public string EdgeKind { get; set; } = null!;

    public string FromKind { get; set; } = null!;

    public string FromId { get; set; } = null!;

    public string ToKind { get; set; } = null!;

    public string ToId { get; set; } = null!;

    public string Data { get; set; } = "{}";

    public DateTime CreatedAtUtc { get; set; }

    public Guid CreatedBy { get; set; }
}
