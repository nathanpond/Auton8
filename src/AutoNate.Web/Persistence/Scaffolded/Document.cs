using System;

namespace AutoNate.Web.Persistence.Scaffolded;

public partial class Document
{
    public Guid Id { get; set; }

    public long Locator { get; set; }

    public Guid ProjectId { get; set; }

    // Nullable for project-root documents. When set, the document lives
    // inside a Folder; the IContentAuthorizer ancestor chain walks through
    // it before reaching the project.
    public Guid? FolderId { get; set; }

    // 'document' | 'template'. Templates and documents share the same
    // entity + EntityKind; the discriminator drives the gallery filter and
    // the create-from-template clone path. Persisted as a lowercase string
    // with a CHECK constraint.
    public string Kind { get; set; } = null!;

    // Set when a document was created from a template; preserved as a
    // back-reference so the template gallery can show "documents created
    // from this template". Independent of the body — once created, the
    // document has its own copy of the body / bindings.
    public Guid? TemplateId { get; set; }

    public string Title { get; set; } = null!;

    public string? Description { get; set; }

    public string BodyJsonb { get; set; } = null!;

    public int CurrentVersionNumber { get; set; }

    public int SortOrder { get; set; }

    public bool IsArchived { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public Guid CreatedBy { get; set; }

    public Guid UpdatedBy { get; set; }
}
