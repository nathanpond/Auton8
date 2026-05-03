using System;

namespace AutoNate.Web.Persistence.Scaffolded;

public partial class Notification
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public string Kind { get; set; } = null!;

    public string Title { get; set; } = null!;

    public string Body { get; set; } = null!;

    public string? RelatedEntityKind { get; set; }

    public string? RelatedEntityId { get; set; }

    public string? ParentEntityKind { get; set; }

    public string? ParentEntityId { get; set; }

    public string? LinkPath { get; set; }

    public bool IsRead { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? ReadAtUtc { get; set; }
}
