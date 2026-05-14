using System;

namespace AutoNate.Web.Persistence.Scaffolded;

public partial class NoteVersion
{
    public Guid Id { get; set; }

    public Guid NoteId { get; set; }

    public int VersionNumber { get; set; }

    public string? Title { get; set; }

    public string NoteKind { get; set; } = null!;

    public string ContentJsonb { get; set; } = null!;

    // 'autosave' | 'manual' | 'restore'
    public string Kind { get; set; } = null!;

    public string? Note { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public Guid CreatedBy { get; set; }
}
