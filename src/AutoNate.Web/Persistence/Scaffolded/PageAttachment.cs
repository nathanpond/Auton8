using System;

namespace AutoNate.Web.Persistence.Scaffolded;

public partial class PageAttachment
{
    public Guid Id { get; set; }

    public Guid PageId { get; set; }

    public string FileName { get; set; } = null!;

    public string ContentType { get; set; } = null!;

    public long ByteSize { get; set; }

    public string Sha256Hex { get; set; } = null!;

    public string StorageKey { get; set; } = null!;

    public bool IsArchived { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public Guid CreatedBy { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public Guid UpdatedBy { get; set; }
}
