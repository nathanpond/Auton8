using System;

namespace AutoNate.Web.Persistence.Scaffolded;

public partial class RecordWatch
{
    public Guid UserId { get; set; }

    public Guid RecordId { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
