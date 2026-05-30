using System;

namespace AutoNate.Web.Persistence.Scaffolded;

public partial class DocumentVersion
{
    public Guid Id { get; set; }

    public Guid DocumentId { get; set; }

    public int VersionNumber { get; set; }

    public string Title { get; set; } = null!;

    public string BodyJsonb { get; set; } = null!;

    // 'autosave' | 'manual' | 'restore'. Same vocabulary as PageVersion /
    // NoteVersion so the version-history UI can render every content kind
    // through one code path.
    public string Kind { get; set; } = null!;

    public string? Note { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public Guid CreatedBy { get; set; }
}
