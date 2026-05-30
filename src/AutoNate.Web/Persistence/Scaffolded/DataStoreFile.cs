namespace AutoNate.Web.Persistence.Scaffolded;

// One row per file in a FileType DataStore. Folder structure is encoded
// in `FolderPath` (POSIX-style, leading "/", e.g. "/reports/2026").
// Root-level files have FolderPath = "/". Storage bytes live on disk
// under DataPaths.DatastoresRoot keyed by `StorageKey`.
public partial class DataStoreFile
{
    public Guid Id { get; set; }

    public Guid DataStoreId { get; set; }

    public string FolderPath { get; set; } = "/";

    public string Filename { get; set; } = null!;

    public string StorageKey { get; set; } = null!;

    public long SizeBytes { get; set; }

    public string? ContentType { get; set; }

    public Guid UploadedBy { get; set; }

    public DateTime UploadedAtUtc { get; set; }
}
