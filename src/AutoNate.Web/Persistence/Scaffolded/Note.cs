using System;

namespace AutoNate.Web.Persistence.Scaffolded;

public partial class Note
{
    public Guid Id { get; set; }

    public long Locator { get; set; }

    public Guid PageId { get; set; }

    // 'richtext' | 'drawing' | 'diagram'
    public string NoteKind { get; set; } = null!;

    public string? Title { get; set; }

    public string ContentJsonb { get; set; } = null!;

    public int CurrentVersionNumber { get; set; }

    public int SortOrder { get; set; }

    public bool IsArchived { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public Guid CreatedBy { get; set; }

    public Guid UpdatedBy { get; set; }
}
