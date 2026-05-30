namespace AutoNate.Web.Services.Content;

// Read/write/delete contract for the import stash. Files are keyed by
// document id so the editor's first-load can fetch the bytes back via
// GET /api/content/documents/{id}/import-buffer and the cleanup endpoint
// can discard them once the JSON snapshot exists. There's no per-project
// nesting — every stashed file is a transient artifact tied to exactly
// one Document, and we want flat directory listings for sweep-based GC.
public interface IDocumentImportStorage
{
    // Returns the absolute path the bytes landed at, so the upload endpoint
    // can log it. Callers should treat the returned path as opaque — the
    // fetch + delete operations only need the documentId.
    Task<string> WriteAsync(Guid documentId, Stream content, CancellationToken ct);

    // Throws FileNotFoundException if the stash has already been discarded
    // (or never existed). The editor route catches this and falls back to
    // the normal blank-canvas open path.
    Task<Stream> ReadAsync(Guid documentId, CancellationToken ct);

    Task DeleteAsync(Guid documentId, CancellationToken ct);

    // Lightweight existence check used by the editor route to decide
    // whether to send documentBuffer or skip straight to Yjs. Cheaper
    // than opening the stream just to test for presence.
    bool Exists(Guid documentId);
}
