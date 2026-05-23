using System;

namespace AutoNate.Web.Persistence.Scaffolded;

public partial class SavedQuery
{
    public Guid Id { get; set; }

    public string Name { get; set; } = null!;

    public string? Description { get; set; }

    public string QueryText { get; set; } = null!;

    // When true, every authenticated user can list and load this saved
    // query (read-only). Only the owner can edit or delete it regardless.
    // v1 has no per-row deny-list; flipping shared off is the way to revoke.
    public bool IsShared { get; set; }

    public Guid OwnerUserId { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public Guid CreatedBy { get; set; }

    public Guid UpdatedBy { get; set; }
}
