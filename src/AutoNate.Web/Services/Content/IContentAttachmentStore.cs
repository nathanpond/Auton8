namespace AutoNate.Web.Services.Content;

// Storage seam for attachment bytes. The default impl writes to disk under
// a configurable root; an S3 / MinIO impl can drop in later behind the same
// interface without touching endpoints or the DB. Metadata always lives in
// page_attachments — `storage_key` is whatever the store chooses to return
// from WriteAsync and is opaque to the caller.
public interface IContentAttachmentStore
{
    // Persists the stream and returns the storage_key used for subsequent
    // Read/Delete calls. projectId scopes the storage path so cleanup on
    // project delete can be a directory-wide operation later.
    Task<string> WriteAsync(Guid projectId, Guid attachmentId, Stream content, CancellationToken ct);

    Task<Stream> ReadAsync(string storageKey, CancellationToken ct);

    Task DeleteAsync(string storageKey, CancellationToken ct);
}
