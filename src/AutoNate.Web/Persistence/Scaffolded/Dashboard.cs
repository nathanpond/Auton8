using System;

namespace AutoNate.Web.Persistence.Scaffolded;

public partial class Dashboard
{
    public Guid Id { get; set; }

    public Guid OwnerUserId { get; set; }

    public string Name { get; set; } = null!;

    public string? Description { get; set; }

    // 'private' | 'shared' | 'public' — kept open as TEXT in the DB so future
    // visibility tiers don't need a migration. v1 only writes 'private'.
    public string Visibility { get; set; } = "private";

    // 'user' | 'team' | 'site' — future-proofing seam for sharing scope.
    // v1 only writes 'user'.
    public string Scope { get; set; } = "user";

    // 'user' | 'template' — marks dashboards that were scaffolded from a
    // page-template's default layout vs. ones the user created from scratch.
    public string Source { get; set; } = "user";

    public string? TemplateKey { get; set; }

    // Raw JSON string ('{...}'). EF surfaces jsonb as a string here; callers
    // parse with System.Text.Json when they need the structured form.
    public string SettingsJsonb { get; set; } = "{}";

    public bool IsArchived { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public Guid CreatedBy { get; set; }

    public Guid UpdatedBy { get; set; }
}
